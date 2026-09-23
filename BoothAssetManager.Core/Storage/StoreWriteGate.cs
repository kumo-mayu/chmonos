namespace BoothAssetManager.Core.Storage;

/// <summary>
/// 保存先を**丸ごと**触る作業（引越し・置き換え・バックアップから戻す）の間、書き込みを止める門
/// （ユーザ判断 2026-09-20・E8：「進み具合を出しつつ、別の競合しない操作は可能な限りできるように」）。
///
/// **読むだけの操作は通す。**手元の JSON を読む道は `UiCommand` を通らないので、
/// ここで止めても画面を見る・検索することはできる。止めるのは書き込みだけ。
///
/// **なぜ要るか：**引越しは「コピー → 突き合わせ → 元を消す」の順で走る。
/// その間に書き込みが通ると、
/// ①コピー済みのファイルへ書いた分は、元を消すときに一緒に消える（黙って失われる）。
/// ②列挙した後に生まれたファイルは運ばれず、元の場所に取り残される。
/// ③突き合わせは取り直した一覧で行うので、増えた1件で「コピーの確認に失敗」になり、
///   コピーは丸ごと済んでいるのに失敗として扱われる。
///
/// **読み手・書き手の錠の形。**書き込みは1ファイルを書く間だけ <see cref="EnterAsync"/> で「書いている」に数えられ、
/// <see cref="HoldAsync"/> は門を閉じてから**走っている書き込みが全部抜けるのを待って**返る。
/// 前は入口で一度見るだけだったので、見た後に走り出した書き込み（裏へ投げた検出・unitypackage の読み込み、
/// 起動時の裏の段）が引越しの最中にも書けていた。
///
/// **数えるのはファイル1つを書く間だけ**（<see cref="JsonStore"/>・画像の保存・商品を外すとき）。
/// 取り込みや裏の段をまるごと数えると、引越しやバックアップが画像の取得（数時間）の終わりまで待たされる。
/// 1ファイルなら数十ms で抜けるので、閉じてからすぐ運び始められる。
///
/// 1つしか持てない（同時に2つの引越しは無い）。持っている間に来た書き込みは、
/// 中止ではなく**待たせる**——押した人は「やった」と思っているので、黙って落とさない。
/// </summary>
public static class StoreWriteGate
{
    /// <summary>引越しは1つずつ。</summary>
    private static readonly SemaphoreSlim HoldLock = new(1, 1);

    private static readonly object Sync = new();

    /// <summary>今書いている数。</summary>
    private static int s_writers;

    /// <summary>閉じている間だけある。開いたら完了し、待っていた書き込みを通す。</summary>
    private static TaskCompletionSource? s_reopened;

    /// <summary>閉じた後、走っている書き込みが全部抜けたら完了する。</summary>
    private static TaskCompletionSource? s_drained;

    /// <summary>今、誰かが保存先を丸ごと触っているか（画面が「今はできません」と言うために見る）。閉じて抜けを待つ間も含む。</summary>
    public static bool IsHeld
    {
        get
        {
            lock (Sync)
            {
                return s_reopened is not null;
            }
        }
    }

    /// <summary>
    /// 閉じていれば開くまで待つ。**数えはしない**（入口で待たせるだけ）。
    ///
    /// `CommandHandler` の入口と起動時の裏の段が通る。押された操作や裏の段が引越しの最中に始まらないようにするためで、
    /// 始まった後の書き込みは1ファイルずつ <see cref="EnterAsync"/> で数えられるので、ここで漏れても守られる。
    /// </summary>
    public static async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        Task? reopened;
        lock (Sync)
        {
            reopened = s_reopened?.Task;
        }

        if (reopened is not null)
        {
            await reopened.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// これから1つ書く。閉じていれば開くまで待ってから数に入る。返った物を捨てると数から抜ける。
    /// </summary>
    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task reopened;
            lock (Sync)
            {
                if (s_reopened is null)
                {
                    s_writers++;
                    return new Writing();
                }

                reopened = s_reopened.Task;
            }

            // 開いた直後に別の引越しが閉じることもあるので、開いたらもう一度見る
            await reopened.WaitAsync(cancellationToken);
        }
    }

    /// <summary>
    /// <see cref="EnterAsync"/> の同期版。同期の保存（<see cref="JsonStore.Write{T}"/>・画像の印）から使う。
    /// 閉じている間はスレッドを止めて待つが、同期の保存は裏のスレッドと起動・初回の窓からしか呼ばれず、
    /// 引越しの最中に画面のスレッドから来ることは無い。
    /// </summary>
    public static IDisposable Enter()
    {
        while (true)
        {
            Task reopened;
            lock (Sync)
            {
                if (s_reopened is null)
                {
                    s_writers++;
                    return new Writing();
                }

                reopened = s_reopened.Task;
            }

            reopened.Wait();
        }
    }

    /// <summary>
    /// 保存先を丸ごと触る間、書き込みを止める。**門を閉じ、走っている書き込みが抜けてから返る。**返った物を捨てると開く。
    /// 抜けを待つ間に中断されたら、閉じたのを戻して投げる（待たせていた書き込みを止めたままにしない）。
    /// </summary>
    public static async Task<IDisposable> HoldAsync(CancellationToken cancellationToken = default)
    {
        await HoldLock.WaitAsync(cancellationToken);

        Task drained;
        lock (Sync)
        {
            s_reopened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            s_drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (s_writers == 0)
            {
                s_drained.TrySetResult();
            }

            drained = s_drained.Task;
        }

        try
        {
            await drained.WaitAsync(cancellationToken);
        }
        catch
        {
            Open();
            throw;
        }

        return new Hold();
    }

    private static void Leave()
    {
        lock (Sync)
        {
            s_writers--;
            if (s_writers == 0)
            {
                s_drained?.TrySetResult();
            }
        }
    }

    private static void Open()
    {
        TaskCompletionSource? reopened;
        lock (Sync)
        {
            reopened = s_reopened;
            s_reopened = null;
            s_drained = null;
        }

        HoldLock.Release();

        // 錠の外で起こす（待っていた側の続きがこの錠を取りに来るため）
        reopened?.TrySetResult();
    }

    private sealed class Writing : IDisposable
    {
        private int _left;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _left, 1) == 0)
            {
                Leave();
            }
        }
    }

    private sealed class Hold : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Open();
            }
        }
    }
}
