using System.IO;
using Chmonos.Api;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.App.Services;

/// <summary>
/// 外から使う API の「Unity へ送る」（docs/history/headless-core-2026-10-13.md 段3。ユーザ判断 2026-10-13：最初の一束に入れる）。
/// 画面の「Unity へ送る」と同じ部品（<see cref="UnityImportQueue"/>）で送り、送れたら「使った」の足跡、送れなかったら「見つからない」の印を確かめ直す。
///
/// 画面と違う所：
/// - 送り先は、エディタが1つだけ開いていればそれ、複数ならプロジェクトを指定してもらう（画面は選ぶ窓を出すが、API は窓を出せない）
/// - 「送りますか」の確認は出さない。Unity の取り込み画面で中身を確かめてから取り込む作りなので、そこが確認になる
/// - 送り終えるまで待たずに返る（取り込み画面で人が考える間、最大30分待つ）。終わりは送りの ID で引く状態と変化の知らせで伝える
/// </summary>
internal sealed class ApiUnitySender(
    AppServiceContainer services,
    Func<IReadOnlyList<OpenUnityEditor>>? openEditors = null,
    Func<OpenUnityEditor, string?>? pathOf = null,
    ApiUnitySender.UnityRun? run = null,
    Func<bool>? isRunning = null) : IApiUnity
{
    /// <summary>送る部品（試験は Unity を動かさない作り物に差し替える）。1件の結果が決まるたびに知らせ、止める合図を受ける。</summary>
    internal delegate Task<IReadOnlyList<UnityQueueOutcome>> UnityRun(
        int processId, IReadOnlyList<UnityPackageEntry> packages, Action<int, UnityQueueOutcome> outcomeAdded, CancellationToken cancellationToken);

    /// <summary>
    /// 覚えておく送りの数。送りは1本ずつで1回に数秒〜数分かかり、改変の使ったものをまとめて送っても1つと数える。
    /// 外の道具は 202 の後に自分の送りを見に来るので、その間に押し出されなければ足りる。画面と行き来しながらの作業でも
    /// 続けて頼むのは多くて数件で、16 あれば読み終える前に消えることはまず無い。1つは数百バイトなので、持つ重さも気にならない
    /// </summary>
    internal const int Kept = 16;

    private readonly Func<IReadOnlyList<OpenUnityEditor>> _openEditors = openEditors ?? UnityEditors.Open;
    private readonly Func<OpenUnityEditor, string?> _pathOf = pathOf ?? (editor => editor.ProjectPath ?? UnityEditors.PathOf(editor));
    private readonly UnityRun _run = run ?? ((processId, packages, outcomeAdded, cancellationToken)
        => UnityImportQueue.RunAsync(processId, packages, null, cancellationToken, outcomeAdded));
    private readonly Func<bool> _isRunning = isRunning ?? (() => UnityImportQueue.IsRunning);

    /// <summary>覚えている送り（古い順）。中身の書き換えは <see cref="_startLock"/> の中で行う。</summary>
    private readonly List<Send> _sends = [];

    /// <summary>
    /// 送っている途中か（後始末まで含む）。確かめと予約を同じ錠の中で行う。前は確かめと予約が分かれていて、同時の頼みが両方 202 を受け、
    /// 前の送りの後始末の間に次が始まると、前の結果が新しい「最後の送り」を上書きした（点検37）
    /// </summary>
    private bool _sending;

    private readonly object _startLock = new();

    /// <summary>送り1つ。状態は錠の中で丸ごと差し替える（読む側は錠の外でも、いつも揃った1つを見る）。</summary>
    private sealed class Send(UnitySendStatus status, CancellationTokenSource stop)
    {
        public UnitySendStatus Status { get; set; } = status;

        public CancellationTokenSource Stop { get; } = stop;

        public Task<UnitySendStatus> Finished { get; set; } = Task.FromResult(status);
    }

    /// <summary>送る列の1件：何を送るかと、状態の1件ずつの何番目に当たるか（送らない行は列に入れず、状態にだけ並べる）。</summary>
    private sealed record Queued(string ItemId, ItemRecord? Item, UnityPackageEntry Package, int Step);

    public UnitySendStatus? Last
    {
        get
        {
            lock (_startLock)
            {
                return _sends.Count == 0 ? null : _sends[^1].Status;
            }
        }
    }

    public UnitySendStatus? Find(string sendId)
    {
        lock (_startLock)
        {
            return _sends.FirstOrDefault(send => send.Status.SendId == sendId)?.Status;
        }
    }

    public IReadOnlyList<UnityEditorRef> OpenEditors()
        => _openEditors().Select(editor => new UnityEditorRef(editor.ProcessId, editor.ProjectName, _pathOf(editor))).ToList();

    public (UnitySendStatus Started, Task<UnitySendStatus> Finished) Start(ItemRecord item, UnitySendRequest request)
    {
        lock (_startLock)
        {
            ThrowIfBusy();
            var package = PickPackage(item, request);
            var (editor, project) = PickEditor(request.Project, null);
            var step = StepOf(null, item.Id, package, UnitySendStates.Sending, null);
            var status = new UnitySendStatus(
                NewId(), item.Id, null, package.Name, package.EntryPath, package.ZipHash, project, UnitySendStates.Sending, null, DateTimeOffset.Now, null, [step]);
            return Begin(status, editor, [new Queued(item.Id, item, package, 0)], []);
        }
    }

    public (UnitySendStatus Started, Task<UnitySendStatus> Finished) StartModification(UnityModificationSend send)
    {
        lock (_startLock)
        {
            ThrowIfBusy();

            // 送り先を省いたら、紐付けたプロジェクトが開いていればそこ（画面の送り先の選び方と同じ。画面は開いていなければ選ぶ窓を出すが、
            // API は聞けないので、開いているのが1つならそこ、複数なら指定してもらう）
            var (editor, project) = PickEditor(send.Project, send.Record.UnityProject);
            var steps = new List<UnitySendStep>();
            var queue = new List<Queued>();
            foreach (var step in send.Steps)
            {
                if (step.Package is { } package)
                {
                    queue.Add(new Queued(step.ItemId, step.Item, package, steps.Count));
                    steps.Add(StepOf(step.Index, step.ItemId, package, queue.Count == 1 ? UnitySendStates.Sending : UnitySendStates.Queued, null));
                }
                else
                {
                    steps.Add(new UnitySendStep(step.Index, step.ItemId, null, null, null, UnitySendStates.Skipped, step.Skipped));
                }
            }

            var status = new UnitySendStatus(
                NewId(), null, send.Record.Id, null, null, null, project, UnitySendStates.Sending, null, DateTimeOffset.Now, null, steps);
            return Begin(status, editor, queue, send.Records.Select(record => (send.Record.Id, record)).ToList());
        }
    }

    public (UnitySendStatus Current, Task<UnitySendStatus> Finished) Cancel(string sendId)
    {
        Send send;
        lock (_startLock)
        {
            send = _sends.FirstOrDefault(candidate => candidate.Status.SendId == sendId)
                ?? throw QueryException.NotFound("unitySendNotFound", "その送りは見つかりません。覚えているのは直近の数件だけです。");
            if (send.Status.EndedAt is not null)
            {
                throw new QueryException(409, "unitySendEnded", $"その送りはもう終わっています（{send.Status.State}）。");
            }
        }

        // 止める合図は送る部品（UnityImportQueue.RunAsync）の止め方へつながっている。画面の「中止」（UnityImportQueue.Stop）は
        // その中で同じトークンにつないだ物を止めるので、止まり方（取り込みの窓を待つ間・取り出しの途中で止まる、残りは「中止しました」）は同じ。
        // 「中止」を直に呼ばないのは、この送りが終わった直後に画面が始めた別の送りを止めないため。錠の外で止める（止めた続きがこの場で走ることがある）
        send.Stop.Cancel();
        return (send.Status, send.Finished);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static UnitySendStep StepOf(int? index, string itemId, UnityPackageEntry package, string state, string? problem)
        => new(index, itemId, package.Name, package.EntryPath, package.ZipHash, state, problem);

    private void ThrowIfBusy()
    {
        // 連続送りの最中は混ぜない。Editor.log は全エディタが共有するので、終わりを取り違える（画面と同じ門）
        if (_sending || _isRunning())
        {
            throw new QueryException(409, "unityBusy", UnityImportQueue.BusyMessage);
        }
    }

    /// <summary>錠の中で呼ぶ。送りを覚え、送り始める。</summary>
    private (UnitySendStatus, Task<UnitySendStatus>) Begin(
        UnitySendStatus status, OpenUnityEditor editor, IReadOnlyList<Queued> queue, IReadOnlyList<(string ModificationId, UnityChoiceRecord Record)> records)
    {
        var send = new Send(status, new CancellationTokenSource());
        _sending = true;
        _sends.Add(send);

        // 古い送りから忘れる。今の送りはいちばん新しいので消えない（止める合図は捨てるだけでよい。時計を持たないので片付ける物が無い）
        while (_sends.Count > Kept)
        {
            _sends.RemoveAt(0);
        }

        send.Finished = Task.Run(() => SendGuardedAsync(send, editor, queue, records));
        return (status, send.Finished);
    }

    /// <summary>送る物：指定があればそれ（zip の中のパスと、あれば zip のハッシュで照らす）、無ければ1つしか無いときだけそれ。</summary>
    internal static UnityPackageEntry PickPackage(ItemRecord item, UnitySendRequest request)
    {
        var packages = UnityImportQueue.PackagesOf(item);
        if (packages.Count == 0)
        {
            throw QueryException.NotFound("noUnityPackage", "この商品には、送れる unitypackage がありません。");
        }

        if (request.Entry is { Length: > 0 } entry)
        {
            return packages.FirstOrDefault(package =>
                    string.Equals(package.EntryPath, entry, StringComparison.OrdinalIgnoreCase)
                    && (request.ZipHash is null || string.Equals(package.ZipHash, request.ZipHash, StringComparison.OrdinalIgnoreCase)))
                ?? throw QueryException.NotFound("unityPackageNotFound", $"この商品に「{entry}」はありません。");
        }

        return packages.Count == 1
            ? packages[0]
            : throw QueryException.Invalid("unityPackageAmbiguous", $"この商品には unitypackage が {packages.Count} つあります。entry で送る物を指定してください。");
    }

    /// <summary>
    /// 送り先：プロジェクトの指定があればそのフォルダを開いているエディタ、無ければ紐付けたプロジェクトを開いているエディタ（改変から送るとき）、
    /// それも無ければ1つだけ開いているときだけそれ。
    /// </summary>
    private (OpenUnityEditor Editor, string Project) PickEditor(string? project, string? linked)
    {
        var editors = _openEditors();
        if (editors.Count == 0)
        {
            throw new QueryException(409, "unityNotOpen", "Unity が開いていません。プロジェクトを開いてから、もう一度送ってください。");
        }

        if (project is { Length: > 0 })
        {
            return EditorOf(editors, project)
                ?? throw new QueryException(409, "unityProjectNotOpen", "そのプロジェクトを開いている Unity が見つかりません。/v1/unity/editors で開いているプロジェクトを確かめてください。");
        }

        if (linked is { Length: > 0 } && EditorOf(editors, linked) is { } found)
        {
            return found;
        }

        if (editors.Count > 1)
        {
            throw new QueryException(409, "unityTargetAmbiguous", $"Unity が {editors.Count} つ開いています。project で送り先を指定してください。");
        }

        return (editors[0], _pathOf(editors[0]) ?? editors[0].ProjectName ?? string.Empty);
    }

    private (OpenUnityEditor Editor, string Project)? EditorOf(IReadOnlyList<OpenUnityEditor> editors, string project)
    {
        var wanted = Path.TrimEndingDirectorySeparator(project);
        foreach (var editor in editors)
        {
            if (_pathOf(editor) is { } path && string.Equals(Path.TrimEndingDirectorySeparator(path), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return (editor, path);
            }
        }

        return null;
    }

    /// <summary>送り終えたら（想定の外の例外でも）「送っている途中」を外す。外さないと、以後の送りが全部 409 になる。</summary>
    private async Task<UnitySendStatus> SendGuardedAsync(
        Send send, OpenUnityEditor editor, IReadOnlyList<Queued> queue, IReadOnlyList<(string ModificationId, UnityChoiceRecord Record)> records)
    {
        try
        {
            return await SendAsync(send, editor, queue, records);
        }
        finally
        {
            lock (_startLock)
            {
                _sending = false;
            }
        }
    }

    /// <summary>
    /// 送る部品の1件の結果を、API の状態の値にする。**状態の値を決めるのはここだけ**（値の一覧は <see cref="UnitySendStates"/>）。
    /// エディタが閉じた・待ちきれなかったは取り込みの窓を出した後でも入ったとは言えないので、取り込んだにしない（API-GAPS 4-1）
    /// </summary>
    internal static string StateOf(UnityQueueOutcome? outcome) => outcome switch
    {
        null => UnitySendStates.Failed,
        _ when UnityImportQueue.IsEditorClosed(outcome.Problem) => UnitySendStates.EditorClosed,
        { Opened: true, Cancelled: true } => UnitySendStates.Cancelled,
        { Opened: true, AlreadyPresent: true } => UnitySendStates.AlreadyPresent,
        { Opened: true, Problem: null } => UnitySendStates.Imported,
        _ when UnityImportQueue.IsStopped(outcome.Problem) => UnitySendStates.Stopped,
        _ => UnitySendStates.Failed,
    };

    private static string? ProblemOf(UnityQueueOutcome? outcome, string state)
        => UnitySendStates.IsTaken(state) || state == UnitySendStates.Cancelled ? null : outcome?.Problem ?? "理由が分かりませんでした。";

    /// <summary>1件の結果を状態に書き、次の1件を「送っている」にする。</summary>
    private void NoteOutcome(Send send, IReadOnlyList<Queued> queue, int index, UnityQueueOutcome outcome)
    {
        if (index < 0 || index >= queue.Count)
        {
            return;
        }

        lock (_startLock)
        {
            var steps = send.Status.Steps.ToArray();
            var state = StateOf(outcome);
            steps[queue[index].Step] = steps[queue[index].Step] with { State = state, Problem = ProblemOf(outcome, state) };
            if (index + 1 < queue.Count && steps[queue[index + 1].Step].State == UnitySendStates.Queued)
            {
                steps[queue[index + 1].Step] = steps[queue[index + 1].Step] with { State = UnitySendStates.Sending };
            }

            send.Status = send.Status with { Steps = steps };
        }
    }

    private async Task<UnitySendStatus> SendAsync(
        Send send, OpenUnityEditor editor, IReadOnlyList<Queued> queue, IReadOnlyList<(string ModificationId, UnityChoiceRecord Record)> records)
    {
        var started = send.Status;
        UnitySendStatus finished;
        try
        {
            // **記録してから送る**（画面の「使ったものを順にUnityへ送る」と同じ。送るのは取り込み画面を待つので長く、途中で閉じられることもある）。
            // 後ろの行から置き換えると、2行に増えても前の行の位置がずれない。記録できなくても送る（画面と同じ）
            foreach (var (modificationId, record) in records.OrderByDescending(record => record.Record.Index))
            {
                if (await services.Commands.ExecuteAsync(
                        new Core.Commands.UiCommand.RecordModificationMemberFiles(modificationId, record.Member, record.Members),
                        origin: Core.Commands.CommandOrigin.Api)
                    is not Core.Commands.CommandResult.Failed)
                {
                    services.NoteApiWrite(new ApiWrite("modifications", [modificationId]));
                }
            }

            var outcomes = await _run(
                editor.ProcessId, queue.Select(entry => entry.Package).ToList(), (index, outcome) => NoteOutcome(send, queue, index, outcome), send.Stop.Token);

            // 知らせの来なかった分（送る部品が知らせずに返した・作り物）も、返った結果で埋める
            lock (_startLock)
            {
                var steps = send.Status.Steps.ToArray();
                for (var index = 0; index < queue.Count; index++)
                {
                    var outcome = index < outcomes.Count ? outcomes[index] : null;
                    var state = StateOf(outcome);
                    steps[queue[index].Step] = steps[queue[index].Step] with { State = state, Problem = ProblemOf(outcome, state) };
                }

                finished = Ended(send.Status with { Steps = steps });
            }

            // 「使った」の足跡。Unity へ送ったことが一番強い証拠（ユーザ判断）。取り込み画面で取り消した物・閉じた物は入っていないので付けない
            // 命令の口を API の起点で通す（人の操作として扱わない・ログに API と残す。点検37）
            foreach (var itemId in queue.Where(entry => UnitySendStates.IsTaken(finished.Steps[entry.Step].State)).Select(entry => entry.ItemId).Distinct())
            {
                if (await services.Commands.ExecuteAsync(
                        new Core.Commands.UiCommand.TouchRecent(itemId, RecentKind.Used, DateTimeOffset.Now), origin: Core.Commands.CommandOrigin.Api)
                    is not Core.Commands.CommandResult.Failed)
                {
                    services.NoteApiWrite(new ApiWrite("recent", [itemId]));
                }
            }

            // 送れなかった理由が「zip が無い」なら記録へ（見つからなくなった日時）。理由の文では見分けず、そのファイルを見直す（画面と同じ）
            foreach (var entry in queue.Where(entry => finished.Steps[entry.Step].State == UnitySendStates.Failed && entry.Item is not null))
            {
                var files = entry.Item!.Local.LocalFiles
                    .Where(file => string.Equals(file.Hash, entry.Package.ZipHash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (await ViewModels.FilePresenceNotes.LookAndNoteAsync(services, entry.Item, files, Core.Commands.CommandOrigin.Api) is not null)
                {
                    services.NoteApiWrite(new ApiWrite("items", [entry.ItemId]));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API の Unity へ送る", exception);
            lock (_startLock)
            {
                var problem = Core.Services.FailureText.Cause(exception);
                var steps = send.Status.Steps
                    .Select(step => UnitySendStates.IsEnded(step.State) ? step : step with { State = UnitySendStates.Failed, Problem = problem })
                    .ToList();
                finished = Ended(send.Status with { Steps = steps }) with { State = UnitySendStates.Failed, Problem = problem };
            }
        }

        // 外から使う API の書き込みと同じく、何をしたかをログに1行残す（Unity へ送るは UiCommand を通らないので、ここで書く）。
        // 結果の種類だけ。商品 ID・包みの名前・プロジェクトの場所は書かない（問い合わせの中身はログに残さない決まり）
        Core.Diagnostics.AppLog.Note("外から使う API", started.ModificationId is null
            ? $"Unity へ送る → {finished.State}"
            : $"改変の使ったものを Unity へ送る（{queue.Count} 件）→ {finished.State}");

        // 終わりを見せるのは、足跡と印を書き終えてから（終わったと見た外の道具が、すぐ検索の recent で引けるように）
        lock (_startLock)
        {
            send.Status = finished;
        }

        return finished;
    }

    /// <summary>
    /// 送り全体を終わりにする。全体の状態は1件ずつから決め（<see cref="UnitySendStates.Overall"/>）、理由は1件の送りならその1件の物、
    /// まとめて送ったなら全体を決めた1件の物
    /// </summary>
    private static UnitySendStatus Ended(UnitySendStatus status)
    {
        var state = UnitySendStates.Overall(status.Steps.Select(step => step.State));
        var problem = status.Steps.FirstOrDefault(step => step.State == state && step.Problem is not null)?.Problem;
        return status with { State = state, Problem = problem, EndedAt = DateTimeOffset.Now };
    }
}
