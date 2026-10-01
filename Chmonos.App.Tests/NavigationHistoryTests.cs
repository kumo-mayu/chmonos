using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 主画面の画面の履歴（戻る・進む）と、戻るの文言（「← {行き先}に戻る」）。
///
/// 履歴は「どの画面からどの画面へ、どの順で移ったか」で決まる。前は画面を行き来して、戻るの文言と行き先を
/// 目で追うしかなかった（`docs/spec/architecture.md` の「画面」の決め事）。
/// </summary>
public class NavigationHistoryTests
{
    private static async Task<MainViewModel> StartWithItemAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        return await app.StartAsync();
    }

    [Fact]
    public Task 商品があれば検索から始まり_戻る先は無い() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);

        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.False(main.CanGoBack);
        Assert.False(main.CanGoForward);
        Assert.Equal("戻る先がありません", main.BackTip);
        Assert.Equal("進む先がありません", main.ForwardTip);
    });

    [Fact]
    public Task 何も無い保存先は_取り込みから始まる() => TestApp.Run(async app =>
    {
        // 空の検索の横で「まだ登録されていません」が並ぶだけでは、次に何をすればよいか分からない
        var main = await app.StartAsync();

        Assert.IsType<ImportViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 商品は無いが未確定があれば_未確定から始まる() => TestApp.Run(async app =>
    {
        await app.Store.Unresolved.SaveAsync(
        [
            new Core.Models.UnresolvedFile
            {
                Hash = Make.HashOf("unresolved"),
                Paths = [app.NewFile("unresolved.zip")],
                SizeBytes = 3,
                ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            },
        ]);

        var main = await app.StartAsync();

        Assert.IsType<ResolveViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 移るたびに離れた画面を積み_戻るの文言は行き先の名前を言う() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);

        main.ShowStatsCommand.Execute(null);
        Assert.IsType<StatsViewModel>(main.CurrentViewModel);
        Assert.Equal("← 検索に戻る", main.BackButtonText);
        Assert.Equal("「検索」へ戻る（Alt+←）", main.BackTip);

        main.ShowSettingsCommand.Execute(null);
        Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        Assert.Equal("← 統計に戻る", main.BackButtonText);

        main.GoBack();
        Assert.IsType<StatsViewModel>(main.CurrentViewModel);
        Assert.Equal("← 検索に戻る", main.BackButtonText);
        Assert.Equal("「設定」へ進む（Alt+→）", main.ForwardTip);

        main.GoBack();
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.False(main.CanGoBack);
    });

    [Fact]
    public Task 戻った先から進める() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);
        main.ShowStatsCommand.Execute(null);
        main.ShowSettingsCommand.Execute(null);
        main.GoBack();
        main.GoBack();

        main.GoForward();
        Assert.IsType<StatsViewModel>(main.CurrentViewModel);

        main.GoForward();
        Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        Assert.False(main.CanGoForward);

        // 進んだ先からも、来た道を戻れる
        main.GoBack();
        Assert.IsType<StatsViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 戻った後で別の画面へ移ると_進む先は捨てる() => TestApp.Run(async app =>
    {
        // ブラウザと同じ。枝分かれした後の「進む」は行き先が決まらない
        var main = await StartWithItemAsync(app);
        main.ShowStatsCommand.Execute(null);
        main.GoBack();
        Assert.True(main.CanGoForward);

        main.ShowSettingsCommand.Execute(null);

        Assert.False(main.CanGoForward);
    });

    [Fact]
    public Task 今いる画面のナビを押しても_同じ画面を積まない() => TestApp.Run(async app =>
    {
        // 積むと、抜けるのに戻るを2回押す羽目になり、そのたびに読み込みも走る
        var main = await StartWithItemAsync(app);
        main.ShowStatsCommand.Execute(null);
        var stats = main.CurrentViewModel;

        main.ShowStatsCommand.Execute(null);

        Assert.Same(stats, main.CurrentViewModel);
        main.GoBack();
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.False(main.CanGoBack);
    });

    [Fact]
    public Task 戻る先が無いときの戻るは_今の画面を積まずに検索へ出す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        Assert.IsType<ImportViewModel>(main.CurrentViewModel);

        main.GoBack();

        // 積むと、戻ったはずなのに戻るがまた光って、今出てきた画面を指す
        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.False(main.CanGoBack);
    });

    [Fact]
    public Task 検索と取り込みは_同じ画面を持ち回す() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);
        var search = main.CurrentViewModel;

        main.ShowImportCommand.Execute(null);
        Assert.Same(main.Import, main.CurrentViewModel);

        main.ShowSearchCommand.Execute(null);
        Assert.Same(search, main.CurrentViewModel);
        Assert.Same(main.Search, main.CurrentViewModel);
    });

    [Fact]
    public Task 商品ページから離れると_戻るの文言は商品の名前を言う() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);
        main.ShowItem(main.Search.FindItem("1000001")!);
        Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        Assert.NotNull(main.CurrentItemPage);

        main.ShowStatsCommand.Execute(null);

        Assert.Equal("← 作り物の衣装に戻る", main.BackButtonText);
    });

    [Fact]
    public Task 長い商品名は_戻るの文言では30字で切る() => TestApp.Run(async app =>
    {
        // 長い商品名が上部バーを占領して、隣の情報を押し出さないように
        var name = new string('あ', 40);
        await app.AddItemAsync(Make.Item("1000001", name));
        var main = await app.StartAsync();
        main.ShowItem(main.Search.FindItem("1000001")!);

        main.ShowStatsCommand.Execute(null);

        Assert.Equal("← " + new string('あ', 30) + "…に戻る", main.BackButtonText);
    });

    [Fact]
    public Task 商品ページへ戻るときは_開き直した時点の中身で出す() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);
        var item = main.Search.FindItem("1000001")!;
        main.ShowItem(item);
        var opened = main.CurrentViewModel;
        main.ShowStatsCommand.Execute(null);

        // 離れている間に、よそで名前を付け直した
        await app.AddItemAsync(item with { Local = item.Local with { DisplayName = "付け直した名前" } });
        main.GoBack();
        await UiThread.Until(() => main.CurrentViewModel is ItemViewModel, "商品ページを開き直す");

        // 覚えた時の画面をそのまま出すと、その後の変更が出ない
        Assert.NotSame(opened, main.CurrentViewModel);
        Assert.Equal("付け直した名前", main.CurrentItemPage!.Name);
    });

    [Fact]
    public Task 戻る先の商品が消えていれば_飛ばしてもう1つ前へ戻る() => TestApp.Run(async app =>
    {
        var main = await StartWithItemAsync(app);
        main.ShowStatsCommand.Execute(null);
        main.ShowItem(main.Search.FindItem("1000001")!);
        main.ShowSettingsCommand.Execute(null);

        // 離れている間に、その商品を消した（登録を外した・IDを変えた）
        await app.Store.Items.DeleteAsync("1000001");
        main.GoBack();
        await UiThread.Until(() => main.CurrentViewModel is not SettingsViewModel, "消えた商品を飛ばして戻る");

        Assert.IsType<StatsViewModel>(main.CurrentViewModel);
        Assert.Equal("← 検索に戻る", main.BackButtonText);
    });

    [Fact]
    public Task 戻るで検索へ着くと_離れたときの条件に戻る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        var main = await app.StartAsync();
        main.Search.QueryText = "衣装";
        main.ShowStatsCommand.Execute(null);

        // よその画面から「これだけ出す」で条件が入れ替わった後でも、戻れば離れたときの条件
        main.Search.ShowOnlyBrokenZip();
        main.GoBack();

        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.Equal("衣装", main.Search.QueryText);
        Assert.Equal(["1000001"], main.Search.ListItems.Select(card => card.Item.Id));
    });
}
