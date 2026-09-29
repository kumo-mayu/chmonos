using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BoothAssetManager.App.Controls;

/// <summary>
/// 画面の幅を変える境目（ユーザ判断 2026-09-14）。ドラッグで幅を変え、**ダブルクリックで既定の幅に戻す**。
///
/// ふだんは見えない（境の線はそれぞれの画面の枠が描いている）。乗ったときだけ色を付け、つかめる所だと分かるようにする。
/// 置き方：左の列の右端なら <c>HorizontalAlignment="Right"</c>、右の列の左端なら <c>"Left"</c>（どちらの列を動かすかは GridSplitter が決める）。
/// 幅は列の Width を画面の値へ TwoWay で結んでおくと、そのまま戻る。
/// </summary>
public sealed class PaneSplitter : GridSplitter
{
    public static readonly DependencyProperty ResetCommandProperty = DependencyProperty.Register(
        nameof(ResetCommand), typeof(ICommand), typeof(PaneSplitter));

    public PaneSplitter()
    {
        Width = 6;
        Background = Brushes.Transparent;
        Cursor = Cursors.SizeWE;
        Focusable = false;
        ShowsPreview = false;
        ResizeDirection = GridResizeDirection.Columns;
        VerticalAlignment = VerticalAlignment.Stretch;
        ToolTip = "ドラッグで幅を変えます。ダブルクリックで元の幅に戻します。";
        DragCompleted += (_, _) => Highlight(IsMouseOver);
    }

    /// <summary>ダブルクリックで呼ぶ（その場所を既定の幅に戻す）。</summary>
    public ICommand? ResetCommand
    {
        get => (ICommand?)GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

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

    // 鍵で指す。色を取り出して入れると、掴んでいる間に色の表を差し替えたとき古い色のまま残る
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
}
