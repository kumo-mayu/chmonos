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
/// 左から字の少ない順（所持・未編集・更新あり・取り込み中・見つからない）に並べ、入らないときは右の札から丸にする。丸になっても左からの順は変わらない。
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
    public void カードの札は_左から字の少ない順()
    {
        // ユーザ判断 2026-10-06「要素を並べる順番は左から文字数の少ないものとし、丸くなるのは右側の要素から」。
        // 見つからない（6字）・一部見つからない（8字）はどちらでも一番右
        var panel = Resources().Descendants().Single(e => e.Name.LocalName == "BadgeOverflowPanel");

        var names = Names(panel);
        Assert.Equal(["所持", "未編集", "更新あり", "取り込み中", "見つからない"], names);
        Assert.Equal(names.Select(name => name.Length).Order(), names.Select(name => name.Length));
    }

    [Fact]
    public void リストの札も_カードと同じ順で_未所持は所持と同じ所()
    {
        // 未所持（3字）は未編集（3字）と同じ字数。所持と入れ替わりに出る札なので、所持の所に置く
        var cell = Resources().Descendants().Single(e => e.Name.LocalName == "DataTemplate" && Attr(e, "Key") == "ItemListChipsCell");
        var wrap = cell.Descendants().First(e => e.Name.LocalName == "WrapPanel");

        Assert.Equal(["所持", "未所持", "未編集", "更新あり", "取り込み中", "見つからない"], Names(wrap));
    }

    [Fact]
    public void 星は_カード22_リスト18()
    {
        // ユーザ判断 2026-10-06「カードはもう5px,リストはあと3px大きく」（カード 17 → 22・リスト 15 → 18）
        var document = Resources();
        var card = document.Descendants().Single(e => e.Name.LocalName == "DataTemplate" && Attr(e, "Key") == "ItemCardTemplate");
        var list = document.Descendants().Single(e => e.Name.LocalName == "DataTemplate" && Attr(e, "Key") == "ItemListFavCell");

        static string? StarSize(XElement template) => template.Descendants()
            .Where(e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "{Binding FavoriteGlyph}")
            .Select(e => Attr(e, "FontSize")).Single();

        Assert.Equal("22", StarSize(card));
        Assert.Equal("18", StarSize(list));
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
    public Task 入り切らないときは_右の札から丸になり_順は左から変わらない() => UiThread.Run(() =>
    {
        // 狭めるほど右から1つずつ丸が増え、どの幅でも左から 所持・未編集・更新あり・取り込み中・一部見つからない の順に並ぶ
        var panel = new BadgeOverflowPanel { Gap = 4 };
        var names = new[] { "所持", "未編集", "更新あり", "取り込み中", "一部見つからない" };
        var badges = names.Select(name => new StatusBadge
        {
            Padding = new Thickness(7, 1, 7, 1),
            Child = new TextBlock { Text = name, Width = name.Length * 10, Height = 14 },
        }).ToList();
        badges.ForEach(badge => panel.Children.Add(badge));

        var seen = new List<int>();
        for (var width = 320.0; width >= 85; width -= 5)
        {
            panel.Measure(new Size(width, 40));
            panel.Arrange(new Rect(panel.DesiredSize));

            var dots = badges.Select(BadgeOverflowPanel.GetIsDot).ToList();
            var count = dots.Count(dot => dot);
            Assert.Equal(Enumerable.Range(0, badges.Count).Select(i => i >= badges.Count - count), dots);
            var lefts = badges.Select(badge => badge.TransformToAncestor(panel).Transform(new Point()).X).ToList();
            Assert.Equal(lefts.Order(), lefts);
            seen.Add(count);
        }

        // 1つずつ増え、0 から 5 まで全部を通る（間が飛ばない）
        Assert.Equal([0, 1, 2, 3, 4, 5], seen.Distinct());
    });

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views");
}
