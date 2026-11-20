using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// リストで出す列を選ぶ（ユーザ判断 2026-11-20）：画面ごと（検索・ショップ・フォルダの右）に別々に覚え、
/// 見出しの右クリックで出す列を選び、ドラッグで並べ替えた順も覚える。最初に出す列は前のまま。
/// </summary>
public class ItemListColumnChoiceTests
{
    [Fact]
    public void 最初に出す列は前の列と同じ並び()
    {
        // 前の XAML の2つの GridView（SearchItemListGridView・ItemListGridView）と同じ並び。ショップはショップの列の場所に入手日を出していた
        Assert.Equal(["select", "icon", "fav", "name", "tags", "attrs", "paid", "avatars", "shop", "chips"], ItemListColumnCatalog.DefaultsOf("search"));
        Assert.Equal(["select", "icon", "fav", "name", "shop", "chips"], ItemListColumnCatalog.DefaultsOf("folder"));
        Assert.Equal(["select", "icon", "fav", "name", "acquired", "chips"], ItemListColumnCatalog.DefaultsOf("shop"));
    }

    [Fact]
    public void 列の名前は重ならず_どの画面のどの列にも幅の範囲がある()
    {
        Assert.Equal(ItemListColumnCatalog.All.Count, ItemListColumnCatalog.All.Select(column => column.Key).Distinct().Count());
        foreach (var screen in ItemListColumnCatalog.Screens)
        {
            foreach (var column in ItemListColumnCatalog.All)
            {
                // 幅を引く鍵が無いと PaneWidths.Get が落ちる
                Assert.True(PaneWidths.All.ContainsKey($"{screen}.col.{column.Key}"), $"{screen}.col.{column.Key}");
            }
        }

        // 前から覚えている幅は前の値のまま（検索の名前は足した列のために狭くしてある）
        Assert.Equal(280, PaneWidths.All["search.col.name"].Default);
        Assert.Equal(420, PaneWidths.All["folder.col.name"].Default);
        Assert.Equal(200, PaneWidths.All["shop.col.acquired"].Default);
    }

    [Fact]
    public void メニューの見出しは条件を追加と同じで_選ぶ列のほかは全部どれかの見出しに入る()
    {
        Assert.Equal(
            [SearchModuleCatalog.BoothInfo, SearchModuleCatalog.ItemInfo, SearchModuleCatalog.FileInfo, SearchModuleCatalog.ModificationRecord],
            ItemListColumnCatalog.Headings);
        foreach (var column in ItemListColumnCatalog.All)
        {
            if (column.Key == ItemListColumnCatalog.Select)
            {
                Assert.Null(column.Heading);
            }
            else
            {
                Assert.Contains(column.Heading, ItemListColumnCatalog.Headings);
                Assert.False(string.IsNullOrEmpty(column.MenuLabel));
            }

            // 文字の列は行の値の名前を持ち、その名前の値がある（型を組むときに名前で結ぶので、綴りの誤りは画面で空になるだけで気付けない）
            if (column.Cell != ItemListCell.Template)
            {
                Assert.NotNull(typeof(ItemListRow).GetProperty(column.RowProperty!));
            }
        }
    }

    [Fact]
    public void 覚えた並びは_知らない名前と重なりを飛ばし_選ぶ列を左端に置き直す()
    {
        Assert.Equal(["select", "name", "memo"], ItemListColumnCatalog.Normalize("search", ["name", "unknown", "select", "memo", "name", null!]));

        // 何も残らない・書いていないときは既定
        Assert.Equal(ItemListColumnCatalog.DefaultsOf("folder"), ItemListColumnCatalog.Normalize("folder", ["unknown"]));
        Assert.Equal(ItemListColumnCatalog.DefaultsOf("shop"), ItemListColumnCatalog.Normalize("shop", null));
    }

    [Fact]
    public Task 出した列は右クリックした列の右に入り_画面ごとに覚えて_次に作った画面でも同じ() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var columns = main.Search.ListColumns;

        columns.SetShown("memo", true, after: "name");
        columns.SetShown("id", true, after: "memo");
        Assert.Equal(["select", "icon", "fav", "name", "memo", "id", "tags", "attrs", "paid", "avatars", "shop", "chips"], columns.Order);

        columns.SetShown("tags", false);
        Assert.False(columns.IsShown("tags"));

        await UiThread.Until(
            () => app.Services.UiState.ItemListColumns?.TryGetValue("search", out var saved) == true && saved.Contains("memo") && !saved.Contains("tags"),
            "検索の列が ui-state に書かれる");

