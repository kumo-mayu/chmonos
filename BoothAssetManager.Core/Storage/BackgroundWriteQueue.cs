namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 画面を待たせたくない小さな書き込み（検索の履歴・「最近」の足跡）を、**画面のスレッドの外で、頼まれた順に1本ずつ**走らせる列。
///
/// **なぜ要るか：**これらの書き込みは非同期の形で書いてあるが、Core は <c>ConfigureAwait(false)</c> を使わないので、
/// 画面から呼ぶと錠が空いている限り最初の待ちまで同期で進み、続きも画面のスレッドへ戻ってくる。
/// 読む・一時ファイルを作る・ディスクへ書き出す（<c>Flush(flushToDisk: true)</c>）・置き換える、が全部画面のスレッドで走り、
/// 商品ページを開くたびに乗っていた（2026-09-30 に SSD で測って、押したときの処理が 11ms → 2ms、最初の1コマまでが約15ms 短くなった。
/// ディスクへの書き出しを待つ時間なので、遅いディスクではその分長く乗る。docs/research/item-page-open-2026-09-30.md）。
///
/// **<c>Task.Run</c> で投げるだけにしない理由：**プールへ投げた順に走り出すとは限らない。
/// 書き込みは錠の中で今の値に当てるので中身は壊れないが、同じ物への2回が入れ替わると、後から押した方が先に当たり、
/// 古い方が勝つ（足跡なら古い時刻が残り、履歴なら古い条件が先頭に来る）。1本の鎖につないで順を守る。
///
/// **閉じる前は <see cref="WhenIdleAsync"/> を待つ。**プールのスレッドは窓が閉じると切られるので、
/// 待たないと閉じる直前に開いた商品の足跡と履歴が落ちる。
/// </summary>
public sealed class BackgroundWriteQueue
{
    private readonly Lock _sync = new();

    /// <summary>最後につないだ作業の終わり。失敗しても完了として持つ（1つの失敗で後ろを止めない）。</summary>
    private Task _tail = Task.CompletedTask;

    /// <summary>
    /// 列の最後につなぐ。前の作業が終わってから、プールのスレッドで走る（呼んだスレッドでは走らない）。
    /// </summary>
    /// <returns>その作業の結果。失敗はここに返る（列は止まらない）。</returns>
    public Task<T> RunAsync<T>(Func<Task<T>> write)
    {
        lock (_sync)
        {
            // 続きをその場で走らせる指定（ExecuteSynchronously）は付けない。前の作業がもう終わっていても、
            // 呼んだスレッド＝画面のスレッドでは走らせず、必ずプールへ出す
            var run = _tail.ContinueWith(
                static (_, state) => ((Func<Task<T>>)state!)(),
                write,
                CancellationToken.None,
                TaskContinuationOptions.DenyChildAttach,
                TaskScheduler.Default).Unwrap();

            _tail = run.ContinueWith(
                static _ => { },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return run;
        }
    }

    /// <inheritdoc cref="RunAsync{T}(Func{Task{T}})"/>
    public Task RunAsync(Func<Task> write)
        => RunAsync(async () =>
        {
            await write();
            return true;
        });

    /// <summary>今つないである分が全部終わるまでの待ち。この後につないだ分は含まない。</summary>
    public Task WhenIdleAsync()
    {
        lock (_sync)
        {
            return _tail;
        }
    }
}
