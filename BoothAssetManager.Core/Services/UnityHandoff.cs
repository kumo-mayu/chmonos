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
/// zipの中の <c>.unitypackage</c> をUnityへ渡すための下ごしらえ。
///
/// **実際に渡すのはWindowsの仕事。**こちらは「何を渡せるか」を数え、
/// 渡すためのパスを組み、Unityが開いているかを読むだけ。
/// 経路の裏付けは <c>設計詳細_Unityへの受け渡し.md</c> にある。
/// </summary>
public static class UnityHandoff
{
    public const string PackageExtension = ".unitypackage";

    /// <summary>
    /// 1つのzipに入っている数の上限。これを超えるものは実データに無く、
    /// 壊れたzipや別物を掴んだときに画面が埋まるのを防ぐだけの歯止め。
    /// </summary>
    private const int MaxPackages = 64;

    static UnityHandoff()
    {
        // ZIPエントリ名のCP932読み取りに必要。UnityPackageInspector と同じ理由で、
        // 登録し忘れると Encoding.GetEncoding(932) が投げて静かに空を返すことになる
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

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
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, Encoding.GetEncoding(932));

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
    /// 中身のアセットのパスを全部返す（<c>Assets/FUKA/撫で音/…</c> のまま）。
    ///
    /// 連続で送るとき、Unity のログの <c>Start importing &lt;パス&gt;</c> が
    /// **送った物の取り込みかを見分けるのに使う**（§11-3）。Editor.log は開いている全エディタが共有するので、
    /// 完了の行だけでは誰の物か分からない。
    ///
    /// 読めないときは空を返す。投げない。
    ///
    /// **一度読んだ結果は覚えておく。**unitypackage は最後まで展開しないとパスが揃わず、4K テクスチャを大量に同梱した物では
    /// 1GB あたり約2.8秒かかる（実測）。商品ページ・改変の画面・「Unityで選択」・プロジェクトの中を調べる・連続送りの前、と
    /// 同じ物を何度も読むので、zip の場所・中の名前・大きさ・更新時刻が同じなら読み直さない。覚えるのはパスの一覧だけで小さい
    /// </summary>
    public static IReadOnlyList<string> ReadAssetPaths(UnityPackageEntry package)
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
        if (zip is { Exists: true }
            && PathCache.TryGetValue(key, out var cached)
            && cached.Length == zip.Length
            && cached.Written == zip.LastWriteTimeUtc)
        {
            return cached.Paths;
        }

        // 取り込みの裏で読んだ控え（2026-09-13）。ハッシュが同じなら中身は変わらないので、zip を解かずに引ける
        if (package.ZipHash is { } hash
            && s_pathStore?.Load(hash) is { } stored
            && stored.TryGetValue(package.EntryPath, out var storedPaths))
        {
            Remember(key, zip, storedPaths);
            return storedPaths;
        }

        var paths = ReadAssetPathsFromDisk(package);
        Remember(key, zip, paths);

        // 取り込みの裏より先に読んだ物も控えに足す（次の起動では解かずに済む）
        if (package.ZipHash is { } readHash && paths.Count > 0 && s_pathStore is { } store)
        {
            try
            {
                store.Add(readHash, package.EntryPath, paths);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 控えは無くても動く。次に読んだときに足し直す
            }
        }

        return paths;
    }

    private static void Remember((string Zip, string Entry) key, FileInfo? zip, IReadOnlyList<string> paths)
    {
        if (zip is not { Exists: true } || paths.Count == 0)
        {
            return;
        }

        // 覚えすぎない。手元の商品の数を大きく超えたら一度忘れる（読み直せば戻る）
        if (PathCache.Count >= MaxCachedPackages)
        {
            PathCache.Clear();
        }

        PathCache[key] = (zip.Length, zip.LastWriteTimeUtc, paths);
    }

    private static Storage.UnityPackagePathStore? s_pathStore;

    /// <summary>中身のパスの控えを使う。アプリの起動時に1度渡す。渡さなければ（試験など）毎回 zip を解く。</summary>
    public static void UsePathStore(Storage.UnityPackagePathStore? store) => s_pathStore = store;

    /// <summary>読んだパスの一覧。キーは zip の場所（大文字小文字をそろえる）と中の名前。</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Zip, string Entry), (long Length, DateTime Written, IReadOnlyList<string> Paths)>
        PathCache = new();

    private const int MaxCachedPackages = 5000;

    private static IReadOnlyList<string> ReadAssetPathsFromDisk(UnityPackageEntry package)
    {
        try
        {
            using var archive = ZipFile.Open(package.ZipPath, ZipArchiveMode.Read, Encoding.GetEncoding(932));
            if (archive.GetEntry(package.EntryPath) is not { } entry)
            {
                return [];
            }

            using var stream = entry.Open();
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);

            var paths = new List<string>();
            while (tar.GetNextEntry(copyData: false) is { } tarEntry)
            {
                if (tarEntry.DataStream is null || !tarEntry.Name.EndsWith("/pathname", StringComparison.Ordinal))
                {
                    continue;
                }

                using var reader = new StreamReader(tarEntry.DataStream, Encoding.UTF8, false, 1024, leaveOpen: true);
                if (reader.ReadLine()?.Trim() is { Length: > 0 } path)
                {
                    paths.Add(path);
                }

                if (paths.Count >= MaxAssetPaths)
                {
                    break;
                }
            }

            return paths;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException
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
    /// プロジェクト名に <c>" - "</c> が入っていると先頭だけを拾って短くなる。
    /// 名前を言い当てられないより、短い方がまだ役に立つ。
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
