using System.Text;
using BoothZipInspector.Models;

namespace BoothZipInspector;

/// <summary>
/// Zone.Identifier(INI形式)のテキスト内容を解析する純粋ロジック。
/// ファイルアクセスに依存しないため単体テストしやすい。
/// </summary>
public static class ZoneIdentifierParser
{
    public static ZoneIdentifierInfo Parse(string content)
    {
        string? zoneId = null;
        string? referrerUrl = null;
        string? hostUrl = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('[') || line.StartsWith(';'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();

            // エクスプローラーの「すべて展開」が書く ReferrerUrl（元のzipのパス）は末尾に NUL が付く。
            // 残すとパスとして比べられず、ファイル名も取り出せない
            var value = line[(separatorIndex + 1)..].Trim().TrimEnd('\0').Trim();

            if (key.Equals("ZoneId", StringComparison.OrdinalIgnoreCase))
            {
                zoneId = value;
            }
            else if (key.Equals("ReferrerUrl", StringComparison.OrdinalIgnoreCase))
            {
                referrerUrl = value;
            }
            else if (key.Equals("HostUrl", StringComparison.OrdinalIgnoreCase))
            {
                hostUrl = value;
            }
        }

        var itemId = BoothUrlExtractor.TryExtractItemId(referrerUrl)
            ?? BoothUrlExtractor.TryExtractItemId(hostUrl);

        return new ZoneIdentifierInfo
        {
            Found = true,
            ZoneId = zoneId,
            ReferrerUrl = referrerUrl,
            HostUrl = hostUrl,
            BoothItemId = itemId,
        };
    }
}

/// <summary>
/// NTFS代替データストリーム "ファイルパス:Zone.Identifier" を実際に読み取る。
/// NTFS以外や、ストリームが存在しない場合はFoundがfalseのZoneIdentifierInfoを返す。
/// </summary>
public static class ZoneIdentifierReader
{
    public static ZoneIdentifierInfo Read(string filePath)
    {
        var adsPath = filePath + ":Zone.Identifier";

        try
        {
            using var stream = new FileStream(adsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return ZoneIdentifierParser.Parse(Decode(memory.ToArray()));
        }
        catch (FileNotFoundException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (DirectoryNotFoundException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (IOException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
        catch (NotSupportedException)
        {
            return ZoneIdentifierInfo.NotFound();
        }
    }

    /// <summary>
    /// ストリームの中身を文字列にする。
    ///
    /// ブラウザが書く URL は ASCII なので何で読んでも同じだが、エクスプローラーの「すべて展開」が
    /// 中のファイルに書く ReferrerUrl（元のzipの絶対パス）は**システムの ANSI コードページ**で書かれる
    /// （日本語環境では CP932。2026-09-11 に実際のバイト列で確認）。UTF-8 で読むと
    /// 「アバター」が「�A�o�^�[」に化け、元のzip名が取れなくなる。
    /// UTF-8 として正しく読めるならそのまま、読めなければ ANSI で読み直す。
    /// </summary>
    /// <param name="ansiCodePage">読み直すコードページ。省略時は今の環境の ANSI コードページ。</param>
    public static string Decode(byte[] bytes, int? ansiCodePage = null)
    {
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        try
        {
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var codePage = ansiCodePage ?? SystemAnsiCodePage();
            return Encoding.GetEncoding(codePage).GetString(bytes);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetACP();

    /// <summary>
    /// **システムの** ANSI コードページ（ブラウザが書くのはこれ）。
    /// 前は今のカルチャ（表示形式の設定）から取っていたので、日本語の Windows で表示形式だけ英語にしている人は
    /// 1252 で読んで化けていた（点検 2026-09-23）。表示形式は人が自由に変えるが、ANSI の方は「Unicode 対応でないプログラムの言語」でしか変わらない。
    /// </summary>
    internal static int SystemAnsiCodePage()
        => OperatingSystem.IsWindows()
            ? (int)GetACP()
            : System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
}
