namespace Chmonos.Core.Services;

/// <summary>送る unitypackage の中の、VPM と同じ GUID の物を、VPM 1つ分にまとめた物。</summary>
/// <param name="Package">重なる VPM（表示名は <see cref="VpmPackage.Shown"/>）。</param>
/// <param name="Count">同じ GUID のファイルの数（unitypackage の中のフォルダの項目は数えない）。</param>
/// <param name="Root">
/// unitypackage の中で、重なるファイルが入る場所の共通の頭（<c>Assets/lilToon</c>）。
/// 頭が <c>Assets</c> だけか、共通の頭が無いときは null（「そのフォルダのチェックを外す」と言えない）。
/// </param>
public sealed record VpmOverlap(VpmPackage Package, int Count, string? Root);

/// <summary>送る unitypackage 1つと、その中で VPM と重なる物（重なりがある包みだけ並ぶ）。</summary>
/// <param name="Index">送る列の中の位置。</param>
public sealed record VpmOverlapPackage(int Index, UnityPackageEntry Entry, IReadOnlyList<VpmOverlap> Overlaps)
{
    public int Count => Overlaps.Sum(overlap => overlap.Count);
}

/// <summary>
/// 送る unitypackage と、送り先のプロジェクトの <c>Packages/</c> に入っている VPM の GUID の重なりを照らす（plan 40・ユーザ判断 2026-11-11）。
///
/// 同じ GUID・違う中身の物を取り込むと、Unity は <c>Assets/</c> に作らず、VPM のファイルを黙って上書きする
/// （<c>docs/research/vpm-duplicate-guid.md</c>）。中身が同じなら何もしないが、それは取り込むまで分からないので、同じ GUID があれば言う。
///
/// **数えるのは <c>package.json</c> を持つフォルダ（<see cref="VpmPackages"/> が並べる物）の中だけ。**
/// <c>Packages/</c> の下には unitypackage が自分で入れた実フォルダもある（BlendShare など）。それを数えると、同じ物を入れ直すたびに言ってしまう。
/// </summary>
public static class VpmOverlaps
{
    /// <summary>
    /// 照らす。<paramref name="stored"/> は包みの中身の控え（無ければ null で、その包みは照らさない＝黙って送る）。
    /// <paramref name="projectGuids"/>（GUID → プロジェクトの中のパス）は、照らす包みと VPM のフォルダが1つでもあるときだけ引く（作るのが重い）。
    /// </summary>
    public static IReadOnlyList<VpmOverlapPackage> Find(
        IReadOnlyList<UnityPackageEntry> packages,
        Func<UnityPackageEntry, IReadOnlyList<UnityPackageAsset>?> stored,
        IReadOnlyList<VpmPackage> vpm,
        Func<IReadOnlyDictionary<string, string>> projectGuids)
    {
        // manifest に無い埋め込みの物は数えない：unitypackage が自分で Packages/ に置いた物（package.json を同梱する物もある）で、
        // 入れ直すたびに「VPMが上書きされます」と言うと、入れたい物そのもののチェックを外させてしまう
        var byFolder = vpm
            .Where(package => package.InManifest && package.Folder is { Length: > 0 })
            .GroupBy(package => package.Folder!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (byFolder.Count == 0)
        {
            return [];
        }

        var contents = packages.Select((package, index) => (Index: index, Package: package, Assets: stored(package))).Where(entry => entry.Assets is { Count: > 0 }).ToList();
        if (contents.Count == 0)
        {
            return [];
        }

        var guids = projectGuids();
        var result = new List<VpmOverlapPackage>();
        foreach (var (index, package, assets) in contents)
        {
            var folders = FolderPaths(assets!);
            var hits = new Dictionary<VpmPackage, List<string>>();
            foreach (var asset in assets!)
            {
                if (folders.Contains(asset.Path)
                    || !guids.TryGetValue(asset.Guid, out var projectPath)
                    || PackageFolder(projectPath) is not { } folder
                    || !byFolder.TryGetValue(folder, out var owner)
                    // 同じ Packages/<フォルダ> の中へ入れる物は、その包み自身を入れ直す物（VPM の配布元が出す unitypackage 版など）。
                    // 「Packages/… のチェックを外して」は入れたい物を外させる文になるので言わない
                    || PackageFolder(asset.Path) is { } own && string.Equals(own, folder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!hits.TryGetValue(owner, out var paths))
                {
                    hits[owner] = paths = [];
                }

                paths.Add(asset.Path);
            }

            if (hits.Count > 0)
            {
                result.Add(new VpmOverlapPackage(
                    index,
                    package,
                    hits.Select(pair => new VpmOverlap(pair.Key, pair.Value.Count, CommonRoot(pair.Value)))
                        .OrderByDescending(overlap => overlap.Count)
                        .ThenBy(overlap => overlap.Package.Shown, StringComparer.OrdinalIgnoreCase)
                        .ToList()));
            }
        }

        return result;
    }

    /// <summary>ディスクのプロジェクトで照らす（VPM は今の manifest を読み、GUID の表は「Unityで選択」と同じ物を使い回す）。</summary>
    public static IReadOnlyList<VpmOverlapPackage> FindInProject(string project, IReadOnlyList<UnityPackageEntry> packages, UnityPackageReads reads)
        => Find(packages, reads.StoredOnly, VpmPackages.Read(project), () => UnityProjectGuids.ForProject(project).Current());

    /// <summary><c>Packages/&lt;フォルダ&gt;/…</c> のフォルダの名前。<c>Packages</c> の外なら null。</summary>
    private static string? PackageFolder(string projectPath)
    {
        var segments = projectPath.Split('/');
        return segments.Length >= 3 && string.Equals(segments[0], "Packages", StringComparison.OrdinalIgnoreCase) ? segments[1] : null;
    }

    /// <summary>unitypackage の中のフォルダの項目（ほかの項目のパスの親になっている物）。「ファイルが n 個」と数えないため。</summary>
    private static HashSet<string> FolderPaths(IReadOnlyList<UnityPackageAsset> assets)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var path = asset.Path;
            for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (!folders.Add(path[..slash]))
                {
                    break;
                }
            }
        }

        return folders;
    }

    /// <summary>
    /// ファイルの入る場所の共通の頭（フォルダの段で比べる）。1つだけならそのファイルのあるフォルダ。
    /// <c>Assets</c> だけ・共通の頭が無いときは null（「Assets のチェックを外す」は全部を外させる文になる）。
    /// </summary>
    internal static string? CommonRoot(IReadOnlyList<string> paths)
    {
        string[]? common = null;
        foreach (var path in paths)
        {
            var folder = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1];
            if (common is null)
            {
                common = folder;
                continue;
            }

            var length = 0;
            while (length < common.Length && length < folder.Length && string.Equals(common[length], folder[length], StringComparison.OrdinalIgnoreCase))
            {
                length++;
            }

            common = common[..length];
        }

        return common is { Length: >= 2 } ? string.Join('/', common) : null;
    }
}
