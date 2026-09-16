namespace BoothAssetManager.Core.Services;

/// <summary>
/// unitypackage が入れる先のフォルダと、プロジェクトに実際にあるフォルダを突き合わせる。
///
/// **ショップは自分のフォルダを Assets の先頭に並べるため、名前の頭に記号を付けることがある**（<c>_FUKA</c>・<c>!FUKA</c>）。
/// 並びを嫌う利用者はこれを消す（ユーザ指摘 2026-09-16）。パッケージの中のパスのまま探すと、消したプロジェクトでは
/// 「入っていない」ことになり、プロジェクトタブでも見つからない。頭の記号を外した名前が同じなら同じフォルダとみなす。
/// </summary>
public static class UnityFolderNames
{
    /// <summary>
    /// 頭の記号として外さない文字。**括弧は並べるための記号ではなく名前の一部**（<c>[作者名]衣装</c>）で、
    /// 開きだけを消すことはまず無い。外すと <c>[A]B</c> と <c>A]B</c> を同じとみなしてしまう。
    /// </summary>
    private static readonly HashSet<char> Openers = ['[', '(', '{', '<', '【', '「', '『', '（', '［', '＜', '〔', '《', '〈'];

    /// <summary>名前の頭の、並べるための記号（と、その後の空白）を外す。外すと何も残らないときは元の名前を返す。</summary>
    public static string StripSortPrefix(string name)
    {
        var start = 0;
        while (start < name.Length
               && !char.IsLetterOrDigit(name[start])
               && !Openers.Contains(name[start]))
        {
            start++;
        }

        return start == name.Length ? name : name[start..];
    }

    /// <summary>
    /// 入り先のルート（<c>Assets/_FUKA</c>）を、プロジェクトに実際にある名前に読み替える。
    ///
    /// - その名前のフォルダがあればそのまま（記号を付けたまま使っている普通の場合）
    /// - 無く、頭の記号を外した名前が同じフォルダが**1つだけ**あればそれ（<c>Assets/FUKA</c>）
    /// - 2つ以上あれば取り違えるので読み替えない。無ければそのまま
    ///
    /// <c>Assets/</c> の直下だけを見る。Packages の下はパッケージの ID の名前で、記号で並べる物ではない。
    /// </summary>
    /// <param name="childDirectories">Unity 上のパス（<c>Assets</c>）の直下にあるフォルダの名前。試験では差し替える。</param>
    public static string ResolveRoot(string root, Func<string, IEnumerable<string>> childDirectories)
    {
        var segments = root.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || !string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        var name = segments[1];
        var children = childDirectories(segments[0]).ToList();
        if (children.Any(child => string.Equals(child, name, StringComparison.OrdinalIgnoreCase)))
        {
            return root;
        }

        var core = StripSortPrefix(name);
        var renamed = children
            .Where(child => string.Equals(StripSortPrefix(child), core, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return renamed.Count == 1 ? $"{segments[0]}/{renamed[0]}" : root;
    }

    /// <summary>ディスクを見る <see cref="ResolveRoot"/> の既定。読めなければ空（読み替えない）。</summary>
    public static Func<string, IEnumerable<string>> DiskChildren(string projectPath) => unityPath =>
    {
        try
        {
            var folder = Path.Combine(projectPath, unityPath.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(folder)
                ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>().ToList()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    };

    /// <summary>
    /// パッケージの中のパス（<c>Assets/_FUKA/撫で音/a.fbx</c>）の頭の2段を、<see cref="ResolveRoot"/> で読み替える。
    /// 同じルートは1度だけ調べる（<paramref name="cache"/>）。
    /// </summary>
    public static string ResolvePath(string path, Func<string, IEnumerable<string>> childDirectories, IDictionary<string, string> cache)
    {
        var segments = path.Split('/');
        if (segments.Length < 3)
        {
            return path;
        }

        var root = $"{segments[0]}/{segments[1]}";
        if (!cache.TryGetValue(root, out var resolved))
        {
            resolved = ResolveRoot(root, childDirectories);
            cache[root] = resolved;
        }

        return resolved == root ? path : resolved + path[root.Length..];
    }
}
