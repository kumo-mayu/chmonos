using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 統計の「空けられる場所」の「ごみ箱へ移す…」（ユーザ判断 2026-11-20）：確かめの窓を出してから送る。既定はやめる。
/// 試験は本物のごみ箱へ送らない（やめる・Core が断る道だけを通す）
/// </summary>
public sealed class StatsDuplicateRemoveTests
{
    private static ItemRecord Item(string id, params string[] paths) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "hash-" + id[^1], Paths = paths, SizeBytes = 4 }] },
    };

    [Fact]
    public void ボタンは同じ中身の組だけに出て_ごみ箱の無いドライブと外したドライブでは押せない()
    {
        var plain = new DuplicatePlaceViewModel(@"C:\a.zip", null, isAvailable: true);
        Assert.False(plain.HasRemove);

        var ok = new DuplicatePlaceViewModel(@"C:\a.zip", null, isAvailable: true, remove: () => Task.CompletedTask, hasRecycleBin: true);
        Assert.True(ok.HasRemove);
        Assert.True(ok.CanRemove);

        var noBin = new DuplicatePlaceViewModel(@"C:\a.zip", null, isAvailable: true, remove: () => Task.CompletedTask, hasRecycleBin: false);
        Assert.False(noBin.CanRemove);
        Assert.Equal("ごみ箱の無いドライブなので移せません。", noBin.RemoveTooltip);

        var away = new DuplicatePlaceViewModel(@"Z:\a.zip", null, isAvailable: false, remove: () => Task.CompletedTask, hasRecycleBin: true);
        Assert.False(away.CanRemove);
        Assert.Equal("ドライブがつながっていません。つなぐと移せます。", away.RemoveTooltip);
    }

    [Fact]
    public void zipと展開したフォルダの組には_zipの行にだけボタンを付ける()
    {
        var group = new DuplicateGroup
        {
            Kind = DuplicateKind.ArchiveAndUnpacked,
            Label = "x",
            UnitBytes = 1,
            ReclaimableBytes = 1,
            Places = [new DuplicatePlace("1", "a", @"C:\x.zip"), new DuplicatePlace("1", "a", @"C:\x")],
        };

        var row = new DuplicateRowViewModel(group, null, (_, _) => Task.CompletedTask);

        Assert.True(row.Places[0].HasRemove);
        Assert.True(row.Places[0].IsArchiveOfUnpacked);
        Assert.False(row.Places[1].HasRemove);
    }

    [Fact]
    public Task 確かめの窓の既定はやめる_やめれば何も消えない() => TestApp.Run(async app =>
    {
        var a = Path.Combine(Path.GetTempPath(), "chmonos-statsdup-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        File.WriteAllText(a, "same");
        try
        {
            var item = Item("1000001", a, @"C:\none\二つ目.zip");
            await app.AddItemAsync(item);
            var main = await app.StartAsync();
            var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
            await UiThread.Until(() => !stats.IsLoading, "統計の集計");
            var group = DuplicateFinder.Find([item]).Single();

            await stats.RemovePlaceAsync(group, group.Places[0]);

            var notice = Assert.Single(app.Notices);
            Assert.Equal("ごみ箱へ移す", notice.Caption);
            Assert.Equal(MessageBoxResult.Cancel, notice.DefaultResult);
            Assert.Contains(a, notice.Text);
            Assert.Contains("ほかの 1 箇所に残ります", notice.Text);
            Assert.True(File.Exists(a));
            stats.OnLeaving();
            await app.SettleAsync();
        }
        finally
        {
            File.Delete(a);
        }
    });

    [Fact]
    public Task 商品をまたぐ組でほかの場所が無い商品には_見つからなくなると言う() => TestApp.Run(async app =>
    {
        var a = Path.Combine(Path.GetTempPath(), "chmonos-statsdup-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        File.WriteAllText(a, "same");
        try
        {
            var first = Item("1000001", a);
            var second = Item("1000002", @"C:\none\二つ目.zip") with
            {
                Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "hash-1", Paths = [@"C:\none\二つ目.zip"], SizeBytes = 4 }] },
            };
            await app.AddItemAsync(first);
            await app.AddItemAsync(second);
            var main = await app.StartAsync();
            var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
            await UiThread.Until(() => !stats.IsLoading, "統計の集計");
            var group = DuplicateFinder.Find([first, second]).Single();

            await stats.RemovePlaceAsync(group, group.Places.Single(place => place.Path == a));

            Assert.Contains("ほかの場所が無いので、移すとこの商品のファイルが見つからなくなります", Assert.Single(app.Notices).Text);
            stats.OnLeaving();
            await app.SettleAsync();
        }
        finally
        {
            File.Delete(a);
        }
    });

    [Fact]
    public Task 窓でOKを押しても_ほかのファイルが無ければCoreが断り_理由を言って消さない() => TestApp.Run(async app =>
    {
        var a = Path.Combine(Path.GetTempPath(), "chmonos-statsdup-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        File.WriteAllText(a, "same");
        try
        {
            var item = Item("1000001", a, @"C:\none\二つ目.zip");
            await app.AddItemAsync(item);
            var main = await app.StartAsync();
            var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
            await UiThread.Until(() => !stats.IsLoading, "統計の集計");
            var group = DuplicateFinder.Find([item]).Single();
            app.Answer = _ => MessageBoxResult.OK;

            await stats.RemovePlaceAsync(group, group.Places[0]);

            Assert.Equal(2, app.Notices.Count);
            Assert.Contains("見つからないので、削除しません", app.Notices[1].Text);
            Assert.True(File.Exists(a));
            stats.OnLeaving();
            await app.SettleAsync();
        }
        finally
        {
            File.Delete(a);
        }
    });
}
