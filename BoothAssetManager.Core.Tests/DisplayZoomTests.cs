using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

public class DisplayZoomTests
{
    [Theory]
    [InlineData(240, 1.0, 240)]
    [InlineData(240, 1.25, 300)]
    [InlineData(240, 1.5, 360)]
    [InlineData(40, 1.5, 60)]
    // 125% の画面で 110% に大きくしたとき。2進で割り切れない倍率でも1画素ぶん余計に読まない
    [InlineData(240, 1.1, 264)]
    [InlineData(240, 1.375, 330)]
    // 割り切れないときは切り上げる（足りないとぼやける）
    [InlineData(96, 1.25, 120)]
    [InlineData(97, 1.25, 122)]
    public void Pixels_multiplies_and_rounds_up(int dip, double scale, int expected)
        => Assert.Equal(expected, DisplayZoom.Pixels(dip, scale));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void Pixels_treats_a_broken_scale_as_one(double scale)
        => Assert.Equal(240, DisplayZoom.Pixels(240, scale));
}
