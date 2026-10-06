using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace Chmonos.Core.Services;

/// <summary>
/// zipの中の1つの <c>.unitypackage</c>。登録したフォルダの中の物も同じ形で持つ（<see cref="InFolder"/>）。
/// </summary>
/// <param name="ZipPath">包んでいるzipの絶対パス（<see cref="InFolder"/> ならフォルダの絶対パス）。</param>
/// <param name="EntryPath">zip内のパス。区切りは <c>/</c>（zipの規約どおり）。フォルダの中の物もフォルダからの場所を同じ書き方で持つ。</param>
/// <param name="SizeBytes">展開後の大きさ。</param>
public sealed record UnityPackageEntry(string ZipPath, string EntryPath, long SizeBytes)
{
    /// <summary>
    /// 登録したフォルダ（展開してある物）の中のファイルか（ユーザ判断 2026-10-05・メモ65-③）。**取り出さずにそのまま Unity へ渡す。**
    /// 包みの名前・候補の表記（「名前 (zip名)」の zip名がフォルダ名になる）・使った記録の場所の書き方は zip の中の物とそろえるので、
    /// 別の型にせず印1つで分ける。ハッシュ（<see cref="ZipHash"/>）は持たない（フォルダはハッシュを取らない）。
    /// </summary>
    public bool InFolder { get; init; }

    /// <summary>
    /// 包んでいる zip のハッシュ（手元のファイルの記録から作ったときだけ入れる）。あれば中身のパスを控え
    /// （<see cref="Storage.UnityPackagePathStore"/>）から引き、zip を解き直さない。
    /// </summary>
    public string? ZipHash { get; init; }

    /// <summary>
    /// Windowsに渡すパス。**zipを「フォルダ」として扱う仮想パス。**
    ///
    /// <c>D:\...\HeartBeatGimmick.zip\なめらか心音ギミック\VRCHeartRate_Installer.unitypackage</c>
    ///
    /// これを <c>ShellExecute</c> に渡すとWindowsが解決して既定のアプリへ渡す。
    /// **こちらでzipを展開する必要が無い。**一時ファイルの置き場所も後始末も要らない。
    /// </summary>
    public string VirtualPath => Path.Combine(ZipPath, EntryPath.Replace('/', '\\'));

    /// <summary>人に見せる名前。拡張子は落とす。</summary>
    public string Name => Path.GetFileNameWithoutExtension(EntryPath);

    /// <summary>zip内のどこに入っていたか。直下なら空。</summary>
    public string Folder
    {
        get
        {
            var slash = EntryPath.LastIndexOf('/');
            return slash < 0 ? string.Empty : EntryPath[..slash].Replace('/', '\\');
        }
    }
}

/// <summary>
/// unitypackage の中の1つのアセット。tar の <c>&lt;GUID&gt;/pathname</c> の組。
/// </summary>
/// <param name="Guid">Unity がアセットに付ける ID（32桁の16進）。取り込んだ先の <c>.meta</c> の <c>guid:</c> と同じ値になる。</param>
/// <param name="Path">Unity 上のパス（<c>Assets/FUKA/…</c>）。</param>
public sealed record UnityPackageAsset(string Guid, string Path);

/// <summary>zip の中の unitypackage 1つと、入る先（<see cref="UnityHandoff.PlacesOf(Models.LocalFileRecord)"/>）。</summary>
/// <param name="Roots">
/// 入る先の一番上（<see cref="UnityHandoff.DestinationRoots"/> の形）。item に書いてあればそれ、無ければ null（使う側が読んで埋める）。
/// </param>
public sealed record UnityPackagePlace(UnityPackageEntry Entry, IReadOnlyList<string>? Roots);

/// <summary>
/// 1回の操作・1回の画面表示の中の、中身の読み（2026-09-29）。**同じ zip の控えを1回だけ読んで、包みに配る。**
///
/// 控え（<c>unitypackages/&lt;ハッシュ&gt;.json</c>）は zip ごとに1つで、中の全部の包みの全部のパスを持つ（手元で最大 136KB ほど）。
/// 前は包みを1つ読むたびに控えを丸ごと読んでいて、1つの zip に包みが N 個あると N 回読んでいた（包み数の2乗の読み）。
///
/// **覚えるのは直前に読んだ zip の控え1つだけ。**呼ぶ側は包みを zip ごとに続けて並べる（<see cref="UnityHandoff.PlacesOf(Models.LocalFileRecord)"/> の順）ので足り、
/// プロジェクトの中を調べるような手元の全商品を読む操作でも、控えを全部抱え込まない。
/// 前の形の控え（読めない）は無いのと同じで、包みごとに zip を解いて控えを書き直す（<see cref="Storage.UnityPackagePathStore.Add"/>）作りは変えない。
/// </summary>
public sealed class UnityPackageReads
{
    private readonly object _gate = new();
    private readonly Func<string, IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>>?> _loadStored;
    private readonly Func<UnityPackageEntry, IReadOnlyList<UnityPackageAsset>> _readZip;
    private readonly Action<string, string, IReadOnlyList<UnityPackageAsset>> _addStored;
    private string? _hash;
    private Dictionary<string, IReadOnlyList<UnityPackageAsset>>? _stored;

