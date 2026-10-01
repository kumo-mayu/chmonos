namespace Chmonos.Core.Services;

/// <summary>
/// **同じ鍵が2つあっても落ちない索引の作り方**（ユーザ判断 2026-09-21・J2）。
///
/// タグ名・属性名・登録簿の商品ID・素体の名前・別名は、手で直した JSON なら重複しうる。
/// <c>ToDictionary</c> をそのまま使っていたので、**その画面が例外で止まっていた**
/// （大文字小文字を無視する比較なので、表記が違うだけでもぶつかる）。
///
/// 残すのは**先に書いてある方**。後勝ちにすると、上から読んでいる人の感覚と合わない。
/// こちらで勝手に直しはせず、食い違いは <see cref="HandEditCheck"/> が要確認に出す。
/// 名前を <c>ToDictionary</c> にしないのは、呼ぶ所で「先勝ち」だと読めるようにするため。
/// </summary>
public static class FirstWins
{
    public static Dictionary<TKey, TValue> Map<TSource, TKey, TValue>(
        IEnumerable<TSource> source,
        Func<TSource, TKey> key,
        Func<TSource, TValue> value,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>(comparer);
        foreach (var entry in source)
        {
            result.TryAdd(key(entry), value(entry));
        }

        return result;
    }

    public static Dictionary<TKey, TSource> Map<TSource, TKey>(
        IEnumerable<TSource> source,
        Func<TSource, TKey> key,
        IEqualityComparer<TKey> comparer)
        where TKey : notnull
        => Map(source, key, entry => entry, comparer);
}
