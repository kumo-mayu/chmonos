using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 2つ以上の商品をまとめた容量の合計（統計・ショップ・カテゴリの棒）。
///
/// 1つのファイルは2つ以上の商品に付けられる（「両方に付ける」・ユーザ判断 2026-11-11 plan 27。セット商品と中身の各商品など）。
/// 商品ごとの容量（<see cref="ItemRecord.OwnedSizeBytes"/>・<see cref="ItemRecord.ActualDiskBytes"/>）を足すと、共有したファイルを
/// 持ち主の数だけ数えてしまう。**合計は、同じ中身（ハッシュ）・同じ場所を商品をまたいで1回だけ数える。**
/// 商品1つの値（カード・並べ替え・重い商品の順位）は、その商品が持つ物の大きさなので今までどおり商品ごとに出す。
/// そのため、商品ごと・ショップごと・カテゴリごとの値を足すと、全体の合計より大きくなることがある。
/// </summary>
public static class LibrarySizes
{
    /// <summary>
    /// 持っている物の大きさ（論理値）。同じ中身のファイルは何か所・何商品にあっても1回、同じ場所の登録フォルダも1回。
    /// 範囲は <see cref="ItemRecord.OwnedSizeBytes"/> と同じ（外したファイル・古い版は数えない。見つからない物は数える）。
    /// </summary>
    public static long Owned(IEnumerable<ItemRecord> items)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0L;
        foreach (var item in items)
        {
            foreach (var file in item.Local.OwnedFiles)
            {
                if (hashes.Add(file.Hash))
                {
                    total += file.SizeBytes;
                }
            }

            foreach (var folder in item.Local.LocalFolders)
            {
                if (folders.Add(FolderKey(folder.Path)))
                {
                    total += folder.TotalBytes;
                }
            }
        }

        return total;
    }

    /// <summary>
    /// ドライブが実際に食われている量。在る場所を1回ずつ数える（同じ中身を2か所に置けば2回、同じ場所を2つの商品が持てば1回）。
    /// 範囲は <see cref="ItemRecord.ActualDiskBytes"/> と同じ（見つからなくなった日時の付いた記録・無いと見たフォルダは0）。
    /// </summary>
    public static long OnDisk(IEnumerable<ItemRecord> items)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0L;
        foreach (var item in items)
        {
            foreach (var file in item.Local.OwnedFiles.Where(file => file.MissingSince is null))
            {
                foreach (var path in file.Paths)
                {
                    if (paths.Add(path))
                    {
                        total += file.SizeBytes;
                    }
                }
            }

            foreach (var folder in item.Local.LocalFolders.Where(folder => folder.MissingSince is null))
            {
                if (folders.Add(FolderKey(folder.Path)))
                {
                    total += folder.TotalBytes;
                }
            }
        }

        return total;
    }

    /// <summary>
    /// 実占有のうち重複コピーの分：同じ中身の在る場所の、2つ目から。商品をまたいだ別の場所の写しも数える
    /// （重複の一覧 <see cref="DuplicateFinder"/> と同じく「1つ残せば空く量」。前は商品の中だけで数えていた）。
    /// 同じ場所を2つの商品が持つだけなら写しではないので0。
    /// </summary>
    public static long DuplicateOnDisk(IEnumerable<ItemRecord> items)
    {
        var byHash = new Dictionary<string, (long Size, HashSet<string> Paths)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            foreach (var file in item.Local.OwnedFiles.Where(file => file.MissingSince is null))
            {
                if (!byHash.TryGetValue(file.Hash, out var entry))
                {
                    entry = (file.SizeBytes, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    byHash[file.Hash] = entry;
                }

                entry.Paths.UnionWith(file.Paths);
            }
        }

        return byHash.Values.Sum(entry => entry.Size * Math.Max(0, entry.Paths.Count - 1));
    }

    /// <summary>同じフォルダを末尾の区切りの有無で別に数えない。</summary>
    private static string FolderKey(string path) => Path.TrimEndingDirectorySeparator(path);
}
