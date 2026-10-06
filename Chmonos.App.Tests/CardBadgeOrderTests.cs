using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// カードとリストの札の並びと色（ユーザ判断 2026-10-06）。
/// 「見つからない」（一部見つからない）はいちばん幅を取るので一番右に置き、入らないときに真っ先に丸にする（丸にするのは後ろの札から）。
/// 「更新あり」は通知の未読の色、「取り込み中」は青のまま——丸にすると文字が消えるので、色だけで見分けられるようにする。
/// </summary>
public class CardBadgeOrderTests
{
    private static XDocument Resources() => XDocument.Load(Path.Combine(ViewsFolder(), "ItemCardResources.xaml"));

    private static string? Attr(XElement element, string name)
        => (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == name);

    /// <summary>札の名前（Label。無ければ中の文字）を並びの順に。</summary>
    private static List<string> Names(XElement panel)
        => panel.Elements()
            .Select(badge => Attr(badge, "BadgeOverflowPanel.Label")
                ?? badge.Descendants().Where(e => e.Name.LocalName == "TextBlock").Select(e => Attr(e, "Text")).FirstOrDefault()
                ?? (Attr(badge, "Style") == "{StaticResource UpdateBadgeButton}" ? "更新あり" : "?"))
            .Select(name => name == "{Binding MissingBadgeText}" ? "見つからない" : name!)
            .ToList();

    [Fact]
    public void カードの札は_見つからないが一番右()
    {
        var panel = Resources().Descendants().Single(e => e.Name.LocalName == "BadgeOverflowPanel");

        Assert.Equal(["更新あり", "取り込み中", "未編集", "所持", "見つからない"], Names(panel));
    }

    [Fact]
    public void リストの札も_カードと同じ順で見つからないは所持の後ろ()
    {
        var cell = Resources().Descendants().Single(e => e.Name.LocalName == "DataTemplate" && Attr(e, "Key") == "ItemListChipsCell");
        var wrap = cell.Descendants().First(e => e.Name.LocalName == "WrapPanel");

        Assert.Equal(["更新あり", "取り込み中", "未編集", "所持", "見つからない", "未所持"], Names(wrap));
    }

    [Fact]
    public void 更新ありと取り込み中は_文字の色も丸の色も違う鍵()
    {
        var document = Resources();
        var update = document.Descendants().Single(e => e.Name.LocalName == "Style" && Attr(e, "Key") == "UpdateBadgeButton")
            .Descendants().First(e => e.Name.LocalName == "TextBlock");
        var importing = document.Descendants().First(e => e.Name.LocalName == "StatusBadge" && Attr(e, "BadgeOverflowPanel.Label") == "取り込み中")
            .Descendants().First(e => e.Name.LocalName == "TextBlock");

        // 丸は中の文字の色で描く（StatusBadge）。文字の色が違えば丸の色も違う
        Assert.Equal("{DynamicResource Unread}", Attr(update, "Foreground"));
        Assert.Equal("{DynamicResource Accent}", Attr(importing, "Foreground"));
    }

    [Fact]
    public Task 入り切らないときは_一番右の札から丸になる() => UiThread.Run(() =>
    {
        // 並びの最後（一番右）に置いた見つからないが、最初に丸になる
        var panel = new BadgeOverflowPanel { Gap = 4 };
        var names = new[] { "更新あり", "取り込み中", "所持", "一部見つからない" };
        var badges = names.Select(name => new StatusBadge
        {
            Padding = new Thickness(7, 1, 7, 1),
            Child = new TextBlock { Text = name, Width = name.Length * 10, Height = 14 },
        }).ToList();
        badges.ForEach(badge => panel.Children.Add(badge));

        var all = badges.Sum(badge => ((TextBlock)badge.Child).Width + 14 + 4);
        panel.Measure(new Size(all - 20, 40));

        Assert.Equal([false, false, false, true], badges.Select(BadgeOverflowPanel.GetIsDot));
    });

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views");
}
