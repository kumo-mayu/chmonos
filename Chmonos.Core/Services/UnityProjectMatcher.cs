namespace Chmonos.Core.Services;

/// <summary>1つの商品が、そのプロジェクトにどれだけ入っているか。</summary>
/// <param name="Present">プロジェクトにあったファイルの数（パスか GUID で見つかった物）。</param>
/// <param name="Total">数えたファイルの数（ほかの商品と共有しているファイルは除いた数）。</param>
public sealed record UnityProjectMatch(string ItemId, int Present, int Total)
{
    public double Ratio => Total == 0 ? 0 : (double)Present / Total;
}

/// <summary>「Unityで選択」で開くフォルダ（<see cref="UnityProjectMatcher.LocateRoot"/>）。</summary>
/// <param name="Root">プロジェクトの中の Unity 上のパス。</param>
/// <param name="Moved">パッケージの入り先には無く、GUID で見つけた今の場所。</param>
public sealed record UnityRootLocation(string Root, bool Moved);

/// <summary>
/// Unity プロジェクトのフォルダを見て、手元のどの商品が入っているかを数える（#72）。
///
/// **画面は読まない。**プロジェクトの <c>Assets/</c> と <c>Packages/</c> にあるファイルを、
/// 商品の unitypackage に入っているアセット（<see cref="UnityHandoff.ReadAssets"/>）と突き合わせるだけ。
/// UI Automation で Project タブを読む道も調べたが、見えたのは窓の枠名6つだけで、
/// そもそもファイルを見れば済む（ユーザ指示：必要のない所に UIA を使わない）。
///
/// **まずパスで、見つからない物だけ GUID で探す**（2026-09-29 ユーザ指示）。利用者がフォルダを移した・名前を変えた物は
/// パスでは見つからないが、<c>.meta</c> の GUID は変わらない（<see cref="UnityProjectGuids"/>）。GUID の表を作るのは重いので、
/// パスで全部見つかったときは作らない。
/// ショップが Assets の先頭に並べるために付けた頭の記号（<c>_FUKA</c>・<c>!FUKA</c>）を利用者が消した物も、名前を変えた物として
/// GUID で見つける。前は記号を外した名前で読み替えていたが（ユーザ指摘 2026-09-16）、名前の当て推量は記号の付け方しだいで外れ、
/// 取り違えもあり得た。Unity 自身も GUID で同じ物と見る（2026-09-29 に Unity 2022.3 で確かめた）ので、GUID に置き換えた。
///
/// **ファイルだけを数える。**unitypackage のパスにはフォルダも入っているが、
/// <c>Assets/FUKA</c> のような上のフォルダは同じ作者の別の商品とも重なる。
///
/// **2つ以上の商品に同じパスか同じ GUID で入っているファイルは数えない。**lilToon を同梱したアバターのように、
/// 共有の部品を持つ商品は多い。数えると、lilToon が入っているだけで同梱した全商品が候補に出る。
/// ただし、それらの商品が**同じ unitypackage**（同じハッシュの zip の、同じ場所の物）から持っているなら数える
/// （セットと単品に同じ zip を付けた。ユーザ判断 2026-11-11。見分け方は <see cref="UnityPackageContents"/>）。
///
/// **欲しい物だけを選んで取り込むのは普通の使い方**なので、1つでもあれば候補に出し、割合で並べる。
/// </summary>
public static class UnityProjectMatcher
{
    /// <param name="projectPath">Unity プロジェクトのフォルダ（<c>Assets</c> を持つ所）。</param>
    /// <param name="items">商品ごとの、unitypackage に入っているアセット（複数のパッケージはまとめて渡す）。</param>
    /// <param name="exists">ファイルがあるか。既定はディスクを見る。試験では差し替える。</param>
    /// <param name="guids">プロジェクトの GUID → 今のパスの表。パスで見つからない物があったときだけ1度呼ぶ。既定はディスクを見る。</param>
    public static IReadOnlyList<UnityProjectMatch> Match(
        string projectPath,
        IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>> items,
        Func<string, bool>? exists = null,
        Func<IReadOnlyDictionary<string, string>>? guids = null)
        => Match(
            projectPath,
            items.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<UnityPackageContents>)[new UnityPackageContents(null, pair.Value)]),
            exists,
            guids);

    /// <param name="items">
    /// 商品ごとの、unitypackage ごとの中身。どの unitypackage かが分かる物（<see cref="UnityPackageContents.Source"/>）は、
    /// 同じ unitypackage を持つ商品どうしで共有の部品と見ない（セットと単品に同じ zip を付けた、など）。
    /// </param>
    public static IReadOnlyList<UnityProjectMatch> Match(
        string projectPath,
        IReadOnlyDictionary<string, IReadOnlyList<UnityPackageContents>> items,
        Func<string, bool>? exists = null,
        Func<IReadOnlyDictionary<string, string>>? guids = null)
    {
        exists ??= File.Exists;
        var disk = new Disk(projectPath, exists, guids);

        // 商品ごとのファイル1つずつに、それが入っていた unitypackage の印を集める（同じ商品の2つの包みに同じ物があってもよい）
        var entries = items
            .SelectMany(pair => pair.Value.SelectMany(contents => contents.Assets
                .Where(asset => IsFilePath(asset.Path))
                .Select(asset => new Entry(pair.Key, asset, contents.Source))))
            .ToList();
        var byItem = entries.ToLookup(entry => entry.ItemId, StringComparer.Ordinal);
        var files = items.Keys.ToDictionary(
            itemId => itemId,
            itemId => byItem[itemId].Select(entry => entry.Asset)
                .DistinctBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase).ToList());

        // 共有の部品は、同じパスで入っていても、同じ GUID で入っていても数えない（取り込むと同じ1つのファイルになる）
        var sharedPaths = SharedKeys(entries, entry => entry.Asset.Path);
        var sharedGuids = SharedKeys(entries.Where(entry => entry.Asset.Guid.Length > 0), entry => entry.Asset.Guid);

        var matches = new List<UnityProjectMatch>();
        foreach (var (itemId, assets) in files)
        {
            var own = assets
                .Where(asset => !sharedPaths.Contains(asset.Path) && (asset.Guid.Length == 0 || !sharedGuids.Contains(asset.Guid)))
                .ToList();
            if (own.Count == 0)
            {
                continue;
            }

            var present = own.Count(asset => disk.FindFile(asset) is not null);
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
    /// 「Unityで選択」で開くフォルダを決める。<paramref name="root"/> はパッケージの入り先（<c>Assets/FUKA</c>）。
    ///
    /// - 入り先に、その商品のファイルがパスで1つでもあればそこ
    /// - 無ければ GUID で探す。入り先のフォルダ自身の GUID が見つかればその場所（フォルダを丸ごと移した・名前を変えた。
    ///   ショップが並べるために付けた頭の記号（<c>_FUKA</c>）を利用者が消した物もここで見つかる）
    /// - フォルダが見つからなければ、GUID で見つかったファイルの今の場所から、入り先に当たるフォルダを推す
    ///   （<c>Assets/FUKA/撫で音/a.fbx</c> が <c>Assets/Mine/撫で音/a.fbx</c> にあれば <c>Assets/Mine</c>。下の段まで同じでなければそのファイルのあるフォルダ）。
    ///   いちばん多くのファイルが指す所を選ぶ
    /// - どれでも見つからなければ入り先のまま（開くかどうかは <c>UnityProjectTab.Plan</c> がディスクで決める）
    /// </summary>
    public static UnityRootLocation LocateRoot(
        string projectPath,
        string root,
        IReadOnlyList<UnityPackageAsset> assets,
        Func<string, bool>? exists = null,
        Func<string, bool>? directoryExists = null,
        Func<IReadOnlyDictionary<string, string>>? guids = null)
    {
        exists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        var disk = new Disk(projectPath, exists, guids);

        var prefix = root.TrimEnd('/') + "/";
        var inside = assets
            .Where(asset => asset.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && IsFilePath(asset.Path))
            .ToList();

        // 入り先に1つでもパスで入っていれば、そこを開く（移したのは一部だけ・別の商品と同じフォルダを使う、などはここに入る）
        if (inside.Count == 0 || inside.Any(asset => disk.FileAtPath(asset.Path)))
        {
            return new UnityRootLocation(root, false);
        }

        var table = disk.Guids();
        var folder = assets.FirstOrDefault(asset => string.Equals(asset.Path.TrimEnd('/'), root.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        if (folder is not null && table.TryGetValue(folder.Guid, out var movedFolder)
            && directoryExists(disk.Full(movedFolder)))
        {
            return new UnityRootLocation(movedFolder, true);
        }

        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in inside)
        {
            if (!table.TryGetValue(asset.Guid, out var now) || !exists(disk.Full(now)))
            {
                continue;
            }

            var below = asset.Path[root.TrimEnd('/').Length..]; // "/撫で音/a.fbx"
            var candidate = now.Length > below.Length && now.EndsWith(below, StringComparison.OrdinalIgnoreCase)
                ? now[..^below.Length]
                : now[..Math.Max(now.LastIndexOf('/'), 0)];
            if (candidate.Length > 0)
            {
                votes[candidate] = votes.GetValueOrDefault(candidate) + 1;
            }
        }

        if (votes.Count == 0)
        {
            return new UnityRootLocation(root, false);
        }

        var best = votes
            .OrderByDescending(vote => vote.Value)
            .ThenBy(vote => vote.Key, StringComparer.OrdinalIgnoreCase)
            .First().Key;
        return new UnityRootLocation(best, true);
    }

    private sealed record Entry(string ItemId, UnityPackageAsset Asset, string? Source);

    /// <summary>
    /// 共有の部品と見るパス（または GUID）：2つ以上の商品が持ち、**その全部の商品が持つ同じ unitypackage が無い**物。
    ///
    /// 全部の商品が同じ unitypackage から持っているなら、その中身はそれらの商品のどれにとっても自分の物なので数える
    /// （セットと単品に同じ zip を付けた。数えないと両方とも候補から落ちる。ユーザ判断 2026-11-11）。
    /// 1つでも別の unitypackage から持つ商品があれば、同梱の部品（lilToon など）と見て今までどおり数えない。
    /// 「包みの数が2以上」と数えないのは、1つの商品が2つの包みに同じ物を持つだけで落ちるため（それは今まで数えていた）。
    /// 印の無い物（<see cref="UnityPackageContents.Source"/> が null）は、その商品だけの印と見て、ほかのどれとも一致させない。
    /// </summary>
    private static HashSet<string> SharedKeys(IEnumerable<Entry> entries, Func<Entry, string> key)
        => entries
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Where(group =>
            {
                var sourcesByItem = group
                    .GroupBy(entry => entry.ItemId, StringComparer.Ordinal)
                    .Select(item => item.Select(entry => entry.Source ?? "\0" + item.Key).ToHashSet(StringComparer.Ordinal))
                    .ToList();
                if (sourcesByItem.Count < 2)
                {
                    return false;
                }

                var common = new HashSet<string>(sourcesByItem[0], StringComparer.Ordinal);
                foreach (var sources in sourcesByItem.Skip(1))
                {
                    common.IntersectWith(sources);
                }

                return common.Count == 0;
            })
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Unity 上のパスのうちファイルか。unitypackage はフォルダにも pathname を持つので、拡張子の有無で分ける。
    /// <c>Assets/</c> か <c>Packages/</c> の下だけを数える（ほかの場所には入らない）。
    /// </summary>
    private static bool IsFilePath(string path)
        => (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
           && Path.HasExtension(path);

    /// <summary>プロジェクトのディスクの見方。GUID の表は、1回の照らし合わせの中で1度だけ読む。</summary>
    private sealed class Disk(string projectPath, Func<string, bool> exists, Func<IReadOnlyDictionary<string, string>>? guids)
    {
        private readonly Func<IReadOnlyDictionary<string, string>> _guids = guids ?? (() => UnityProjectGuids.ForProject(projectPath).Current());
        private IReadOnlyDictionary<string, string>? _table;

        public string Full(string unityPath) => Path.Combine(projectPath, unityPath.Replace('/', Path.DirectorySeparatorChar));

        public IReadOnlyDictionary<string, string> Guids() => _table ??= _guids();

        /// <summary>パッケージの中のパスのままそこにあるか。</summary>
        public bool FileAtPath(string path) => exists(Full(path));

        /// <summary>今どこにあるか。パスで無ければ GUID で探す。どちらでも無ければ null。</summary>
        public string? FindFile(UnityPackageAsset asset)
        {
            if (exists(Full(asset.Path)))
            {
                return asset.Path;
            }

            return asset.Guid.Length > 0 && Guids().TryGetValue(asset.Guid, out var now) && exists(Full(now)) ? now : null;
        }
    }
}
