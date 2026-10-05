using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>列に積んだ「このIDで登録」1件。保存する分（<see cref="Record"/>）と、走っている間だけの進み具合を持つ。</summary>
public sealed class RegistrationJob
{
    public required QueuedRegistration Record { get; init; }

    public string ItemId => Record.ItemId;

    public string ItemName => Record.ItemName;

    public IReadOnlyList<string> FileHashes => Record.FileHashes;

    /// <summary>一覧でチェックした物をまとめて積んだか。終わったときに次の行へ送るかを決める（1件ずつ片付ける流れのときだけ送る）。</summary>
    public bool FromChecked { get; init; }

    public bool IsRunning { get; internal set; }

    /// <summary>済んだファイルの数（束の n / m 件）。</summary>
    public int Done { get; internal set; }

    /// <summary>今のファイルの BOOTH への問い合わせの残り。分からない・要らないときは null（Core が流す数。メモ34）。</summary>
    public int? RequestsLeft { get; internal set; }

    /// <summary>今のファイルで Core から残りの数を受けた回数。始めの2回は画像の枚数を知る前の数（後述 <see cref="RegistrationQueue"/>）。</summary>
    internal int Reports { get; set; }
}

/// <summary>1件の登録が終わった知らせ。画面が受け止めたら <see cref="Handled"/> を立てる（立たなければ主画面の帯で知らせる）。</summary>
public sealed class RegistrationOutcome(RegistrationJob job, IReadOnlyList<string> settledHashes, string? failure)
{
    public RegistrationJob Job { get; } = job;

    public IReadOnlyList<string> SettledHashes { get; } = settledHashes;

    /// <summary>1つでも登録できなかったときの理由（最初の物）。</summary>
    public string? Failure { get; } = failure;

    public bool Handled { get; set; }
}

/// <summary>
/// 未確定の画面の「このIDで登録」の順番待ち（ユーザ判断 2026-10-05 メモ60：1-A 主画面が持つ・2-A 待っている物だけやめられる・4 記録して次の起動で続ける）。
///
/// **BOOTH の門だけでは1件ずつ順に終わらない。**登録の問い合わせはどれも「人が押した」段に並び、同じ段の中は着いた順なので、
/// 2件を並べると A の JSON → B の JSON → A のページ… と交互に進み、どちらも遅く終わっていた（docs/research/proposals-2026-10-05.md §1）。
/// 門の手前にこの列を置き、1件ずつ今までと同じ <see cref="UiCommand.AssignItemId"/> を流す（入口で「人が押した」優先度が張られるのも同じ）。
///
/// **主画面が持つ**：未確定の画面は開くたびに作り直す（フォルダビューの右にも別に作られる）ので、画面に持たせると離れたところで止まる。
/// 画面の行とはハッシュで結ぶ（行は読み直しで作り直される）。
/// 列は <c>registration-queue.json</c> にも書き、閉じても次の起動で同じ順に続ける。書くのは積んだ・済んだ・やめたの3つだけ
/// （進み具合は書かない。再開すると済んだファイルは未確定から消えていて、命令が失敗として返す）。
/// </summary>
public sealed class RegistrationQueue : ViewModelBase
{
    private readonly CommandHandler _commands;
    private readonly Func<int> _intervalMs;
    private readonly Func<string, Task> _settled;
    private readonly List<RegistrationJob> _jobs = [];

    /// <summary>登録できなかったファイルの理由。行の札と吹き出しに出す。積み直すか、開き直して行が消えるまで覚える（記録には書かない）。</summary>
    private readonly Dictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

    private bool _running;

    /// <summary>記録へ書く順を守るための鎖。積んだ書き込みより先に「済んだ」が当たると、済んだ物が記録に残り、次の起動で空振りする。</summary>
    private Task _writes = Task.CompletedTask;

    /// <param name="commands">登録と記録の書き込みを通す口（<see cref="UiCommand"/> 1本）。</param>
    /// <param name="intervalMs">問い合わせの間隔（設定の値）。見込みの時間に使う。</param>
    /// <param name="settled">登録できた商品を、画面に知らせる前に主画面の控えと検索の写しへ足す。</param>
    public RegistrationQueue(CommandHandler commands, Func<int> intervalMs, Func<string, Task> settled)
    {
        _commands = commands;
        _intervalMs = intervalMs;
        _settled = settled;
    }

    /// <summary>列が変わった（積んだ・始まった・進んだ・終わった・やめた）。画面は行の札と右の欄を出し直す。</summary>
    public event Action? Changed;

    /// <summary>1件が終わった。開いている未確定の画面が受け止める。</summary>
    public event Action<RegistrationOutcome>? Finished;

    /// <summary>どの画面も受け止めなかった終わり（未確定の画面を開いていない）。主画面が帯で知らせる。</summary>
    public event Action<RegistrationOutcome>? Unhandled;

    /// <summary>列の全部（先頭が走っている物）。</summary>
    public IReadOnlyList<RegistrationJob> Jobs => _jobs;

    public bool HasJobs => _jobs.Count > 0;

