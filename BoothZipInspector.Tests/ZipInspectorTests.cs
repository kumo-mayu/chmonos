using System.IO.Compression;
using System.Text;
using BoothZipInspector;
using BoothZipInspector.Models;
using Xunit;

namespace BoothZipInspector.Tests;

public class ZipInspectorTests : IDisposable
{
    private readonly string _tempZipPath = Path.Combine(Path.GetTempPath(), $"booth-zip-inspector-test-{Guid.NewGuid():N}.zip");

    public void Dispose()
    {
        if (File.Exists(_tempZipPath))
        {
            File.Delete(_tempZipPath);
        }
    }

    [Fact]
    public void FindsUrlInUtf8Text()
    {
        CreateZipWithEntry("README.txt", Encoding.UTF8.GetBytes("商品はこちら: https://booth.pm/ja/items/1234567"));

        var result = ZipInspector.Inspect(_tempZipPath);

        Assert.Equal(1, result.Summary.EntryCount);
        Assert.Contains(result.Clues, c => c.Kind == BoothClueKind.ItemUrl && c.ItemId == "1234567");
    }

    [Fact]
    public void FindsUrlInShiftJisText()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var shiftJis = Encoding.GetEncoding(932);
        CreateZipWithEntry("info.txt", shiftJis.GetBytes("商品ページ: https://shop-name.booth.pm/items/7654321"));

        var result = ZipInspector.Inspect(_tempZipPath);

        Assert.Contains(result.Clues, c => c.Kind == BoothClueKind.ItemUrl && c.ItemId == "7654321");
    }

    [Fact]
    public void CompletesNormallyWhenNoTextEntries()
    {
        CreateZipWithEntry("Assets/model.fbx", new byte[] { 1, 2, 3, 4 });

        var result = ZipInspector.Inspect(_tempZipPath);

        Assert.Equal(1, result.Summary.EntryCount);
        Assert.Empty(result.Clues);
    }

    [Fact]
    public void ThrowsForCorruptZip()
    {
        File.WriteAllBytes(_tempZipPath, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

        Assert.ThrowsAny<InvalidDataException>(() => ZipInspector.Inspect(_tempZipPath));
    }

    // Mac の圧縮は UTF-8 の印を付けずに UTF-8 で名前を書く。前は印の無い名前を全部 CP932 で読んで化けていた（点検 2026-09-23）
    [Fact]
    public void ReadsUnflaggedUtf8NamesFromMac()
    {
        // ZipNameEncoding で書くと、UTF-8 のバイトで印の無い名前になる（コードページが 65001 ではないので印を付けない）
        CreateZipWithEntry("衣装/テスト用_セーター.unitypackage", [1, 2, 3], ZipNameEncoding.Instance);
        AssertUnflagged();

        var result = ZipInspector.Inspect(_tempZipPath);

        Assert.Contains(result.Summary.Files, file => file.RelativePath.EndsWith("テスト用_セーター.unitypackage", StringComparison.Ordinal));
    }

    [Fact]
    public void StillReadsUnflaggedCp932Names()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        CreateZipWithEntry("衣装/テスト用_セーター.unitypackage", [1, 2, 3], Encoding.GetEncoding(932));
        AssertUnflagged();

        var result = ZipInspector.Inspect(_tempZipPath);

        Assert.Contains(result.Summary.Files, file => file.RelativePath.EndsWith("テスト用_セーター.unitypackage", StringComparison.Ordinal));
    }

    [Fact]
    public void AsciiNamesReadTheSame()
    {
        var bytes = Encoding.ASCII.GetBytes("Assets/model_v1.2.fbx");

        Assert.Equal("Assets/model_v1.2.fbx", ZipNameEncoding.Instance.GetString(bytes));
    }

    /// <summary>試験の前提：書いた名前に UTF-8 の印（汎用フラグの 11 ビット目）が付いていない。</summary>
    private void AssertUnflagged()
    {
        var bytes = File.ReadAllBytes(_tempZipPath);
        Assert.Equal(0x04034b50u, BitConverter.ToUInt32(bytes, 0));
        var flags = BitConverter.ToUInt16(bytes, 6);
        Assert.Equal(0, flags & 0x0800);
    }

    private void CreateZipWithEntry(string entryName, byte[] content, Encoding encoding)
    {
        using (var fileStream = new FileStream(_tempZipPath, FileMode.Create))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false, encoding))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }
    }

    private void CreateZipWithEntry(string entryName, byte[] content)
    {
        using (var fileStream = new FileStream(_tempZipPath, FileMode.Create))
        using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(content, 0, content.Length);
        }
    }
}
