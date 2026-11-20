using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の一覧の余白と札の右クリックで、区切りの札を出すかを切り替える（ユーザ指示 2026-11-06）。
/// 設定の「並べ替えたとき、まとまりごとに区切りを表示」と同じ値を読み書きする。
/// </summary>
public class SearchSortDividerMenuTests
{
    private static async Task<SearchViewModel> ShopsAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の商品1", shop: "作り物の店A"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の商品2", shop: "作り物の店B"));
        var search = (await app.StartAsync()).Search;
        search.SortField = search.SortFields.First(field => field.Kind == SortKind.Shop);
        return search;
    }

    [Fact]
    public Task 切ると設定に書き_札が消え_入れると戻る() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);
        Assert.True(search.ShowSortDividers);
        Assert.Equal(2, search.ShownDividers.Count());

        search.ShowSortDividers = false;
        await UiThread.Until(() => !app.Services.Settings.ShowSortDividers, "設定に書く");
        await UiThread.Until(() => !search.ShownDividers.Any(), "一覧を組み直す");
        Assert.False(search.ShowSortDividers);

        search.ShowSortDividers = true;
        await UiThread.Until(() => app.Services.Settings.ShowSortDividers, "設定に書く");
        await UiThread.Until(() => search.ShownDividers.Count() == 2, "一覧を組み直す");
    });

    [Fact]
    public Task 札の項目を押すと_札が消える() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);
        var divider = search.ShownDividers.First();

        Assert.NotNull(divider.HideDividersCommand);
        divider.HideDividersCommand!.Execute(null);

        await UiThread.Until(() => !app.Services.Settings.ShowSortDividers, "設定に書く");
        await UiThread.Until(() => !search.ShownDividers.Any(), "一覧を組み直す");
    });

    [Fact]
    public Task 札の右クリックは_チェックの入った区切りの項目_ショップの札はショップの2つの後ろに区切り線を挟む() => UiThread.Run(() =>
    {
        var hide = new RelayCommand(() => { });
        var category = new SortDivider("category:a", "カテゴリ", "衣装", null) { HideDividersCommand = hide };
        var shop = new SortDivider("shop:a", "ショップ", "作り物の店", null)
        {
            OpenShopCommand = new RelayCommand(() => { }),
            OpenInBoothCommand = new RelayCommand(() => { }),
            HideDividersCommand = hide,
        };

        var only = Assert.Single(SortDividerBorder.BuildMenu(category)!.Items.OfType<MenuItem>());
        Assert.Equal("まとまりごとに区切りを表示", only.Header);
        Assert.True(only.IsChecked);
        Assert.Same(hide, only.Command);

        var items = SortDividerBorder.BuildMenu(shop)!.Items.Cast<object>().ToList();
        Assert.Equal(4, items.Count);
        Assert.IsType<Separator>(items[2]);
        Assert.Equal(["ショップを開く", "BOOTHで開く", "まとまりごとに区切りを表示"], items.OfType<MenuItem>().Select(item => (string)item.Header));

        // 札を消す口が無く押せもしない札は、メニューを持たない（一覧の余白のメニューへ任せる）
        Assert.Null(SortDividerBorder.BuildMenu(new SortDivider("category:b", "カテゴリ", "髪型", null)));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task 一覧の余白の右クリックの項目は_設定と同じ値で_外すと設定を切る(bool listMode) => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);
        search.IsListMode = listMode;
        var view = new SearchView { DataContext = search, Width = 1280, Height = 800 };
        using var source = new HwndSource(new HwndSourceParameters("SearchSortDividerMenuTests")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = new Canvas { Children = { view } },
        };
        view.UpdateLayout();
        await app.SettleAsync();

        FrameworkElement list = listMode ? Descendant<ItemListView>(view) : Descendant<CardRowsListBox>(view);
        // 開いたときに一覧の DataContext を受ける（閉じている間は値が入らない）。アプリと同じく一覧に載せて開く
        var menu = list.ContextMenu!;
        menu.PlacementTarget = list;
        menu.IsOpen = true;
        menu.UpdateLayout();
        var item = Assert.Single(menu.Items.OfType<MenuItem>());
        Assert.Equal("まとまりごとに区切りを表示", item.Header);
        Assert.True(item.IsCheckable);
        Assert.True(item.IsChecked);

        item.IsChecked = false;
        await UiThread.Until(() => !app.Services.Settings.ShowSortDividers, "設定に書く");
        await UiThread.Until(() => !search.ShownDividers.Any(), "一覧を組み直す");
        menu.IsOpen = false;
    });

    private static SortMenuEntry DividerEntry(SearchViewModel search) => search.SortMenu[^1];

    [Fact]
    public Task 表示順のメニューの一番下は_区切り線とチェック付きの区切りの項目() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);
        var menu = search.SortMenu;
        Assert.True(menu[^2].IsSeparator);
        Assert.Equal("まとまりごとに区切りを表示", menu[^1].Label);
        Assert.Equal("SearchSortField.ShowDividers", menu[^1].AutomationId);
        Assert.True(menu[^1].IsChecked);
    });

    [Fact]
    public Task ショップ順では_メニューの項目は全体の設定を切り替え_入手日の設定は動かない() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);

        DividerEntry(search).ChooseCommand!.Execute(null);
        await UiThread.Until(() => !app.Services.Settings.ShowSortDividers, "設定に書く");
        Assert.False(app.Services.Settings.ShowAcquiredSortDividers);
        await UiThread.Until(() => !DividerEntry(search).IsChecked, "印が切に変わる");
        await UiThread.Until(() => !search.ShownDividers.Any(), "一覧を組み直す");

        DividerEntry(search).ChooseCommand!.Execute(null);
        await UiThread.Until(() => app.Services.Settings.ShowSortDividers, "設定に書く");
        await UiThread.Until(() => DividerEntry(search).IsChecked, "印が入に戻る");
    });

    [Fact]
    public Task 入手日順では_メニューの項目は入手日の設定を切り替え_全体の設定は動かず_印も入手日の値を映す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の商品1") with { Local = Make.Item("1000001", "x").Local with { AcquiredAt = new DateOnly(2026, 9, 3) } });
        var search = (await app.StartAsync()).Search;
        search.SortField = search.SortFields.First(field => field.Kind == SortKind.AcquiredAt);

        // 全体は入・入手日は切（既定）：印は入手日の値（切）を映す
        Assert.True(app.Services.Settings.ShowSortDividers);
        Assert.False(app.Services.Settings.ShowAcquiredSortDividers);
        Assert.False(DividerEntry(search).IsChecked);
        Assert.False(search.ShownDividers.Any());

        DividerEntry(search).ChooseCommand!.Execute(null);
        await UiThread.Until(() => app.Services.Settings.ShowAcquiredSortDividers, "入手日の設定に書く");
        Assert.True(app.Services.Settings.ShowSortDividers);
        await UiThread.Until(() => DividerEntry(search).IsChecked, "印が入に変わる");
        await UiThread.Until(() => search.ShownDividers.Any(), "札が出る");

        // 入手日以外へ替えると、印は全体の設定（入）を映す。入手日を切に戻しても全体は動かない
        search.SortField = search.SortFields.First(field => field.Kind == SortKind.Shop);
        Assert.True(DividerEntry(search).IsChecked);
        await app.ChangeSettingsAsync(settings => settings with { ShowSortDividers = false });
        search.RefreshSortMenu();
        Assert.False(DividerEntry(search).IsChecked);
        Assert.True(app.Services.Settings.ShowAcquiredSortDividers);
    });

    private static T Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (next is T found)
            {
                return found;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(next); index++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(next, index));
            }
        }

        throw new InvalidOperationException($"{typeof(T).Name} が見つからない");
    }
}