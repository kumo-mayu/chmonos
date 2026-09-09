namespace BoothAssetManager.Core.Booth;

/// <summary>
/// 一度に1つだけ通す門。**空いたときは、待っている中でいちばん急ぐものを通す。**
///
/// <see cref="SemaphoreSlim"/> は待った順に通すので、
/// 100商品の取り込みが並んでいる後ろに人の操作が着くと最後まで待たされる。
/// ここでは待っている人を優先度ごとに分けて持ち、解放のたびに上から選ぶ。
///
/// 同じ優先度の中では**着いた順**。そうしないと同じ段の中で順番が入れ替わり、
/// 「①は商品を古い順に」といった段ごとの約束が崩れる。
/// </summary>
public sealed class PriorityGate
{
    private readonly object _sync = new();

    /// <summary>優先度ごとの待ち行列。キーは <see cref="BoothPriority"/> の値で、小さいほど先。</summary>
    private readonly SortedDictionary<int, Queue<TaskCompletionSource>> _waiting = [];

    private bool _busy;

    /// <summary>
    /// 順番待ちの人数。中断されたものも、通す番が来るまでは数に入っている。
    /// 「今どれだけ溜まっているか」を見るためのもので、判断には使わない。
    /// </summary>
    public int WaitingCount
    {
        get
        {
            lock (_sync)
            {
                return _waiting.Values.Sum(queue => queue.Count);
            }
        }
    }

    /// <summary>
    /// 今すぐ通れるか。通れなければ順番待ちに入る。
    ///
    /// 中断の登録を**並ぶ前に**済ませているのは、並んでから登録すると
    /// その隙間に来た中断を取りこぼすため。
    /// 一方、門が空いているかの判定と行列への追加は**同じ錠の中**でなければならない。
    /// 分けると、判定の後・追加の前に解放が起きたとき、
    /// 誰も起こしに来ない行列に並んだまま止まる。
    /// </summary>
    public async Task EnterAsync(BoothPriority priority, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            waiter);

        lock (_sync)
        {
            if (!_busy)
            {
                _busy = true;
                return;
            }

            if (!_waiting.TryGetValue((int)priority, out var queue))
            {
                queue = new Queue<TaskCompletionSource>();
                _waiting[(int)priority] = queue;
            }

            queue.Enqueue(waiter);
        }

        await waiter.Task;
    }

    /// <summary>
    /// 通り終わったら必ず呼ぶ。次に急ぐものを通す。
    ///
    /// 中断された待ち手は飛ばす。**渡せたかどうかは <c>TrySetResult</c> の戻り値だけで判断する。**
    /// 「中断されているか」を先に見てから渡すと、その隙間で中断されたときに
    /// 誰も持っていないのに門が閉じたままになる。
    /// </summary>
    public void Release()
    {
        lock (_sync)
        {
            while (_waiting.Count > 0)
            {
                var key = _waiting.Keys.First();
                var queue = _waiting[key];

                while (queue.Count > 0)
                {
                    if (queue.Dequeue().TrySetResult())
                    {
                        if (queue.Count == 0)
                        {
                            _waiting.Remove(key);
                        }

                        // 門は開けたまま。次に通る人が持つ
                        return;
                    }
                }

                _waiting.Remove(key);
            }

            _busy = false;
        }
    }
}
