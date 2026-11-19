using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// ギャラリーの大きな絵の高さは欄の幅に合わせる（ユーザ判断 2026-11-20）。幅が 420 より狭い間は高さを幅に合わせ（正方形）、
/// 420 以上は幅の 3/4 で下限 420・上限 720。
/// </summary>
public class ItemGalleryHeightTests
{
    [Theory]
    [InlineData(260, 260)]   // 左の列の最小幅。細長い灰色の枠を残さず、幅に合わせる
    [InlineData(300, 300)]
    [InlineData(419, 419)]
    [InlineData(420, 420)]   // 境目（ここから下限 420 に収まる）
    [InlineData(560, 420)]   // 3/4 がちょうど 420
    [InlineData(800, 600)]   // 広げた分だけ高くなる
    [InlineData(960, 720)]
    [InlineData(1600, 720)]  // 上限
    [InlineData(0, 420)]     // 配置の前（幅がまだ無い）
    [InlineData(double.NaN, 420)]
    [InlineData(double.PositiveInfinity, 420)]
    public void 大きな絵の高さは_幅が420より狭ければ幅に合わせ_以上は幅の4分の3で下限420_上限720(double width, double expected)
        => Assert.Equal(expected, ItemGalleryPanel.BigImageHeight(width));
}
