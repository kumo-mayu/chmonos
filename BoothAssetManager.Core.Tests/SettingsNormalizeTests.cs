using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 読み込んだ設定を約束の範囲に戻す。取得の間隔は1.5秒より詰めない（CLAUDE.md の絶対に破らないこと）。
/// 以前の版は設定画面から500msまで保存できたので、その設定もここで直る。
/// </summary>
public sealed class SettingsNormalizeTests
{
    [Fact]
    public void 既定の間隔は下限と同じ()
        => Assert.Equal(AppSettings.MinFetchIntervalMs, new AppSettings().FetchIntervalMs);

    [Theory]
    [InlineData(500, 1500)]
    [InlineData(0, 1500)]
    [InlineData(1499, 1500)]
    [InlineData(1500, 1500)]
    // 広げるのは自由
    [InlineData(3000, 3000)]
    public void 短すぎる間隔は下限に戻す(int saved, int expected)
        => Assert.Equal(expected, new AppSettings { FetchIntervalMs = saved }.Normalized().FetchIntervalMs);

    [Fact]
    public void ショートカットの既定はユーザが決めた4つ()
    {
        var shortcuts = new AppSettings().Shortcuts;

        Assert.Equal("Ctrl+Enter", shortcuts.SaveAndNext);
        Assert.Equal("Ctrl+Shift+Right", shortcuts.Skip);
        Assert.Equal("Ctrl+F", shortcuts.FindInPage);
        Assert.Equal("Alt+Left", shortcuts.Back);
    }

    [Fact]
    public void 取り込みの自動開始は落としたときだけが既定()
    {
        // 落とすのははっきりした指示。起動しただけで通信が走るのは嫌う人がいる（#38・ユーザ判断）
        var settings = new AppSettings();

        Assert.True(settings.StartImportOnDrop);
        Assert.False(settings.StartImportOnLaunch);
    }

    [Fact]
    public void ショートカットが消えた設定は既定に戻す()
        => Assert.Equal(new ShortcutSettings(), new AppSettings { Shortcuts = null! }.Normalized().Shortcuts);

    [Theory]
    // 以前の既定。画面に出したことが無いので、保存された192は既定を写しただけ（#71）
    [InlineData(192, AppSettings.DefaultThumbnailCacheBudgetMb)]
    // 1つ前の既定。これも画面に出したことが無い（U12で132へ上げた）
    [InlineData(32, AppSettings.DefaultThumbnailCacheBudgetMb)]
    // 手で書き換えた値はそのまま
    [InlineData(128, 128)]
    [InlineData(16, 16)]
    public void サムネイルの保持上限は以前の既定だけ新しい既定に置き換える(int saved, int expected)
        => Assert.Equal(expected, new AppSettings { ThumbnailCacheBudgetMb = saved }.Normalized().ThumbnailCacheBudgetMb);

    [Fact]
    public void 範囲内なら他の項目も含めてそのまま返す()
    {
        var settings = new AppSettings { FetchIntervalMs = 2000, ImageMaxEdgePixels = 512 };

        Assert.Same(settings, settings.Normalized());
    }
}
