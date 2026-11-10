using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 画面の木（Visual）をたどる小さな手伝い。部品ごとに同じ関数が写されていたので、1か所に寄せた（2026-11-10）。
/// 見方が違う物（<see cref="ContentItemsControl.FindScrollViewer"/> は項目の中へ下りない、
/// <see cref="DragEdgeScroll.FindScroller"/> は並びの外側の流す枠を取る、一覧の板を探す3通り）は、見方そのものが違うので寄せていない。
/// </summary>
public static class VisualTreeFinder
{
    /// <summary>
    /// 部品の外側にある、画面に置いた「ページを流す部品」。部品の型の中の流す部品（一覧や箱が自分で持つ物。TemplatedParent が有る）は飛ばす。
    /// <see cref="DeferredSection"/> と <see cref="ProgressiveItems"/> は、同じ流す部品を見る前提で連携している
    /// （<c>FeedAllNow</c> が欄を出す先と、足し切る先がずれないよう、両方がここを呼ぶ）
    /// </summary>
    public static ScrollViewer? FindPageScroller(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer { TemplatedParent: null } scroller)
            {
                return scroller;
            }
        }

        return null;
    }

    /// <summary>子孫のうち最初の T（深さ優先。木の並びの順）。</summary>
    public static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }

            if (FindDescendant<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    /// <summary>子孫のうち、浅い方から数えて最初の T（幅優先）。条件を渡せる。</summary>
    public static T? FindDescendantBreadthFirst<T>(DependencyObject root, Func<T, bool>? match = null)
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

    /// <summary>祖先のうち最も近い T。</summary>
    public static T? FindAncestor<T>(DependencyObject from)
        where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(from); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }
}
