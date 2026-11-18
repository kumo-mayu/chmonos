using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 属性の管理の「数値｜グラフ」（open.md 164）：値の小さな円グラフの形と、切り替えが行と ui-state に届くこと。
/// </summary>
public sealed class AttributeValuePieTests
{
    [Fact]
    public void 零は形を作らず_地の円だけになる() => Assert.Equal(string.Empty, PieShape.Data(0, 14));

    /// <summary>円弧1本では始まりと終わりが同じ点になり何も描かれないので、100 % は半円2つ</summary>
    [Fact]
    public void 百は半円2つの丸() => Assert.Equal("M 7,0 A 7,7 0 1 1 7,14 A 7,7 0 1 1 7,0 Z", PieShape.Data(100, 14));

    [Fact]
    public void 四分の一は12時から3時まで_小さい方の弧() => Assert.Equal("M 7,7 L 7,0 A 7,7 0 0 1 14,7 Z", PieShape.Data(25, 14));

    [Fact]
    public void 半分を超えると大きい方の弧() => Assert.StartsWith("M 7,7 L 7,0 A 7,7 0 1 1 ", PieShape.Data(75, 14));

    [Fact]
    public void 範囲の外は丸める()
    {
        Assert.Equal(PieShape.Data(100, 14), PieShape.Data(140, 14));
        Assert.Equal(string.Empty, PieShape.Data(-5, 14));
    }

    [Fact]
    public Task グラフを選ぶと行が円グラフになり_次に開いても続く() => TestApp.Run(async app =>
    {
        var item = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item with { Local = item.Local with { Attributes = new Dictionary<string, int> { ["作り物の質感"] = 40 } } });
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の質感" }] });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count > 0, "属性の管理の読み込みが済む");
        attributes.IsItemsExpanded = true;
        await UiThread.Until(() => attributes.Items.Count > 0, "属性を持つ商品が並ぶ");

        var row = attributes.Items.OfType<TagItemRow>().Single();
        Assert.True(attributes.ValuesAsText);
        Assert.True(row.ShowsValueText);
        Assert.False(row.ShowsPie);

        attributes.ValuesAsPie = true;
        Assert.True(row.ShowsPie);
        Assert.False(row.ShowsValueText);
        Assert.NotNull(row.PieGeometry);
        Assert.Equal("40 %", row.ValueText);

        await app.SettleAsync();
        Assert.True(app.Services.UiState.AttributeValuesAsPie);
    });
}
