using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// フォルダの木の1ノード。
/// </summary>
public sealed record FolderNode
{
    /// <summary>この階層までの完全なパス。条件そのもの（前方一致で使う）。</summary>
    public required string Path { get; init; }

    /// <summary>画面に出す名前。通過点を畳んだノードは畳んだ全体が入る。</summary>
    public required string Name { get; init; }

    /// <summary>この下にファイルを持つ商品の数。子孫を含む。</summary>
    public required int ItemCount { get; init; }

    /// <summary>降りられるか。商品が1件しかないフォルダはここで止める。</summary>
    public required bool CanDescend { get; init; }
}

/// <summary>
/// ファイルの置き場所から木を作る。
///
/// 対象は「散らばって管理している人」と「フォルダ構成を相当練っている人」。
/// 既存ツール（KonoAsset 等）はzipを自前の倉庫へ入れてユーザの構成を置き換えるが、
/// このアプリはパスをその場で読むので置き換えない。
/// だからこの木は、既に構成に投資した人の投資を尊重するためのものになる。
///
/// 保存するデータは増やさない。毎回ここで計算する。
/// </summary>
public static class FolderTree
{
    private const char Separator = '\\';

    /// <summary>
    /// 比較用にパスを畳む。Windowsでは大文字小文字を区別しないので、
    /// 同じ場所が別のノードに割れないように揃える。表示は最初に見た表記を使う。
    /// </summary>
    public static string Normalize(string path)
        => path.Replace('/', Separator).TrimEnd(Separator).ToLowerInvariant();

    /// <summary>
    /// <paramref name="parent"/> の直下に出す行を作る。
    /// <paramref name="parent"/> が null なら根を返す。
    /// </summary>
    public static IReadOnlyList<FolderNode> Children(IReadOnlyList<ItemRecord> items, string? parent)
    {
        // フォルダごとに、そこにファイルを持つ商品を集める。
        // 同じ商品が同じフォルダに複数ファイルを持っていても1回だけ数える
        var byFolder = new Dictionary<string, (string Display, HashSet<string> Items)>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            foreach (var folder in FoldersOf(item))
            {
                var key = Normalize(folder);
                if (!byFolder.TryGetValue(key, out var entry))
                {
                    entry = (folder, new HashSet<string>(StringComparer.Ordinal));
                    byFolder[key] = entry;
                }

                entry.Items.Add(item.Id);
            }
        }

        var normalizedParent = parent is null ? null : Normalize(parent);
        var result = new List<FolderNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, entry) in byFolder)
        {
            if (!IsDirectChild(key, normalizedParent))
            {
                continue;
            }

            // 通過点は畳む。子が1つしかない階層が続くところは1行にまとめる
            var node = Collapse(byFolder, key, entry.Display, entry.Items.Count);

            if (seen.Add(Normalize(node.Path)))
            {
                result.Add(node);
            }
        }

        return result
            .OrderBy(node => node.Name, NaturalComparer.Instance)
            .ToList();
    }

    /// <summary>この商品がファイルを持つフォルダ（祖先を全部含む）。</summary>
    private static IEnumerable<string> FoldersOf(ItemRecord item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var paths = item.Local.OwnedFiles.SelectMany(file => file.Paths)
            .Concat(item.Local.LocalFolders.Select(folder => folder.Path));

        foreach (var path in paths)
        {
            var directory = ParentOf(path.Replace('/', Separator));

            while (directory is not null)
            {
                if (!seen.Add(directory))
                {
                    break;
                }

                yield return directory;
                directory = ParentOf(directory);
            }
        }
    }

    private static string? ParentOf(string path)
    {
        var index = path.LastIndexOf(Separator);

        // "D:\x" の親は "D:" だが、ドライブ自体は名前に情報が無いので根として扱う
        return index <= 2 ? null : path[..index];
    }

    private static bool IsDirectChild(string candidate, string? parent)
    {
        if (parent is null)
        {
            return ParentOf(candidate) is null;
        }

        if (!candidate.StartsWith(parent + Separator, StringComparison.Ordinal))
        {
            return false;
        }

        return !candidate[(parent.Length + 1)..].Contains(Separator);
    }

    /// <summary>
    /// 子が1つしかない階層が続く限り降りて、1行にまとめる（VS Code の compact folders と同じ）。
    /// 散らばって管理している人の根がすぐ見えるようにするため。
    /// </summary>
    private static FolderNode Collapse(
        Dictionary<string, (string Display, HashSet<string> Items)> byFolder,
        string key,
        string display,
        int count)
    {
        while (true)
        {
            var children = byFolder
                .Where(pair => IsDirectChild(pair.Key, key))
                .ToList();

            // 商品が1件のフォルダは畳まない。
            // 展開後の書庫は「商品フォルダ → Assets → …」と1本道で続くので、
            // 畳むと商品の名前ではなく書庫の中身の名前が出てしまう。
            // 畳むのは、根の手前にある意味の無い通過点を飛ばすためのもの。
            if (count <= 1 || children.Count != 1 || children[0].Value.Items.Count != count)
            {
                // 商品が1件しかないフォルダで止める。
                // それ以上降りても同じ1件しか出ないので降りる意味がない
                // （展開後の書庫の中身は全部これに当たる）
                return new FolderNode
                {
                    Path = display,
                    Name = LastSegment(display),
                    ItemCount = count,
                    CanDescend = count > 1 && children.Count > 0,
                };
            }

            var child = children[0];
            display = child.Value.Display;
            key = child.Key;
        }
    }

    private static string LastSegment(string path)
    {
        var index = path.LastIndexOf(Separator);
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>この商品が、指定したフォルダの下にファイルを持つか（子孫を含む）。</summary>
    public static bool IsUnder(ItemRecord item, string folder)
    {
        var target = Normalize(folder);

        return item.Local.OwnedFiles.SelectMany(file => file.Paths)
            .Concat(item.Local.LocalFolders.Select(f => f.Path))
            .Any(path =>
            {
                var normalized = Normalize(path.Replace('/', Separator));
                return normalized.StartsWith(target + Separator, StringComparison.Ordinal);
            });
    }
}

/// <summary>
/// 数字を数として比べる並び。
/// 辞書順だと 10_ が 2_ より前に来て、番号接頭辞（10_Project / 20_必須Package）で
/// 処理順を表している人の設計が壊れる。
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        int i = 0, j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var start1 = i;
                var start2 = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var left = x[start1..i].TrimStart('0');
                var right = y[start2..j].TrimStart('0');

                if (left.Length != right.Length)
                {
                    return left.Length - right.Length;
                }

                var digits = string.CompareOrdinal(left, right);
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            var one = string.Compare(
                x[i].ToString(), y[j].ToString(), StringComparison.CurrentCultureIgnoreCase);

            if (one != 0)
            {
                return one;
            }

            i++;
            j++;
        }

        return (x.Length - i) - (y.Length - j);
    }
}
