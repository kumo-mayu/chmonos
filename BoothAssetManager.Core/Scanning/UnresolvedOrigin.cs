using BoothAssetManager.Core.Models;
using BoothZipInspector;

namespace BoothAssetManager.Core.Scanning;

/// <summary>未確定ファイルの展開元のzip。</summary>
/// <param name="ArchiveName">zipのファイル名。一覧で束ねる単位と、検索語の元になる。</param>
/// <param name="ArchivePath">記録にあったzipのパス。今もそこにあるとは限らない。</param>
public sealed record ArchiveOrigin(string ArchiveName, string ArchivePath);

/// <summary>
/// 未確定ファイルが、どのzipを展開したものかを割り出す。
///
/// エクスプローラーの「すべて展開」は、中のファイル1つずつの Zone.Identifier に
/// **元のzipの絶対パス**を ReferrerUrl として書く。zipを消した後も、展開先を別のドライブへ
/// 移した後も残る（docs/research/id-resolution.md、2026-09-11 に実物のバイト列で確認）。
/// フォルダで束ねると、展開の仕方次第で1つのzipが何か所にも割れる——友人のデータでは
/// 元zip 12 本のうち 6 本が複数のフォルダに割れていた。zipは配布された単位そのものなので、
/// こちらで束ねる。
///
/// 計算で出せる値なので保存しない。unresolved.json には読んだままの ReferrerUrl だけがある。
/// </summary>
public static class UnresolvedOrigin
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    /// <summary>
    /// 元zipを割り出す。分からなければ null（呼び出し側はフォルダで束ねる）。
    ///
    /// **今あるファイルから読み直すのを先にする。**旧版の読み取りは CP932 で書かれた
    /// ReferrerUrl を UTF-8 として読んでおり、日本語が U+FFFD に化けたまま保存されている
    /// （友人のデータでは 306 件全部のパスのどこかが化けていた）。ファイルが手元にあれば、
    /// 直した読み取りで正しい名前が取れる。
    /// </summary>
    /// <param name="readReferrer">パスから ReferrerUrl を読む。省略時は実際の Zone.Identifier を読む。</param>
    public static ArchiveOrigin? For(UnresolvedFile file, Func<string, string?>? readReferrer = null)
    {
        if (file.Paths.Count == 0)
        {
            return FromReferrer(file.ZoneReferrerUrl);
        }

        var path = file.Paths[0];

        // zipそのものが未確定になっている場合は、それ自身が元zip。
        // 同じzipを展開した中身と同じ束に入り、zipと中身を一緒に片付けられる
        if (IsArchive(path))
        {
            return new ArchiveOrigin(Path.GetFileName(path), path);
        }

        var read = readReferrer ?? ReadReferrer;
        return FromReferrer(read(path)) ?? FromReferrer(file.ZoneReferrerUrl);
    }

    /// <summary>ReferrerUrl の値から元zipを取り出す。zipのパスでなければ null。</summary>
    public static ArchiveOrigin? FromReferrer(string? referrerUrl)
    {
        if (string.IsNullOrWhiteSpace(referrerUrl))
        {
            return null;
        }

        // 旧版は末尾の NUL を落とさずに保存していた
        var value = referrerUrl.Trim().TrimEnd('\0').Trim();

        // ブラウザで落としたファイルの ReferrerUrl は商品ページのURLで、zipではない。
        // ドライブ文字か共有フォルダで始まるものだけを、展開元のパスとみなす
        var isLocalPath = (value.Length > 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
            || value.StartsWith(@"\\", StringComparison.Ordinal);
        if (!isLocalPath)
        {
            return null;
        }

        var name = value[(value.LastIndexOfAny(['\\', '/']) + 1)..];
        if (!IsArchive(name))
        {
            return null;
        }

        // 名前そのものが化けていると、束の見出しにも検索語にも使えない。フォルダで束ねる側へ回す。
        // 途中のフォルダ名だけが化けている分には、名前は正しいので使う
        if (name.Contains('�'))
        {
            return null;
        }

        return new ArchiveOrigin(name, value);
    }

    private static bool IsArchive(string path)
        => ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string? ReadReferrer(string path) => ZoneIdentifierReader.Read(path).ReferrerUrl;
}
