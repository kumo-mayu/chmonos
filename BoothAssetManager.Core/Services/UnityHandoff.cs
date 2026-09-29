using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// zipの中の1つの <c>.unitypackage</c>。
/// </summary>
/// <param name="ZipPath">包んでいるzipの絶対パス。</param>
/// <param name="EntryPath">zip内のパス。区切りは <c>/</c>（zipの規約どおり）。</param>
/// <param name="SizeBytes">展開後の大きさ。</param>
public sealed record UnityPackageEntry(string ZipPath, string EntryPath, long SizeBytes)
{
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
    private const int MaxPackages = 64;

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
    {
        FileInfo? zip = null;
        try
        {
            zip = new FileInfo(package.ZipPath);
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
        if (package.ZipHash is { } hash
            && s_pathStore?.Load(hash) is { } stored
            && stored.TryGetValue(package.EntryPath, out var storedAssets))
        {
            if (remember)
            {
                Remember(key, zip, storedAssets);
            }

            return storedAssets;
        }

        var assets = ReadAssetsFromDisk(package);
        if (remember)
        {
            Remember(key, zip, assets);
        }

        // 取り込みの裏より先に読んだ物も控えに足す（次の起動では解かずに済む）
        if (package.ZipHash is { } readHash && assets.Count > 0 && s_pathStore is { } store)
        {
            try
            {
                store.Add(readHash, package.EntryPath, assets);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 控えは無くても動く。次に読んだときに足し直す
            }
        }

        return assets;
    }

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

    private static IReadOnlyList<UnityPackageAsset> ReadAssetsFromDisk(UnityPackageEntry package)
    {
        try
        {
            using var archive = ZipFile.Open(package.ZipPath, ZipArchiveMode.Read, BoothZipInspector.ZipNameEncoding.Instance);
            if (archive.GetEntry(package.EntryPath) is not { } entry)
            {
                return [];
            }

            using var stream = entry.Open();
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

                using var reader = new StreamReader(tarEntry.DataStream, Encoding.UTF8, false, 1024, leaveOpen: true);
                if (reader.ReadLine()?.Trim() is { Length: > 0 } path)
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
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException
                                              or ArgumentException or ArithmeticException or InvalidOperationException
                                              or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
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
