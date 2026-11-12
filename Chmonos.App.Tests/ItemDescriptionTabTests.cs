using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの説明の欄の Tab（計画 37・ユーザ判断 2026-11-12「直しましょう」）。
/// 前は節の本文から Tab を押すと、下の見出しを飛ばして欄の頭の「すべて折りたたむ」へ上り、もう1回で右の列の頭の星へ上った（先頭まで流れた）。
/// 欄の中は見た目の上から下の順にし、欄の最後の本文からは今までどおりページの次の止まり先（右の列の頭の星）へ移る。
///
/// 実際のキーは押さず、画面に出さない窓に商品ページを載せ、止まっている所に Tab の PreviewKeyDown を送る。
/// 手でつないだ道（<see cref="DescriptionTabs"/>）が受けなければ、WPF の「次へ進む」（Tab と同じ道）で進める（SearchTabOrderTests と同じ窓の作り）。
/// Shift+Tab はキーの修飾を作れないので、行き先の決め方（<see cref="DescriptionTabs.Target"/>）を直に呼んで確かめる
/// </summary>
public class ItemDescriptionTabTests
{
    [Theory]
    // 見出しの無い説明の本文（-1）から：最初の見出しへ
    [InlineData(-1, new[] { 0, 1, 2 }, false, 0)]
    // 節の本文から：すぐ下の見出しへ（前に触った見出しや欄の頭ではなく）
    [InlineData(1, new[] { 0, 1, 2 }, false, 2)]
    // 隠した見出し（消えた節）は飛ばす
    [InlineData(0, new[] { 0, 2 }, false, 2)]
    // 最後の節の本文から：手では移さない（ページの次の止まり先へ）
    [InlineData(2, new[] { 0, 1, 2 }, false, null)]
    // 前へ：その節の見出しへ戻る
    [InlineData(1, new[] { 0, 1, 2 }, true, 1)]
    // 前へ：見出しの無い説明の本文からは手では移さない（欄の三角へ）
    [InlineData(-1, new[] { 0, 1, 2 }, true, null)]
    public void 本文からのTabは_すぐ下の見出しへ_前へはその節の見出しへ(int fromRow, int[] headerRows, bool backward, int? expected)
        => Assert.Equal(expected, DescriptionTabs.HeaderRowFromBody(fromRow, headerRows, backward));

    [Fact]
    public Task 節の本文からのTabは_欄の頭へ戻らず_すぐ下の見出しへ移る() => WithItemView(Sectioned(), (view, body) =>
    {
        var bodies = SectionBodies(view);
        Assert.Equal(4, bodies.Count);
        var headers = SectionHeaders(view);

        // 見出しの無い所の本文（説明の頭）から：最初の見出しへ
        Assert.Same(headers[0], PressTab(view, body, DescriptionBody(view)));

        // 節の本文から：すぐ下の見出しへ。流れは下へ進み、欄の頭へは戻らない
        for (var row = 0; row < 3; row++)
        {
            var before = ScrollTo(view, body, bodies[row]);
            Assert.Same(headers[row + 1], PressTab(view, body, bodies[row]));
            Assert.True(body.VerticalOffset >= before, $"{row} 番目の節の本文から Tab で上へ流れた（{before} → {body.VerticalOffset}）");
        }

        // 最後の節の本文から：欄の外の、ページの次の止まり先へ（左の列の後ろ＝右の列の頭の星）。「すべて折りたたむ」へは上らない
        ScrollTo(view, body, bodies[3]);
        Assert.Equal("ItemFavorite", Id(PressTab(view, body, bodies[3])));
    });

    [Fact]
    public Task すべて折りたたむは_欄の三角の次に止まり_見出しの後ろには来ない() => WithItemView(Sectioned(), (view, body) =>
    {
        var toggle = Toggle(view);
        var toggleAll = Find(view, "ItemToggleAllSections");
        var headers = SectionHeaders(view);

        // 三角 → すべて折りたたむ → 見出しの並び → 右の列の頭の星
        Assert.Same(toggleAll, PressTab(view, body, toggle));
        Assert.Same(headers[0], PressTab(view, body, toggleAll));
        Assert.Equal("ItemFavorite", Id(PressTab(view, body, headers[0])));

        // 前へは逆にたどる
        var expander = (Expander)view.FindName("DescriptionExpander");
        var sections = (ItemsControl)view.FindName("SectionList");
        Assert.Same(toggleAll, DescriptionTabs.Target(expander, (Button)toggleAll, sections, headers[0], backward: true));
        Assert.Same(toggle, DescriptionTabs.Target(expander, (Button)toggleAll, sections, toggleAll, backward: true));

        // 節の本文から前へは、その節の見出しへ
        Assert.Same(headers[2], DescriptionTabs.Target(expander, (Button)toggleAll, sections, SectionBodies(view)[2], backward: true));
    });

