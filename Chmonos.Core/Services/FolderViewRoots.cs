using System.Text.RegularExpressions;
using static Chmonos.Core.Services.PathText;

namespace Chmonos.Core.Services;

/// <summary>
/// フォルダビューの根1つ。
/// </summary>
/// <param name="Volume">ボリューム（<c>D:</c>・<c>\\nas\share</c>）。</param>
/// <param name="Path">根の場所。</param>
/// <param name="IsLooseBucket">
/// 「〇〇（直下など）」の根か。境目のフォルダの直下に置いた物と、境目の直下で置き場所が1か所しか無いフォルダをまとめる。
/// </param>
/// <param name="LooseFolders">「直下など」にまとめたフォルダ（境目の直下の、置き場所が1か所しか無いフォルダ）。</param>
public sealed record FolderViewRoot(string Volume, string Path, bool IsLooseBucket, IReadOnlyList<string> LooseFolders);

/// <summary>
/// フォルダビューの根を決める（<c>docs/history/folder-view.md</c> §2・ユーザ判断 2026-09-13）。
///
/// **同じボリュームにあり、共通の祖先が「境目のフォルダ」より下にあれば1つの根。**
/// 祖先が境目になるときだけ1段下で分け、分けた先で繰り返す。見出しの数に上限は持たない——
/// 「上限まで縮める」案は、きれいに整理した人ほど根を細かく割った（作り物と友人のデータで試した）。
///
/// 散らかった置き方の人でも崩れないことを、作り物6通り（ダウンロードに溜める・ドライブのあちこちに1本ずつ・
/// 外付けとNASに分かれる・ドライブの直下に直接・1つのフォルダに1000本など）で確かめてから決めた。
/// 保存するデータは増やさない。毎回ここで計算する。
/// </summary>
public static class FolderViewRoots
{
    /// <summary>
    /// 1か所でも置き場として意味があるフォルダ。境目の直下にあっても「直下など」に畳まない
    /// （畳むと、デスクトップとダウンロードが根から消えた・試して分かった）。
    /// </summary>
    private static readonly string[] KnownPlaces = ["Desktop", "Downloads", "Documents", "デスクトップ", "ダウンロード", "ドキュメント"];

    /// <summary>
    /// 境目のフォルダか。**置き場ではない**ので根にしない（それより上には上がらない）。
    /// ドライブの直下・共有の直下・<c>C:\Users</c>・ユーザのフォルダ・AppData・OneDrive の同期フォルダの根。
    /// デスクトップ・ダウンロード・ドキュメントは境目にしない（そこに直接置く人がいる）。
    /// </summary>
    public static bool IsHardBoundary(string path, string? oneDriveRoot = null)
    {
        var trimmed = Trim(path);
        return Regex.IsMatch(trimmed, @"^[A-Za-z]:$")
            || Regex.IsMatch(trimmed, @"^\\\\[^\\]+\\[^\\]+$")
            || Regex.IsMatch(trimmed, @"^[A-Za-z]:\\Users(\\[^\\]+)?$", RegexOptions.IgnoreCase)
            || Regex.IsMatch(trimmed, @"^[A-Za-z]:\\Users\\[^\\]+\\AppData(\\[^\\]+)?$", RegexOptions.IgnoreCase)
            || (!string.IsNullOrEmpty(oneDriveRoot) && string.Equals(trimmed, Trim(oneDriveRoot), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>ボリューム。<c>D:\x</c> なら <c>D:</c>、<c>\\nas\share\x</c> なら <c>\\nas\share</c>。</summary>
    public static string VolumeOf(string path)
    {
        var trimmed = Trim(path);
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = trimmed[2..].Split('\\');
            return parts.Length >= 2 ? $@"\\{parts[0]}\{parts[1]}" : trimmed;
        }

        return trimmed.Length >= 2 ? trimmed[..2] : trimmed;
    }

    /// <param name="placeFolders">置き場所（ファイルの親フォルダ・フォルダとして登録した商品の親）。</param>
    /// <param name="isHardBoundary">境目か。省略時は <see cref="IsHardBoundary"/>。</param>
    public static IReadOnlyList<FolderViewRoot> Roots(IEnumerable<string> placeFolders, Func<string, bool>? isHardBoundary = null)
    {
        var boundary = isHardBoundary ?? (path => IsHardBoundary(path));
        var roots = new List<FolderViewRoot>();

        foreach (var volume in placeFolders
                     .Select(Trim)
                     .Where(path => path.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .GroupBy(VolumeOf, StringComparer.OrdinalIgnoreCase))
        {
            Split(volume.Key, volume.ToList(), boundary, roots);
        }

        return roots;
    }

    private static void Split(string volume, IReadOnlyList<string> folders, Func<string, bool> boundary, List<FolderViewRoot> roots)
    {
        var ancestor = CommonAncestor(folders);
        if (!boundary(ancestor))
        {
            roots.Add(new FolderViewRoot(volume, ancestor, false, []));
            return;
        }

        var below = folders.Where(folder => !Same(folder, ancestor)).ToList();
        var groups = below
            .GroupBy(folder => folder[ancestor.Length..].Trim('\\').Split('\\')[0], StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 境目の直下で置き場所が1か所しか無いフォルダは、自分の根を作らず「直下など」にまとめる（ユーザ判断 2026-09-13）。
        // ドライブの直下に小さなフォルダを大量に作る置き方で、根が32個に割れた（作り物 E）
        var loose = groups
            .Where(group => group.Count() == 1
                && Depth(group.First()) == Depth(ancestor) + 1
                && !KnownPlaces.Contains(group.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (folders.Any(folder => Same(folder, ancestor)) || loose.Count > 0)
        {
            roots.Add(new FolderViewRoot(volume, ancestor, true, loose.Select(group => group.First()).ToList()));
        }

        foreach (var group in groups.Except(loose))
        {
            Split(volume, group.ToList(), boundary, roots);
        }
    }

    private static string CommonAncestor(IReadOnlyList<string> folders)
    {
        var split = folders.Select(folder => folder.Split('\\')).ToList();
        var length = split.Min(parts => parts.Length);
        var common = 0;
        while (common < length
               && split.All(parts => string.Equals(parts[common], split[0][common], StringComparison.OrdinalIgnoreCase)))
        {
            common++;
        }

        return string.Join('\\', split[0], 0, common);
    }

    private static int Depth(string path) => path.Split('\\').Length;


    private static string Trim(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
