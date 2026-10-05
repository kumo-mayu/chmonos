using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の購入記録の行の日付の欄（メモ45）と、商品ページの購入の札の日付。
/// 1件目（種類の行）にも2件目以降にも欄を出し、名前は行の種類に合わせる。空欄は入手日を使うので null で書く（推定で埋めない）。
/// </summary>
public class EditPurchaseDateTests
{
    private static ItemRecord Item(string id, params Purchase[] purchases)
    {
        var item = Make.Item(id, "作り物の衣装 " + id);
        return item with
        {
            Booth = item.Booth with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                    new BoothVariation { Id = 2, Name = "テクスチャ", Price = 500, Type = "downloadable" },
                ],
            },
            Local = item.Local with { AcquiredAt = new DateOnly(2024, 6, 1), Purchases = [.. purchases] },
        };
    }

    private static async Task<EditViewModel> OpenAsync(TestApp app, ItemRecord item)
    {
        await app.AddItemAsync(item);
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        await main.ShowEditAsync([item.Id]);
        await app.SettleAsync();
        return Assert.IsType<EditViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task 保存してある日付を_1件目の行にも2件目の行にも出す() => TestApp.Run(async app =>
    {
        var edit = await OpenAsync(app, Item("9900501",
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500, PurchasedAt = new DateOnly(2025, 3, 10) },
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500, Kind = PurchaseKind.Given }));

        var row = edit.Variations.Single(variation => variation.VariationId == 1);
        Assert.Equal("2025-03-10", row.PurchasedAt);
        Assert.Equal(string.Empty, Assert.Single(row.Extras).PurchasedAt);
        Assert.Equal(string.Empty, edit.Variations.Single(variation => variation.VariationId == 2).PurchasedAt);
    });

    [Fact]
    public Task 欄の名前は行の種類に合わせる() => TestApp.Run(async app =>
    {
        var edit = await OpenAsync(app, Item("9900502",
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 }));
        var row = edit.Variations.Single(variation => variation.VariationId == 1);

        Assert.Equal("買った日", row.PurchasedAtLabel);
        row.Kind = PurchaseKind.Received;
        Assert.Equal("貰った日", row.PurchasedAtLabel);

        row.AddPurchaseCommand!.Execute(null);
        var extra = Assert.Single(row.Extras);
        Assert.Equal("贈った日", extra.PurchasedAtLabel);
        extra.Kind = PurchaseKind.ForSelf;
        Assert.Equal("買った日", extra.PurchasedAtLabel);

        // 2件目は別の日に買った記録。1件目の日付を写さない
        Assert.Equal(string.Empty, extra.PurchasedAt);
    });

    [Fact]
    public Task 入れた日付は購入ごとに書き_空欄はnullのまま書く() => TestApp.Run(async app =>
    {
        var edit = await OpenAsync(app, Item("9900503",
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 }));
        var row = edit.Variations.Single(variation => variation.VariationId == 1);

        row.PurchasedAt = "2025/3/10";
        row.AddPurchaseCommand!.Execute(null);
        row.Extras[0].PurchasedAt = "2025年12月24日";
        var texture = edit.Variations.Single(variation => variation.VariationId == 2);
        texture.IsPurchased = true;

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync("9900503"))!.Local.Purchases;
        Assert.Equal(
            [new DateOnly(2025, 3, 10), new DateOnly(2025, 12, 24), null],
            saved.Select(purchase => purchase.PurchasedAt).ToArray());

        // 入手日は書き換えない（購入の日付は別の欄）
        Assert.Equal(new DateOnly(2024, 6, 1), (await app.Store.Items.LoadAsync("9900503"))!.Local.AcquiredAt);
    });

    [Fact]
    public Task 読めない日付は空のまま保存し_欄の名前で知らせる() => TestApp.Run(async app =>
    {
        var edit = await OpenAsync(app, Item("9900504",
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500, PurchasedAt = new DateOnly(2025, 3, 10) },
            new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500, Kind = PurchaseKind.Given }));
        var row = edit.Variations.Single(variation => variation.VariationId == 1);

        row.PurchasedAt = "きのう";
        row.Extras[0].PurchasedAt = "そのうち";
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync("9900504"))!.Local.Purchases;
        Assert.All(saved, purchase => Assert.Null(purchase.PurchasedAt));
        Assert.True(edit.StatusIsProblem);
        Assert.StartsWith("買った日「きのう」・贈った日「そのうち」は読めなかったので、空のまま保存しました。", edit.StatusText, StringComparison.Ordinal);
    });

    // ---- 商品ページの購入の札 ----

    [Theory]
    [InlineData(PurchaseKind.ForSelf, 1200, null, "¥1,200 で買った")]
    [InlineData(PurchaseKind.ForSelf, 1200, "2025-03-10", "2025-03-10 に ¥1,200 で買った")]
    [InlineData(PurchaseKind.Given, 1500, "2025-12-24", "2025-12-24 に ¥1,500 で贈った")]
    [InlineData(PurchaseKind.Received, null, "2025-03-10", "2025-03-10 に貰った")]
    [InlineData(PurchaseKind.Received, 800, "2025-03-10", "2025-03-10 に ¥800 で貰った")]
    [InlineData(PurchaseKind.Given, null, "2026-02-14", "2026-02-14 価格未入力（贈った）")]
    public void 購入の札は_日付を入れた物だけ頭に日付を添える(PurchaseKind kind, int? price, string? date, string expected)
        => Assert.Equal(expected, ItemViewModel.PurchaseLine(new Purchase
        {
            VariationId = 1,
            Kind = kind,
            Price = price,
            PurchasedAt = date is null ? null : DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture),
        }));
}
