using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の保存容量の1行（外部の点検 2026-10-06）。読めなかった項目を 0 と出すと、本当に空なのと見分けが付かない。
/// </summary>
public class SettingsUsageTextTests
{
    [Fact]
    public void 数える前は点を出す()
        => Assert.Equal("…", SettingsViewModel.UsageText(loaded: false, null, null));

    [Fact]
    public void 本当に空なら0を出す()
        => Assert.EndsWith("/ 0 ファイル", SettingsViewModel.UsageText(loaded: true, 0, 0));

    [Fact]
    public void 読めなかった項目は0と出さない()
    {
        var text = SettingsViewModel.UsageText(loaded: true, null, null);

        Assert.Equal("読めませんでした", text);
        Assert.DoesNotContain("0", text);
    }
}
