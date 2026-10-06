using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// カードの下の段の札の並び（ユーザ判断 2026-10-06）。札が入り切らないときは、後ろの札から文字の無い色の丸にして幅に収める
/// （前は入らない分を「+n」にまとめていた・メモ52）。左のカードの端を越えず、容量の文字に重ならない。
/// 丸に乗せると札の名前の吹き出し、押せる札（更新あり）は丸でも押せる。
/// </summary>
public class BadgeOverflowPanelTests
{
    /// <summary>札1枚の幅：中の文字 26 ＋ 余白 7×2。</summary>
    private const double BadgeWidth = 40;

    private static StatusBadge MakeBadge(string label, string tip = "")
    {
        var badge = new StatusBadge
        {
            Padding = new Thickness(7, 1, 7, 1),
            Child = new TextBlock { Text = label, Width = BadgeWidth - 14, Height = 14, Foreground = Brushes.Red },
        };
        BadgeOverflowPanel.SetLabel(badge, label);
        BadgeOverflowPanel.SetTip(badge, tip);
        return badge;
    }

    private static (BadgeOverflowPanel Panel, List<StatusBadge> Badges) Build(int badgeCount, int collapsed = 0)
    {
        var panel = new BadgeOverflowPanel { Gap = 4 };
        var badges = new List<StatusBadge>();
        for (var i = 0; i < badgeCount; i++)
        {
            var badge = MakeBadge($"札{i}");
            if (i < collapsed)
            {
                badge.Visibility = Visibility.Collapsed;
            }

            badges.Add(badge);
            panel.Children.Add(badge);
        }

        return (panel, badges);
    }