    [Fact]
    public Task 見出しの無い説明の本文からのTabは_ページの次の止まり先へ移る() => WithItemView(Plain(), (view, body) =>
    {
        // 見出しが無いので「すべて折りたたむ」は出ない。左の列はここで終わり、次は右の列の頭の星（Windows の普通の Tab の順）
        Assert.False(Find(view, "ItemToggleAllSections").IsVisible);
        Assert.Equal("ItemFavorite", Id(PressTab(view, body, DescriptionBody(view))));
    });

    private static ItemRecord Sectioned()
    {
        var item = Make.Item("9900001", "作り物の衣装");
        return item with
        {
            Booth = item.Booth with
            {
                Description = string.Join('\n', Enumerable.Range(1, 20).Select(i => $"説明の行 {i}")),
                H2Sections = Enumerable.Range(1, 4)
                    .Select(k => new H2Section { Heading = $"見出し{k}", Text = string.Join('\n', Enumerable.Range(1, 15).Select(i => $"節{k}の行 {i}")) })
                    .ToList(),
            },
        };
    }

    private static ItemRecord Plain()
    {
        var item = Make.Item("9900002", "作り物の小物");
        return item with { Booth = item.Booth with { Description = string.Join('\n', Enumerable.Range(1, 60).Select(i => $"説明の行 {i}")) } };
    }

    /// <summary>その部品に止まって Tab を押したことにし、移った先を返す。手でつないだ道が受けなければ WPF の既定の「次へ」で進める。</summary>
    private static IInputElement? PressTab(FrameworkElement view, ScrollViewer body, UIElement element)
    {
        Assert.True(element.Focus(), $"{Id(element)} にフォーカスを置けませんでした（試験の窓が手前に無い環境）");
        Settle(view);
        var source = PresentationSource.FromVisual(element)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        if (!args.Handled)
        {
            element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        Settle(view);
        return Keyboard.FocusedElement;
    }

    /// <summary>本文が見える所まで流す（読んでいる所から押す）。流した位置を返す。</summary>
    private static double ScrollTo(FrameworkElement view, ScrollViewer body, UIElement element)
    {
        var at = element.TranslatePoint(new Point(0, 0), (UIElement)body.Content);
        body.ScrollToVerticalOffset(Math.Max(0, at.Y - 100));
        Settle(view);
        return body.VerticalOffset;
    }

    private static UIElement DescriptionBody(FrameworkElement view)
        => Descendants((DependencyObject)view.FindName("DescriptionAnchor")).OfType<RichTextBox>().First();

    private static List<RichTextBox> SectionBodies(FrameworkElement view)
        => Descendants((DependencyObject)view.FindName("SectionList")).OfType<RichTextBox>().Where(box => box.IsVisible).ToList();

    private static List<FrameworkElement> SectionHeaders(FrameworkElement view)
        => Descendants((DependencyObject)view.FindName("SectionList")).OfType<FrameworkElement>()
            .Where(element => AutomationProperties.GetAutomationId(element) == "ItemSectionHeader" && element.IsVisible).ToList();

    private static UIElement Toggle(FrameworkElement view)
        => Descendants((DependencyObject)view.FindName("DescriptionExpander")).OfType<ExpandToggle>().First();

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
    /// 商品ページを、フォーカスを受けられる見えない窓に載せる。メッセージ専用の窓はフォーカスを受けられないので、
    /// 出さない（WS_VISIBLE の無い）ポップアップの窓を画面の外に作る（SearchTabOrderTests と同じ）
    /// </summary>
    private static Task WithItemView(ItemRecord item, Action<ItemView, ScrollViewer> body) => TestApp.Run(async app =>
    {
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var view = new ItemView { DataContext = page, Width = 1280, Height = 800 };
        using var source = new HwndSource(new HwndSourceParameters(nameof(ItemDescriptionTabTests))
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
        body(view, (ScrollViewer)view.FindName("Body"));
    });
}
