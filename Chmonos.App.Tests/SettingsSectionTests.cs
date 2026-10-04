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
    public Task 範囲の外を打ったときは_丸めた旨をその欄の下に出し_保存が終わっても残る_上の段には出さない() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);

        settings.SearchHistoryCount = 500;
        await app.SettleAsync();

        var note = settings.Notes[nameof(SettingsViewModel.SearchHistoryCount)];
        Assert.Contains("検索の履歴を残す件数に入れられるのは 1〜", note);
        Assert.Contains("にしました。", note);
        Assert.False(settings.HasStatus);

        // ほかの欄には出ない。範囲の中に打ち直したら消える
        Assert.Equal(string.Empty, settings.Notes[nameof(SettingsViewModel.RefreshIntervalDays)]);
        settings.SearchHistoryCount = 5;
        await app.SettleAsync();
        Assert.Equal(string.Empty, settings.Notes[nameof(SettingsViewModel.SearchHistoryCount)]);
    });

    [Fact]
    public Task 同じキーを割り当てると_外した旨を割り当てた行に出し_上の段には出さない() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);
        var first = settings.ShortcutRows[0];
        var second = settings.ShortcutRows[1];

        settings.AssignShortcut(first, "Ctrl+Shift+F9");
        settings.AssignShortcut(second, "Ctrl+Shift+F9");
        await app.SettleAsync();

        Assert.Equal(string.Empty, first.Gesture);
        Assert.Equal($"同じキーだったので、「{first.Label}」の割り当てを外しました。", second.Note);
        Assert.Equal(string.Empty, first.Note);
        Assert.False(settings.HasStatus);

        // 次の割り当てで前の知らせは消える
        settings.AssignShortcut(second, "Ctrl+Shift+F8");
        Assert.Equal(string.Empty, second.Note);
    });

    [Fact]
    public Task 幅を戻した知らせは_そのボタンの下に出し_上の段には出さない() => TestApp.Run(async app =>
    {
        var settings = await OpenSettingsAsync(app);

        settings.ResetPaneWidthsCommand.Execute(null);

        Assert.Equal("画面の幅をすべて元に戻しました。", settings.PaneWidthNote);
        Assert.False(settings.HasStatus);
        Assert.Equal(string.Empty, settings.DataStatus);
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
