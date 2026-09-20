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
/// 1つしか持てない（同時に2つの引越しは無い）。持っている間に押された書き込みは、
/// 中止ではなく**待たせる**——押した人は「やった」と思っているので、黙って落とさない。
/// </summary>
public static class StoreWriteGate
{
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>今、誰かが保存先を丸ごと触っているか（画面が「今はできません」と言うために見る）。</summary>
    public static bool IsHeld => Lock.CurrentCount == 0;

    /// <summary>書き込みの前に通る。止まっていれば、終わるまで待つ。</summary>
    public static async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        // 止まっていないときに待たせない（毎回の書き込みが通る道なので、空振りは速く抜ける）
        if (!IsHeld)
        {
            return;
        }

        await Lock.WaitAsync(cancellationToken);
        Lock.Release();
    }

    /// <summary>保存先を丸ごと触る間、書き込みを止める。返った物を捨てると解ける。</summary>
    public static async Task<IDisposable> HoldAsync(CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        return new Hold();
    }

    private sealed class Hold : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Lock.Release();
        }
    }
}
