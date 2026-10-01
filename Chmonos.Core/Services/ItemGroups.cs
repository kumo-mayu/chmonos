using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 並べた一覧の中の、同じまとまり（同じショップ・同じカテゴリ・同じ月）の続き1つ。
/// <see cref="Key"/> はまとまりを見分ける鍵（並べ直しても同じまとまりなら同じ値）、<see cref="Label"/> は札に出す名前。
/// </summary>
/// <param name="Parent">カテゴリの親（「3Dモデル」）。親を出さないまとまりは null。</param>
/// <param name="Start">並べた一覧の中で、このまとまりの最初の商品が何番目か。</param>
public sealed record ItemGroup(string Key, string Label, string? Parent, int Start, int Count);

/// <summary>
/// 並べ替えた一覧を、まとまりごとに切る（検索の並べ替えの区切りの札。ユーザ判断 2026-10-01：図書館やビデオショップの分類の札）。
///
/// **並べ替えが同じまとまりを続けて並べることを当てにして、隣どうしで鍵が変わった所で切る。**
/// まとまりごとに集め直すと、並べ替えの決まり（<see cref="ItemOrder"/>）と別の順が生まれ、札と並びが食い違う。
/// 並べ替えと切り方が食い違ったときは、同じ鍵の札が2枚出るだけで、商品の順は変わらない。
/// </summary>
public static class ItemGroups
{
    /// <summary>ショップの無い商品の札の名前。</summary>
    public const string NoShop = "ショップなし";

    /// <summary>カテゴリの無い商品の札の名前。</summary>
    public const string NoCategory = "カテゴリなし";

    /// <summary>入手日の無い商品の札の名前。</summary>
    public const string NoAcquired = "入手日なし";

    /// <summary>公開日の取れていない商品の札の名前。</summary>
    public const string NoPublished = "公開日なし";

    /// <summary>隣どうしで鍵が変わった所で切る。</summary>
    public static IReadOnlyList<ItemGroup> Split<T>(IReadOnlyList<T> ordered, Func<T, (string Key, string Label, string? Parent)> groupOf)
    {
        var groups = new List<ItemGroup>();
        var start = 0;
        (string Key, string Label, string? Parent)? current = null;

        for (var index = 0; index < ordered.Count; index++)
        {
            var here = groupOf(ordered[index]);
            if (current is { } open && string.Equals(open.Key, here.Key, StringComparison.Ordinal))
            {
                continue;
            }

            if (current is { } done)
            {
                groups.Add(new ItemGroup(done.Key, done.Label, done.Parent, start, index - start));
            }

            current = here;
            start = index;
        }

        if (current is { } last)
        {
            groups.Add(new ItemGroup(last.Key, last.Label, last.Parent, start, ordered.Count - start));
        }

        return groups;
    }

    /// <summary>
    /// ショップのまとまり。鍵は画面に出すショップ名（並べ替え <see cref="ItemOrder.ByShop"/> もショップ名で並べるので、
    /// 名前の同じ別の店は1つのまとまりになる。店の ID で分けると、並びの上では続いているのに札が2枚出る）。
    /// </summary>
    public static (string Key, string Label, string? Parent) ShopOf(ItemRecord item)
        => item.ShopName is { Length: > 0 } name
            ? ("shop:" + name, name, null)
            : ("shop-none", NoShop, null);

    /// <summary>
    /// カテゴリのまとまり。名前は子の名前、親は表から引く（表に無い・人が入れたカテゴリで表にも無いときは、BOOTH から取れた親）。
    /// 商品ページと同じく、人が入れたカテゴリを先に見る（<see cref="ItemRecord.CategoryName"/>）。
    /// </summary>
    public static (string Key, string Label, string? Parent) CategoryOf(ItemRecord item, CategoryTable table)
    {
        if (item.CategoryName?.Trim() is not { Length: > 0 } child)
        {
            return ("category-none", NoCategory, null);
        }

        var observedParent = string.IsNullOrWhiteSpace(item.Local.Category) ? item.Booth.Category?.ParentName : null;
        var parent = table.ParentOf(child) ?? (string.IsNullOrWhiteSpace(observedParent) ? null : observedParent.Trim());
        return ("category:" + child, child, parent);
    }

    /// <summary>
    /// 年と月のまとまり（入手日・公開日）。日ごとにすると札が商品より多くなり、年ごとにすると1年に数百件の人で札が意味を持たない。
    /// 値の無い商品は「{none}」の1つにまとめる（並べ替えも向きによらず後ろにまとめている）。
    /// </summary>
    public static (string Key, string Label, string? Parent) MonthOf(int? year, int? month, string none)
        => year is { } y && month is { } m
            ? ($"month:{y:D4}-{m:D2}", $"{y}年{m}月", null)
            : ("month-none", none, null);

    /// <summary>
    /// 入手日の年と月。並べ替え（<see cref="ItemOrder.ByAcquired"/>）と同じ <see cref="LocalBlock.AcquiredAt"/> を見る。
    /// 別の値（購入の記録の日など）で切ると、並びの上では続いているのに札が2枚出る
    /// </summary>
    public static (string Key, string Label, string? Parent) AcquiredOf(ItemRecord item)
        => MonthOf(item.Local.AcquiredAt?.Year, item.Local.AcquiredAt?.Month, NoAcquired);

    /// <summary>BOOTH の公開日の年と月。並べ替え（<see cref="ItemOrder.ByPublished"/>）と同じ値を見る。</summary>
    public static (string Key, string Label, string? Parent) PublishedOf(ItemRecord item)
        => MonthOf(item.Booth.PublishedAt?.Year, item.Booth.PublishedAt?.Month, NoPublished);
}
