using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>統計の未処理「ファイルが見つからない」は、フォルダの画面ではなく、検索の条件「見つからないファイル」を入れて開く（ユーザ判断 2026-11-18）。</summary>
public sealed class StatsMissingFileJumpTests
{
    private static readonly DateTimeOffset Since = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public Task 押すと検索が見つからないファイルの条件で開き_全部見つからない商品も数と同じに入る() => TestApp.Run(async app =>
    {
        var present = app.NewFile("present.zip");
        await app.AddItemAsync(Make.Item("1000001", "一部見つからない").WithFiles(
            Make.File(present), Make.File(@"D:\gone\a.zip") with { MissingSince = Since }));
        await app.AddItemAsync(Make.Item("1000002", "全部見つからない").WithFiles(
            Make.File(@"D:\gone\b.zip") with { MissingSince = Since }));
        await app.AddItemAsync(Make.Item("1000003", "見つかる").WithFiles(Make.File(app.NewFile("ok.zip"))));
        var main = await app.StartAsync();
        var stats = new StatsViewModel(app.Services, main, new DateOnly(2026, 11, 19));
        await UiThread.Until(() => !stats.IsLoading && stats.Backlog.Count > 0, "統計の集計");

        var row = stats.Backlog.Single(r => r.Label == "ファイルが見つからない");
        Assert.Equal(2, row.Count);
        row.OpenCommand.Execute(null);

        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.Equal(["1000001", "1000002"], main.Search.ListItems.Select(card => card.Item.Id).Order());
        stats.OnLeaving();
        await app.SettleAsync();
    });
}
