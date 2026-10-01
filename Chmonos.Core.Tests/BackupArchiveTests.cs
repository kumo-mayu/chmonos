using System.IO.Compression;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>バックアップの書き出しと戻し（#61）。戻すのは別の空の場所へ。</summary>
public sealed class BackupArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-backup-" + Guid.NewGuid().ToString("N")[..8]);

    private string Store => Path.Combine(_dir, "store");

    public BackupArchiveTests()
    {
        Directory.CreateDirectory(Path.Combine(Store, "items"));
        Directory.CreateDirectory(Path.Combine(Store, "images", "111"));
        File.WriteAllText(Path.Combine(Store, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Store, "items", "111.json"), "{\"id\":\"111\"}");
        File.WriteAllText(Path.Combine(Store, "images", "111", "a.webp"), "画像");
        File.WriteAllText(Path.Combine(Store, "items", "111.json.tmp"), "書きかけ");
        File.WriteAllText(Path.Combine(Store, "search-bridge.cache"), "索引");
        File.WriteAllText(Path.Combine(Store, "location.json"), "{}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static List<string> EntriesOf(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void 計算し直せる物と戻すと害になる物は入れない()
    {
        var zip = Path.Combine(_dir, "backup.zip");

        var result = BackupArchive.Export(Store, zip, includeImages: true);

        Assert.Equal(["backup-info.json", "images/111/a.webp", "items/111.json", "settings.json"], EntriesOf(zip));
        Assert.Equal(3, result.Files);
    }

    [Fact]
    public void 画像は選んだときだけ入れる()
    {
        var zip = Path.Combine(_dir, "no-images.zip");

        BackupArchive.Export(Store, zip, includeImages: false);

        Assert.DoesNotContain("images/111/a.webp", EntriesOf(zip));
    }

    [Fact]
    public void 保存先の中に書き出しても自分自身は入れない()
    {
        var zip = Path.Combine(Store, "inside.zip");

        BackupArchive.Export(Store, zip, includeImages: false);

        Assert.DoesNotContain(EntriesOf(zip), name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void 空の場所へ戻すと中身がそろう()
    {
        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Store, zip, includeImages: true);
        var destination = Path.Combine(_dir, "restored");

        var files = BackupArchive.Restore(zip, destination);

        Assert.Equal(3, files);
        Assert.Equal("{\"id\":\"111\"}", File.ReadAllText(Path.Combine(destination, "items", "111.json")));
        Assert.True(StoreLocation.LooksLikeStore(destination));
        Assert.False(File.Exists(Path.Combine(destination, BackupArchive.InfoFileName)));
    }

    [Fact]
    public void 空でない場所には戻さない()
    {
        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Store, zip, includeImages: false);

        // 今の保存先に重ねると混ざる
        Assert.Throws<IOException>(() => BackupArchive.Restore(zip, Store));
    }

    [Fact]
    public void このアプリのバックアップでないzipは戻さない()
    {
        var zip = Path.Combine(_dir, "other.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("readme.txt").Open());
            writer.Write("別物");
        }

        Assert.False(BackupArchive.LooksLikeBackup(zip));
        Assert.Throws<InvalidDataException>(() => BackupArchive.Restore(zip, Path.Combine(_dir, "x")));
    }
}
