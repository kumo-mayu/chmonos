using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>表示の大きさの段と、絵を読む画素の計算（ユーザ指示 2026-09-29）。</summary>
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

    [Fact]
    public void 段は90から150までの5つで既定は100()
    {
        Assert.Equal([90, 100, 110, 125, 150], DisplayZoom.Steps);
        Assert.Equal(100, DisplayZoom.DefaultPercent);
        Assert.Equal(100, new AppSettings().DisplayZoomPercent);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(125, 125)]
    // 手で書き換えた値は近い段へ
    [InlineData(120, 125)]
    [InlineData(104, 100)]
    [InlineData(137, 125)]
    // 真ん中は小さい方（大きすぎて収まらないより困らない）
    [InlineData(105, 100)]
    // 範囲の外は端へ
    [InlineData(0, 90)]
    [InlineData(-50, 90)]
    [InlineData(300, 150)]
    public void 段に丸める(int saved, int expected)
        => Assert.Equal(expected, DisplayZoom.Normalize(saved));

    [Theory]
    [InlineData(90, 100)]
    [InlineData(100, 110)]
    [InlineData(110, 125)]
    [InlineData(125, 150)]
    // いちばん上ではそのまま
    [InlineData(150, 150)]
    // 段にない値は丸めてから進める
    [InlineData(120, 150)]
    public void 一段大きく(int from, int expected)
        => Assert.Equal(expected, DisplayZoom.Next(from));

    [Theory]
    [InlineData(150, 125)]
    [InlineData(125, 110)]
    [InlineData(110, 100)]
    [InlineData(100, 90)]
    // いちばん下ではそのまま
    [InlineData(90, 90)]
    [InlineData(120, 110)]
    public void 一段小さく(int from, int expected)
        => Assert.Equal(expected, DisplayZoom.Previous(from));

    [Fact]
    public void 名前は百分率()
        => Assert.Equal("125%", DisplayZoom.Label(125));

    [Fact]
    public void 表示の大きさは人が読める数で書き戻せる()
    {
        var json = JsonSerializer.Serialize(new AppSettings { DisplayZoomPercent = 125 }, JsonStore.Options);

        Assert.Contains("\"displayZoomPercent\": 125", json);
        Assert.Equal(125, JsonSerializer.Deserialize<AppSettings>(json, JsonStore.Options)!.DisplayZoomPercent);
    }

    [Fact]
    public void 表示の大きさが無い設定を読むと100()
    {
        // この項目を足す前（2026-09-29）に保存した settings.json には無い
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "cardWidth": 228 }""", JsonStore.Options)!;

        Assert.Equal(100, settings.DisplayZoomPercent);
    }

    [Fact]
    public void 大きさのショートカットの既定はブラウザと同じ()
    {
        var shortcuts = new AppSettings().Shortcuts;

        Assert.Equal("Ctrl+OemPlus", shortcuts.ZoomIn);
        Assert.Equal("Ctrl+OemMinus", shortcuts.ZoomOut);
        Assert.Equal("Ctrl+D0", shortcuts.ZoomReset);
    }

    [Fact]
    public void 大きさの割り当てが無い設定を読むと既定が入る()
    {
        const string json = """{ "shortcuts": { "skip": "Ctrl+Shift+Right" } }""";

        var shortcuts = JsonSerializer.Deserialize<AppSettings>(json, JsonStore.Options)!.Shortcuts;

        Assert.Equal("Ctrl+OemPlus", shortcuts.ZoomIn);
        Assert.Equal("Ctrl+OemMinus", shortcuts.ZoomOut);
        Assert.Equal("Ctrl+D0", shortcuts.ZoomReset);
    }
}