    public UnityPackageReads()
        : this(UnityHandoff.LoadStored, UnityHandoff.ReadAssetsFromDisk, UnityHandoff.AddStored)
    {
    }

    /// <param name="loadStored">控えを読む（試験ではディスクを見ない物に差し替える）。</param>
    /// <param name="readZip">zip を解いて中身を読む（同上）。</param>
    /// <param name="addStored">zip から読んだ物を控えに足す（同上）。</param>
    internal UnityPackageReads(
        Func<string, IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>>?> loadStored,
        Func<UnityPackageEntry, IReadOnlyList<UnityPackageAsset>> readZip,
        Action<string, string, IReadOnlyList<UnityPackageAsset>> addStored)
    {
        _loadStored = loadStored;
        _readZip = readZip;
        _addStored = addStored;
    }

    /// <summary>中身のアセットを全部返す（<see cref="UnityHandoff.ReadAssets(UnityPackageEntry)"/> と同じ答え）。</summary>
    public IReadOnlyList<UnityPackageAsset> ReadAssets(UnityPackageEntry package) => UnityHandoff.ReadAssets(package, remember: true, this);

    /// <summary>入る先の一番上（<see cref="UnityHandoff.ReadDestinations"/> と同じ答え）。</summary>
    public IReadOnlyList<string> ReadDestinations(UnityPackageEntry package)
        => UnityHandoff.DestinationRoots(ReadAssets(package).Select(asset => asset.Path));

