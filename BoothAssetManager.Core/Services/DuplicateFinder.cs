using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.Core.Services;

/// <summary>重複の種類。消し方が違うので分けて出す。</summary>
public enum DuplicateKind
{
    /// <summary>同じ中身のファイルが2箇所以上にある。どれか1つ残せばよい。</summary>
    SameFile,

    /// <summary>zipと、それを展開したフォルダの両方を持っている。</summary>
    ArchiveAndUnpacked,
}

/// <summary>重複の1つ分。どの商品のどこにあるか。</summary>
/// <param name="ItemId">この場所を持っている商品。</param>
/// <param name="ItemName">画面に出す商品名。</param>
/// <param name="Path">実際の場所。</param>
public sealed record DuplicatePlace(string ItemId, string ItemName, string Path);

/// <summary>
/// 重複1組。**「どれとどれか」と「消せばいくら空くか」を持つ。**
/// 合計だけでは、どこを触ればよいか分からない。
/// </summary>
public sealed record DuplicateGroup
{
    public required DuplicateKind Kind { get; init; }

    /// <summary>人に見せる名前。ファイル名かフォルダ名。</summary>
    public required string Label { get; init; }

    /// <summary>1つ分の大きさ。</summary>
    public required long UnitBytes { get; init; }

    public required IReadOnlyList<DuplicatePlace> Places { get; init; }

    /// <summary>
    /// 1つ残して他を消したら空く量。
    ///
    /// <see cref="DuplicateKind.ArchiveAndUnpacked"/> では**展開したフォルダを消す前提**の値。
    /// zipは配布物そのもので、消しても展開し直せないが、
    /// 展開したものはzipから作り直せる。逆向きは選べるが、既定はこちら。
    /// </summary>
    public required long ReclaimableBytes { get; init; }

    /// <summary>商品をまたいでいるか。1つの商品の中だけなら false。</summary>
    public bool CrossesItems => Places.Select(place => place.ItemId).Distinct().Count() > 1;
}

/// <summary>
/// 容量を空けられる場所を挙げる。
///
/// **統計の「うち重複コピー N MB」だけでは足りなかった。**
/// どれだけ重複しているかは言えていたが、
/// **どれがどれと重複しているのか、どれを消せば空くのか**が言えていなかった。
///
/// 数え方の穴も2つあった。
/// <list type="bullet">
/// <item>商品をまたいだ同一ハッシュ。<c>StatsService.LogicalSizeOf</c> の
/// <c>DistinctBy(Hash)</c> は1商品の中だけなので、別商品に同じファイルがあると
/// 2回数えられる。**これは直さない**（ユーザ判断 2026-09-18：同じファイルを別の商品として
/// 持つことは、ほとんど起きない）。重複の一覧（<see cref="Find"/>）には商品をまたいでも出る</item>
/// <item>zipと展開済フォルダの両方持ち。フォルダは <c>TotalBytes</c> を
/// 丸ごと足すだけで、対応するzipと突き合わせていない</item>
/// </list>
///
/// **ファイルには触らない。**記録だけで数える純粋な計算にしてある
/// （フォルダを毎回測り直すのは高く、測っている間に人が触ると答が変わる）。
/// </summary>
public static class DuplicateFinder
{
    /// <summary>
    /// 重複を、空く量の大きい順に返す。
    ///
    /// 空く量が0のものは出さない（1箇所しか無いものは重複ではない）。
    /// </summary>
    public static IReadOnlyList<DuplicateGroup> Find(IEnumerable<ItemRecord> items)
    {
        var all = items.ToList();
        return SameFiles(all).Concat(ArchiveAndUnpacked(all))
            .Where(group => group.ReclaimableBytes > 0)
            .OrderByDescending(group => group.ReclaimableBytes)
            .ThenBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 同じ中身のファイルが2箇所以上にあるもの。
    ///
    /// **商品をまたいで束ねる。**同じzipを2つの商品に紐付けていることは普通にあり、
    /// 商品ごとに数えると重複と分からない。
    /// </summary>
    private static IEnumerable<DuplicateGroup> SameFiles(IReadOnlyList<ItemRecord> items)
    {
        var byHash = new Dictionary<string, List<(ItemRecord Item, LocalFileRecord File)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            foreach (var file in item.Local.OwnedFiles)
            {
                if (!byHash.TryGetValue(file.Hash, out var list))
                {
                    list = [];
                    byHash[file.Hash] = list;
                }

                list.Add((item, file));
            }
        }

        foreach (var (_, entries) in byHash)
        {
            // 場所で数える。同じ商品に2パス、別商品に1パスずつ、どちらも同じ重複
            var places = entries
                .SelectMany(entry => entry.File.Paths.Select(path =>
                    new DuplicatePlace(entry.Item.Id, entry.Item.DisplayName, path)))
                .DistinctBy(place => place.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (places.Count < 2)
            {
                continue;
            }

            var unit = entries[0].File.SizeBytes;
            yield return new DuplicateGroup
            {
                Kind = DuplicateKind.SameFile,
                Label = Path.GetFileName(places[0].Path),
                UnitBytes = unit,
                Places = places,
                ReclaimableBytes = unit * (places.Count - 1),
            };
        }
    }

    /// <summary>
    /// zipと、それを展開したフォルダの両方を持っているもの。
    ///
    /// **同じ商品の中だけで見る。**フォルダ名がzipの名前と一致していれば対とみなす
    /// （<see cref="UnpackedFolderDetector.FindMatchingArchive"/> と同じ規則）。
    /// 別商品のzipと名前が一致しても、それは同じ配布物とは言えない。
    /// </summary>
    private static IEnumerable<DuplicateGroup> ArchiveAndUnpacked(IReadOnlyList<ItemRecord> items)
    {
        foreach (var item in items)
        {
            if (item.Local.LocalFolders.Count == 0 || item.Local.OwnedFiles.Count == 0)
            {
                continue;
            }

            // この商品が持っているファイル名を、突き合わせの候補にする
            var candidates = item.Local.OwnedFiles
                .SelectMany(file => file.Paths)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToList();

            foreach (var folder in item.Local.LocalFolders)
            {
                var folderName = Path.GetFileName(folder.Path.TrimEnd('\\', '/'));
                if (UnpackedFolderDetector.FindMatchingArchive(folderName, candidates) is not { } archiveName)
                {
                    continue;
                }

                var archivePath = item.Local.OwnedFiles
                    .SelectMany(file => file.Paths)
                    .FirstOrDefault(path =>
                        string.Equals(Path.GetFileName(path), archiveName, StringComparison.OrdinalIgnoreCase));

                if (archivePath is null)
                {
                    continue;
                }

                yield return new DuplicateGroup
                {
                    Kind = DuplicateKind.ArchiveAndUnpacked,
                    Label = folderName,
                    UnitBytes = folder.TotalBytes,
                    Places =
                    [
                        new DuplicatePlace(item.Id, item.DisplayName, archivePath),
                        new DuplicatePlace(item.Id, item.DisplayName, folder.Path),
                    ],

                    // 展開した方を消す前提。zipは配布物そのもので作り直せない
                    ReclaimableBytes = folder.TotalBytes,
                };
            }
        }
    }
}
