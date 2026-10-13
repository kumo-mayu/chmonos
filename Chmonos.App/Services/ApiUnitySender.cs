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
/// - 送り終えるまで待たずに返る（取り込み画面で人が考える間、最大30分待つ）。終わりは <see cref="Last"/> と変化の知らせで伝える
/// </summary>
internal sealed class ApiUnitySender(
    AppServiceContainer services,
    Func<IReadOnlyList<OpenUnityEditor>>? openEditors = null,
    Func<OpenUnityEditor, string?>? pathOf = null,
    Func<int, IReadOnlyList<UnityPackageEntry>, CancellationToken, Task<IReadOnlyList<UnityQueueOutcome>>>? run = null,
    Func<bool>? isRunning = null) : IApiUnity
{
    private readonly Func<IReadOnlyList<OpenUnityEditor>> _openEditors = openEditors ?? UnityEditors.Open;
    private readonly Func<OpenUnityEditor, string?> _pathOf = pathOf ?? (editor => editor.ProjectPath ?? UnityEditors.PathOf(editor));
    private readonly Func<int, IReadOnlyList<UnityPackageEntry>, CancellationToken, Task<IReadOnlyList<UnityQueueOutcome>>> _run
        = run ?? ((processId, packages, cancellationToken) => UnityImportQueue.RunAsync(processId, packages, null, cancellationToken));
    private readonly Func<bool> _isRunning = isRunning ?? (() => UnityImportQueue.IsRunning);

    private volatile UnitySendStatus? _last;

    /// <summary>
    /// 送っている途中か（後始末まで含む）。確かめと予約を同じ錠の中で行う。前は確かめと予約が分かれていて、同時の頼みが両方 202 を受け、
    /// 前の送りの後始末の間に次が始まると、前の結果が新しい「最後の送り」を上書きした（点検37）
    /// </summary>
    private bool _sending;

    private readonly object _startLock = new();

    public UnitySendStatus? Last => _last;

    public IReadOnlyList<UnityEditorRef> OpenEditors()
        => _openEditors().Select(editor => new UnityEditorRef(editor.ProcessId, editor.ProjectName, _pathOf(editor))).ToList();

    public (UnitySendStatus Started, Task<UnitySendStatus> Finished) Start(ItemRecord item, UnitySendRequest request)
    {
        lock (_startLock)
        {
            // 連続送りの最中は混ぜない。Editor.log は全エディタが共有するので、終わりを取り違える（画面と同じ門）
            if (_sending || _isRunning())
            {
                throw new QueryException(409, "unityBusy", UnityImportQueue.BusyMessage);
            }

            var package = PickPackage(item, request);
            var (editor, project) = PickEditor(request.Project);

            var started = new UnitySendStatus(item.Id, package.Name, project, "sending", null, DateTimeOffset.Now, null);
            _sending = true;
            _last = started;
            return (started, Task.Run(() => SendGuardedAsync(item, editor, package, started)));
        }
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

    /// <summary>送り先：プロジェクトの指定があればそのフォルダを開いているエディタ、無ければ1つだけ開いているときだけそれ。</summary>
    private (OpenUnityEditor Editor, string Project) PickEditor(string? project)
    {
        var editors = _openEditors();
        if (editors.Count == 0)
        {
            throw new QueryException(409, "unityNotOpen", "Unity が開いていません。プロジェクトを開いてから、もう一度送ってください。");
        }

        if (project is { Length: > 0 })
        {
            var wanted = Path.TrimEndingDirectorySeparator(project);
            foreach (var editor in editors)
            {
                if (_pathOf(editor) is { } path && string.Equals(Path.TrimEndingDirectorySeparator(path), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return (editor, path);
                }
            }

            throw new QueryException(409, "unityProjectNotOpen", "そのプロジェクトを開いている Unity が見つかりません。/v1/unity/editors で開いているプロジェクトを確かめてください。");
        }

        if (editors.Count > 1)
        {
            throw new QueryException(409, "unityTargetAmbiguous", $"Unity が {editors.Count} つ開いています。project で送り先を指定してください。");
        }

        return (editors[0], _pathOf(editors[0]) ?? editors[0].ProjectName ?? string.Empty);
    }

    /// <summary>送り終えたら（想定の外の例外でも）「送っている途中」を外す。外さないと、以後の送りが全部 409 になる。</summary>
    private async Task<UnitySendStatus> SendGuardedAsync(ItemRecord item, OpenUnityEditor editor, UnityPackageEntry package, UnitySendStatus started)
    {
        try
        {
            return await SendAsync(item, editor, package, started);
        }
        finally
        {
            lock (_startLock)
            {
                _sending = false;
            }
        }
    }

    private async Task<UnitySendStatus> SendAsync(ItemRecord item, OpenUnityEditor editor, UnityPackageEntry package, UnitySendStatus started)
    {
        UnitySendStatus finished;
        try
        {
            var outcome = (await _run(editor.ProcessId, [package], CancellationToken.None)).FirstOrDefault();
            finished = started with
            {
                State = outcome switch
                {
                    null => "failed",
                    { Opened: true, Cancelled: true } => "cancelled",
                    { Opened: true, AlreadyPresent: true } => "alreadyPresent",
                    { Opened: true } => "imported",
                    _ when UnityImportQueue.IsStopped(outcome.Problem) => "stopped",
                    _ => "failed",
                },
                Problem = outcome is { Opened: true } ? null : outcome?.Problem ?? "理由が分かりませんでした。",
                EndedAt = DateTimeOffset.Now,
            };

            // 「使った」の足跡。Unity へ送ったことが一番強い証拠（ユーザ判断）。取り込み画面で取り消した物は入っていないので付けない（画面と同じ）
            // 命令の口を API の起点で通す（人の操作として扱わない・ログに API と残す。点検37）
            if (finished.State is "imported" or "alreadyPresent"
                && await services.Commands.ExecuteAsync(
                    new Core.Commands.UiCommand.TouchRecent(item.Id, RecentKind.Used, DateTimeOffset.Now), origin: Core.Commands.CommandOrigin.Api)
                    is not Core.Commands.CommandResult.Failed)
            {
                services.NoteApiWrite(new ApiWrite("recent", [item.Id]));
            }

            // 送れなかった理由が「zip が無い」なら記録へ（見つからなくなった日時）。理由の文では見分けず、そのファイルを見直す（画面と同じ）
            if (finished.State == "failed")
            {
                var files = item.Local.LocalFiles
                    .Where(file => string.Equals(file.Hash, package.ZipHash, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (await ViewModels.FilePresenceNotes.LookAndNoteAsync(services, item, files, Core.Commands.CommandOrigin.Api) is not null)
                {
                    services.NoteApiWrite(new ApiWrite("items", [item.Id]));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API の Unity へ送る", exception);
            finished = started with { State = "failed", Problem = Core.Services.FailureText.Cause(exception), EndedAt = DateTimeOffset.Now };
        }

        // 外から使う API の書き込みと同じく、何をしたかをログに1行残す（Unity へ送るは UiCommand を通らないので、ここで書く）。
        // 結果の種類だけ。商品 ID・包みの名前・プロジェクトの場所は書かない（問い合わせの中身はログに残さない決まり）
        Core.Diagnostics.AppLog.Note("外から使う API", $"Unity へ送る → {finished.State}");
        lock (_startLock)
        {
            // この送りが今も「最後の送り」のときだけ結果で置き換える
            if (ReferenceEquals(_last, started))
            {
                _last = finished;
            }
        }

        return finished;
    }
}
