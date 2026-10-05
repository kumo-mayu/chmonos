using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// カードの下の段の札の並び（メモ52）。札が入り切らないときは、入るだけ出して残りを「+n」にまとめ、
/// 幅からはみ出さない（左のカードの端を越えたり、容量の文字に重なったりしない）。
/// </summary>
public class BadgeOverflowPanelTests
{
    private const double BadgeWidth = 40;

    private static (BadgeOverflowPanel Panel, List<Border> Badges, Border More) Build(int badgeCount, int collapsed = 0)
    {
        var panel = new BadgeOverflowPanel { Gap = 4 };
        var badges = new List<Border>();
        for (var i = 0; i < badgeCount; i++)
        {
            var badge = new Border { Width = BadgeWidth, Height = 16 };
            BadgeOverflowPanel.SetLabel(badge, $"札{i}");
            if (i < collapsed)
            {
                badge.Visibility = Visibility.Collapsed;
            }

            badges.Add(badge);
            panel.Children.Add(badge);
        }

        var more = new Border { Child = new TextBlock { FontSize = 10 }, Padding = new Thickness(7, 1, 7, 1) };
        BadgeOverflowPanel.SetIsMore(more, true);
        panel.Children.Add(more);
        return (panel, badges, more);
    }

    private static void Lay(Panel panel, double width)
    {
        panel.Measure(new Size(width, 40));
        panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));
    }

    [Fact]
    public Task 全部入るときは_そのまま出し_ほかの札は出さない() => UiThread.Run(() =>
    {
        var (panel, badges, more) = Build(3);
        Lay(panel, 3 * (BadgeWidth + 4));

        Assert.Equal(0, panel.HiddenCount);
        Assert.All(badges, badge => Assert.True(BadgeOverflowPanel.GetIsShown(badge)));
        Assert.False(BadgeOverflowPanel.GetIsShown(more));
    });

    [Fact]
    public Task 入り切らないときは_入るだけ前から出して_残りを数にまとめる() => UiThread.Run(() =>
    {
        var (panel, badges, more) = Build(5);
        Lay(panel, 130);

        Assert.True(panel.HiddenCount is > 0 and < 5);
        var shown = 5 - panel.HiddenCount;
        Assert.All(badges.Take(shown), badge => Assert.True(BadgeOverflowPanel.GetIsShown(badge)));
        Assert.All(badges.Skip(shown), badge => Assert.False(BadgeOverflowPanel.GetIsShown(badge)));
        Assert.Equal($"+{panel.HiddenCount}", ((TextBlock)more.Child).Text);
        Assert.True(BadgeOverflowPanel.GetIsShown(more));
    });

    [Fact]
    public Task 出した札は_左端を越えず_ほかの札と重ならない() => UiThread.Run(() =>
    {
        var (panel, badges, more) = Build(5);
        Lay(panel, 130);

        var rects = badges.Concat([more]).Where(element => BadgeOverflowPanel.GetIsShown(element))
            .Select(element => element.TransformToAncestor(panel).TransformBounds(new Rect(element.RenderSize))).ToList();
        Assert.All(rects, rect => Assert.InRange(rect.Left, 0, 130));
        Assert.All(rects, rect => Assert.True(rect.Right <= panel.RenderSize.Width + 0.01));
        for (var i = 0; i < rects.Count; i++)
        {
            for (var j = i + 1; j < rects.Count; j++)
            {
                Assert.False(rects[i].IntersectsWith(new Rect(rects[j].X + 0.5, rects[j].Y, Math.Max(0, rects[j].Width - 1), rects[j].Height)));
            }
        }

        Assert.True(panel.DesiredSize.Width <= 130);
    });

    [Fact]
    public Task 幅がごく狭いときは_札を出さず_数だけを出す() => UiThread.Run(() =>
    {
        var (panel, _, more) = Build(3);
        Lay(panel, 45);

        Assert.Equal(3, panel.HiddenCount);
        Assert.Equal("+3", ((TextBlock)more.Child).Text);
    });

    [Fact]
    public Task 見えていない札は_数えない() => UiThread.Run(() =>
    {
        var (panel, _, _) = Build(5, collapsed: 3);
        Lay(panel, 2 * (BadgeWidth + 4));

        Assert.Equal(0, panel.HiddenCount);
    });

    [Fact]
    public Task 出せなかった札の名前は_ほかの札の吹き出しに全部書く() => UiThread.Run(() =>
    {
        var (panel, _, more) = Build(4);
        Lay(panel, 100);

        var tip = (string)more.ToolTip;
        Assert.Contains("札3", tip);
        Assert.DoesNotContain("札0", tip);
    });
}
