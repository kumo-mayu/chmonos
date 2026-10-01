using Chmonos.Core.Models;

namespace Chmonos.Core.Scanning;

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
/// 計算で出せる値なので保存しない。unresolved.json には、取り込みの走査が読んだままの ReferrerUrl だけがある。
/// </summary>
public static class UnresolvedOrigin
{
    private static readonly string[] ArchiveExtensions = [".zip", ".rar", ".7z"];

    /// <summary>
    /// 元zipを割り出す。分からなければ null（呼び出し側はフォルダで束ねる）。
    ///
    /// **保存した値だけで決める。ファイルは読まない**（ユーザ判断 2026-09-30）。前は、今あるファイルの Zone.Identifier を
    /// 先に読み直していた——9月前半の版が CP932 の ReferrerUrl を UTF-8 として読み、日本語が U+FFFD に化けたまま
    /// 保存していたための救済だった。だが未確定の画面を開くたび・ナビの札が数え直すたびに、未確定の件数ぶん
    /// ディスクを読むことになる（印の無い6万件で約0.8秒・印のある2万件で約3.2秒。docs/research/large-files-2026-09-30.md）。
    /// 公開前なので古い記録には合わせない。Zone.Identifier を読むのは取り込みの走査で記録を作るときだけで、
    /// 化けた値の残る記録は、その取り込み元を取り込み直せば今の読み方の値で書き直される。
    /// </summary>
    public static ArchiveOrigin? For(UnresolvedFile file)
    {
        // zipそのものが未確定になっている場合は、それ自身が元zip。
        // 同じzipを展開した中身と同じ束に入り、zipと中身を一緒に片付けられる
        if (file.Paths.Count > 0 && IsArchive(file.Paths[0]))
        {
            return new ArchiveOrigin(Path.GetFileName(file.Paths[0]), file.Paths[0]);
        }

        return FromReferrer(file.ZoneReferrerUrl);
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
}
