using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 画面を**見えている幅で**並べ、それでも中身が収まらないときだけ横に送らせる入れ物（ScrollViewer の中に置く）。
///
/// 窓の最小の幅は付けない（ユーザ判断 2026-09-23：狭いときは横にもスクロール）。
/// そのままでは、窓が画面の中身の最小（左右の列の最小の合計など）より狭いと、右の方が窓の外に切れて届かなかった
/// （編集画面の「前へ」「スキップ」・タグ・属性）。
///
/// 素の ScrollViewer に入れると中身を無限の幅で測るので、「*」の列も折り返しも効かなくなる。
/// そこで送る仕組み（IScrollInfo）を自分で持ち、見えている幅で測る。中身が最小の幅のせいでそれより広く並んだら
/// （WPF は要る幅より狭く並べられた部品を、要る幅のまま並べて端を切る）、その幅を送れる幅として ScrollViewer に知らせる。
/// 見えている幅に収まる画面は、入れる前と同じ並びになる。縦は画面ごとに自分で送るので、ここでは送らない
/// </summary>
public sealed class ViewportFitHost : Decorator, IScrollInfo
{
    // 矢印・ホイールで1回に送る幅。行の高さ（16px 前後）に合わせた
    private const double LineWidth = 16;

    private double _offset;
    private double _extent;
    private Size _viewport;

    public ViewportFitHost()
    {
        // 送る仕組みを自分で持つと、ScrollViewer は外へはみ出た分を切ってくれない。
        // 切らないと、送った分だけ画面がナビの上に重なって描かれた
        ClipToBounds = true;
    }

    public ScrollViewer? ScrollOwner { get; set; }

    public bool CanHorizontallyScroll { get; set; }

    public bool CanVerticallyScroll { get; set; }

    public double ExtentWidth => _extent;

    public double ExtentHeight => _viewport.Height;

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => _offset;

    public double VerticalOffset => 0;

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
        {
            return default;
        }

        // CanContentScroll の下では、ScrollViewer は見えている大きさをそのまま渡してくる（無限にしない）
        Child.Measure(constraint);
        return new Size(
            double.IsInfinity(constraint.Width) ? Child.DesiredSize.Width : constraint.Width,
            double.IsInfinity(constraint.Height) ? Child.DesiredSize.Height : constraint.Height);
    }

    private readonly List<FrameworkElement> _tracked = [];

    /// <summary>
    /// 最小の幅を持つ列の入れ物（<see cref="PaneGrid"/>）を見張りに加える。
    ///
    /// WPF は、要る幅より狭く並べられた部品を要る幅のまま並べて端を切るが、その親は渡された幅のまま答える。
    /// 画面の奥にある列の入れ物がはみ出しても、ここ（画面のいちばん外）までは知らせが上がってこないので、
    /// はみ出しうる所を直に見る。どの画面も、窓より広くなりうるのは左右の列の最小の合計だけ
    /// </summary>
    internal void Track(FrameworkElement element)
    {
        if (!_tracked.Contains(element))
        {
            _tracked.Add(element);
            element.SizeChanged += OnTrackedSizeChanged;
            InvalidateArrange();
        }
    }

    internal void Untrack(FrameworkElement element)
    {
        if (_tracked.Remove(element))
        {
            element.SizeChanged -= OnTrackedSizeChanged;
            InvalidateArrange();
        }
    }

    private void OnTrackedSizeChanged(object sender, SizeChangedEventArgs e) => InvalidateArrange();

    /// <summary>見張っている入れ物が、並べられた枠からはみ出した幅のうち最大。</summary>
    private double TrackedOverflow()
    {
        var overflow = 0.0;
        foreach (var element in _tracked)
        {
            if (!element.IsVisible)
            {
                continue;
            }

            var slot = LayoutInformation.GetLayoutSlot(element);
            var width = slot.Width - element.Margin.Left - element.Margin.Right;
            overflow = Math.Max(overflow, element.RenderSize.Width - width);
        }

        return overflow;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var extent = arrangeSize.Width;
        if (Child is not null)
        {
            // まず見えている幅で並べる。最小の幅のせいで収まらない中身は、要る幅のまま並んで返ってくる
            Child.Arrange(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height));
            extent = Math.Max(arrangeSize.Width, Child.RenderSize.Width);
            extent = Math.Max(extent, arrangeSize.Width + Math.Ceiling(TrackedOverflow()));
        }

        var changed = Math.Abs(extent - _extent) > 0.5 || _viewport != arrangeSize;
        _extent = extent;
        _viewport = arrangeSize;
        _offset = Clamp(_offset);

        // 見えている幅を超える分は、送った位置から並べ直す（端で切れていた所が届く）
        if (Child is not null && (extent > arrangeSize.Width || _offset > 0))
        {
            Child.Arrange(new Rect(-_offset, 0, extent, arrangeSize.Height));
        }

        if (changed)
        {
            ScrollOwner?.InvalidateScrollInfo();
        }

        return arrangeSize;
    }

    private double Clamp(double offset) => Math.Max(0, Math.Min(offset, _extent - _viewport.Width));

    public void SetHorizontalOffset(double offset)
    {
        var next = Clamp(offset);
        if (Math.Abs(next - _offset) < 0.5)
        {
            return;
        }

        _offset = next;
        InvalidateArrange();
        ScrollOwner?.InvalidateScrollInfo();
    }

    public void SetVerticalOffset(double offset)
    {
    }

    public void LineLeft() => SetHorizontalOffset(_offset - LineWidth);

    public void LineRight() => SetHorizontalOffset(_offset + LineWidth);

    public void PageLeft() => SetHorizontalOffset(_offset - _viewport.Width);

    public void PageRight() => SetHorizontalOffset(_offset + _viewport.Width);

    public void MouseWheelLeft() => SetHorizontalOffset(_offset - LineWidth * 3);

    public void MouseWheelRight() => SetHorizontalOffset(_offset + LineWidth * 3);

    // 縦は画面ごとの一覧が送る。ここでは何もしない
    public void LineUp()
    {
    }

    public void LineDown()
    {
    }

    public void PageUp()
    {
    }

    public void PageDown()
    {
    }

    public void MouseWheelUp()
    {
    }

    public void MouseWheelDown()
    {
    }

    /// <summary>
    /// Tab で移った先が窓の外にあれば、横に送って見せる（キーボードだけでも右の欄へ届くように）。
    /// </summary>
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (rectangle.IsEmpty || visual is null || !IsAncestorOf(visual))
        {
            return Rect.Empty;
        }

        var bounds = visual.TransformToAncestor(this).TransformBounds(rectangle);
        var left = bounds.Left;
        var right = bounds.Right;
        if (left < 0)
        {
            SetHorizontalOffset(_offset + left);
        }
        else if (right > _viewport.Width)
        {
            // 要素が見える幅より広ければ左端を合わせる
            SetHorizontalOffset(_offset + Math.Min(left, right - _viewport.Width));
        }

        return bounds;
    }
}
