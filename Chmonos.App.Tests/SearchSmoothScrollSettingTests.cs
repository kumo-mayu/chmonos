using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の一覧をホイールで滑らかに流す設定（ユーザ判断 2026-11-20：設定の項目にし、試しに入れる）。
/// 一覧の側は回すたびに検索の <c>SmoothScrolling</c> を読むので、設定を変えるとすぐ効くことと、既定を確かめる。
/// </summary>
public class SearchSmoothScrollSettingTests
{
    [Fact]
    public void 既定は入() => Assert.True(new AppSettings().SmoothScrolling);

    [Fact]
    public Task 設定を切ると_検索が読む値も変わり_入れると戻る() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;
        Assert.True(search.SmoothScrolling);

        await app.ChangeSettingsAsync(settings => settings with { SmoothScrolling = false });
        Assert.False(search.SmoothScrolling);

        await app.ChangeSettingsAsync(settings => settings with { SmoothScrolling = true });
        Assert.True(search.SmoothScrolling);
    });
}
