using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの上の2本の帯（使っている商品の新しい版・BOOTHで変わったところ）は本文と一緒に流れ去り、
/// 流した後は1行の見出しの右端の印から同じ中身を開く（ユーザ判断 2026-11-20）。
/// 画面に出さない窓に商品ページを載せて確かめる（ItemDescriptionTabTests と同じ窓の作り）
/// </summary>
public class ItemBarMarksTests
{
    private const string ItemId = "9900001";

    [Fact]
    public Task 変わった所の帯は本文と一緒に流れ_流した後は見出しの右端の変更の印から同じ中身を開く() => WithItemView(async (view, body) =>
    {
        var changesBar = (FrameworkElement)view.FindName("ChangesBar");
        var compactBar = (ItemCompactBar)view.FindName("CompactBar");
        Assert.True(changesBar.IsVisible, "未読の変化があれば帯を出す");
        Assert.True(body.IsAncestorOf(changesBar), "帯は流す本文の中にある（流すと一緒に去る）");
        Assert.False(compactBar.IsVisible);

        body.ScrollToVerticalOffset(1500);
        Settle(view);
        Assert.True(compactBar.IsVisible, "流すと1行の見出しが出る");
        var changesMark = Mark(compactBar, "ItemCompactChanges");
        Assert.True(changesMark.IsVisible, "変わった所がある間は「変更」の印を出す");
        Assert.False(Mark(compactBar, "ItemCompactUsedUpdate").IsVisible, "新しい版が無ければ「更新」の印は出さない");

        changesMark.IsChecked = true;
        Settle(view);
        var popup = Popups(compactBar).Single(popup => Equals(popup.Tag, "Changes"));
        Assert.True(popup.IsOpen);
        Assert.Equal(compactBar.ActualWidth, ((FrameworkElement)popup.Child).Width);
        Assert.Contains(Descendants(popup.Child).OfType<Button>(), button => AutomationProperties.GetAutomationId(button) == "ItemMarkChangesRead");

        // 上まで戻すと見出しが引っ込み、開いた中身も閉じる（帯が本文の頭に見えている）
        body.ScrollToVerticalOffset(0);
        Settle(view);
        await Task.Yield();
        Assert.False(compactBar.IsVisible);
        Assert.False(popup.IsOpen);
        Assert.False(changesMark.IsChecked);
    });

    private static ToggleButton Mark(ItemCompactBar bar, string automationId)
        => Descendants(bar).OfType<ToggleButton>().Single(mark => AutomationProperties.GetAutomationId(mark) == automationId);

    private static IEnumerable<Popup> Popups(ItemCompactBar bar) => ((Panel)bar.Trailing!).Children.OfType<Popup>();

    private static ItemRecord LongItem()
    {
        var item = Make.Item(ItemId, "作り物の衣装");
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static void Settle(FrameworkElement view)
    {
        view.UpdateLayout();
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    private static Task WithItemView(Func<ItemView, ScrollViewer, Task> body) => TestApp.Run(async app =>
    {
        var item = LongItem();
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            var diffs = new[] { new NotificationDiff { Field = BoothChanges.NameField, Before = "作り物の服", After = item.Booth.Name } };
            list.Add(new NotificationRecord
            {
                Id = $"item-updated:{ItemId}",
                Kind = NotificationKind.ItemUpdated,
                ItemId = ItemId,
                Title = item.Booth.Name,
                Detail = BoothChanges.Summarize(diffs),
                Diffs = diffs,
                CreatedAt = DateTimeOffset.Now,
            });
            return list;
        });
        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var view = new ItemView { DataContext = page, Width = 1280, Height = 800 };
        using var source = new HwndSource(new HwndSourceParameters(nameof(ItemBarMarksTests))
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
        await body(view, (ScrollViewer)view.FindName("Body"));
    });
}
