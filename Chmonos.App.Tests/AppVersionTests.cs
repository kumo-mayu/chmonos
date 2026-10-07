using Chmonos.App.Services;

namespace Chmonos.App.Tests;

/// <summary>設定の「このアプリについて」に出す版（2026-10-07）。</summary>
public class AppVersionTests
{
    [Fact]
    public void 版は組んだときの版で_コミットの印は付けない()
    {
        var built = typeof(AppVersion).Assembly.GetName().Version!.ToString(3);

        Assert.Equal(built, AppVersion.Text);
        Assert.DoesNotContain("+", AppVersion.Text);
    }

    [Theory]
    [InlineData("1.0.0+9eb11eb78e89a5859", "1.0.0")]
    [InlineData("1.2.3-beta", "1.2.3-beta")]
    public void 情報用の版の後ろのコミットの印を切る(string informational, string shown)
        => Assert.Equal(shown, AppVersion.Strip(informational, null));

    [Fact]
    public void 情報用の版が無ければアセンブリの版の上3つ()
        => Assert.Equal("1.0.0", AppVersion.Strip(null, new Version(1, 0, 0, 0)));

    [Fact]
    public void 設定の文はバージョンの後に版を付ける()
        => Assert.Equal($"バージョン {AppVersion.Text}", Chmonos.App.ViewModels.SettingsViewModel.VersionTextFor(AppVersion.Text));
}
