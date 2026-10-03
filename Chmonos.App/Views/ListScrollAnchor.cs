using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 仮想化した一覧の位置を、先頭に見えていた項目で読み出し・戻す（<see cref="ListAnchor"/>）。
/// 画面は開き直すたびに作り直すので、位置は画面の履歴に預け、戻ったときに一覧を組み終えてから当てる。
/// </summary>
internal static class ListScrollAnchor
{
    /// <summary>今の位置を読む。先頭にいるとき・まだ組めていないときは null（戻すときも先頭のままでよい）。</summary>
    /// <param name="keyOf">一覧の項目（行・カード）から鍵を引く。行なら先頭のカードの鍵。</param>
    public static ListAnchor? Capture(ItemsControl list, Func<object, string?> keyOf)
        => FindDescendant<ScrollViewer>(list) is { } viewer ? Capture(viewer, list, keyOf) : null;

    /// <summary>
    /// 一覧を外の ScrollViewer の中に置いているとき（ショップの中：1本のスクロール）の読み出し。
    /// 一覧は仮想化しない（全部の項目の入れ物がある）ので、位置は外の ScrollViewer の上端からのずれで持つ
    /// </summary>
    public static ListAnchor? Capture(ScrollViewer viewer, ItemsControl list, Func<object, string?> keyOf)
    {
        if (!list.IsVisible || viewer.VerticalOffset <= 0 || FindItemsHost(list) is not { } host)
        {
            return null;
        }

        object? top = null;
        var topY = 0.0;
        foreach (var child in host.Children.OfType<FrameworkElement>())
        {
            // 使い回しを待っている入れ物も子に残る（前の位置のまま）。項目とつながっている物だけを見る
            var item = list.ItemContainerGenerator.ItemFromContainer(child);
            if (item == DependencyProperty.UnsetValue || !child.IsVisible)
            {
                continue;
            }

            var y = child.TranslatePoint(new Point(0, 0), viewer).Y;
            if (y + child.ActualHeight <= 0)
            {
                continue;
            }

            if (top is null || y < topY)
            {
                top = item;
                topY = y;
            }
        }

        return top is not null && keyOf(top) is { } key ? new ListAnchor(key, topY) : null;
    }

    /// <summary>覚えた項目を、覚えたときと同じ高さに戻す。項目が無ければ何もしない（先頭のまま）。</summary>
    /// <param name="keysOf">一覧の項目が持つ鍵。行なら行に並ぶカード全部（列数が変わっても、その行を探し当てられるように）。</param>
    public static void Restore(ItemsControl list, ListAnchor anchor, Func<object, IEnumerable<string>> keysOf)
    {
        var index = -1;
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (keysOf(list.Items[i]).Contains(anchor.Key, StringComparer.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        list.UpdateLayout();
        if (FindDescendant<ScrollViewer>(list) is not { } viewer)
        {
            return;
        }

        // 行単位で流す一覧（リストの表示）は、位置がそのまま行の番号。行の途中の高さは持てないので行の頭に合わせる
        if (viewer.CanContentScroll && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Item)
        {
            viewer.ScrollToVerticalOffset(index);
            return;
        }

        // px で流す一覧は、画面外の行の高さが見積もりなので、行の番号から量を計算しても合わない。
        // いったんその行を作って見える所へ出し、実際の高さを測ってから、覚えたずれに合わせて寄せる
        if (FindItemsHost(list) is VirtualizingStackPanel panel)
        {
            panel.BringIndexIntoViewPublic(index);
        }
        else if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement element)
        {
            element.BringIntoView();
        }

        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
        {
            var y = container.TranslatePoint(new Point(0, 0), viewer).Y;
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + y - anchor.Offset);
        }
    }

    /// <summary>
    /// 外の ScrollViewer の中の、仮想化していない一覧の戻し。項目の入れ物は全部あるので、実際の位置を測って寄せる。
    /// 項目が無ければ false（呼んだ側が、どこに置くかを決める）
    /// </summary>
    public static bool Restore(ScrollViewer viewer, ItemsControl list, ListAnchor anchor, Func<object, IEnumerable<string>> keysOf)
    {
        var index = -1;
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (keysOf(list.Items[i]).Contains(anchor.Key, StringComparer.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement container)
        {
            return false;
        }

        var y = container.TranslatePoint(new Point(0, 0), viewer).Y;
        viewer.ScrollToVerticalOffset(viewer.VerticalOffset + y - anchor.Offset);
        return true;
    }

    private static Panel? FindItemsHost(ItemsControl list)
    {
        // 行の中の一覧（カードを横に並べる ItemsControl）の板も IsItemsHost なので、持ち主で見分ける
        return FindDescendant<Panel>(list, panel => panel.IsItemsHost && ReferenceEquals(ItemsControl.GetItemsOwner(panel), list));
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool>? match = null)
        where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T found && (match is null || match(found)))
                {
                    return found;
                }

                queue.Enqueue(child);
            }
        }

        return null;
    }
}
