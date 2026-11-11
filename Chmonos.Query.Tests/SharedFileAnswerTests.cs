using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Query.Tests;

/// <summary>
/// 1つのファイルを2つの商品に付けたとき（plan 27・2026-11-11）、外から使う API の商品の答えには、両方の商品に同じファイルが出る。
/// 片方で外した物は、その商品の答えでだけ「外した」になる（既定の答えには出ない）。
/// </summary>
public sealed class SharedFileAnswerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-query-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task 両方の商品の答えに同じファイルが出て_外した方は既定の答えから消える()
    {
        var store = new DataStore(new AppPaths(_root));
        var shared = new LocalFileRecord { Hash = "H-SET", Paths = [Path.Combine(_root, "無い", "set.zip")], SizeBytes = 100 };
        await store.Items.SaveAsync(new ItemRecord { Id = "9500101", Booth = new BoothBlock { Name = "作り物のセット" }, Local = new LocalBlock { LocalFiles = [shared] } });
        await store.Items.SaveAsync(new ItemRecord { Id = "9500102", Booth = new BoothBlock { Name = "作り物の衣装" }, Local = new LocalBlock { LocalFiles = [shared] } });
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9500103",
            Booth = new BoothBlock { Name = "外した作り物" },
            Local = new LocalBlock { LocalFiles = [shared with { Detached = true }] },
        });

        var snapshot = await QuerySnapshot.LoadAsync(store);

        Assert.Equal("H-SET", Assert.Single(ItemMapper.Detail(snapshot.ItemsById["9500101"], snapshot).Files).Hash);
        Assert.Equal("H-SET", Assert.Single(ItemMapper.Detail(snapshot.ItemsById["9500102"], snapshot).Files).Hash);
        Assert.Empty(ItemMapper.Detail(snapshot.ItemsById["9500103"], snapshot).Files);
        Assert.Equal(
            "H-SET",
            Assert.Single(ItemMapper.Detail(snapshot.ItemsById["9500103"], snapshot, ItemIncludes.AllFiles).Files).Hash);
    }
}
