using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// 流せるページの中の欄1つの入れ物。**別の商品へ進んだとき、最初の画面に見えない欄は、絵が出た後に1つずつ作る**（ユーザ指示 2026-11-08）。
///
/// 商品ページは1つの画面を持ち回し（<c>ItemViewHost</c>）、商品が替わると全部の欄の結び付けと一覧が新しい商品で作り直される。
/// 画面の外の欄（対応アバター・ファイル・改変など）の分も、最初の絵が出る前に済ませていた。
///
/// 動き：
/// <list type="bullet">
/// <item>中身（子）の DataContext は、この入れ物が自分で渡す（上から受け継がせない）。進むときは前の商品のまま隠し、新しい商品の結び付けを走らせない</item>
/// <item>次の配置の終わりに、ページの先頭から見える範囲（＋先読み）に入った欄は、その場で出す。描く前にもう一度配置が回るので、最初のコマは全部を作ったときと同じ</item>
/// <item>残りは絵を描いた後に、1コマに1つずつ出す（入力と描画を先に通す。<see cref="AfterRender"/>）</item>
/// </list>
///
/// 分けないとき（その場で出す）：流せるページの中に無い・最初の配置で測れない（ページに載っていない・欄ごと隠れている）・前の位置を保つ開き直し（先頭へ戻る印が無く、流した位置が残る）。
/// 戻る・進むで途中へ戻すとき・画面内検索・変わった所へ流すときは、<see cref="ProgressiveItems.FeedAllNow"/> がここも全部出す
/// </summary>
public sealed class DeferredSection : Decorator
{
    private static readonly List<DeferredSection> Held = [];
    private static bool _scheduled;
    private static UIElement? _watcher;

    private object? _shown;
    private bool _held;

    public DeferredSection()
    {
        DataContextChanged += (_, e) => OnContextChanged(e.NewValue);
    }

    internal bool IsHeld => _held;

    protected override void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        if (visualAdded is FrameworkElement child)
        {
            _shown = DataContext;
            child.DataContext = DataContext;
        }
    }

    private void OnContextChanged(object? next)
    {
        if (Child is not FrameworkElement child)
        {
            return;
        }

        if (ReferenceEquals(next, _shown))
        {
            Release();
            return;
        }

        var scroller = VisualTreeFinder.FindPageScroller(this);
        var startsAtTop = scroller is not null && (scroller.VerticalOffset <= 0 || ProgressiveItems.GetReturnsToTop(scroller));
        // 載り終わる（Loaded）前でも分ける。検索から開くと、画面を載せ直す途中で商品が替わる（ItemViewHost）
        if (next is null || _shown is null || !startsAtTop)
        {
            Show(child, next);
            return;
        }

        // 前の商品の中身のまま隠す（新しい商品の結び付けはまだ走らせない）
        if (!_held)
        {
            _held = true;
            Held.Add(this);
            child.Visibility = Visibility.Collapsed;
            Watch(this);
        }
    }

    /// <summary>
    /// 配置の回の終わりを1か所で聞く。配置の終わりの知らせは隠している欄すべてに同時に届き、どの欄も高さ0のまま測ると
    /// 全部が「見える」と出てしまう。1回の知らせで、見える範囲にあるいちばん上の欄を1つだけ出す。
    /// 出すと描く前にもう一度配置が回るので、同じコマの中で次の欄を測り直せる（ProgressiveItems が1行ずつ足すのと同じ）
    /// </summary>
    private static void Watch(UIElement element)
    {
        if (_watcher is null)
        {
            _watcher = element;
            element.LayoutUpdated += OnLayout;
        }
    }

    private static void OnLayout(object? sender, EventArgs e)
    {
        Held.RemoveAll(section => !section._held);
        DeferredSection? topmost = null;
        var best = double.PositiveInfinity;
        foreach (var section in Held.ToList())
        {
            if (VisualTreeFinder.FindPageScroller(section) is not { ViewportHeight: > 0, Content: UIElement content } scroller || !section.IsVisible || !content.IsAncestorOf(section))
            {
                // 測れない（ページから外れた・欄ごと隠れた）。分ける理由が無いので出す
                section.Release();
                return;
            }

            // 流した位置ではなく、ページの先頭からの距離で測る（先頭へ戻す命令は、こちらが並べ直しを頼む間は後へ回る。ProgressiveItems.NeedsMore と同じ）
            var top = section.TranslatePoint(default, content).Y;
            if (top < scroller.ViewportHeight + ProgressiveItems.LookAhead && top < best)
            {
                best = top;
                topmost = section;
            }
        }

        if (topmost is not null)
        {
            topmost.Release();
            return;
        }

        _watcher!.LayoutUpdated -= OnLayout;
        _watcher = null;
        if (Held.Count > 0)
        {
            Schedule();
        }
    }

    /// <summary>次のコマを描いた後に、欄を1つ出す（Background に積むだけでは、絵の前に割り込むことがある：<see cref="AfterRender"/>）。</summary>
    private static void Schedule()
    {
        if (!_scheduled)
        {
            _scheduled = true;
            AfterRender.Run(ReleaseNextHeld);
        }
    }

    /// <summary>隠している欄を1つ出す（ページの上の欄から）。続きがあれば、また次のコマの後に頼む（1コマに1つ。入力と描画を先に通す）。</summary>
    private static void ReleaseNextHeld()
    {
        _scheduled = false;
        Held.RemoveAll(section => !section._held);
        if (Held.Count == 0 || _watcher is not null)
        {
            return;
        }

        Held.MinBy(section => VisualTreeFinder.FindPageScroller(section)?.Content is UIElement content && content.IsAncestorOf(section)
            ? section.TranslatePoint(default, content).Y
            : double.NegativeInfinity)!.Release();

        if (Held.Count > 0)
        {
            Schedule();
        }
    }

    private void Release()
    {
        if (Child is FrameworkElement child)
        {
            Show(child, DataContext);
        }
    }

    private void Show(FrameworkElement child, object? context)
    {
        if (_held)
        {
            _held = false;
            Held.Remove(this);
        }

        _shown = context;
        child.DataContext = context;
        child.ClearValue(VisibilityProperty);
    }

    /// <summary>隠している欄を今すぐ全部出す（<paramref name="scope"/> の中だけ）。出した物があれば true。</summary>
    internal static bool ReleaseAllNow(DependencyObject? scope)
    {
        var released = false;
        foreach (var section in Held.ToList())
        {
            if (scope is Visual visual && !visual.IsAncestorOf(section))
            {
                continue;
            }

            section.Release();
            released = true;
        }

        return released;
    }

}
