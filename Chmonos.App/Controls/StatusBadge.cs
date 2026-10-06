using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chmonos.App.Controls;

/// <summary>
/// カードの下の段の札（枠と中の文字1つ）。<see cref="BadgeOverflowPanel"/> が丸にした間（<see cref="BadgeOverflowPanel.IsDotProperty"/>）は、
/// 文字を描かずに中の文字の色の丸だけを描く（ユーザ判断 2026-10-06）。
///
/// 丸のための部品を別に持たないのは、カードが数千枚並び、1枚の部品の数がそのまま流す重さになるため（`CardInfoStrip` と同じ考え）。
/// 中の文字は消さずに大きさ0で置く——読み上げには札の名前が残り、結び付けの値も上書きしない。
/// 札のままの幅は丸の間も測っておき（<see cref="FullWidth"/>）、幅が広がったら札に戻せるかをパネルが丸を解かずに見られる。
/// </summary>
public sealed class StatusBadge : Border
{
    /// <summary>丸1つの幅（押せる所）。丸そのものより少し広くし、押しやすくする。</summary>
    public const double DotSlot = 10;

    /// <summary>丸の直径。札の文字（10pt）の字面の高さほどにした。</summary>
    public const double DotDiameter = 8;

    /// <summary>札のまま描いたときの幅（最後に測ったとき）。</summary>
    public double FullWidth { get; private set; }

    private bool IsDot => BadgeOverflowPanel.GetIsDot(this);

    protected override Size MeasureOverride(Size constraint)
    {
        var full = base.MeasureOverride(constraint);
        FullWidth = full.Width;

        // 高さは札のままにする（丸の札と文字の札で段の高さが変わらない）
        return IsDot ? new Size(DotSlot, full.Height) : full;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (!IsDot)
        {
            return base.ArrangeOverride(finalSize);
        }

        Child?.Arrange(new Rect(0, 0, 0, 0));
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsDot)
        {
            base.OnRender(dc);
            return;
        }

        // 透明な地を敷くのは、丸の外の隙間でも押せるようにするため（更新ありの丸はボタン）
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var brush = (Child as TextBlock)?.Foreground ?? BorderBrush;
        var radius = DotDiameter / 2;
        dc.DrawEllipse(brush, null, new Point(RenderSize.Width / 2, RenderSize.Height / 2), radius, radius);
    }
}
