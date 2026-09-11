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
    public void 範囲内なら他の項目も含めてそのまま返す()
    {
        var settings = new AppSettings { FetchIntervalMs = 2000, ImageMaxEdgePixels = 512 };

        Assert.Same(settings, settings.Normalized());
    }
}
