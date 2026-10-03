using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 欄の下の縁に置いて、ドラッグで欄の高さを変えるつまみ（ユーザ指摘 2026-10-02 メモ4：編集画面の商品説明）。
/// **ダブルクリックで既定の高さに戻す**のと、乗ったときだけ色を付けるのは、画面の幅の境目（<see cref="PaneSplitter"/>）と同じ作法。
///
/// PaneSplitter（GridSplitter）を縦に使わないのは、欄が縦に流れる列（StackPanel）の中にあり、
/// GridSplitter が要る「行を分けた Grid」と、譲り合う隣の行が無いため。高さの値を直に動かす。
///
/// 高さは <see cref="Length"/> に TwoWay で結ぶ。測り始めは <see cref="Target"/> の今の高さ——値は上限として使うので、
/// 短い説明では今の高さが値より低く、値から動かすと、引き上げてもしばらく何も変わらない
/// </summary>
public sealed class HeightGrip : Thumb
{
    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(HeightGrip),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MinLengthProperty = DependencyProperty.Register(
        nameof(MinLength), typeof(double), typeof(HeightGrip), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaxLengthProperty = DependencyProperty.Register(
        nameof(MaxLength), typeof(double), typeof(HeightGrip), new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(FrameworkElement), typeof(HeightGrip));

    public static readonly DependencyProperty ResetCommandProperty = DependencyProperty.Register(
        nameof(ResetCommand), typeof(ICommand), typeof(HeightGrip));

    private double _dragged;

    public HeightGrip()
    {
        Height = 8;
        Cursor = Cursors.SizeNS;
        Focusable = false;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip = "ドラッグで高さを変えます。ダブルクリックで元の高さに戻します。";
        Template = BuildTemplate();
        Highlight(false);

        DragStarted += (_, _) => BeginResize();
        DragDelta += (_, e) =>
        {
            Resize(e.VerticalChange);
            if (e.VerticalChange > 0)
            {
                // 伸ばしたぶんの配置が済んでから、つまみの位置を測る
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FollowIntoView);
            }
        };
        DragCompleted += (_, _) => Highlight(IsMouseOver);
    }

    /// <summary>欄の高さ（上限）。ドラッグで書き換える。</summary>
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    public double MinLength
    {
        get => (double)GetValue(MinLengthProperty);
        set => SetValue(MinLengthProperty, value);
    }

    public double MaxLength
    {
        get => (double)GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>高さを変える欄。ドラッグを始めた時点の高さを、ここから測る。</summary>
    public FrameworkElement? Target
    {
        get => (FrameworkElement?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>ダブルクリックで呼ぶ（既定の高さに戻す）。</summary>
    public ICommand? ResetCommand
    {
        get => (ICommand?)GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    /// <summary>
    /// 縦に <paramref name="change"/> だけ引いた。つまみは欄の下の縁に付いて一緒に動くので、Thumb が渡す量はその都度の差分。
    /// 範囲の外へ引いた分も手元では数え続ける——端で止めた所から数え直すと、引き戻したときにマウスとつまみがずれる
    /// </summary>
    internal void Resize(double change)
    {
        _dragged += change;
        Length = Math.Round(Math.Clamp(_dragged, MinLength, Math.Max(MinLength, MaxLength)));
    }

    /// <summary>
    /// 伸ばしてつまみが外側の流れる入れ物の見える範囲の下へ出たら、出た分だけ外側を流す（メモ18）。
    /// 流さないと、つまみが見えなくなって引き続けられない。縮めるときは呼ばない（欄が縮むと外側の範囲の方が縮み、位置が勝手に戻る）
    /// </summary>
    internal void FollowIntoView()
    {
        var scroller = FindAncestor<ScrollViewer>(this);
        if (scroller is null)
        {
            return;
        }

        var bottom = TransformToAncestor(scroller).Transform(new Point(0, ActualHeight)).Y;
        var excess = OverflowBelow(bottom, scroller.ViewportHeight);
        if (excess > 0)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset + excess);
        }
    }

    /// <summary>つまみの下端（見える範囲の上端から測る）が、見える高さを越えた量。越えていなければ 0。</summary>
    internal static double OverflowBelow(double gripBottom, double viewportHeight) =>
        Math.Max(0, gripBottom - viewportHeight);

    private static T? FindAncestor<T>(DependencyObject from) where T : DependencyObject
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

    /// <summary>ドラッグを始めた。今の欄の高さから数える（試験からも呼ぶ）。</summary>
    internal void BeginResize() => _dragged = Target?.ActualHeight ?? Length;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Highlight(true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Highlight(IsDragging);
    }

    protected override void OnMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (ResetCommand?.CanExecute(null) == true)
        {
            ResetCommand.Execute(null);
        }

        e.Handled = true;
    }

    // 鍵で指す。色を取り出して入れると、掴んでいる間に色の表を差し替えたとき古い色のまま残る（PaneSplitter と同じ）
    private void Highlight(bool on)
    {
        if (on)
        {
            SetResourceReference(BackgroundProperty, "BorderStrong");
        }
        else
        {
            Background = Brushes.Transparent;
        }
    }

    /// <summary>
    /// 帯の真ん中に短い線を1本引く。幅の境目は線が枠と重なって見えるが、欄の下の縁は枠の内側にあり、
    /// 何も描かないと掴める所だと分からない
    /// </summary>
    private static ControlTemplate BuildTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var mark = new FrameworkElementFactory(typeof(Border));
        mark.SetValue(WidthProperty, 32.0);
        mark.SetValue(HeightProperty, 3.0);
        mark.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetResourceReference(Border.BackgroundProperty, "BorderStrong");
        root.AppendChild(mark);

        return new ControlTemplate(typeof(HeightGrip)) { VisualTree = root };
    }
}
