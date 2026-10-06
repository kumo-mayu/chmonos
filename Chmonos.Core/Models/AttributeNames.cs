namespace Chmonos.Core.Models;

/// <summary>
/// 属性の名前の比べ方。**大文字と小文字の違いは同じ属性と見る**（改名・削除・使われていない属性の判定がそう決めてある）。
///
/// 商品の属性の表もこの比べ方で持つ（外部の点検 2026-10-06）。前は表が文字の全く同じ物しか引けず、
/// 手で直した JSON の「soft」が属性の「Soft」として数えられず、検索にも当たらないのに、使われていない属性にも出なかった。
/// 引く所は9か所あり、1つずつ直すと漏れるので、表の側で揃える
/// </summary>
public static class AttributeNames
{
    public static StringComparer Comparer => StringComparer.CurrentCultureIgnoreCase;

    public static StringComparison Comparison => StringComparison.CurrentCultureIgnoreCase;

    /// <summary>
    /// この比べ方の表にする。大文字と小文字だけが違う名前が1つの商品に2つあれば（手で直した JSON だけで起きる）、先の方を残す。
    /// 欠けた表（null）は空として受ける
    /// </summary>
    internal static IReadOnlyDictionary<string, int> Table(IReadOnlyDictionary<string, int>? source)
    {
        if (source is Dictionary<string, int> dictionary && dictionary.Comparer.Equals(Comparer))
        {
            return dictionary;
        }

        var table = new Dictionary<string, int>(Comparer);
        if (source is not null)
        {
            foreach (var (name, value) in source)
            {
                table.TryAdd(name, value);
            }
        }

        return table;
    }
}
