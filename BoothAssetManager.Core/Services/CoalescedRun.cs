using System.Runtime.ExceptionServices;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 走っている間に来た依頼を**1回にまとめ、取りこぼさずに**もう一度走らせる。
///
/// 対応アバターの検出の依頼（手で紐付けるたびに来る）に使う。検出は毎回ライブラリ全体を見るので、
/// 10回頼まれても最後の1回と結果は同じ（N4）。ただし**走り出した後に来た依頼は、その回に間に合っていない**ので、
/// 必ずもう1回走らせる必要がある。
///
/// 前の作り（「走っているか」の錠と「もう一度」の印）には穴が2つあった。
/// ①走り終えて印を見た後・錠を放す前に来た依頼は、錠が取れないので印だけ立てて帰り、誰にも拾われなかった。
/// ②走っている回が例外で抜けると、その間に立った印ごと捨てられた。
/// ここでは、依頼は必ず印を立ててから錠を試し、走る側は錠を放した後にも印を見直す（①）。
/// 例外は覚えておいて印の続きを走らせ、最後に投げる（②。呼ぶ側はログに残す）。
/// </summary>
public sealed class CoalescedRun
{
    private readonly Func<CancellationToken, Task> _run;
    private readonly SemaphoreSlim _running = new(1, 1);

    /// <summary>まだ走らせていない依頼があるか（1＝ある）。</summary>
    private int _requested;

    public CoalescedRun(Func<CancellationToken, Task> run)
    {
        _run = run;
    }

    /// <summary>
    /// 依頼する。誰も走っていなければここで走り、走っている人がいれば任せてすぐ返る
    /// （任せた依頼はその人が、今の回の後にもう1回走らせる）。
    /// </summary>
    public async Task RequestAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _requested, 1);

        ExceptionDispatchInfo? failure = null;

        while (Volatile.Read(ref _requested) == 1)
        {
            if (!await _running.WaitAsync(0, cancellationToken))
            {
                // 走っている人がいる。その人は錠を放した後にも印を見直すので、この依頼は必ず拾われる
                break;
            }

            try
            {
                while (Interlocked.Exchange(ref _requested, 0) == 1)
                {
                    try
                    {
                        await _run(cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // 1回の失敗で、その間に来た依頼まで捨てない。続きを走らせてから投げる
                        failure = ExceptionDispatchInfo.Capture(exception);
                    }
                }
            }
            finally
            {
                _running.Release();
            }
        }

        failure?.Throw();
    }
}
