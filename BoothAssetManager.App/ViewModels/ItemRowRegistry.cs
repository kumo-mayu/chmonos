namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 画面が作った商品の行を弱く持ち、商品 ID で引く。画像が届いたときに行の絵を引き直すため（洗い出し 6）。
///
/// 行は開き直し・探す欄の1文字ごとに作り直されて捨てられるので、強く持つと画面を開いている間ずっと溜まる。
/// 捨てられた行は引くときに片付ける
/// </summary>
internal sealed class ItemRowRegistry<T>(Func<T, string> idOf) where T : class
{
    private readonly List<WeakReference<T>> _rows = [];

    public void Add(T row) => _rows.Add(new WeakReference<T>(row));

    /// <summary>その商品の、まだ生きている行。画面のスレッドで呼ぶ。</summary>
    public List<T> Find(string itemId)
    {
        var found = new List<T>();
        _rows.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _rows)
        {
            if (reference.TryGetTarget(out var row) && string.Equals(idOf(row), itemId, StringComparison.Ordinal))
            {
                found.Add(row);
            }
        }

        return found;
    }
}
