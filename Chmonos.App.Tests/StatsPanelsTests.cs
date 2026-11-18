using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 統計の欄を畳んだかを、次に起動した時まで覚える（ユーザ判断 2026-11-19・open.md 110）。
/// 「軸どうしの関係」だけ既定が畳みなので、人が開いたことも覚える。
/// </summary>
public sealed class StatsPanelsTests
{
    [Fact]
    public Task 既定は軸どうしの関係だけ畳み_ほかは開く() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var panels = new StatsPanelsViewModel(app.Services.StatsPanels);

        Assert.True(panels["shops"]);
        Assert.False(panels["correlations"]);
        await app.SettleAsync();
        Assert.Null(app.Services.UiState.StatsPanelsOpen);
    });

    [Fact]
    public Task 畳んだ欄と開いた欄を覚え_開き直した画面と保存に引き継ぐ() => TestApp.Run(async app =>
    {
        await app.StartAsync();
        var panels = new StatsPanelsViewModel(app.Services.StatsPanels);
        panels["shops"] = false;
        panels["correlations"] = true;

        // 画面は開くたびに作り直す。書き込みが済む前に開き直しても、覚えた開き方で出る
        var reopened = new StatsPanelsViewModel(app.Services.StatsPanels);
        Assert.False(reopened["shops"]);
        Assert.True(reopened["correlations"]);
        Assert.True(reopened["chart"]);

        await app.SettleAsync();
        var saved = app.Services.UiState.StatsPanelsOpen;
        Assert.NotNull(saved);
        Assert.False(saved["shops"]);
        Assert.True(saved["correlations"]);
        Assert.False(saved.ContainsKey("chart"));
    });
}