    private static void Lay(Panel panel, double width)
    {
        panel.Measure(new Size(width, 40));
        panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));
    }

    private const double Gap = 4;

    /// <summary>丸の隣の間（パネルの既定）。</summary>
    private static readonly double DotGap = new BadgeOverflowPanel().DotGap;

    /// <summary>前から札 <paramref name="texts"/> 枚・丸 <paramref name="dots"/> つが、ちょうど入る幅（端の間は数えない）。</summary>
    private static double Needed(int texts, int dots)
    {
        var width = texts * BadgeWidth + dots * StatusBadge.DotSlot;
        width += Math.Max(0, texts - 1) * Gap;
        width += Math.Max(0, dots - 1) * DotGap;
        width += texts > 0 && dots > 0 ? DotGap : 0;
        return width;
    }

    /// <summary>札3枚＋丸2つが入る幅。</summary>
    private static readonly double ThreeAndTwoDots = Needed(3, 2);

    [Fact]
    public Task 全部入るときは_札のまま出す() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(3);
        Lay(panel, Needed(3, 0));

        Assert.Equal(0, panel.DotCount);
        Assert.All(badges, badge => Assert.False(BadgeOverflowPanel.GetIsDot(badge)));
        Assert.All(badges, badge => Assert.Equal(BadgeWidth, badge.DesiredSize.Width));
    });

    [Fact]
    public Task 入り切らないときは_後ろの札から丸にして_前の札は文字のまま残す() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, ThreeAndTwoDots);

        Assert.Equal(2, panel.DotCount);
        Assert.Equal(0, panel.HiddenCount);
        Assert.All(badges.Take(3), badge => Assert.False(BadgeOverflowPanel.GetIsDot(badge)));
        Assert.All(badges.Skip(3), badge => Assert.True(BadgeOverflowPanel.GetIsDot(badge)));
        Assert.All(badges.Skip(3), badge => Assert.Equal(StatusBadge.DotSlot, badge.DesiredSize.Width));
        Assert.All(badges, badge => Assert.True(BadgeOverflowPanel.GetIsShown(badge)));
    });

    [Fact]
    public Task 丸にしても_段の高さは札のときと変わらない() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, 1000);
        var height = panel.DesiredSize.Height;

        Lay(panel, ThreeAndTwoDots);

        Assert.Equal(height, panel.DesiredSize.Height);
        Assert.All(badges, badge => Assert.Equal(height, badge.DesiredSize.Height));
    });

    [Fact]
    public Task 幅が広がれば_丸から札に戻る() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, ThreeAndTwoDots);
        Assert.Equal(2, panel.DotCount);

        Lay(panel, Needed(5, 0));

        Assert.Equal(0, panel.DotCount);
        Assert.All(badges, badge => Assert.False(BadgeOverflowPanel.GetIsDot(badge)));
        Assert.All(badges, badge => Assert.Equal(BadgeWidth, badge.DesiredSize.Width));
    });

    /// <summary>出した札の、パネルの中の四角（並びの順）。</summary>
    private static List<Rect> ShownRects(BadgeOverflowPanel panel, IEnumerable<StatusBadge> badges)
        => badges.Where(BadgeOverflowPanel.GetIsShown)
            .Select(element => element.TransformToAncestor(panel).TransformBounds(new Rect(element.RenderSize))).ToList();

    [Fact]
    public Task 出した札と丸は_左端を越えず_重ならない() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, 130);

        Assert.True(panel.DotCount > 0);
        var rects = ShownRects(panel, badges);
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

    /// <summary>
    /// 見えている形の左右の端（丸は押せる幅の真ん中に描く直径の分、札は枠いっぱい）。
    /// </summary>
    private static (double Left, double Right) Visible(Rect rect, bool dot)
        => dot
            ? (rect.Left + (rect.Width - StatusBadge.DotDiameter) / 2, rect.Right - (rect.Width - StatusBadge.DotDiameter) / 2)
            : (rect.Left, rect.Right);

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    [InlineData(0, 5)]
    public Task 丸の隣は_丸の直径より広く空ける(int texts, int dots) => UiThread.Run(() =>
    {
        // 間4では、丸どうしの見える隙間が6で直径8より狭く、並んだ丸が詰まって1つの塊に見えた（ユーザ指摘 2026-10-06）
        var (panel, badges) = Build(texts + dots);
        Lay(panel, Needed(texts, dots));
        Assert.Equal(dots, panel.DotCount);

        var rects = ShownRects(panel, badges);
        Assert.Equal(texts + dots, rects.Count);
        for (var i = 1; i < rects.Count; i++)
        {
            var leftDot = BadgeOverflowPanel.GetIsDot(badges[i - 1]);
            var rightDot = BadgeOverflowPanel.GetIsDot(badges[i]);
            var space = Visible(rects[i], rightDot).Left - Visible(rects[i - 1], leftDot).Right;
            if (leftDot || rightDot)
            {
                Assert.True(space >= StatusBadge.DotDiameter, $"{i - 1} と {i} の見える隙間 {space}");
            }
            else
            {
                Assert.True(space >= Gap - 0.01, $"{i - 1} と {i} の隙間 {space}");
            }
        }
    });

    [Fact]
    public Task 札と丸が混ざっても_左から並びの順のまま() => UiThread.Run(() =>
    {
        // 丸だけを右へ寄せ集めない。丸は並びの後ろの札なので、右の端に続けて出る
        var (panel, badges) = Build(5);
        Lay(panel, ThreeAndTwoDots);

        var lefts = ShownRects(panel, badges).Select(rect => rect.Left).ToList();
        Assert.Equal(lefts.Order(), lefts);
        Assert.Equal([false, false, false, true, true], badges.Select(BadgeOverflowPanel.GetIsDot));
    });

    [Fact]
    public Task 全部を丸にしても入らないほど狭いときは_後ろの丸から出さない() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, Needed(0, 2));

        Assert.Equal(5, panel.DotCount);
        Assert.Equal(3, panel.HiddenCount);
        Assert.All(badges.Take(2), badge => Assert.True(BadgeOverflowPanel.GetIsShown(badge)));
        Assert.All(badges.Skip(2), badge => Assert.False(BadgeOverflowPanel.GetIsShown(badge)));
    });

    [Fact]
    public Task 見えていない札は_数えない() => UiThread.Run(() =>
    {
        var (panel, _) = Build(5, collapsed: 3);
        Lay(panel, 2 * (BadgeWidth + 4));

        Assert.Equal(0, panel.DotCount);
    });

    [Fact]
    public Task 丸の吹き出しは札の名前で_札の吹き出しがあれば次の行に続ける() => UiThread.Run(() =>
    {
        var panel = new BadgeOverflowPanel { Gap = 4 };
        var first = MakeBadge("見つからない", "ファイルが見つかりません");
        var tipped = MakeBadge("未編集", "まだユーザータグを付けていません");
        var plain = MakeBadge("所持");
        panel.Children.Add(first);
        panel.Children.Add(tipped);
        panel.Children.Add(plain);
        Lay(panel, Needed(1, 2));

        Assert.False(BadgeOverflowPanel.GetIsDot(first));
        Assert.Equal("ファイルが見つかりません", first.ToolTip);
        Assert.Equal("未編集\nまだユーザータグを付けていません", tipped.ToolTip);
        Assert.Equal("所持", plain.ToolTip);

        // 札に戻れば、札の吹き出しに戻る（吹き出しの無い札は出さない）
        Lay(panel, 1000);
        Assert.Equal("まだユーザータグを付けていません", tipped.ToolTip);
        Assert.Null(plain.ToolTip);
    });

    [Fact]
    public Task 丸の間も中の文字は残り_読み上げには札の名前が出る() => UiThread.Run(() =>
    {
        var (panel, badges) = Build(5);
        Lay(panel, ThreeAndTwoDots);

        var dot = badges[4];
        var text = (TextBlock)dot.Child;
        Assert.True(BadgeOverflowPanel.GetIsDot(dot));
        Assert.Equal(Visibility.Visible, text.Visibility);
        Assert.Equal("札4", new TextBlockAutomationPeer(text).GetName());
    });

    private const string UpdateTemplate = """
        <ControlTemplate TargetType="Button"
                         xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:c="clr-namespace:Chmonos.App.Controls;assembly=Chmonos">
            <c:StatusBadge Padding="7,1" BorderThickness="1" CornerRadius="9" Background="LightBlue" BorderBrush="Blue">
                <TextBlock Text="更新あり" Width="26" Height="14" Foreground="Blue" />
            </c:StatusBadge>
        </ControlTemplate>
        """;

    [Fact]
    public Task 押せる札は_丸になっても押せば札と同じ動きをする() => UiThread.Run(() =>
    {
        var pressed = 0;
        var panel = new BadgeOverflowPanel { Gap = 4 };
        panel.Children.Add(MakeBadge("見つからない"));
        var update = new Button
        {
            Template = (ControlTemplate)XamlReader.Parse(UpdateTemplate),
            Command = new RelayCommand(_ => pressed++),
        };
        BadgeOverflowPanel.SetLabel(update, "更新あり");
        BadgeOverflowPanel.SetTip(update, "通知に更新があります。押すとその通知を開きます。");
        panel.Children.Add(update);
        var host = new Grid { Children = { panel } };
        host.Measure(new Size(Needed(1, 1), 40));
        host.Arrange(new Rect(host.DesiredSize));

        Assert.True(BadgeOverflowPanel.GetIsDot(update));
        Assert.Equal(StatusBadge.DotSlot, update.DesiredSize.Width);
        Assert.Equal("更新あり\n通知に更新があります。押すとその通知を開きます。", update.ToolTip);

        // 丸の外の隙間（透明な地）でも、押す所はボタンに当たる
        var corner = update.TransformToAncestor(host).Transform(new Point(1, 1));
        var hit = VisualTreeHelper.HitTest(host, corner)?.VisualHit as DependencyObject;
        Assert.NotNull(hit);
        Assert.Same(update, FindAncestor<Button>(hit!));

        // 押したときと同じ道（ButtonBase.OnClick がコマンドを呼ぶ）
        typeof(System.Windows.Controls.Primitives.ButtonBase)
            .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(update, null);
        Assert.Equal(1, pressed);
        Assert.True(update.IsEnabled);
    });

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }
}
