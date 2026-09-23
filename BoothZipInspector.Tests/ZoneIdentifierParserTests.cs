using BoothZipInspector;
using Xunit;

namespace BoothZipInspector.Tests;

public class ZoneIdentifierParserTests
{
    [Fact]
    public void ParsesZoneIdReferrerAndHost()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://booth.pm/ja/items/1234567\r\nHostUrl=https://booth.pm/\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.True(info.Found);
        Assert.Equal("3", info.ZoneId);
        Assert.Equal("https://booth.pm/ja/items/1234567", info.ReferrerUrl);
        Assert.Equal("https://booth.pm/", info.HostUrl);
        Assert.Equal("1234567", info.BoothItemId);
    }

    [Fact]
    public void FallsBackToHostUrlWhenReferrerHasNoItemId()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\nHostUrl=https://shop-name.booth.pm/items/7654321\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.Equal("7654321", info.BoothItemId);
    }

    /// <summary>
    /// エクスプローラーの「すべて展開」が中のファイルに書く形。ReferrerUrl は元のzipの絶対パスで、
    /// ANSI（日本語環境は CP932）で書かれ、末尾に NUL が付く。2026-09-11 に実機で取ったバイト列の形（名前の部分だけ作り話に差し替えた）。
    /// </summary>
    [Fact]
    public void ReadsTheArchivePathWrittenByExplorerExtraction()
    {
        byte[] prefix = [.. "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=E:\\"u8];

        // 「アバター\ふわもこテスト.zip」を CP932 で書いたもの＋末尾の NUL
        byte[] cp932 =
        [
            0x83, 0x41, 0x83, 0x6F, 0x83, 0x5E, 0x81, 0x5B, 0x5C,
            0x82, 0xD3, 0x82, 0xED, 0x82, 0xE0, 0x82, 0xB1, 0x83, 0x65, 0x83, 0x58, 0x83, 0x67,
            0x2E, 0x7A, 0x69, 0x70, 0x00,
        ];

        var info = ZoneIdentifierParser.Parse(ZoneIdentifierReader.Decode([.. prefix, .. cp932], ansiCodePage: 932));

        Assert.Equal("3", info.ZoneId);
        Assert.Equal(@"E:\アバター\ふわもこテスト.zip", info.ReferrerUrl);
    }

    /// <summary>UTF-8 として正しく読めるもの（ブラウザが書くURL）は、そのまま読む。</summary>
    [Fact]
    public void KeepsUtf8ContentAsIs()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://booth.pm/ja/items/1234567\r\n");

        var info = ZoneIdentifierParser.Parse(ZoneIdentifierReader.Decode(bytes, ansiCodePage: 932));

        Assert.Equal("1234567", info.BoothItemId);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetACP();

    /// <summary>
    /// 表示形式（今のカルチャ）を英語にしていても、システムの ANSI コードページで読む（点検 2026-09-23）。
    /// ブラウザが書くのはシステムの方なので、カルチャから取ると 1252 で読んで化けていた。
    /// </summary>
    [Fact]
    public void DecodesWithTheSystemCodePageEvenWhenTheCultureDiffers()
    {
        if (!OperatingSystem.IsWindows() || GetACP() != 932)
        {
            return; // 日本語の Windows でしか、この取り違えは起きない
        }

        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var bytes = System.Text.Encoding.GetEncoding(932).GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=C:\\dl\\アバター.zip\r\n");
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            Assert.Contains("アバター.zip", ZoneIdentifierReader.Decode(bytes), StringComparison.Ordinal);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void ReturnsNullItemIdWhenNeitherUrlContainsOne()
    {
        var content = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/\r\n";

        var info = ZoneIdentifierParser.Parse(content);

        Assert.Null(info.BoothItemId);
    }
}
