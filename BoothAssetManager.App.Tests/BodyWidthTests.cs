using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 左右2列の画面の、本文の幅の下限。下限が列の最小の合計より小さいと、狭い入れ物で横に送れないまま右が切れる。
/// </summary>
public class BodyWidthTests
{
    [Fact]
    public void 組み込んだ改変の本文の下限は_左の最小と右の最小と余白の合計()
    {
        // 左の最小 360・右の最小 320・本文の左右の余白 20×2
        Assert.Equal(720, ModificationViewModel.MinBodyWidth(embedded: true, leftMin: 360));
    }

    [Fact]
    public void 単独の改変の本文の下限は_左の最小に400を足した幅のまま()
    {
        Assert.Equal(860, ModificationViewModel.MinBodyWidth(embedded: false, leftMin: 460));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 本文の下限は_列の最小の合計を下回らない(bool embedded)
    {
        const double leftMin = 360;

        Assert.True(ModificationViewModel.MinBodyWidth(embedded, leftMin) >= leftMin + 320 + 40);
    }

    [Fact]
    public void 組み込んだ商品ページの本文の下限は_左の最小と右の最小と余白の合計()
    {
        // フォルダビューの右・改変の画面の右。左の最小 360・右の最小 320・本文の左右の余白 20×2。
        // 前は 0 で、狭い入れ物では右の列が切れたまま横に送れなかった
        Assert.Equal(720, ItemViewModel.MinBodyWidth(embedded: true, leftMin: 360));
    }

    [Fact]
    public void 主の窓の商品ページの本文の下限は_左の最小に400を足した幅のまま()
    {
        Assert.Equal(860, ItemViewModel.MinBodyWidth(embedded: false, leftMin: 460));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 商品ページの本文の下限は_列の最小の合計を下回らない(bool embedded)
    {
        const double leftMin = 360;

        Assert.True(ItemViewModel.MinBodyWidth(embedded, leftMin) >= leftMin + 320 + 40);
    }

    [Fact]
    public Task フォルダビューに組み込んだ商品ページは_覚えた左の幅ではなく左の最小から下限を出す() => Support.TestApp.Run(async app =>
    {
        var item = Support.Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var embedded = new ItemViewModel(item, app.Services, main, main.Thumbnails) { IsEmbedded = true };
        var standalone = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Equal(embedded.LeftPane.MinPixels + 360, embedded.BodyMinWidth);
        Assert.Equal(standalone.LeftPane.MinPixels + 400, standalone.BodyMinWidth);
    });
}