    public RegistrationJob? JobFor(string hash)
        => _jobs.FirstOrDefault(job => job.FileHashes.Contains(hash, StringComparer.OrdinalIgnoreCase));

    public string? FailureFor(string hash) => _failures.TryGetValue(hash, out var failure) ? failure : null;

    /// <summary>待っている中の何番目か（1から）。走っている物と列に無い物は 0。</summary>
    public int WaitingPosition(RegistrationJob job)
        => job.IsRunning ? 0 : _jobs.Where(other => !other.IsRunning).ToList().IndexOf(job) + 1;

    /// <summary>この登録の見込みの時間（秒）。見込みの数が分からない（記録から続けた）なら null。</summary>
    public double? SecondsFor(RegistrationJob job)
        => RequestsOf(job, _jobs.IndexOf(job)) is { } requests ? requests * _intervalMs() / 1000.0 : null;

    /// <summary>
    /// この登録が始まるまでの見込み（秒）。前に並んだ物の残りの数 × 間隔。
    /// 自動検索の問い合わせが割り込むと延びるので、画面では「約」を付ける（今の「BOOTHへあと n 件・約 m 分」と同じ数え方）。
    /// </summary>
    public double SecondsUntilStart(RegistrationJob job)
    {
        var index = _jobs.IndexOf(job);
        var requests = 0;
        for (var i = 0; i < index; i++)
        {
            requests += RemainingOf(_jobs[i], i);
        }

        return requests * _intervalMs() / 1000.0;
    }

    /// <summary>列の全部が終わるまでの見込み（秒）。閉じるときの確認に出す。</summary>
    public double SecondsLeftAll()
    {
        var requests = 0;
        for (var i = 0; i < _jobs.Count; i++)
        {
            requests += RemainingOf(_jobs[i], i);
        }

        return requests * _intervalMs() / 1000.0;
    }

    /// <summary>
    /// 見込みの問い合わせの数。**前に同じ商品の登録が並んでいれば 0**——その登録が商品を作った後は、手元の商品に足すだけで BOOTH へ行かない。
    /// </summary>
    private int? RequestsOf(RegistrationJob job, int index)
        => _jobs.Take(Math.Max(index, 0)).Any(other => other.ItemId == job.ItemId) ? 0 : job.Record.EstimatedRequests;

    /// <summary>
    /// 残りの数。走っている物は Core が流す残り（2件目からは手元の商品に足すだけなので 0）。
    /// 見込みが分からない物は、少なくとも要る2つ（商品JSONと商品ページ）で数える。
    /// </summary>
    /// <remarks>
    /// Core は商品JSONを読むまで画像の枚数を知らないので、始めの2回は「JSON と商品ページの2つ」「ページの1つ」と流す（メモ34）。
    /// 押したときに確かめた枚数を知っているので、その2回の間は残りに画像とアイコンの見込みを足す
    /// （足さないと、40枚の商品の前で「開始まで1分以内」と出て、実際は2分待つ）。
    /// </remarks>
    private int RemainingOf(RegistrationJob job, int index)
    {
        var estimate = RequestsOf(job, index);
        if (!job.IsRunning)
        {
            return estimate ?? MinimumRequests;
        }

        if (job.RequestsLeft is { } left)
        {
            return job.Reports <= 2 && estimate is { } known ? left + Math.Max(known - MinimumRequests, 0) : left;
        }

        return job.Done == 0 ? estimate ?? MinimumRequests : 0;
    }

    private const int MinimumRequests = 2;

    /// <summary>積む。走っていなければ始める。</summary>
    public void Enqueue(RegistrationJob job)
    {
        foreach (var hash in job.FileHashes)
        {
            _failures.Remove(hash);
        }

        _jobs.Add(job);
        Persist(list => [.. list, job.Record]);
        Changed?.Invoke();
        Start();
    }

    /// <summary>待っている物をやめる。**走っている物は止めない**（ユーザ判断 2026-10-05「2-A」）。やめたら true。</summary>
    public bool Cancel(RegistrationJob job)
    {
        if (job.IsRunning || !_jobs.Remove(job))
        {
            return false;
        }

        Persist(list => Without(list, job.Record));
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 前の起動で残った列を、同じ順で続ける（ユーザ判断 2026-10-05「4」）。起動時の裏の作業とは別の列で、優先度は押した登録と同じ。
    /// 記録は既に書いてあるので書き足さない。ファイルが無い行は捨てる（手で直した記録）。
    /// </summary>
    public void Resume(IReadOnlyList<QueuedRegistration> saved)
    {
        foreach (var record in saved.Where(record => record.ItemId.Length > 0 && record.FileHashes.Count > 0))
        {
            _jobs.Add(new RegistrationJob { Record = record });
        }

        Changed?.Invoke();
        if (_jobs.Count > 0)
        {
            Start();
        }
    }

    private void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RunAsync().Forget();
    }

    private async Task RunAsync()
    {
        try
        {
            while (_jobs.Count > 0)
            {
                await RunOneAsync(_jobs[0]);
            }
        }
        finally
        {
            _running = false;
        }
    }