    /// <summary>控えにある、この包みの中身。控えに無ければ null。同じ zip の控えは読み直さない。</summary>
    internal IReadOnlyList<UnityPackageAsset>? Stored(string hash, string entry)
    {
        lock (_gate)
        {
            if (!string.Equals(_hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                _hash = hash;
                _stored = _loadStored(hash)?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }

            return _stored is not null && _stored.TryGetValue(entry, out var assets) ? assets : null;
        }
    }

    internal IReadOnlyList<UnityPackageAsset> ReadZip(UnityPackageEntry package) => _readZip(package);

    /// <summary>zip から読んだ物を控えに足し、手元の写しにも入れる（同じ操作でもう一度引いても zip を解かない）。</summary>
    internal void Added(string hash, string entry, IReadOnlyList<UnityPackageAsset> assets)
    {
        _addStored(hash, entry, assets);
        lock (_gate)
        {
            if (string.Equals(_hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                (_stored ??= new Dictionary<string, IReadOnlyList<UnityPackageAsset>>(StringComparer.Ordinal))[entry] = assets;
            }
        }
    }
}

/// <summary>Unity の窓の題から言い当てたプロジェクト（<see cref="UnityHandoff.IdentifyProject"/>）。</summary>
/// <param name="Name">プロジェクト名。題から読めなければ null。</param>
/// <param name="Path">場所。一覧で言い当てられたときだけ。</param>
/// <param name="IsAmbiguous">同じ名前の開いているプロジェクトが複数あって、どれの窓か見分けられない。</param>
public sealed record UnityWindowProject(string? Name, string? Path, bool IsAmbiguous);

/// <summary>
/// zipの中の <c>.unitypackage</c> をUnityへ渡すための下ごしらえ。
///
/// **実際に渡すのはWindowsの仕事。**こちらは「何を渡せるか」を数え、
/// 渡すためのパスを組み、Unityが開いているかを読むだけ。
/// 経路の裏付けは <c>docs/history/unity-handoff.md</c> にある。
/// </summary>
public static class UnityHandoff
{
    public const string PackageExtension = ".unitypackage";

    /// <summary>
    /// 1つのzipに入っている数の上限。これを超えるものは実データに無く、
    /// 壊れたzipや別物を掴んだときに画面が埋まるのを防ぐだけの歯止め。
    /// </summary>
    internal const int MaxPackages = 64;

    /// <summary>
    /// zipの中の <c>.unitypackage</c> を、zipに入っている順で返す。
    ///
    /// **並べ替えない。**手元の実データでは10件中2件に2つ入っていて、
    /// どちらも片方が依存物だった（<c>VRCHeartRate_Installer</c>、<c>BlendShare-0.0.10-User</c>）。
    /// 依存を先に入れないと本体が通らないが、**2件から順序の規則は決められない。**
    /// 名前や大きさで当てにいくと、外したときに黙って壊れる。
    /// 人に選ばせて、こちらは並びを保つ。
    ///
    /// 読めないzipでは空を返す。**投げない**——
    /// 商品ページを開くたびに数えるので、1つ壊れていても画面は出したい。
    /// </summary>
    public static IReadOnlyList<UnityPackageEntry> FindPackages(string zipPath)
    {
        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);

            var found = new List<UnityPackageEntry>();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found.Add(new UnityPackageEntry(zipPath, entry.FullName, entry.Length));
                if (found.Count >= MaxPackages)
                {
                    break;
                }
            }

            return found;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
                                              or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>
    /// Unityのどこに入るかを読む。<c>Assets/Piyo_crafts</c> のような、入る先の一番上を多い順に返す。
    ///
    /// **取り込んだ後に「何という名前で入ったか」を忘れる**（友人の要望）。
    /// <c>.unitypackage</c> は tar.gz で、アセットごとの <c>pathname</c> に Unity 上のパスが入っている。
    /// tar は先頭から順に読むしかないので、パスを集めるには最後まで解くことになる
    /// （手元の実測で 40MB の物が 0.2 秒ほど）。画面を組むときに同期で読まず、裏で読む。
    ///
    /// **Assets/ に限らない。**BlendShare のように Packages/ に入る物があり、
    /// Assets/ だけ見て「入っていない」と取り違えたことがある。
    ///
    /// 読めないときは空を返す。投げない——入る先が分からなくても送ることはできる。
    /// </summary>
    public static IReadOnlyList<string> ReadDestinations(UnityPackageEntry package)
        => DestinationRoots(ReadAssetPaths(package));

    /// <summary>
    /// 中身のアセットのパスを全部返す（<c>Assets/FUKA/撫で音/…</c> のまま）。<see cref="ReadAssets"/> のパスだけ。
    ///
    /// 連続で送るとき、Unity のログの <c>Start importing &lt;パス&gt;</c> が
    /// **送った物の取り込みかを見分けるのに使う**（§11-3）。Editor.log は開いている全エディタが共有するので、
    /// 完了の行だけでは誰の物か分からない。
    /// </summary>
    public static IReadOnlyList<string> ReadAssetPaths(UnityPackageEntry package)
        => ReadAssets(package).Select(asset => asset.Path).ToList();

    /// <summary>
    /// 中身のアセットを全部返す（GUID と Unity 上のパスの組）。
    ///
    /// **GUID も持つ**（2026-09-29）。利用者がプロジェクトの中でフォルダを移した・名前を変えた物は、パスでは見つからないが、
    /// Unity は取り込んだアセットの <c>.meta</c> にパッケージと同じ GUID を書き、移しても変えない（<see cref="UnityProjectGuids"/>）。
    ///
    /// 読めないときは空を返す。投げない。
    ///
    /// **一度読んだ結果は覚えておく。**unitypackage は最後まで展開しないとパスが揃わず、4K テクスチャを大量に同梱した物では
    /// 1GB あたり約2.8秒かかる（実測）。商品ページ・改変の画面・「Unityで選択」・プロジェクトの中を調べる・連続送りの前、と
    /// 同じ物を何度も読むので、zip の場所・中の名前・大きさ・更新時刻が同じなら読み直さない。覚えるのはパスと GUID の一覧だけで小さい
    /// </summary>
    public static IReadOnlyList<UnityPackageAsset> ReadAssets(UnityPackageEntry package)
        => ReadAssets(package, remember: true);

    /// <param name="remember">
    /// 読んだ結果をメモリの表（<see cref="PathMemory"/>）に覚えるか。取り込みの裏の読み取り（<see cref="UnityPackageCatalog.ReadAsync"/>）は
    /// 手元の全部の unitypackage を1回ずつ読むだけで、結果は控えのファイルに書くので、表に入れない（入れると画面が使う物を押し出す）。
    /// </param>
    internal static IReadOnlyList<UnityPackageAsset> ReadAssets(UnityPackageEntry package, bool remember)
        => ReadAssets(package, remember, new UnityPackageReads());

    /// <param name="reads">
    /// 控えの読みを配る入れ物（<see cref="UnityPackageReads"/>）。同じ zip の包みを続けて読むとき、控えを1回だけ読む。
    /// </param>
    internal static IReadOnlyList<UnityPackageAsset> ReadAssets(UnityPackageEntry package, bool remember, UnityPackageReads reads)
    {
        FileInfo? zip = null;
        try
        {
            // フォルダの中の物は、そのファイル自身の大きさ・更新時刻で覚えた物が今も正しいかを見る
            zip = new FileInfo(package.InFolder ? package.VirtualPath : package.ZipPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or UnauthorizedAccessException
                                              or PathTooLongException)
        {
        }

        var key = (Zip: package.ZipPath.ToUpperInvariant(), Entry: package.EntryPath);
        if (zip is { Exists: true } && PathMemory.TryGet(key, zip.Length, zip.LastWriteTimeUtc) is { } cached)
        {
            return cached;
        }

        // 取り込みの裏で読んだ控え（2026-09-13）。ハッシュが同じなら中身は変わらないので、zip を解かずに引ける
        if (package.ZipHash is { } hash && reads.Stored(hash, package.EntryPath) is { } storedAssets)
        {
            if (remember)
            {
                Remember(key, zip, storedAssets);
            }

            return storedAssets;
        }

        var assets = reads.ReadZip(package);
        if (remember)
        {
            Remember(key, zip, assets);
        }

        // 取り込みの裏より先に読んだ物も控えに足す（次の起動では解かずに済む）
        if (package.ZipHash is { } readHash && assets.Count > 0)
        {
            reads.Added(readHash, package.EntryPath, assets);
        }

        return assets;
    }

    internal static IReadOnlyDictionary<string, IReadOnlyList<UnityPackageAsset>>? LoadStored(string hash) => s_pathStore?.Load(hash);

    internal static void AddStored(string hash, string entry, IReadOnlyList<UnityPackageAsset> assets)
    {
        if (s_pathStore is not { } store)
        {
            return;
        }

        try
        {
            store.Add(hash, entry, assets);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 控えは無くても動く。次に読んだときに足し直す
        }
    }

    /// <summary>
    /// 手元のファイルが zip なら、中の Unity へ送れる物と、分かっていれば入る先を返す（zip に入っている順）。
    ///
    /// **item に書いてある要約（<see cref="Models.LocalFileRecord.UnityPackages"/>）があれば zip を開かない**（2026-09-29）。
    /// 前は商品ページを開くたびに zip を開いて中の一覧を読み、包みごとに控えの JSON を丸ごと読んでいた。大きな zip が HDD にあると開くだけで重い。
    /// 中身は zip のハッシュで決まり、要約はそのハッシュについて取り込みの裏で書いた物なので、zip を読み直しても同じ答えになる
    /// （同じ場所の zip が差し替えられた物は別のハッシュ＝別の手元のファイルで、取り込み直すまでこの記録のハッシュの物ではない。
    /// 前の読み方もこの記録のハッシュで控えを引いていた）。
    ///
    /// **zip が在ることは今までどおり見る**（無ければ送れないので行を出さない）。
    /// </summary>
    public static IReadOnlyList<UnityPackagePlace> PlacesOf(Models.LocalFileRecord file) => PlacesOf(file, File.Exists, FindPackages);

    /// <param name="exists">ファイルが在るか（試験ではディスクを見ない物に差し替える）。</param>
    /// <param name="find">zip を開いて中の unitypackage を数える（同上）。</param>
    internal static IReadOnlyList<UnityPackagePlace> PlacesOf(
        Models.LocalFileRecord file, Func<string, bool> exists, Func<string, IReadOnlyList<UnityPackageEntry>> find)
    {
        var zip = file.Paths.FirstOrDefault(exists);
        if (zip is null || !zip.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (KnownPackages(file) is { } known)
        {
            // 大きさは要約に無い。行には出さず、送るときも zip の中の場所で取り出すので 0 にする（改変の記録から作る物と同じ）
            return known
                .Select(summary => new UnityPackagePlace(new UnityPackageEntry(zip, summary.Entry, 0) { ZipHash = file.Hash }, summary.Roots ?? []))
                .ToList();
        }

        return find(zip).Select(package => new UnityPackagePlace(package with { ZipHash = file.Hash }, null)).ToList();
    }

    /// <summary>
    /// 登録したフォルダの中の、Unity へ送れる物（ユーザ判断 2026-10-05・メモ65-③）。数えたときに記録した一覧
    /// （<see cref="Models.LocalFolderRecord.UnityPackages"/>）から作り、フォルダの中は並べ直さない。
    /// **ファイルが今も在ることは1つずつ見る**（zip の <see cref="PlacesOf(Models.LocalFileRecord)"/> が zip の在ることを見るのと同じ。無ければ送れないので出さない）。
    /// 入る先は記録に無いので null（使う側が読んで埋める）。
    /// </summary>
    public static IReadOnlyList<UnityPackagePlace> PlacesOf(Models.LocalFolderRecord folder) => PlacesOf(folder, File.Exists);

    /// <param name="exists">ファイルが在るか（試験ではディスクを見ない物に差し替える）。</param>
    internal static IReadOnlyList<UnityPackagePlace> PlacesOf(Models.LocalFolderRecord folder, Func<string, bool> exists)
        => (folder.UnityPackages ?? [])
            .Select(entry => new UnityPackageEntry(folder.Path, entry, 0) { InFolder = true })
            .Where(package => exists(package.VirtualPath))
            .Select(package => new UnityPackagePlace(package, null))
            .ToList();

    /// <summary>
    /// item の要約を、zip の中の順に並べて返す。**要約が zip の中の unitypackage を全部覆っていなければ null**（zip を読む）。
    ///
    /// 要約は控えから写すが、控えは商品ページなどで包みを1つずつ足しても作られる（<see cref="Storage.UnityPackagePathStore.Add"/>）。
    /// 取り込みの裏はその控えを「ある」と見て読み直さないので、一部の包みしか載っていない要約があり得る。
    /// 中身の一覧（<see cref="Models.LocalFileRecord.Contents"/>。zip の中の順）と名前がそろうときだけ使い、そろわなければ今までどおり zip を読む
    /// （包みが黙って消えるより、1回 zip を開く方がよい）。
    /// </summary>
    internal static IReadOnlyList<Models.UnityPackageSummary>? KnownPackages(Models.LocalFileRecord file)
    {
        if (file.UnityPackages is not { } summaries)
        {
            return null;
        }

        var inZip = PackageEntriesIn(file);
        var byEntry = summaries
            .GroupBy(summary => summary.Entry, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (inZip.Count != byEntry.Count || !inZip.All(byEntry.ContainsKey))
        {
            return null;
        }

        return inZip.Select(name => byEntry[name]).ToList();
    }

    /// <summary>
    /// 中身の一覧（<see cref="Models.LocalFileRecord.Contents"/>）にある unitypackage の zip の中の場所（zip の中の順）。
    /// zip を開いて数える <see cref="FindPackages"/> と同じ数で切る（切らないと、多すぎる zip は要約も控えも永久にそろわないと見える）。
    /// </summary>
    public static IReadOnlyList<string> PackageEntriesIn(Models.LocalFileRecord file)
        => file.Contents
            .Where(name => name.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxPackages)
            .ToList();

    private static void Remember((string Zip, string Entry) key, FileInfo? zip, IReadOnlyList<UnityPackageAsset> assets)
    {
        if (zip is not { Exists: true } || assets.Count == 0)
        {
            return;
        }

        PathMemory.Put(key, zip.Length, zip.LastWriteTimeUtc, assets);
    }

    private static Storage.UnityPackagePathStore? s_pathStore;

    /// <summary>中身のパスの控えを使う。アプリの起動時に1度渡す。渡さなければ（試験など）毎回 zip を解く。</summary>
    public static void UsePathStore(Storage.UnityPackagePathStore? store) => s_pathStore = store;

    /// <summary>読んだパスの一覧。キーは zip の場所（大文字小文字をそろえる）と中の名前。</summary>
    private static readonly PathTable PathMemory = new(PathMemoryBudgetBytes);

    /// <summary>
    /// 覚えておくパスの一覧の合計の上限（文字列の大きさの見積もり）。**件数ではなく大きさで決める**（2026-09-24）。
    ///
    /// 前は5000件まで覚えてから丸ごと忘れていた。大きな unitypackage は1件で数千のパス（数百KB〜数MB）を持つので、
    /// 件数の上限では何百MBでも握れた。取り込みの裏で手元の全部を読むとそのたびに表が埋まってもいた。
    /// 16MB は、画面が開く商品（商品ページ・改変・プロジェクトの突き合わせ）で使う分には足り、目標のメモリ（300〜400MB）の5%に収まる。
    /// </summary>
    private const long PathMemoryBudgetBytes = 16L * 1024 * 1024;

    /// <summary>
    /// 大きさの上限つきで、古く使った物から捨てる表。画面・Unity の送り・プロジェクトの突き合わせが別々のスレッドから引くので錠で守る。
    /// </summary>
    internal sealed class PathTable(long budgetBytes)
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Zip, string Entry), LinkedListNode<Kept>> _byKey = new();
        private readonly LinkedList<Kept> _recent = new();
        private long _bytes;

        private sealed record Kept((string Zip, string Entry) Key, long Length, DateTime Written, IReadOnlyList<UnityPackageAsset> Assets, long Bytes);

        public long Bytes
        {
            get
            {
                lock (_gate)
                {
                    return _bytes;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _byKey.Count;
                }
            }
        }

        /// <summary>zip の大きさと更新時刻が覚えたときと同じなら返す（使った物として前へ出す）。</summary>
        public IReadOnlyList<UnityPackageAsset>? TryGet((string Zip, string Entry) key, long length, DateTime written)
        {
            lock (_gate)
            {
                if (!_byKey.TryGetValue(key, out var node))
                {
                    return null;
                }

                if (node.Value.Length != length || node.Value.Written != written)
                {
                    return null;
                }

                _recent.Remove(node);
                _recent.AddFirst(node);
                return node.Value.Assets;
            }
        }

        public void Put((string Zip, string Entry) key, long length, DateTime written, IReadOnlyList<UnityPackageAsset> assets)
        {
            var bytes = SizeOf(key, assets);
            lock (_gate)
            {
                if (_byKey.Remove(key, out var old))
                {
                    _recent.Remove(old);
                    _bytes -= old.Value.Bytes;
                }

                // 1件だけで上限を超える物は覚えない（覚えると他を全部押し出す。読み直せば戻る）
                if (bytes > budgetBytes)
                {
                    return;
                }

                while (_bytes + bytes > budgetBytes && _recent.Last is { } oldest)
                {
                    _recent.RemoveLast();
                    _byKey.Remove(oldest.Value.Key);
                    _bytes -= oldest.Value.Bytes;
                }

                _byKey[key] = _recent.AddFirst(new Kept(key, length, written, assets, bytes));
                _bytes += bytes;
            }
        }

        /// <summary>
        /// 文字列の大きさの見積もり。1文字2バイトと、1件あたりの入れ物の分
        /// （パスと GUID の文字列2本の頭で約40バイト、組の record で約40バイト）。
        /// </summary>
        private static long SizeOf((string Zip, string Entry) key, IReadOnlyList<UnityPackageAsset> assets)
        {
            long bytes = (key.Zip.Length + key.Entry.Length) * 2 + 64;
            foreach (var asset in assets)
            {
                bytes += (asset.Path.Length + asset.Guid.Length) * 2 + 80;
            }

            return bytes;
        }
    }

    internal static IReadOnlyList<UnityPackageAsset> ReadAssetsFromDisk(UnityPackageEntry package)
    {
        try
        {
            // フォルダの中の物は zip を開かず、そのファイルを読む（中身の形は同じ）
            if (package.InFolder)
            {
                using var file = File.OpenRead(package.VirtualPath);
                return ReadAssetsFrom(file);
            }

            using var archive = ZipFile.Open(package.ZipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);
            if (archive.GetEntry(package.EntryPath) is not { } entry)
            {
                return [];
            }

            using var stream = entry.Open();
            return ReadAssetsFrom(stream);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException
                                              or ArgumentException or ArithmeticException or InvalidOperationException
                                              or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>unitypackage（tar.gz）の流れから、中身のアセットを読む。</summary>
    private static List<UnityPackageAsset> ReadAssetsFrom(Stream stream)
    {
        {
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            var assets = new List<UnityPackageAsset>();
            while (tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                if (tarEntry.DataStream is null || !tarEntry.Name.EndsWith("/pathname", StringComparison.Ordinal))
                {
                    continue;
                }

                // 名前は "<GUID>/pathname"。頭に "./" が付いた物もある（UnityPackageInspector と同じ読み方）
                var parts = tarEntry.Name.TrimStart('.', '/').Split('/');
                if (parts.Length != 2 || parts[0].Length == 0)
                {
                    continue;
                }

                // 長さの上限を超える物は読まずに飛ばす（Resolution.UnityPackagePathname）
                if (Resolution.UnityPackagePathname.Read(tarEntry) is { } path)
                {
                    assets.Add(new UnityPackageAsset(parts[0].ToLowerInvariant(), path));
                }

                if (assets.Count >= MaxAssetPaths)
                {
                    break;
                }
            }

            return assets;
        }
    }

    /// <summary>
    /// 1つの物が持つアセットの数の上限。実データの最多は105件で、
    /// 壊れた物を掴んだときに読み続けないための歯止め（<c>UnityPackageInspector</c> と同じ値）。
    /// </summary>
    private const int MaxAssetPaths = 20000;

    /// <summary>
    /// Unity上のパスを、入る先の一番上（最初の2段）にまとめて多い順に並べる。
    ///
    /// **2段で止める。**Unity の Project 窓で最初に探すのがそこで、
    /// 手元の12件はすべて1か所（<c>Assets/FUKA</c> など）に収まっていた。
    /// </summary>
    public static IReadOnlyList<string> DestinationRoots(IEnumerable<string> assetPaths)
        => assetPaths
            .Select(path => path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Where(segments => segments.Length >= 2)
            .Select(segments => $"{segments[0]}/{segments[1]}")
            .GroupBy(root => root, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .ToList();

    /// <summary>
    /// 包み全部の入る先（改変の画面の構成物の欄）。**item に書いてある入る先で言い切れるときは中身を読まない。**
    ///
    /// 入る先は全部のパスを数えて多い順に並べる（<see cref="DestinationRoots(IEnumerable{string})"/>）ので、包みが2つ以上で入る先が分かれると、
    /// 包みごとの要約（数を持たない）からは並びを決められない。そのときと、書いていない包みがあるときだけ読む（控えは zip ごとに1回）。
    /// 並びを変えると、3か所を超えたときに出る3か所が変わる。
    /// </summary>
    public static IReadOnlyList<string> DestinationRoots(IReadOnlyList<UnityPackagePlace> places, UnityPackageReads reads)
    {
        if (places.All(place => place.Roots is not null))
        {
            if (places.Count == 1)
            {
                return places[0].Roots!;
            }

            var union = places.SelectMany(place => place.Roots!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (union.Count <= 1)
            {
                return union;
            }
        }

        return DestinationRoots(places.SelectMany(place => reads.ReadAssets(place.Entry)).Select(asset => asset.Path));
    }

    /// <summary>入る先を1文にする。多すぎるときは3か所まで出して残りは数だけ言う。</summary>
    public static string DescribeDestinations(IReadOnlyList<string> roots) => roots.Count switch
    {
        0 => string.Empty,
        <= 3 => $"{string.Join("・", roots)} に入ります",
        _ => $"{string.Join("・", roots.Take(3))} ほか {roots.Count - 3} か所に入ります",
    };

    /// <summary>
    /// メニューの項目名をそろえる（#69）。Windows のメニューの文字には、キーの印の <c>&amp;</c> と、
    /// タブの後ろのショートカット表記（<c>Ctrl+R</c>）が入っている。比べる前にどちらも落とす。
    /// </summary>
    public static string NormalizeMenuText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var tab = text.IndexOf('\t');
        var head = tab < 0 ? text : text[..tab];
        return head.Replace("&", string.Empty).Trim();
    }

    /// <summary>
    /// 「Assets &gt; Import Package &gt; Custom Package...」の段ごとの名前。
    /// エディタを日本語にしている人のために日本語の名前も並べる（日本語のエディタでは確かめていない）。
    /// </summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> CustomPackageMenuPath =
    [
        ["Assets", "アセット"],
        ["Import Package", "パッケージをインポート"],
        ["Custom Package...", "カスタムパッケージ..."],
    ];

    /// <summary>「Window &gt; General &gt; Project」。プロジェクトタブにフォーカスを移す（改変の画面の「Unityで選択」）。</summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> ProjectWindowMenuPath =
    [
        ["Window", "ウィンドウ"],
        ["General", "一般"],
        ["Project", "プロジェクト"],
    ];

    /// <summary>「Edit &gt; Find」（Ctrl+F と同じ）。フォーカスのあるタブの検索欄にフォーカスを移す。</summary>
    public static readonly IReadOnlyList<IReadOnlyList<string>> FindMenuPath =
    [
        ["Edit", "編集"],
        ["Find", "検索"],
    ];

    /// <summary>
    /// Unityエディタの窓のタイトルからプロジェクト名を取る。
    ///
    /// <c>kip01 - SampleScene - Windows, Mac, Linux - Unity 2022.3.22f1 &lt;DX11&gt;</c> → <c>kip01</c>
    ///
    /// **窓のタイトルから読む。**起動引数の <c>-projectPath</c> にも入っているが、
    /// 他プロセスの引数を読むにはWMIが要る（依存が1つ増える）。
    /// 見せたいのはプロジェクト名そのもので、タイトルの先頭がまさにそれ。
    ///
    /// プロジェクト名に <c>" - "</c> が入っていると先頭だけを拾って短くなる。短い名前が別のプロジェクトと重なると取り違えるので、
    /// 一覧（Hub・VCC）に載っているプロジェクトは <see cref="ProjectFromWindowTitle"/> で言い当てる。これはその控え。
    ///
    /// **<c>" - "</c> を含まない題はプロジェクト名として読まない。**起動中やコンパイル中の題は
    /// 「Compiling Scripts」「Reloading Domain」のような作業の名前で、これを名前として読むと
    /// 開いているのに見つからず、起動し直して弾かれていた（実機で観測）。
    /// </summary>
    public static string? ProjectNameFromWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cut = title.IndexOf(" - ", StringComparison.Ordinal);
        if (cut < 0)
        {
            return null;
        }

        var name = title[..cut].Trim();
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// 窓の題から、知っているプロジェクト（Hub・VCC の一覧）のどれかを言い当てる。場所も返す。
    ///
    /// **題の先頭で切るだけだと、名前に <c>" - "</c> を含むプロジェクトを取り違える**（2026-09-19 実機：
    /// 「cleanTest - コピー」を開いているのに「cleanTest」と読み、同じ名前の別のプロジェクトを調べて「まだ入っていません」と言っていた）。
    /// 題が「名前 + <c>" - "</c>」で始まる一覧の名前を探し、開いている（<paramref name="isOpen"/>）方を先に、次に長い名前を選ぶ
    /// ——短い方は長い方の頭と重なるので、両方当たったら長い方が本物のことが多い。一覧に無ければ、先頭で切る元の読み方に戻る（場所は分からない）
    /// </summary>
    public static (string? Name, string? Path) ProjectFromWindowTitle(
        string? title, IEnumerable<string> knownProjectPaths, Func<string, bool> isOpen)
    {
        var found = IdentifyProject(title, knownProjectPaths, isOpen);
        return (found.Name, found.Path);
    }

    /// <summary>
    /// <see cref="ProjectFromWindowTitle"/> に、**見分けられなかった**印を足した物。
    /// </summary>
    /// <remarks>
    /// 同じ名前のフォルダのプロジェクト（D:\A\proj と E:\B\proj）を両方開くと、題はどちらも「proj - …」で、
    /// 開いているかでも名前の長さでも差が付かない。前は一覧の先の方に決め打ちしていたので、2つのエディタを同じ場所と読み、
    /// 片方へ送る・調べるつもりがもう片方に働いていた。題からは見分ける手掛かりが無いので、場所は出さずに
    /// 見分けられないと返す（使う側は決め打ちせず、選ばせるか見分けられないと言う）。
    /// </remarks>
    public static UnityWindowProject IdentifyProject(
        string? title, IEnumerable<string> knownProjectPaths, Func<string, bool> isOpen)
    {
        if (ProjectNameFromWindowTitle(title) is not { } headName)
        {
            return new UnityWindowProject(null, null, false);
        }

        var ranked = knownProjectPaths
            .Select(path => (Path: path, Name: System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))))
            .Where(candidate => candidate.Name.Length > 0
                && title!.StartsWith(candidate.Name + " - ", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => (candidate.Path, candidate.Name, Open: isOpen(candidate.Path)))
            .OrderByDescending(candidate => candidate.Open)
            .ThenByDescending(candidate => candidate.Name.Length)
            .ToList();

        if (ranked.Count == 0)
        {
            return new UnityWindowProject(headName, null, false);
        }

        var best = ranked[0];

        // 題の頭に当たって長さが同じなら名前も同じ。開いているかまで同じで場所が違えば、題からは見分けられない
        var ambiguous = ranked.Skip(1).Any(other => other.Open == best.Open
            && other.Name.Length == best.Name.Length
            && !PathText.Same(other.Path.TrimEnd('\\', '/'), best.Path.TrimEnd('\\', '/')));

        return ambiguous
            ? new UnityWindowProject(best.Name, null, true)
            : new UnityWindowProject(best.Name, best.Path, false);
    }

    /// <summary>
    /// 起動引数から <c>-projectPath</c> を取る。窓のタイトルが取れないときの控え。
    /// 引用符付き・無しの両方を受ける。
    /// </summary>
    public static string? ProjectPathFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        const string flag = "-projectPath";
        var at = commandLine.IndexOf(flag, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return null;
        }

        var rest = commandLine[(at + flag.Length)..].TrimStart();
        if (rest.Length == 0)
        {
            return null;
        }

        if (rest[0] == '"')
        {
            var close = rest.IndexOf('"', 1);
            return close < 0 ? null : Blank(rest[1..close]);
        }

        var space = rest.IndexOf(' ');
        return Blank(space < 0 ? rest : rest[..space]);

        static string? Blank(string value) => value.Length == 0 ? null : value;
    }
}
