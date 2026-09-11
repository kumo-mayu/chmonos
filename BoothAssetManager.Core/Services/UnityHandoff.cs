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