    private async Task RunOneAsync(RegistrationJob job)
    {
        job.IsRunning = true;
        Changed?.Invoke();

        var settled = new List<string>();
        string? failure = null;
        var progress = new InlineProgress(left => RunOnUiThread(() =>
        {
            // 終わった後に遅れて届いた分で、消した残りが戻らないようにする
            if (job.IsRunning)
            {
                job.RequestsLeft = left;
                job.Reports++;
                Changed?.Invoke();
            }
        }));

        foreach (var hash in job.FileHashes)
        {
            try
            {
                var result = await _commands.ExecuteAsync(new UiCommand.AssignItemId(hash, job.ItemId, progress));
                if (result is CommandResult.Failed failed)
                {
                    failure ??= failed.Message;
                }
                else
                {
                    settled.Add(hash);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 1件が落ちても列を止めない（止めると後ろに並んだ物が二度と始まらない）
                Core.Diagnostics.AppLog.Error("登録の列", exception);
                failure ??= "登録できませんでした。" + Core.Services.FailureText.Cause(exception);
            }

            job.Done++;
            job.RequestsLeft = null;
            job.Reports = 0;
            Changed?.Invoke();
        }

        job.IsRunning = false;
        _jobs.Remove(job);
        foreach (var hash in job.FileHashes.Except(settled, StringComparer.OrdinalIgnoreCase))
        {
            if (failure is not null)
            {
                _failures[hash] = failure;
            }
        }

        Persist(list => Without(list, job.Record));

        // 画面が行を外す前に、主画面の控えと検索の写しへ入れる（フォルダビューの右は、残りの数が減ったのを見て写しから木を組み直す）
        if (settled.Count > 0)
        {
            await _settled(job.ItemId);
        }

        Changed?.Invoke();
        var outcome = new RegistrationOutcome(job, settled, failure);
        Finished?.Invoke(outcome);
        if (!outcome.Handled)
        {
            Unhandled?.Invoke(outcome);
        }
    }

    private static List<QueuedRegistration> Without(List<QueuedRegistration> list, QueuedRegistration record)
    {
        // 同じ物を2回積むことは無い（積んだ行は押せなくなる）ので、最初に合う1件だけを外す
        var index = list.FindIndex(other => other.ItemId == record.ItemId
            && other.FileHashes.SequenceEqual(record.FileHashes, StringComparer.OrdinalIgnoreCase));
        if (index >= 0)
        {
            list.RemoveAt(index);
        }

        return list;
    }

    private void Persist(Func<List<QueuedRegistration>, List<QueuedRegistration>> change)
    {
        var previous = _writes;
        _writes = WriteAfterAsync(previous, change);
        _writes.Forget();
    }

    private async Task WriteAfterAsync(Task previous, Func<List<QueuedRegistration>, List<QueuedRegistration>> change)
    {
        await previous;
        try
        {
            await _commands.ExecuteAsync(new UiCommand.ChangeRegistrationQueue(change));
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 書けなくても登録は進める。次の起動で続けられないだけ（記録が読めなければ、続ける物が無いのと同じ）
            Core.Diagnostics.AppLog.Error("登録の列の記録", exception);
        }
    }

    /// <summary>記録へ書き終わるのを待つ（閉じる前・試験）。</summary>
    public Task WhenWrittenAsync() => _writes;

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    // ---- 画面の文（計算で決まるので試験で確かめる） ----

    /// <summary>待っている行の右の欄（ユーザの例「別の商品を登録中。開始まで約 n 分・この商品は約 m 分」を ui-wording で整えた）。</summary>
    internal static string WaitingText(double secondsUntilStart, double? ownSeconds)
        => ownSeconds is { } own
            ? $"ほかの商品を登録しています。開始まで{ImportViewModel.Duration(secondsUntilStart)}、この商品は{ImportViewModel.Duration(own)}です。"
            : $"ほかの商品を登録しています。開始まで{ImportViewModel.Duration(secondsUntilStart)}です。";

    /// <summary>行の札。待っている物は順番（次に始まる物が1番目）、走っている物は「登録中」。</summary>
    internal static string BadgeText(int waitingPosition)
        => waitingPosition <= 0 ? "登録中" : $"登録待ち {waitingPosition}番目";

    /// <summary>札の吹き出し。どの商品として登録するか。名前が空（読めなかった記録）なら出さない。</summary>
    internal static string BadgeTip(RegistrationJob job)
        => string.IsNullOrWhiteSpace(job.ItemName)
            ? string.Empty
            : job.IsRunning ? $"「{job.ItemName}」として登録しています。" : $"「{job.ItemName}」として登録します。";

    /// <summary>
    /// 閉じるときの確認。列が空なら null（聞かない）。
    /// ユーザの例「登録作業が続いています。終了予定まであとn分。閉じても次回起動時に再開されます」を ui-wording で整えた
    /// </summary>
    internal static (string Title, string Question, string Detail)? CloseConfirm(int jobs, double secondsLeft)
        => jobs <= 0
            ? null
            : ("登録が続いています",
                $"登録が終わっていない商品が {jobs} 件あります。閉じますか？",
                $"終わるまであと{ImportViewModel.Duration(secondsLeft)}です。閉じても、次に起動したときに続きから登録します。");
}
