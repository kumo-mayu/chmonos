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

    /// <summary>
    /// この下の物を記録したときのドライブ文字（外付けの文字が変わって、今の文字に読み替えた物だけ）。
    /// 読み替えた結果だと画面で分かるようにするため（ユーザ指示 2026-09-14）。
    /// </summary>
    public IReadOnlyList<string> RecordedLetters { get; init; } = [];
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
    /// <param name="map">記録のパスを今の場所に読み替える（外付けのドライブ文字が変わったとき・<see cref="VolumeTable"/>）。</param>
    public static IReadOnlyList<FolderNode> Children(
        IReadOnlyList<ItemRecord> items,
        string? parent,
        Func<string, string>? map = null)
    {
        // フォルダごとに、そこにファイルを持つ商品を集める。
        // 同じ商品が同じフォルダに複数ファイルを持っていても1回だけ数える
        var byFolder = new Dictionary<string, (string Display, HashSet<string> Items, SortedSet<string> From)>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            foreach (var (folder, from) in FoldersOf(item, map))
            {
                var key = Normalize(folder);
                if (!byFolder.TryGetValue(key, out var entry))
                {
                    entry = (folder, new HashSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                    byFolder[key] = entry;
                }

                entry.Items.Add(item.Id);
                if (from is not null)
                {
                    entry.From.Add(from);
                }
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

    /// <summary>
    /// 商品がファイルを持つフォルダを全部（祖先を含む・今の場所で）。検索の「ファイルの場所」の候補に使う。
    /// 木（<see cref="Children"/>）と違って通過点を畳まない——候補は打って絞るので、どの階層でも選べた方がよい。
    /// </summary>
    public static IReadOnlyList<string> AllFolders(IReadOnlyList<ItemRecord> items, Func<string, string>? map = null)
        => items
            .SelectMany(item => FoldersOf(item, map).Select(entry => entry.Folder))
            .DistinctBy(Normalize)
            .OrderBy(path => path, NaturalComparer.Instance)
            .ToList();

    /// <summary>この商品がファイルを持つフォルダ（祖先を全部含む・今の場所で）と、読み替えたなら記録の文字。</summary>
    private static IEnumerable<(string Folder, string? From)> FoldersOf(ItemRecord item, Func<string, string>? map)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var paths = item.Local.OwnedFiles.SelectMany(file => file.Paths)
            .Concat(item.Local.LocalFolders.Select(folder => folder.Path));

        foreach (var recorded in paths)
        {
            var path = recorded.Replace('/', Separator);
            var current = map?.Invoke(path) ?? path;
            var from = string.Equals(current, path, StringComparison.OrdinalIgnoreCase) ? null : VolumeTable.LetterOf(path);
            var directory = ParentOf(current);

            while (directory is not null)
            {
                // 同じフォルダでも、読み替えた物とそうでない物は別に数える（記録の文字を取りこぼさない）
                if (!seen.Add(directory + "|" + from))
                {
                    break;
                }

                yield return (directory, from);
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
        Dictionary<string, (string Display, HashSet<string> Items, SortedSet<string> From)> byFolder,
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
                    RecordedLetters = byFolder[key].From.ToList(),
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
    /// <param name="folder">今の場所のパス（木に出したもの）。</param>
    /// <param name="map">記録のパスを今の場所に読み替える。木と同じ物を渡す。</param>
    public static bool IsUnder(ItemRecord item, string folder, Func<string, string>? map = null)
    {
        var target = Normalize(folder);

        return item.Local.OwnedFiles.SelectMany(file => file.Paths)
            .Concat(item.Local.LocalFolders.Select(f => f.Path))
            .Any(path =>
            {
                var slashed = path.Replace('/', Separator);
                var normalized = Normalize(map?.Invoke(slashed) ?? slashed);
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
