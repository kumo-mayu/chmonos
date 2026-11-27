using Chmonos.Core.Services;

namespace Chmonos.Query;

/// <summary>「最近」の足跡1商品ぶん（<c>/v1/recent</c>）。付いていない種類は null。</summary>
/// <param name="Name">商品の表示名。写しにもう無い商品（消した直後で、起動時の整理の前）は null（行は消さずに返す）。</param>
/// <param name="AddedAt">取り込んだ日時。</param>
/// <param name="UsedAt">使った日時（今は Unity へ送ったときだけ）。</param>
/// <param name="ViewedAt">商品ページを開いた日時。</param>
public sealed record RecentRef(string ItemId, string? Name, DateTimeOffset? AddedAt, DateTimeOffset? UsedAt, DateTimeOffset? ViewedAt);

/// <param name="Items">商品 ID の順。</param>
/// <param name="Total">全部の行の数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record RecentPage(IReadOnlyList<RecentRef> Items, int Total, string? NextCursor);

/// <summary>
/// 「最近」の足跡（<c>recent.json</c>）を読む道。書く口（<c>POST /v1/items/{id}/recent</c>）はあるのに読めなかった（2026-11-27 に出すと決めた）。
/// 値は写しの <see cref="QuerySnapshot.Recent"/>＝検索の条件「最近」と同じ <see cref="RecentActivity.Times"/> で組んだ物（同じ商品の行が2つあれば新しい方）。
/// ファイルを読み直さない：写しは recent.json の変化を見張って読み直しているので、条件の答えとこの一覧が食い違わない。
/// </summary>
public static class RecentList
{
    private const string ListName = "recent";

    /// <summary>
    /// 並びは商品 ID の順（3種類の日時があり、どれで並べても1つに決まらないため。Core の <see cref="RecentActivity.Touch"/> も並びを持たない）。
    /// 種類で絞って新しい順に並べたいときは、検索の条件 <c>recent</c> を使う。
    /// </summary>
    public static RecentPage Page(QuerySnapshot snapshot, string? cursor, int? limit)
    {
        var times = snapshot.Recent;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(times.Added.Keys);
        ids.UnionWith(times.Used.Keys);
        ids.UnionWith(times.Viewed.Keys);
        var ordered = ids.Order(StringComparer.Ordinal).ToList();

        var (page, next) = ListPages.Take<string>(
            ordered,
            id => [id],
            (left, right) => string.CompareOrdinal(left[0], right[0]),
            ListName,
            keyLength: 1,
            cursor,
            limit);

        return new RecentPage(
            page.Select(id => new RecentRef(
                id,
                snapshot.ItemsById.GetValueOrDefault(id)?.DisplayName,
                times.Of(id, RecentKind.Added),
                times.Of(id, RecentKind.Used),
                times.Of(id, RecentKind.Viewed))).ToList(),
            ordered.Count,
            next);
    }
}
