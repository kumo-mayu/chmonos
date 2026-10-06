using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Tests.Support;
using Chmonos.App.Views;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の画面の Tab の順（ユーザ指摘 2026-10-06：「新しい順/古い順の tab の順序がおかしい」「絞り込みの折り畳みの矢印に tab が止まらない」）。
///
/// どちらも重なりの順（Panel.ZIndex）が原因だった。WPF の Tab は描く順に子をたどり、Panel は ZIndex の大きい子を後ろへ回して描く。
/// 繋がったボタンは選んでいる方を上に重ねる（枠の色が隣に隠れないように）ので、選んでいる方が後ろへ回り、
/// 「新しい順」を選んでいると「古い順 → 新しい順」と逆に通った。境のつまみは結果の上に重ねる（ZIndex 2）ので、画面の最後（結果の後ろ）へ回っていた。
///
/// 実際のキーは押さず、画面に出さない窓に検索の画面を載せ、WPF の「次へ進む」（Tab と同じ道）を繰り返して止まった所をたどる
/// （ViewShot の tabs と同じ窓の作り）
/// </summary>
public class SearchTabOrderTests
{
    [Fact]
    public Task 表示順の帯は_項目_新しい順_古い順_カード_リストの順に止まる() => WithSearchView(async (app, search, view) =>
    {
        // 既定は新しい順を選んでいる。選んでいる方が重なりで後ろへ回っても、並んでいる順に通る
        Assert.True(search.SortsDescending);

        Assert.Equal(
            ["SearchSortField", "SearchSortDescending", "SearchSortAscending", "ItemViewMode.Cards", "ItemViewMode.List"],
            Walk(view, "SearchSortField", FocusNavigationDirection.Next, 5));

        // 逆向きも同じ並びを戻る
        Assert.Equal(
            ["ItemViewMode.List", "ItemViewMode.Cards", "SearchSortAscending", "SearchSortDescending", "SearchSortField"],
            Walk(view, "ItemViewMode.List", FocusNavigationDirection.Previous, 5));

        // 古い順を選んでも同じ（選んだ方で順が変わらない）
        search.SortsAscending = true;
        await app.SettleAsync();
        Settle(view);
        Assert.Equal(
            ["SearchSortField", "SearchSortDescending", "SearchSortAscending", "ItemViewMode.Cards"],
            Walk(view, "SearchSortField", FocusNavigationDirection.Next, 4));
    });

    [Fact]
    public Task 絞り込みのつまみは_開いている間は条件をクリアの次に止まり_畳んだ間は画面の最初に止まる() => WithSearchView((app, search, view) =>
    {
        // 開いている間、つまみはいつもの Tab の流れから外れ（絞り込みの外の部品なので、書いた順では間に挟めない）、
        // 前後の部品が Tab のキーで手でつなぐ（ユーザ指示 2026-10-06：見た目どおり「条件をクリア」の次・「条件を追加」の前）
        view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        Settle(view);
        Assert.Equal("SearchFilterMenu", Id(Keyboard.FocusedElement));
        Assert.Equal(
            ["SearchFilterMenu", "SearchClearFilters"],
            Walk(view, "SearchFilterMenu", FocusNavigationDirection.Next, 2));

        // 手でつないだ道：条件をクリア → Tab → つまみ → Tab → 条件を追加（既定の並びに条件があるので、条件をクリアは押せる）
        Assert.True(Find(view, "SearchClearFilters").IsEnabled);
        PressTab(view, Find(view, "SearchClearFilters"));
        Assert.Equal("SearchToggleFilterPanel", Id(Keyboard.FocusedElement));
        PressTab(view, Find(view, "SearchToggleFilterPanel"));
        Assert.Equal("SearchAddModule", Id(Keyboard.FocusedElement));

        // 結果の後ろへは回らない（前は結果のカードの後ろで止まっていた）
        Assert.Equal(
            ["ItemCardSize", "ItemCard"],
            Walk(view, "ItemCardSize", FocusNavigationDirection.Next, 2));

        // 畳んだ後も止まる（畳むと絞り込みの中身は消え、つまみの次は検索欄の側）
        search.ToggleFilterPanelCommand.Execute(null);
        Settle(view);
        var toggle = Find(view, "SearchToggleFilterPanel");
        Assert.True(toggle.Focusable && toggle.IsVisible);
        Assert.Equal("SearchToggleFilterPanel", Walk(view, "SearchToggleFilterPanel", FocusNavigationDirection.Next, 1)[0]);
        return Task.CompletedTask;
    });

    [Fact]
    public Task 絞り込みのつまみは_止まったことを丸い枠で見せる() => WithSearchView((app, search, view) =>
    {
        var toggle = (Button)Find(view, "SearchToggleFilterPanel");
        Assert.NotNull(toggle.FocusVisualStyle);
        Assert.Same(view.FindResource("PanelToggleFocusVisual"), toggle.FocusVisualStyle);
        return Task.CompletedTask;
    });

    private static List<string?> Walk(FrameworkElement view, string fromId, FocusNavigationDirection direction, int count)
    {
        var start = Find(view, fromId);
        Assert.True(start.Focus(), $"{fromId} にフォーカスを移せません（見えない窓がフォーカスを取れていない）");
        Settle(view);
        var stops = new List<string?> { Id(Keyboard.FocusedElement) };
        while (stops.Count < count)
        {
            ((UIElement)Keyboard.FocusedElement!).MoveFocus(new TraversalRequest(direction));
            Settle(view);
            stops.Add(Id(Keyboard.FocusedElement));
        }

        return stops;
    }

    /// <summary>その部品で Tab のキーを押したことにする（手でつないだ道は PreviewKeyDown で受けるので、MoveFocus では通らない）。</summary>
    private static void PressTab(FrameworkElement view, FrameworkElement element)
    {
        element.Focus();
        var source = PresentationSource.FromVisual(element)!;
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Settle(view);
    }

    private static string? Id(IInputElement? element)
        => element is DependencyObject target ? AutomationProperties.GetAutomationId(target) : null;

    private static FrameworkElement Find(DependencyObject root, string id)
        => Descendants(root).OfType<FrameworkElement>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id)
            ?? throw new InvalidOperationException($"ID {id} の部品が見つかりません。");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>Loaded・束縛・後回しの配置を済ませる（画面のスレッドの列が空くまで回す）。</summary>
    private static void Settle(FrameworkElement view)
    {
        view.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    /// <summary>
    /// 検索の画面を、フォーカスを受けられる見えない窓に載せる。メッセージ専用の窓はフォーカスを受けられないので、
    /// 出さない（WS_VISIBLE の無い）ポップアップの窓を画面の外に作る（ViewShot の Stage と同じ）
    /// </summary>
    private static Task WithSearchView(Func<TestApp, ViewModels.SearchViewModel, SearchView, Task> body) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("9900002", "作り物の小物"));
        var main = await app.StartAsync();
        var search = main.Search;
        var view = new SearchView { DataContext = search, Width = 1280, Height = 800 };
        using var source = new HwndSource(new HwndSourceParameters("SearchTabOrderTests")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP だけ
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            PositionX = -30000,
            PositionY = -30000,
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = new Canvas { Children = { view } },
        };
        SetFocus(source.Handle);
        Settle(view);
        await app.SettleAsync();
        Settle(view);
        await body(app, search, view);
    });
}
