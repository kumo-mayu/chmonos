using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// ギャラリーの大きな絵の高さは欄の幅に合わせる（ユーザ判断 2026-11-20）。狭い欄は前の 420 のまま、広げると幅の 3/4、上限は 720。
/// </summary>
public class ItemGalleryHeightTests
{
    [Theory]
    [InlineData(300, 420)]   // 狭い欄は前と同じ高さ（小さくはしない）
    [InlineData(560, 420)]   // 3/4 がちょうど 420
    [InlineData(800, 600)]   // 広げた分だけ高くなる
    [InlineData(960, 720)]
    [InlineData(1600, 720)]  // 上限
    [InlineData(0, 420)]     // 配置の前（幅がまだ無い）
    [InlineData(double.NaN, 420)]
    [InlineData(double.PositiveInfinity, 420)]
    public void 大きな絵の高さは幅の4分の3で_下限420_上限720(double width, double expected)
        => Assert.Equal(expected, ItemGalleryPanel.BigImageHeight(width));
}
