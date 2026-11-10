using Chmonos.Core.Booth;

namespace Chmonos.Core.Resolution;

/// <summary>
/// 先回りで引く1件。<paramref name="Target"/> は未確定の画面が引く対象（元zip・展開物の根のフォルダ・ファイル）と同じ物で、
/// 控えの鍵になる。<paramref name="NewestSeenAt"/> は持ち主のうちいちばん新しく見つかった日時（新しい順に引くため）。
/// </summary>
public sealed record PrefetchTarget(string Target, IReadOnlyList<string> Files, DateTimeOffset NewestSeenAt);

/// <summary>先回りの1回がどう終わったか。</summary>
public enum PrefetchEnd
{
    /// <summary>引く物が無くなった。</summary>
    Finished,

    /// <summary>BOOTH に届かなかった（その回の残りは引かない。次の起動・設定を入れ直したときにまた始める）。</summary>
    Unreachable,

    /// <summary>設定が切られた。</summary>
    Disabled,
}

/// <summary>先回りの1回の結果（試験とログ用）。</summary>
public sealed record PrefetchRun(PrefetchEnd End, int Searched);

/// <summary>
/// 未確定の自動検索を、通信の門が空いているときに先回りして走らせ、結果を控える（ユーザ判断 2026-11-10・2026-11-11・plan 21）。
/// 友人たちは未確定のほとんど全部で自動検索を押していて、毎回1件ずつ分単位で待っていた。
///
/// <list type="bullet">
/// <item>**既定はオフ**で、設定でオンにしたときだけ走る（呼び手が始める・止める）。</item>
/// <item>中身は人が押す自動検索と同じ（<see cref="FallbackResolver.ProposeAsync"/>。新しい問い合わせの種類は作らない）。</item>
/// <item>優先度は梯子のいちばん下の⑧（<see cref="BoothPriority.Prefetch"/>）。門は問い合わせ1本ごとに取り直し、
///   間隔を待ち終えたときに誰かが並んでいれば譲る（<see cref="BoothClient"/>）。1本ずつ・1.5秒以上は門が守る。</item>
/// <item>**取り込みの間は止める**（<see cref="Pause"/>）。走っている検索は中断して結果を捨て、終わったら同じ対象から引き直す。
///   取り込みは①②の段が⑧より上なので門は譲るが、1ファイルで検索1本＋商品の JSON 数本を出すので、止めないと取り込みの間に少しずつ混ざる。</item>
/// <item>**新しく見つかった未確定から**引く（取り込んだばかりの物から片付ける人が多い）。</item>
/// <item>控えのある対象は引かない（最初に取った物を使い続ける）。BOOTH に届かなかったら、その回の残りは引かない。</item>
/// </list>
/// </summary>
public sealed class CandidatePrefetch(
    CandidateSearchCache cache,
    Func<CancellationToken, Task<IReadOnlyList<PrefetchTarget>>> plan,
    Func<PrefetchTarget, CancellationToken, Task<ResolutionProposal>> propose,
    Func<bool> enabled,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _sync = new();

    /// <summary>
    /// 止めているか。数えない：取り込みの係は始まり（Started）と終わり（Finished）を1回ずつ知らせるが、走っている取り込みに積んだときに
    /// 始まりが重なっても、終わりは1回。数えると止まったまま戻らない
    /// </summary>
    private bool _paused;

    /// <summary>止めたら取り消す合図。止めるたびに作り直す。</summary>
    private CancellationTokenSource _pauseSignal = new();

    /// <summary>止めている間、再開を待つ物。止めていなければ済んでいる。</summary>
    private TaskCompletionSource _resumed = Completed();

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _paused;
            }
        }
    }

    /// <summary>取り込みが始まった。走っている検索を中断し、<see cref="Resume"/> まで次を始めない。</summary>
    public void Pause()
    {
        CancellationTokenSource signal;
        lock (_sync)
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = _pauseSignal;
            _pauseSignal = new CancellationTokenSource();
        }

        // 捨てない：走っている検索がこの合図につないだ取り消しを、まだ持っていることがある
        signal.Cancel();
    }

    /// <summary>取り込みが終わった（中断を含む）。</summary>
    public void Resume()
    {
        TaskCompletionSource resumed;
        lock (_sync)
        {
            if (!_paused)
            {
                return;
            }

            _paused = false;
            resumed = _resumed;
        }

        resumed.TrySetResult();
    }

    /// <summary>引く物が無くなるまで引く。止めている間は待ち、再開したら一覧を組み直して続ける。</summary>
    /// <param name="progress">引いた数と、この回に引く予定の数（常設の1行に出す）。</param>
    public async Task<PrefetchRun> RunAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        // この回に引いた（引こうとした）対象。控えを書けなかった物（検索の間に片付いた・読めない応答で落ちた）を、組み直すたびに引き直さない
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searched = 0;

        while (true)
        {
            if (!enabled())
            {
                return new PrefetchRun(PrefetchEnd.Disabled, searched);
            }

            CancellationToken paused;
            Task resumed;
            lock (_sync)
            {
                paused = _pauseSignal.Token;
                resumed = _resumed.Task;
            }

            if (!resumed.IsCompleted)
            {
                await resumed.WaitAsync(cancellationToken);
                continue;
            }

            var targets = await plan(cancellationToken);

            // 未確定に残っていない持ち主を、控えから外す。決める入口が増えても、ここで突き合わせれば控えが残り続けない
            await Storage.StoreWriteGate.WaitAsync(cancellationToken);
            await cache.KeepOnlyAsync(
                targets.SelectMany(target => target.Files).ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);

            var known = cache.Targets();
            var queue = targets
                .Where(target => !known.Contains(target.Target) && !tried.Contains(target.Target))
                .OrderByDescending(target => target.NewestSeenAt)
                .ToList();
            if (queue.Count == 0)
            {
                return new PrefetchRun(PrefetchEnd.Finished, searched);
            }

            for (var index = 0; index < queue.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!enabled())
                {
                    return new PrefetchRun(PrefetchEnd.Disabled, searched);
                }

                if (paused.IsCancellationRequested)
                {
                    break;
                }

                var target = queue[index];

                // 先回りの間に人が同じ対象を引いて控えができていれば、そちらを使う
                if (cache.Find(target.Target) is not null)
                {
                    continue;
                }

                progress?.Report((index, queue.Count));

                ResolutionProposal proposal;
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, paused))
                {
                    try
                    {
                        using (BoothClient.Prioritize(BoothPriority.Prefetch))
                        {
                            proposal = await propose(target, linked.Token);
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // 取り込みが始まった。この対象は捨てて、再開したら組み直した一覧から引き直す
                        break;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // 1件の失敗（読めない zip など）で残りを止めない。この回はもう引かない
                        Diagnostics.AppLog.Error("未確定の自動検索の先回り", exception);
                        tried.Add(target.Target);
                        continue;
                    }
                }

                tried.Add(target.Target);
                if (proposal.BoothUnreachable)
                {
                    // 圏外の間に全件を叩かない（届かない失敗が続けば検索の中でも打ち切っている）。次の起動か、設定を入れ直したときにまた始める
                    Diagnostics.AppLog.Warn("未確定の自動検索の先回り", $"BOOTHに届かなかったので、残りは次の機会に回す（この回に {searched} 件を控えた）");
                    return new PrefetchRun(PrefetchEnd.Unreachable, searched);
                }

                // 裏の作業は UiCommand を通らないので、保存先を運ぶ間の門はここで待つ（起動時の裏の作業と同じ）
                await Storage.StoreWriteGate.WaitAsync(cancellationToken);
                if (await cache.AddIfMissingAsync(target.Target, target.Files, proposal, _clock.GetLocalNow(), cancellationToken))
                {
                    searched++;
                }

                progress?.Report((index + 1, queue.Count));
            }

            // 一巡した・止められた。どちらも一覧を組み直してから続ける（取り込みで未確定が増えている）
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult();
        return done;
    }
}
