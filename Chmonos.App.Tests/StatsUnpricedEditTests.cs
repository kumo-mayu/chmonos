using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>統計の「金額の記録が無い商品」の帯：文と、右のボタンで同じ商品の集合を編集の画面へ積んで開くこと。</summary>
public sealed class StatsUnpricedEditTests
{
    private static ItemRecord Item(string id, params Purchase[] purchases) => new()
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "item " + id,
            FetchedAt = DateTimeOffset.Now,
            Shop = new BoothShop { Subdomain = "s", Name = "s", Url = "https://s.booth.pm/" },
        },
        Local = new LocalBlock
        {
            LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [@"C:\none\" + id + ".zip"], SizeBytes = 1 }],
            Purchases = [.. purchases],
        },
    };

    [Fact]
    public Task 帯の数と同じ商品を編集の画面へ積んで開く() => TestApp.Run(async app =>
    {
        var date = new DateOnly(2026, 11, 5);
        await app.AddItemAsync(Item("1000001", new Purchase { VariationId = 1, Price = 500, PurchasedAt = date }));
        await app.AddItemAsync(Item("1000002", new Purchase { VariationId = 1, Price = 0, PurchasedAt = date }));
        await app.AddItemAsync(Item("1000003", new Purchase { VariationId = 1, Price = 800, Kind = PurchaseKind.Received, PurchasedAt = date }));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.HasUnpricedNote, "統計の集計");
        stats.Range = StatsViewModel.Ranges.First(option => option.Kind == StatsRangeKind.All);
        await UiThread.Until(() => stats.IsAllTime && stats.UnpricedNote.StartsWith("金額の記録が無い商品が 2 件"), "全期間の集計");

        stats.EditUnpricedCommand.Execute(null);
        await app.SettleAsync();

        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        Assert.Equal(["1000002", "1000003"], edit.QueueTiles.Select(tile => tile.ItemId).Order());
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
