using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定の「保存しました。」を出さない決め事と、節の開け閉めの記憶（ユーザ判断 2026-10-02）。
/// </summary>
public class SettingsSectionTests
{
    private static async Task<SettingsViewModel> OpenSettingsAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        return settings;
    }

    [Fact]
    public Task 保存できたときは_何も出さない() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);

        settings.ShowSubTagsInList = !settings.ShowSubTagsInList;
        await app.SettleAsync();

        Assert.False(settings.HasStatus);
        Assert.Equal(string.Empty, settings.Status);
    });

    [Fact]
    public Task 範囲の外を打ったときは_丸めた旨を出し_保存が終わっても残る() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);

        settings.SearchHistoryCount = 500;
        await app.SettleAsync();

        Assert.True(settings.HasStatus);
        Assert.Contains("検索の履歴を残す件数に入れられるのは 1〜", settings.Status);
        Assert.Contains("にしました。", settings.Status);
    });

    [Fact]
    public Task 節は既定で全部開いていて_畳んだ形はアプリを閉じるまで覚える() => TestApp.Run(async app =>
    {
        SettingsViewModel.ResetSectionsForTest();
        try
        {
            var settings = await OpenSettingsAsync(app);
            Assert.True(settings.SectionDisplayOpen);
            Assert.True(settings.SectionOperationOpen);
            Assert.True(settings.SectionImportFetchOpen);
            Assert.True(settings.SectionHiddenOpen);
            Assert.True(settings.SectionDataOpen);
            Assert.True(settings.SectionAboutOpen);

            settings.SectionOperationOpen = false;

            // 画面を移って戻ると作り直される。畳んだ節だけが畳んだまま
            var reopened = await OpenSettingsAsync(app);
            Assert.False(reopened.SectionOperationOpen);
            Assert.True(reopened.SectionDisplayOpen);

            // アプリを閉じて開き直した形（全部開く）
            SettingsViewModel.ResetSectionsForTest();
            var fresh = await OpenSettingsAsync(app);
            Assert.True(fresh.SectionOperationOpen);
        }
        finally
        {
            SettingsViewModel.ResetSectionsForTest();
        }
    });
}
