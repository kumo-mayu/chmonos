using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests.Robustness;

public sealed class NotificationArchiveAdversarialTests : IDisposable
{
    private readonly RobustnessDirectory _directory = new();
    private string ArchiveDirectory => Path.Combine(_directory.Root, "archive");
    private NotificationArchive Archive => new(ArchiveDirectory);
    private const string Month = "2026-11";
    public void Dispose() => _directory.Dispose();

    private static NotificationRecord Record(string id, string? detail = null) => new()
    {
        Id = id, Kind = NotificationKind.ItemUpdated, Title = "作り物の通知", Detail = detail ?? "作り物",
        CreatedAt = new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), ItemId = "9900001", IsRead = true,
    };

    [Fact]
    public async Task 別インスタンスから同じ月へ同時追記しても_全件残る()
    {
        var expected = Enumerable.Range(0, 32).Select(index => Record("notice-" + index)).ToArray();
        await Task.WhenAll(expected.Select(record => Archive.AppendAsync([record])));
        var read = await Archive.ReadAsync(Month);
        Assert.Equal(expected.Select(record => record.Id).Order(), read.Records.Select(record => record.Id).Order());
        Assert.Equal(0, read.UnreadableBlocks);
        Assert.Equal(0, read.UnreadableLines);
    }

    [Fact]
    public async Task 追記の途中を取り消しても_前の月ファイルはバイト単位で残り_再試行できる()
    {
        var archive = Archive;
        await archive.AppendAsync([Record("before")]);
        var before = File.ReadAllBytes(archive.FileFor(Month));
        using var stop = new CancellationTokenSource();
        archive.MidWriteForTests = () => { stop.Cancel(); return Task.CompletedTask; };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => archive.AppendAsync([Record("after")], stop.Token));
        Assert.Equal(before, File.ReadAllBytes(archive.FileFor(Month)));
        archive.MidWriteForTests = null;
        await archive.AppendAsync([Record("after")]);
        Assert.Equal(2, (await archive.ReadAsync(Month)).Records.Count);
        Assert.Empty(Directory.GetFiles(ArchiveDirectory, "*.tmp"));
    }

    [Fact]
    public async Task 大きい行の後ろに切れたJSON行があっても_前後の通知とページの対応を保つ()
    {
        var archive = Archive;
        var large = Record("large", new string('あ', 2 * 1024 * 1024));
        Directory.CreateDirectory(ArchiveDirectory);
        using (var output = File.Create(archive.FileFor(Month)))
        {
            WriteMember(output, JsonSerializer.Serialize(large, new JsonSerializerOptions(JsonStore.Options) { WriteIndented = false }) + "\n{\"id\":");
        }
        await archive.AppendAsync([Record("after")]);
        var read = await archive.ReadAsync(Month);
        var index = await archive.IndexAsync(Month);
        var page = await archive.ReadEntriesAsync(index, index.Newest);
        Assert.Equal(new[] { "after", "large" }, read.Records.Select(record => record.Id).Order());
        Assert.Equal(read.Records.Select(record => record.Id).Order(), page.Select(record => record.Id).Order());
        Assert.Equal(large.Detail, Assert.Single(page, record => record.Id == "large").Detail);
        Assert.True(read.UnreadableLines + read.UnreadableBlocks > 0);
    }

    [Fact]
    public async Task 目録を作った後の追記は_既存ページを別の通知に化けさせない()
    {
        var archive = Archive;
        await archive.AppendAsync([Record("before")]);
        var index = await archive.IndexAsync(Month);
        await archive.AppendAsync([Record("after")]);
        Assert.Equal("before", Assert.Single(await archive.ReadEntriesAsync(index, index.Newest)).Id);
    }

    private static void WriteMember(Stream output, string text)
    {
        using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
        var bytes = Encoding.UTF8.GetBytes($"{{\"formatVersion\":{StoreFormat.Current}}}\n" + text);
        gzip.Write(bytes);
    }
}
