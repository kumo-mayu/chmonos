using System.Runtime.ExceptionServices;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 走っている間に来た依頼を**1回にまとめ、取りこぼさずに**もう一度走らせる。
///
/// 対応アバターの検出の依頼（手で紐付けるたびに来る）に使う。検出は毎回ライブラリ全体を見るので、
/// 10回頼まれても最後の1回と結果は同じ（N4）。ただし**走り出した後に来た依頼は、その回に間に合っていない**ので、
/// 必ずもう1回走らせる必要がある。
///
/// 前の作り（「走っているか」の錠と「もう一度」の印）には穴が3つあった。
/// ①走り終えて印を見た後・錠を放す前に来た依頼は、錠が取れないので印だけ立てて帰り、誰にも拾われなかった。
/// ②走っている回が例外で抜けると、その間に立った印ごと捨てられた。
/// ③走っている側が**中断**で抜けると、その間に任されていた依頼を誰も拾わなかった（中断したのは走っていた人の都合で、
///   任せた人は走ってほしいままなのに）。
/// ここでは、依頼は必ず印を立ててから錠を試し、走る側は錠を放した後にも印を見直す（①）。
/// 例外は覚えておいて印の続きを走らせ、最後に投げる（②。呼ぶ側はログに残す）。
/// 中断で抜けるときに任された依頼が残っていれば、任せた人の中断の合図で裏に続きを走らせる（③）。
/// </summary>
public sealed class CoalescedRun
{
    private readonly Func<CancellationToken, Task> _run;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly object _sync = new();

    /// <summary>まだ走らせていない依頼があるか（1＝ある）。</summary>
    private int _requested;

    /// <summary>走っている人に任せた依頼の数（増えるだけ。走る側は取った時の値と比べる）。</summary>
    private long _handedOver;

    /// <summary>最後に任せた人の中断の合図。中断で抜けた人の続きは、これで走らせる。</summary>
    private CancellationToken _handedOverToken;

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
        ExceptionDispatchInfo? canceled = null;

        while (canceled is null && Volatile.Read(ref _requested) == 1)
        {
            if (!await _running.WaitAsync(0, cancellationToken))
            {
                // 走っている人がいる。その人は錠を放した後にも印を見直す（中断で抜けても裏に続きを回す）ので、この依頼は必ず拾われる
                lock (_sync)
                {
                    _handedOver++;
                    _handedOverToken = cancellationToken;
                }

                break;
            }

            long handedOverAtStart;
            lock (_sync)
            {
                handedOverAtStart = _handedOver;
            }

            try
            {
                while (Interlocked.Exchange(ref _requested, 0) == 1)
                {
                    try
                    {
                        await _run(cancellationToken);
                    }
                    catch (OperationCanceledException exception)
                    {
                        canceled = ExceptionDispatchInfo.Capture(exception);
                        break;
                    }
                    catch (Exception exception)
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

            if (canceled is not null)
            {
                HandOverAfterCancel(handedOverAtStart);
            }
        }

        canceled?.Throw();
        failure?.Throw();
    }

    /// <summary>
    /// 中断で抜けた。**自分が走っている間に任された依頼が残っていれば、裏で続きを走らせる。**
    ///
    /// 任された依頼は、中断した回に食われたかもしれないし、まだ印として残っているかもしれない。
    /// どちらか見分けないのは、走らせ過ぎても結果は同じ（全体を見直すだけ）で、落とすと手で紐付けた分の検出が
    /// 次の依頼まで走らないため。
    /// </summary>
    private void HandOverAfterCancel(long handedOverAtStart)
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_handedOver == handedOverAtStart && Volatile.Read(ref _requested) == 0)
            {
                return;
            }

            token = _handedOverToken;
        }

        Interlocked.Exchange(ref _requested, 1);
        _ = Task.Run(async () =>
        {
            try
            {
                await RequestAsync(token);
            }
            catch (OperationCanceledException)
            {
                // 任せた人も中断していた
            }
            catch (Exception exception)
            {
                Diagnostics.AppLog.Error("まとめた依頼の続き", exception);
            }
        });
    }
}
