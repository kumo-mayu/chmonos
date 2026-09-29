using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Tests;

/// <summary>表示の色の設定と、「Windows に合わせる」の決め方（ユーザ指示 2026-09-29）。</summary>
public class ColorThemeTests
{
    [Theory]
    [InlineData(ColorThemeMode.Light, 0, false)]
    [InlineData(ColorThemeMode.Light, 1, false)]
    [InlineData(ColorThemeMode.Dark, 1, true)]
    [InlineData(ColorThemeMode.Dark, null, true)]
    // Windows に合わせる：AppsUseLightTheme が 0 のときだけ暗い
    [InlineData(ColorThemeMode.System, 0, true)]
    [InlineData(ColorThemeMode.System, 1, false)]
    // 読めなかったときは今までどおり明るい
    [InlineData(ColorThemeMode.System, null, false)]
    // 0 と 1 以外の値（手で書き換えたレジストリ）は明るい側
    [InlineData(ColorThemeMode.System, 2, false)]
    public void 暗い表を使うか(ColorThemeMode mode, int? appsUseLightTheme, bool expected)
        => Assert.Equal(expected, ColorTheme.IsDark(mode, appsUseLightTheme));

    [Fact]
    public void 既定はWindowsに合わせる()
        => Assert.Equal(ColorThemeMode.System, new AppSettings().ColorTheme);

    [Fact]
    public void 表示の色は人が読める語で書き戻せる()
    {
        var json = JsonSerializer.Serialize(new AppSettings { ColorTheme = ColorThemeMode.Dark }, JsonStore.Options);

        Assert.Contains("\"colorTheme\": \"dark\"", json);
        Assert.Equal(ColorThemeMode.Dark, JsonSerializer.Deserialize<AppSettings>(json, JsonStore.Options)!.ColorTheme);
    }

    [Fact]
    public void 表示の色が無い設定を読むとWindowsに合わせる()
    {
        // この項目を足す前（2026-09-29）に保存した settings.json には無い
        var settings = JsonSerializer.Deserialize<AppSettings>("""{ "cardWidth": 228 }""", JsonStore.Options)!;

        Assert.Equal(ColorThemeMode.System, settings.ColorTheme);
    }

    [Fact]
    public void 選択肢の名前()
    {
        Assert.Equal("明るい", ColorTheme.Label(ColorThemeMode.Light));
        Assert.Equal("暗い", ColorTheme.Label(ColorThemeMode.Dark));
        Assert.Equal("Windows に合わせる", ColorTheme.Label(ColorThemeMode.System));
    }
}
