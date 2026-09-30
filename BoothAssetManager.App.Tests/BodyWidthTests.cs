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
}
