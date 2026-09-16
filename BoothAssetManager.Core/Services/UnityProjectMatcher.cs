namespace BoothAssetManager.Core.Services;

/// <summary>1つの商品が、そのプロジェクトにどれだけ入っているか。</summary>
/// <param name="Present">プロジェクトにあったファイルの数。</param>
/// <param name="Total">数えたファイルの数（ほかの商品と共有しているファイルは除いた数）。</param>
public sealed record UnityProjectMatch(string ItemId, int Present, int Total)
{
    public double Ratio => Total == 0 ? 0 : (double)Present / Total;
}

/// <summary>
/// Unity プロジェクトのフォルダを見て、手元のどの商品が入っているかを数える（#72）。
///
/// **画面は読まない。**プロジェクトの <c>Assets/</c> と <c>Packages/</c> にあるファイルを、
/// 商品の unitypackage に入っているパス（<see cref="UnityHandoff.ReadAssetPaths"/>）と突き合わせるだけ。
/// UI Automation で Project タブを読む道も調べたが、見えたのは窓の枠名6つだけで、
/// そもそもファイルを見れば済む（ユーザ指示：必要のない所に UIA を使わない）。
///
/// **ファイルだけを数える。**unitypackage のパスにはフォルダも入っているが、
/// <c>Assets/FUKA</c> のような上のフォルダは同じ作者の別の商品とも重なる。
///
/// **2つ以上の商品に同じパスで入っているファイルは数えない。**lilToon を同梱したアバターのように、
/// 共有の部品を持つ商品は多い。数えると、lilToon が入っているだけで同梱した全商品が候補に出る。
///
/// **欲しい物だけを選んで取り込むのは普通の使い方**なので、1つでもあれば候補に出し、割合で並べる。
/// </summary>
public static class UnityProjectMatcher
{
    /// <param name="projectPath">Unity プロジェクトのフォルダ（<c>Assets</c> を持つ所）。</param>
    /// <param name="items">商品ごとの、unitypackage に入っているパス（複数のパッケージはまとめて渡す）。</param>
    /// <param name="exists">ファイルがあるか。既定はディスクを見る。試験では差し替える。</param>
    /// <param name="childDirectories">フォルダの直下のフォルダの名前。既定はディスクを見る。試験では差し替える。</param>
    public static IReadOnlyList<UnityProjectMatch> Match(
        string projectPath,
        IReadOnlyDictionary<string, IReadOnlyList<string>> items,
        Func<string, bool>? exists = null,
        Func<string, IEnumerable<string>>? childDirectories = null)
    {
        exists ??= File.Exists;
        // 直下の一覧は、読み替えるルートごとではなく1度だけ読む（プロジェクトを調べるときは商品の数だけルートがある）
        var read = childDirectories ?? UnityFolderNames.DiskChildren(projectPath);
        var listed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        childDirectories = folder => listed.TryGetValue(folder, out var names) ? names : listed[folder] = read(folder).ToList();

        // 利用者が入り先のフォルダの頭の記号を消していても、入っていると数える（UnityFolderNames）
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var files = items.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Where(IsFilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        var owners = files
            .SelectMany(pair => pair.Value.Select(path => (Path: path, Item: pair.Key)))
            .GroupBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Item).Distinct().Count(), StringComparer.OrdinalIgnoreCase);

        var matches = new List<UnityProjectMatch>();
        foreach (var (itemId, paths) in files)
        {
            var own = paths.Where(path => owners[path] == 1).ToList();
            if (own.Count == 0)
            {
                continue;
            }

            var present = own
                .Select(path => UnityFolderNames.ResolvePath(path, childDirectories, renamed))
                .Count(path => exists(Path.Combine(projectPath, path.Replace('/', Path.DirectorySeparatorChar))));
            if (present > 0)
            {
                matches.Add(new UnityProjectMatch(itemId, present, own.Count));
            }
        }

        return matches
            .OrderByDescending(match => match.Ratio)
            .ThenByDescending(match => match.Present)
            .ToList();
    }

    /// <summary>
    /// Unity 上のパスのうちファイルか。unitypackage はフォルダにも pathname を持つので、拡張子の有無で分ける。
    /// <c>Assets/</c> か <c>Packages/</c> の下だけを数える（ほかの場所には入らない）。
    /// </summary>
    private static bool IsFilePath(string path)
        => (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
           && Path.HasExtension(path);
}
