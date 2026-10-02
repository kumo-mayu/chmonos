using Chmonos.Core.Models;

namespace Chmonos.Core.Tests;

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
    public void ショートカットの既定はユーザが決めた5つ()
    {
        var shortcuts = new AppSettings().Shortcuts;

        Assert.Equal("Ctrl+Enter", shortcuts.SaveAndNext);
        Assert.Equal("Ctrl+N", shortcuts.Skip);
        Assert.Equal("Ctrl+P", shortcuts.Previous);
        Assert.Equal("Ctrl+F", shortcuts.FindInPage);
        Assert.Equal("Alt+Left", shortcuts.Back);
    }

    [Fact]
    public void 前への割り当てが無い設定を読むと既定が入る()
    {
        // 前へ（2026-09-28）を足す前に保存した settings.json には項目が無い。空のまま読むと割り当てなしになってしまう
        const string json = """{ "shortcuts": { "skip": "Ctrl+Shift+Right", "back": "Alt+Left" } }""";

        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, Chmonos.Core.Storage.JsonStore.Options)!;

        Assert.Equal("Ctrl+P", settings.Shortcuts.Previous);
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
    [InlineData(192)]
    [InlineData(132)]
    [InlineData(32)]
    public void 保存されたサムネイルの保持上限はそのまま使う(int saved)
        => Assert.Equal(saved, new AppSettings { ThumbnailCacheBudgetMb = saved }.Normalized().ThumbnailCacheBudgetMb);

    [Fact]
    public void 範囲内なら他の項目も含めてそのまま返す()
    {
        var settings = new AppSettings { FetchIntervalMs = 2000, ImageMaxEdgePixels = 512 };

        Assert.Same(settings, settings.Normalized());
    }
}
