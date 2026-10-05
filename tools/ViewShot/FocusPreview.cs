using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ViewShot;

/// <summary>
/// キーボードのフォーカスの印（<c>FocusVisualStyle</c>）を、窓の無い舞台で見えるようにする。
/// WPF 自身も印を装飾の層（AdornerLayer）の Control として出すので、同じ層に同じ型の Control を載せれば、
/// 一覧の端での切れ方も同じになる
/// </summary>
internal static class FocusPreview
{
    public static void Show(FrameworkElement target, string styleKey, FrameworkElement? resourceOwner = null)
    {
        var layer = AdornerLayer.GetAdornerLayer(target) ?? throw new InvalidOperationException("装飾の層がない");

        // 主窓の資源に置いた型（ナビの RailFocusVisual）は、アプリの資源からは引けない
        var style = (Style)(resourceOwner is null ? Application.Current.FindResource(styleKey) : resourceOwner.FindResource(styleKey));
        layer.Add(new Mark(target, style));
    }

    private sealed class Mark : Adorner
    {
        private readonly Control _visual;

        public Mark(UIElement target, Style style)
            : base(target)
        {
            IsHitTestVisible = false;
            _visual = new Control { Style = style };
            AddVisualChild(_visual);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _visual;

        protected override Size MeasureOverride(Size constraint)
        {
            _visual.Measure(AdornedElement.RenderSize);
            return AdornedElement.RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _visual.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
