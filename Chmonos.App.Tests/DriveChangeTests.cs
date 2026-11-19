using System.IO;
using System.Runtime.InteropServices;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>ドライブの抜き差しで、カードの帯「取り外し中」を付け直す（ユーザ判断 2026-11-20）。知らせは差し替えて確かめる（実マシンのドライブには頼らない）。</summary>
public sealed class DriveChangeTests
{
    private const int WmDeviceChange = 0x0219;

    private static IntPtr Header(int deviceType)
    {
        // DEV_BROADCAST_HDR { size; devicetype; reserved }
        var memory = Marshal.AllocHGlobal(12);
        Marshal.WriteInt32(memory, 0, 12);
        Marshal.WriteInt32(memory, 4, deviceType);
        Marshal.WriteInt32(memory, 8, 0);
        return memory;
    }

    [Fact]
    public void ボリュームの付いた知らせと外れた知らせだけを拾う()
    {
        var volume = Header(2);
        var device = Header(5);
        try
        {
            Assert.True(DriveChangeWatcher.IsVolumeChange(WmDeviceChange, 0x8000, volume));
            Assert.True(DriveChangeWatcher.IsVolumeChange(WmDeviceChange, 0x8004, volume));
            Assert.False(DriveChangeWatcher.IsVolumeChange(WmDeviceChange, 0x8000, device));
            Assert.False(DriveChangeWatcher.IsVolumeChange(WmDeviceChange, 0x0007, volume));
            Assert.False(DriveChangeWatcher.IsVolumeChange(WmDeviceChange, 0x8000, IntPtr.Zero));
            Assert.False(DriveChangeWatcher.IsVolumeChange(0x0200, 0x8000, volume));
        }
        finally
        {
            Marshal.FreeHGlobal(volume);
            Marshal.FreeHGlobal(device);
        }
    }

    [Fact]
    public Task 知らせが続けて来ても確かめの合図は1回にまとまる() => UiThread.Run(async () =>
    {
        var watcher = new DriveChangeWatcher(TimeSpan.FromMilliseconds(60));
        var count = 0;
        watcher.Changed += (_, _) => count++;

        for (var i = 0; i < 8; i++)
        {
            watcher.Signal();
        }

        await UiThread.Until(() => count > 0, "合図");
        await Task.Delay(300);
        Assert.Equal(1, count);
    });

    [Fact]
    public Task 確かめを続けて頼んでも積み上がらず_最後の答えが入り_ショップなどへ知らせる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(new ItemRecord
        {
            Id = "1000001",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "h", Paths = [Path.Combine(app.Root, "here.zip")], SizeBytes = 1 }] },
        });
        var search = (await app.StartAsync()).Search;
        await UiThread.Until(() => search.DetachedRuns > 0 && search.DetachedIdle, "最初の確かめ");

        var before = search.DetachedRuns;
        var applied = 0;
        search.DetachedApplied += (_, _) => applied++;
        for (var i = 0; i < 10; i++)
        {
            search.RefreshDetached(refreshRemap: true);
        }

        await UiThread.Until(() => search.DetachedIdle && applied >= 2, "続けて頼んだ確かめ");

        // 1本走らせ、走っている間の9回は終わった後の1回にまとまる
        Assert.Equal(before + 2, search.DetachedRuns);
        Assert.Equal(2, applied);
        Assert.False(search.IsDetached("1000001"));
    });

    [Fact]
    public Task 答えが変わるとカードの帯が付き直る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(new ItemRecord
        {
            Id = "1000001",
            Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "h", Paths = [Path.Combine(app.Root, "here.zip")], SizeBytes = 1 }] },
        });
        var search = (await app.StartAsync()).Search;
        await UiThread.Until(() => search.DetachedRuns > 0 && search.DetachedIdle, "最初の確かめ");
        var card = search.ListItems.Single(c => c.Item.Id == "1000001");
        Assert.False(card.IsDetached);

        search.ApplyDetached(new HashSet<string>(["1000001"], StringComparer.Ordinal));
        Assert.True(card.IsDetached);
        search.ApplyDetached([]);
        Assert.False(card.IsDetached);
    });
}
