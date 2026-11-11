using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の「ファイルごとの種類」と、種類ごとの選ぶ欄の並び（ユーザ判断 2026-11-11）。
/// 設定の「ファイルの表示順」が名前の2つの順のときだけそれに従い、ほかの順では今までの名前順のまま。名前・ID は作り物。
/// </summary>
public class EditFileOrderTests
{
    private const string ItemId = "9900861";

    /// <summary>種類が2つで両方買い、どの種類にも結んでいないファイルを3つ持つ商品。記録の順は v10・v2・Base。</summary>
    private static ItemRecord TwoVariations()
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        return item with
        {
            Booth = item.Booth! with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "本体", Price = 1500, Type = "downloadable" },
                    new BoothVariation { Id = 2, Name = "テクスチャ", Price = 500, Type = "downloadable" },
                ],
            },
            Local = item.Local with
            {
                Purchases =
                [
                    new Purchase { VariationId = 1, NameSnapshot = "本体", Price = 1500 },
                    new Purchase { VariationId = 2, NameSnapshot = "テクスチャ", Price = 500 },
                ],
                LocalFiles =
                [
                    Make.File(@"D:\files\Sample_v10.zip"),
                    Make.File(@"D:\files\Sample_v2.zip"),
                    Make.File(@"D:\files\Base.zip"),
                ],
            },
        };
    }

    [Theory]
    // 名前の2つの順以外は、向きがどちらでも今までの名前順（A→Z）のまま
    [InlineData(FileOrder.Added, false, new[] { "Base.zip", "Sample_v10.zip", "Sample_v2.zip" })]
    [InlineData(FileOrder.Added, true, new[] { "Base.zip", "Sample_v10.zip", "Sample_v2.zip" })]
    [InlineData(FileOrder.Size, true, new[] { "Base.zip", "Sample_v10.zip", "Sample_v2.zip" })]
    [InlineData(FileOrder.Name, false, new[] { "Base.zip", "Sample_v10.zip", "Sample_v2.zip" })]
    [InlineData(FileOrder.Name, true, new[] { "Sample_v2.zip", "Sample_v10.zip", "Base.zip" })]
    [InlineData(FileOrder.NameWithVersion, false, new[] { "Base.zip", "Sample_v2.zip", "Sample_v10.zip" })]
    [InlineData(FileOrder.NameWithVersion, true, new[] { "Sample_v10.zip", "Sample_v2.zip", "Base.zip" })]
    public Task 名前の2つの順だけ設定と向きに従い_ほかは今までの名前順(FileOrder order, bool descending, string[] expected) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(TwoVariations());
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false, FileOrder = order, FileOrderDescending = descending });
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);

        Assert.Equal(expected, edit.FileSortRows.Select(row => row.Name));
        Assert.Equal(expected, edit.PurchasedVariationRows.Single(row => row.Name == "本体").FileChoices.Select(choice => choice.Name));
    });
}