        // ほかの画面の並びは変わらない（画面ごとに別々に覚える）
        Assert.False(app.Services.UiState.ItemListColumns!.ContainsKey("folder"));
        Assert.Equal(ItemListColumnCatalog.DefaultsOf("folder"), new ItemListColumns(app.Services.PaneWidths, "folder", hasSelect: true, shopHeader: "ショップ・場所").Order);

        // 画面を作り直しても同じ並び（ショップとフォルダの画面は開くたびに作る）
        var again = new ItemListColumns(app.Services.PaneWidths, "search", hasSelect: true, shopHeader: "ショップ");
        Assert.Equal(columns.Order, again.Order);

        // 別の画面の列を変えても、先に覚えた検索の列は消えない（書くときにファイルの今の値へ足す）
        new ItemListColumns(app.Services.PaneWidths, "folder", hasSelect: true, shopHeader: "ショップ・場所").SetShown("size", true);
        await UiThread.Until(
            () => app.Services.UiState.ItemListColumns?.TryGetValue("folder", out var saved) == true && saved.Contains("size"),
            "フォルダの列が ui-state に書かれる");
        Assert.Contains("memo", app.Services.UiState.ItemListColumns!["search"]);
    });

    [Fact]
    public Task 最後の1列は外せない() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        foreach (var key in columns.Order.Where(key => key is not "select" and not "name").ToList())
        {
            columns.SetShown(key, false);
        }

        Assert.Equal(["select", "name"], columns.Order);
        Assert.False(columns.CanHide("name"));

        columns.SetShown("name", false);
        Assert.Equal(["select", "name"], columns.Order);
    });

    [Fact]
    public Task 並べ替えた順を覚え_選ぶ列を動かしても左端に戻る() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        columns.Reorder(["icon", "name", "select", "fav", "chips"]);
        Assert.Equal(["select", "icon", "name", "fav", "chips"], columns.Order);
    });

    [Fact]
    public Task 列を1つ左右へ動かし_端では動かず_選ぶ列は左端のまま_並びは画面ごとに覚える() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        Assert.Equal(["select", "icon", "fav", "name", "tags", "attrs", "paid", "avatars", "shop", "chips"], columns.Order);

        // 左端の列（選ぶ列の次）は左へ、右端は右へ動かせない。選ぶ列はどちらにも動かせない
        Assert.False(columns.CanMove("icon", -1));
        Assert.True(columns.CanMove("icon", 1));
        Assert.False(columns.CanMove("chips", 1));
        Assert.True(columns.CanMove("chips", -1));
        Assert.False(columns.CanMove("select", 1));
        Assert.False(columns.CanMove("select", -1));

        columns.Move("name", -1);
        Assert.Equal(["select", "icon", "name", "fav", "tags", "attrs", "paid", "avatars", "shop", "chips"], columns.Order);
        columns.Move("icon", -1);
        columns.Move("chips", 1);
        Assert.Equal(["select", "icon", "name", "fav", "tags", "attrs", "paid", "avatars", "shop", "chips"], columns.Order);

        // 選ぶ列の左へは行けない（動かそうとしても何も起きない）
        columns.Move("select", 1);
        Assert.Equal("select", columns.Order[0]);

        await UiThread.Until(
            () => app.Services.UiState.ItemListColumns?.TryGetValue("search", out var saved) == true && saved.ToList().IndexOf("name") < saved.ToList().IndexOf("fav"),
            "動かした並びが ui-state に書かれる");
        Assert.False(app.Services.UiState.ItemListColumns!.ContainsKey("folder"));
        Assert.Equal(columns.Order, new ItemListColumns(app.Services.PaneWidths, "search", hasSelect: true, shopHeader: "ショップ").Order);
    });

    /// <summary>列を指す見出し。<c>Column</c> は GridView の表示が決める読み取り専用の値なので、反射で入れる（窓を開かずに試すため）。</summary>
    private static GridViewColumnHeader HeaderOf(GridViewColumn column)
    {
        var header = new GridViewColumnHeader();
        var key = (DependencyPropertyKey)typeof(GridViewColumnHeader)
            .GetField("ColumnPropertyKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        header.SetValue(key, column);
        return header;
    }

    [Fact]
    public Task 見出しのメニューに_この列を左へ右へ_があり_開いた見出しの列に効き_端では押せない() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        var list = new ListView();
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);
        var menu = ((GridView)list.View).ColumnHeaderContextMenu;

        // 見出しを指さずに開いた（余白）：動かす列が無いので両方押せない
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        var left = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveLeft");
        var right = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveRight");
        Assert.Equal("この列を左へ", left.Header);
        Assert.Equal("この列を右へ", right.Header);
        Assert.False(left.IsEnabled);
        Assert.False(right.IsEnabled);

        // 見出しの列を指して開いた（Shift+F10・メニューキーでも、焦点のある見出しが PlacementTarget になる）。
        // 列は選ぶ・絵・星・名前…の順。名前の列は左右とも動かせる
        var gridColumns = ((GridView)list.View).Columns;
        menu.PlacementTarget = HeaderOf(gridColumns[3]);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        left = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveLeft");
        right = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveRight");
        Assert.True(left.IsEnabled);
        Assert.True(right.IsEnabled);
        left.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, left));
        Assert.Equal(["select", "icon", "name", "fav"], columns.Order.Take(4));

        // 左端の列（絵）は左へ押せない。動かした後に開き直すと、その列の今の位置で決まる
        menu.PlacementTarget = HeaderOf(((GridView)list.View).Columns[1]);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        left = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveLeft");
        Assert.False(left.IsEnabled);

        // 右端の列（状態）は右へ押せない
        var last = ((GridView)list.View).Columns.Count - 1;
        menu.PlacementTarget = HeaderOf(((GridView)list.View).Columns[last]);
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        right = menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.MoveRight");
        Assert.False(right.IsEnabled);
    });

    [Fact]
    public Task 見出しのメニューの_この列を非表示_は開いた見出しの列を外し_最後の1列と選ぶ列と余白では押せない() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        var list = new ListView();
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);
        var menu = ((GridView)list.View).ColumnHeaderContextMenu;
        MenuItem Hide() => menu.Items.OfType<MenuItem>().Single(item => System.Windows.Automation.AutomationProperties.GetAutomationId(item) == "ItemListColumnsMenu.Hide");
        void OpenOn(string key)
        {
            var view = (GridView)list.View;
            menu.PlacementTarget = HeaderOf(view.Columns[columns.Order.ToList().IndexOf(key)]);
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        }

        // 見出しを指さずに開いた（余白）：外す列が無いので押せない
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        Assert.Equal("この列を非表示", Hide().Header);
        Assert.False(Hide().IsEnabled);

        // 選ぶ列はメニューで外さない
        OpenOn("select");
        Assert.False(Hide().IsEnabled);

        // 属性の見出しで押すと、属性の列だけが消え、ui-state に書かれる。サブメニューのチェックも外れる
        OpenOn("attrs");
        Assert.True(Hide().IsEnabled);
        Hide().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, Hide()));
        Assert.False(columns.IsShown("attrs"));
        Assert.DoesNotContain(((GridView)list.View).Columns, column => (string)column.Header == "属性");
        await UiThread.Until(
            () => app.Services.UiState.ItemListColumns?.TryGetValue("search", out var saved) == true && !saved.Contains("attrs"),
            "外した列が ui-state から消える");
        OpenOn("name");
        var items = menu.Items.OfType<MenuItem>().SelectMany(group => group.Items.OfType<MenuItem>()).ToDictionary(item => (string)item.Tag);
        Assert.False(items["attrs"].IsChecked);

        // 最後の1列は押せない
        foreach (var key in columns.Order.Where(key => key is not "select" and not "name").ToList())
        {
            columns.SetShown(key, false);
        }

        OpenOn("name");
        Assert.False(Hide().IsEnabled);
        Assert.Equal("ほかの列を表示すると非表示にできます。", Hide().ToolTip);
        Hide().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, Hide()));
        Assert.True(columns.IsShown("name"));
    });

    [Fact]
    public Task 列の幅は列ごとに覚え_範囲に収める() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;

        // 前からある列は前の既定のまま（ユーザ判断 2026-10-04・案C の検索の4列）
        Assert.Equal(160, columns.WidthOf("tags").Width);
        Assert.Equal(76, columns.WidthOf("paid").Width);

        columns.WidthOf("paid").Width = 9999;
        Assert.Equal(160, columns.WidthOf("paid").Width);

        columns.WidthOf("memo").Width = 300;
        Assert.Equal(300, columns.WidthOf("memo").Width);
    });

    [Fact]
    public Task 前の版でショップの画面の入手日に合わせた幅は_新しい入手日の列へ移して書く() => TestApp.Run(async app =>
    {
        await app.StartAsync();

        // 前の版は、ショップの画面のショップの列（shop.col.shop）の場所に入手日を出していた
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(state => state with
        {
            PaneWidths = new Dictionary<string, double> { ["shop.col.shop"] = 330 },
            ItemListColumns = null,
        }));
        var widths = new PaneWidths(app.Services.SettingsStore, app.Services.Commands) { SaveDelay = TimeSpan.Zero };
        Assert.Equal(330, new ItemListColumns(widths, "shop", hasSelect: true, shopHeader: "ショップ").WidthOf("acquired").Width);
        await UiThread.Until(
            () => app.Services.UiState.PaneWidths.TryGetValue("shop.col.acquired", out var saved) && saved == 330,
            "移した幅が ui-state に書かれる");
    });

    [Fact]
    public void 入手日の幅を移すのは前の版が書いた形のときだけ()
    {
        var state = new Core.Models.UiState();

        // 入手日の幅がもうある・ショップの列の幅が無い：移さない
        Assert.False(PaneWidths.CarryOverShopAcquired(new() { ["shop.col.shop"] = 330, ["shop.col.acquired"] = 150 }, state));
        Assert.False(PaneWidths.CarryOverShopAcquired(new() { ["folder.col.shop"] = 330 }, state));

        // 新しい版でショップの列を選んだ後のショップの列の幅は、ショップの列の物（入手日へ移さない）
        var chosen = state with { ItemListColumns = new Dictionary<string, IReadOnlyList<string>> { ["shop"] = ["select", "name", "shop"] } };
        Assert.False(PaneWidths.CarryOverShopAcquired(new() { ["shop.col.shop"] = 330 }, chosen));

        var widths = new Dictionary<string, double> { ["shop.col.shop"] = 330 };
        Assert.True(PaneWidths.CarryOverShopAcquired(widths, state));
        Assert.Equal(330, widths["shop.col.acquired"]);
    }

    [Fact]
    public Task 一覧の列は並びのとおりに組まれ_選び直すと足し引きされる() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        var list = new ListView();
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);

        var view = Assert.IsType<GridView>(list.View);
        Assert.True(view.AllowsColumnReorder);
        Assert.NotNull(view.ColumnHeaderContextMenu);
        Assert.Equal(columns.Order.Count, view.Columns.Count);
        Assert.Equal(
            ["", "", "★", "名前", "ユーザータグ", "属性", "払った額", "対応", "ショップ", "状態"],
            view.Columns.Select(column => (string)column.Header));

        columns.SetShown("memo", true, after: "name");
        Assert.Equal("メモ", (string)view.Columns[4].Header);
        columns.SetShown("memo", false);
        Assert.DoesNotContain(view.Columns, column => (string)column.Header == "メモ");

        // 見出しのドラッグ（列の並びの Move）で並べ替えた順を覚える。覚えるのは並べ替えの知らせの後
        view.Columns.Move(3, 2);
        await UiThread.Settle();
        Assert.Equal(["select", "icon", "name", "fav"], columns.Order.Take(4));
    });

    [Fact]
    public Task 見出しの右クリックは条件を追加と同じ見出しで分け_今の列にチェックが付き_押すと出し入れする() => TestApp.Run(async app =>
    {
        var columns = (await app.StartAsync()).Search.ListColumns;
        var list = new ListView();
        list.Resources.MergedDictionaries.Add(new Views.ItemCardResources());
        ItemListColumnsView.SetColumns(list, columns);
        var menu = ((GridView)list.View).ColumnHeaderContextMenu;

        // 開いたときに中身を作る（見出しの無い所で開いた扱い：出した列は右端へ）
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        var groups = menu.Items.OfType<MenuItem>().Where(item => item.HasItems).ToList();
        Assert.Equal(ItemListColumnCatalog.Headings, groups.Select(group => (string)group.Header));
        var items = groups.SelectMany(group => group.Items.OfType<MenuItem>()).ToDictionary(item => (string)item.Tag);
        Assert.Equal(ItemListColumnCatalog.All.Count - 1, items.Count);
        Assert.True(items["name"].IsChecked);
        Assert.False(items["memo"].IsChecked);

        // 押すとチェックが替わってから Click が来る（IsCheckable の MenuItem と同じ順）
        items["memo"].IsChecked = true;
        items["memo"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, items["memo"]));
        Assert.Equal("memo", columns.Order[^1]);

        items["memo"].IsChecked = false;
        items["memo"].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, items["memo"]));
        Assert.False(columns.IsShown("memo"));

        // 1列だけになると、その列は外せない（押せない）
        foreach (var key in columns.Order.Where(key => key is not "select" and not "name").ToList())
        {
            columns.SetShown(key, false);
        }

        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
        items = menu.Items.OfType<MenuItem>().SelectMany(group => group.Items.OfType<MenuItem>()).ToDictionary(item => (string)item.Tag);
        Assert.False(items["name"].IsEnabled);
        Assert.True(items["memo"].IsEnabled);
    });
}
