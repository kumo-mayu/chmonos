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
