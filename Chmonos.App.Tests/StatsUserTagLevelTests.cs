using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計のユーザータグの「大分類／小分類」の切り替えと、上位10件の畳み（ユーザ判断 2026-11-20）。</summary>
public sealed class StatsUserTagLevelTests
{
    private static ItemRecord Item(string id, params UserTagAssignment[] tags) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock
        {
            UserTags = tags,
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [new Purchase { VariationId = 1, Price = 100, PurchasedAt = new DateOnly(2026, 11, 5) }],
        },
    };

    [Fact]
    public Task 小分類に切り替えると_大分類_小分類の形で出て_11件目以降は畳まれる() => TestApp.Run(async app =>
    {
        // 大分類1つに小分類12個
        for (var i = 1; i <= 12; i++)
        {
            await app.AddItemAsync(Item("10000" + (10 + i), new UserTagAssignment { Top = "衣装", Subs = ["小" + i.ToString("00")] }));
        }

        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.UserTagCounts.Count > 0, "統計の集計");

        Assert.Equal(["衣装"], stats.UserTagCounts.Select(row => row.Label));
        Assert.False(stats.HasMoreUserTags);

        stats.UserTagsBySub = true;

        Assert.False(stats.UserTagsByTop);
        Assert.Equal(StatsViewModel.UserTagCollapsedRows, stats.UserTagCounts.Count);
        Assert.All(stats.UserTagCounts, row => Assert.StartsWith("衣装 / 小", row.Label));
        Assert.True(stats.HasMoreUserTags);

        stats.ShowAllUserTags = true;
        Assert.Equal(12, stats.UserTagCounts.Count);

        stats.UserTagsByTop = true;
        Assert.Equal(["衣装"], stats.UserTagCounts.Select(row => row.Label));
        Assert.False(stats.ShowAllUserTags);
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
