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

    /// <summary>
    /// 途中で中止したら、書きかけの zip も .tmp も残さず、保存先にも手を付けない（公開前の点検 2026-10-01）。
    /// 1ファイル書いた所で止める（進み具合の知らせはその場で呼ばれる。Progress&lt;T&gt; と違って画面へ回さない）
    /// </summary>
    [Fact]
    public void 途中で中止すると_書きかけを残さず_保存先も変えない()
    {
        var zip = Path.Combine(_dir, "out", "stopped.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        var before = Directory.EnumerateFiles(Store, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        using var stop = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() => BackupArchive.Export(
            Store, zip, includeImages: true, new StopAfterFirst(stop), stop.Token));

        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(zip)!));
        var after = Directory.EnumerateFiles(Store, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        Assert.Equal(before, after);
    }

    private sealed class StopAfterFirst(CancellationTokenSource stop) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => stop.Cancel();
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

    /// <summary>
    /// 戻すが途中で止まったら、展開した物を消す（2026-10-01）。残すと展開先が空でなくなり、
    /// 同じ場所へ戻し直すと断られた（作り物の2GBで、半分で止めると 44,014 ファイルが残った）。
    /// </summary>
    [Fact]
    public void 戻すが途中で止まると_展開した物を消し_同じ場所へ戻し直せる()
    {
        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Store, zip, includeImages: true);
        var destination = Path.Combine(_dir, "restored");
        using var stop = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() => BackupArchive.Restore(zip, destination, new StopAfterFirst(stop), stop.Token));

        // 作った展開先ごと消える
        Assert.False(Directory.Exists(destination));
        Assert.Equal(3, BackupArchive.Restore(zip, destination));
    }

    /// <summary>選んだ空のフォルダは消さずに、空に戻す（人が作って選んだフォルダなので）。</summary>
    [Fact]
    public void 選んだ空のフォルダへ戻すのが途中で止まると_フォルダは残して空に戻す()
    {
        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Store, zip, includeImages: true);
        var destination = Path.Combine(_dir, "chosen");
        Directory.CreateDirectory(destination);
        using var stop = new CancellationTokenSource();

        Assert.ThrowsAny<OperationCanceledException>(() => BackupArchive.Restore(zip, destination, new StopAfterFirst(stop), stop.Token));

        Assert.True(Directory.Exists(destination));
        Assert.True(StoreLocation.IsEmpty(destination));
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
