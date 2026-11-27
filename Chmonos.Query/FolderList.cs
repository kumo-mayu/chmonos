namespace Chmonos.Query;

/// <summary>商品として登録したフォルダ1つ（<c>/v1/folders</c>）。中の欄は商品の詳細の <c>folders</c>（<see cref="FolderRef"/>）と同じ値。</summary>
/// <param name="ItemName">商品の表示名（<c>/v1/files/lookup</c> と同じ）。</param>
/// <param name="State"><c>present</c>・<c>missing</c>（<c>/v1/files/lookup</c> のフォルダと同じ。取り込みが「無い」と見た記録だけで決め、ディスクは見ない）。</param>
/// <param name="MissingSince">取り込みが「無い」と見た日時（見つかっていれば null）。</param>
/// <param name="RegisteredAt">この商品のフォルダとして記録した日時（記録に無ければ null）。</param>
public sealed record FolderEntryRef(
    string ItemId,
    string ItemName,
    string Path,
    string State,
    DateTimeOffset? MissingSince,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<string> UnityPackages,
    DateTimeOffset? RegisteredAt);

/// <param name="Items">場所の順（大文字小文字を区別しない）。同じ場所は商品 ID の順。</param>
/// <param name="Total">全部のフォルダの数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record FolderPage(IReadOnlyList<FolderEntryRef> Items, int Total, string? NextCursor);

/// <summary>
/// 商品として登録したフォルダ（各商品の <c>LocalFolders</c>）を全部の商品から並べる道（2026-11-27 に出すと決めた物）。
/// 「登録したフォルダ」は商品のフォルダの方で、設定の取り込み元の履歴・監視するフォルダではない（設定は出さない決まり。最初の案 2026-10-12 も「商品として登録したフォルダ」）。
/// 商品ごとの答えと同じ値にするため、1つの形は <see cref="ItemMapper.Folder"/>・状態は <see cref="ItemMapper.FolderState"/> から作る。
/// </summary>
public static class FolderList
{
    private const string ListName = "folders";

    public static FolderPage Page(QuerySnapshot snapshot, string? cursor, int? limit)
    {
        var rows = snapshot.Items
            .SelectMany(item => item.Local.LocalFolders.Select((folder, index) => (Item: item, Folder: folder, Index: index)))
            .ToList();

        // 並びを必ず一意にする：場所（大文字小文字を区別しない → 区別する）・商品 ID・商品の中の位置（手で書いた記録で同じ場所が2行ある時のため）
        static IReadOnlyList<string> Key((Core.Models.ItemRecord Item, Core.Models.LocalFolderRecord Folder, int Index) row)
            => [row.Folder.Path, row.Item.Id, ListPages.Text(row.Index)];
        static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(left[0], right[0]);
            if (order == 0)
            {
                order = string.CompareOrdinal(left[0], right[0]);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(left[1], right[1]);
            }

            return order != 0 ? order : ListPages.Number(left[2]).CompareTo(ListPages.Number(right[2]));
        }

        rows.Sort((left, right) => Compare(Key(left), Key(right)));
        var (page, next) = ListPages.Take(rows, Key, Compare, ListName, keyLength: 3, cursor, limit);

        return new FolderPage(
            page.Select(row =>
            {
                var folder = ItemMapper.Folder(row.Folder);
                return new FolderEntryRef(
                    row.Item.Id, row.Item.DisplayName, folder.Path, ItemMapper.FolderState(row.Folder), folder.MissingSince,
                    folder.FileCount, folder.TotalBytes, folder.UnityPackages, folder.RegisteredAt);
            }).ToList(),
            rows.Count,
            next);
    }
}
