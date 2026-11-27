using Chmonos.Core.Models;

namespace Chmonos.Query;

/// <summary>管理から外したファイル1つ（<c>/v1/excluded</c>）。</summary>
/// <param name="Hash">中身のハッシュ（移されても効かせる、外した物の正体）。</param>
/// <param name="Paths">外したときの場所の全部（記録の順。画面は1つ目だけを見せるが、API は潰さない）。</param>
/// <param name="ExcludedAt">外した日時。</param>
/// <param name="Source">外した操作の出どころ（<c>resolve</c>・<c>resolveArchive</c>・<c>resolveBulk</c>・<c>folderView</c>・知らない文は <c>other</c>）。記録に無ければ null。値は <see cref="ExcludeSources.ValueOf"/>。</param>
public sealed record ExcludedRef(string Hash, IReadOnlyList<string> Paths, DateTimeOffset ExcludedAt, string? Source);

/// <param name="Items">画面（設定の「除外したファイル」）と同じ並び：外した日時の新しい順・同じ日時は記録の後ろ（後から外した物）が先。</param>
/// <param name="Total">全部の数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record ExcludedPage(IReadOnlyList<ExcludedRef> Items, int Total, string? NextCursor);

/// <summary>
/// 管理から外したファイル（<c>excluded.json</c>）を読む道（2026-11-27 に出すと決めた物）。読むだけで、戻すのは画面に残す。
/// 記録は写しの <see cref="QuerySnapshot.Excluded"/>（見張りの部分 <see cref="StorePart.Excluded"/>）から取り、要求のたびに読み直さない
/// （記録は外すほど大きくなる。2万件で読むのに約 57ms：docs/research/large-files-2026-09-30.md）。
/// </summary>
public static class ExcludedList
{
    private const string ListName = "excluded";

    public static ExcludedPage Page(QuerySnapshot snapshot, string? cursor, int? limit)
    {
        // 並びは画面の Core の SettingsService.LoadExcluded と同じ。まとめて外すと同じ日時が何千件も並ぶので、記録の位置で一意にする
        var rows = snapshot.Excluded
            .Select((entry, index) => (Entry: entry, Index: index))
            .OrderByDescending(row => row.Entry.ExcludedAt)
            .ThenByDescending(row => row.Index)
            .ToList();

        static IReadOnlyList<string> Key((ExcludedEntry Entry, int Index) row)
            => [ListPages.Text(row.Entry.ExcludedAt.UtcTicks), ListPages.Text(row.Index)];
        static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var order = ListPages.Number(right[0]).CompareTo(ListPages.Number(left[0]));
            return order != 0 ? order : ListPages.Number(right[1]).CompareTo(ListPages.Number(left[1]));
        }

        var (page, next) = ListPages.Take(rows, Key, Compare, ListName, keyLength: 2, cursor, limit);
        return new ExcludedPage(
            page.Select(row => new ExcludedRef(row.Entry.Hash, row.Entry.Paths ?? [], row.Entry.ExcludedAt, ExcludeSources.ValueOf(row.Entry.Reason))).ToList(),
            rows.Count,
            next);
    }
}
