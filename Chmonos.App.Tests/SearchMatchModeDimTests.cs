using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using System.IO;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件の AND／OR は、1つしか無くて意味を成さない間も隠さず、薄く押せなくする（ユーザ判断 2026-10-06
/// 「ANDとORのラジオボタンが消えたりついたりすると縦に揺れるので意味を成さない時はグレーアウト。他のモジュールでも同様」）。
/// 押せない理由は吹き出しで言う。
/// </summary>
public class SearchMatchModeDimTests
{
    private static void AssertDimmed(bool canChoose, bool dimmed, string? tip, string expectedTip)
    {
        Assert.False(canChoose);
        Assert.True(dimmed);
        Assert.Equal(expectedTip, tip);
    }

    [Fact]
    public void 対応アバターなど候補から積む条件は_1つの間は薄く押せず_2つで押せる()
    {
        var module = new ListModule(SearchModuleKind.Avatar, allowsAnd: true, "名前で絞り込む", "", (_, _, _, _) => true);
        module.AddKey("a");

        AssertDimmed(module.CanChooseMatchMode, module.MatchModeDimmed, module.MatchModeTip, "2つ以上追加すると選べます。");

        module.AddKey("b");
        Assert.True(module.CanChooseMatchMode);
        Assert.False(module.MatchModeDimmed);
        Assert.Equal(MatchModeText.AllHint, module.MatchModeTip);
    }

    [Fact]
    public void ANDを持たない条件は_何個積んでも押せない()
    {
        var module = new ListModule(SearchModuleKind.Shop, allowsAnd: false, "ショップで絞り込む", "", (_, _, _, _) => true);
        module.AddKey("a");
        module.AddKey("b");

        Assert.False(module.AllowsAnd);
        Assert.False(module.CanChooseMatchMode);
    }

    [Fact]
    public void ユーザータグは_大分類どうしは1つの間は出さず_枠の中の小分類どうしは薄く押せない()
    {
        var module = new UserTagModule();
        module.SetMasters([("衣装", (IReadOnlyList<string>)["上着", "靴"]), ("髪", (IReadOnlyList<string>)["長い"])]);
        var top = module.AddTop("衣装")!;
        top.AddCommand.Execute("上着");

        // 大分類どうしは0〜1件の間は出さない（ユーザ判断 2026-10-06「大分類は0-1件では表示しない」）
        Assert.False(module.ShowsMatchMode);
        Assert.False(module.MatchModeDimmed);
        AssertDimmed(top.CanChooseMatchMode, top.MatchModeDimmed, top.MatchModeTip, "小分類を2つ以上追加すると選べます。");

        module.AddTop("髪");
        top.AddCommand.Execute("靴");
        Assert.True(module.ShowsMatchMode);
        Assert.True(module.CanChooseMatchMode);
        Assert.Equal(module.TopMatchAllHint, module.MatchModeTip);
        Assert.True(top.CanChooseMatchMode);
        Assert.Equal(MatchModeText.AllHint, top.MatchModeTip);
    }

    [Fact]
    public void 属性は_1つの間は薄く押せない()
    {
        var module = new AttributeModule();
        module.AddRow("かわいさ", notify: false);

        AssertDimmed(module.CanChooseMatchMode, module.MatchModeDimmed, module.MatchModeTip, "属性を2つ以上追加すると選べます。");

        module.AddRow("品質", notify: false);
        Assert.True(module.CanChooseMatchMode);
        Assert.False(module.MatchModeDimmed);
    }

    [Fact]
    public void 改変は_アバターどうしは1つの間は出さず_改変どうしは薄く押せない()
    {
        var module = new ModificationModule(_ => null, _ => null);
        var row = module.AddAvatar("9900001");
        row.AddModificationQuietly("mod-0000a001");

        // アバターどうしもユーザータグの大分類と同じく出さない（ユーザ判断 2026-10-06）
        Assert.False(module.ShowsMatchMode);
        Assert.False(module.MatchModeDimmed);
        AssertDimmed(row.CanChooseMatchMode, row.MatchModeDimmed, row.MatchModeTip, "改変を2つ以上追加すると選べます。");

        module.AddAvatar("9900002");
        row.AddModificationQuietly("mod-0000a002");
        Assert.True(module.ShowsMatchMode);
        Assert.True(module.CanChooseMatchMode);
        Assert.Equal(module.AvatarMatchAllHint, module.MatchModeTip);
        Assert.True(row.CanChooseMatchMode);
    }

    [Fact]
    public Task 編集状況は_見る項目が1つの間は薄く押せず_両方の間は外の欄だけが薄い() => UiThread.Run(() =>
    {
        var module = new UneditedModule(_ => false);
        module.Load(new SearchModuleState { Kind = "Unedited", Choice = "unedited", Fields = ["userTags"] });

        AssertDimmed(module.CanChooseMatchMode, module.MatchModeDimmed, module.MatchModeTip, "見る項目が2つ以上のときに選べます。");

        // 「両方」の間は外の欄ごと薄いので、つなぎ方を重ねて薄くしない
        module.Selected = module.Options.First(option => option.Key == "both");
        Assert.False(module.MatchModeDimmed);

        module.Selected = module.Options.First(option => option.Key == "unedited");
        module.Fields.First(toggle => toggle.Field == EditField.Memo).IsOn = true;
        Assert.True(module.CanChooseMatchMode);
        Assert.False(module.MatchModeDimmed);
        Assert.Null(module.MatchModeTip);
    });

    [Fact]
    public Task 更新通知ありは_見る種類が1つの間は薄く押せない() => UiThread.Run(() =>
    {
        var module = new UpdateNoticeModule(_ => Core.Services.BoothChangeKind.None);
        foreach (var kind in module.Kinds.Skip(1))
        {
            kind.IsOn = false;
        }

        AssertDimmed(module.CanChooseMatchMode, module.MatchModeDimmed, module.MatchModeTip, "見る種類が2つ以上のときに選べます。");

        module.Kinds[1].IsOn = true;
        Assert.True(module.CanChooseMatchMode);
        Assert.Null(module.MatchModeTip);
    });

    /// <summary>
    /// 画面の元の文で、AND／OR の部品が数で隠れないこと（隠すと、足し引きのたびに下の欄が縦に揺れる）。
    /// 出し入れは AND を持つかだけ（`AllowsAnd`）、押せるかは数で決める（`MatchModeCheck`・`MatchModeRadios` のスタイル）。
    /// </summary>
    [Fact]
    public void 検索の画面のANDとORは_数で隠さず_薄くするスタイルを当てる()
    {
        var document = XDocument.Load(Path.Combine(ViewsFolder(), "SearchView.xaml"));
        string? Attr(XElement element, string name) => (string?)element.Attributes().FirstOrDefault(a => a.Name.LocalName == name);

        var checks = document.Descendants()
            .Where(e => e.Name.LocalName == "CheckBox" && Attr(e, "IsChecked") == "{Binding MatchAll, Mode=TwoWay}")
            .Where(e => Attr(e, "Style") != "{StaticResource SettingCheck}")
            .ToList();
        var radios = document.Descendants()
            .Where(e => e.Name.LocalName == "WrapPanel" && e.Descendants().Any(child => child.Name.LocalName == "RadioButton"
                && (Attr(child, "IsChecked") ?? "").StartsWith("{Binding MatchAll", StringComparison.Ordinal)))
            .ToList();

        // チェックの形：対応アバターなどの候補から積む条件・ユーザータグの2段・改変の2段・属性（価格の「すべての価格が範囲内」は数に依らないので別）
        var dimmable = checks.Where(e => Attr(e, "Style") == "{StaticResource MatchModeCheck}").ToList();
        Assert.Equal(6, dimmable.Count);

        // 2段の外側（ユーザータグの大分類どうし・改変のアバターどうし）だけは0〜1件の間は出さない（ユーザ判断 2026-10-06。ずれは受け入れる）
        var outer = dimmable.Where(e => Attr(e, "Visibility")?.Contains("ShowsMatchMode") == true).ToList();
        Assert.Equal(["すべての大分類を満たす商品のみ", "すべてのアバターを満たす商品のみ"],
            outer.Select(e => Attr(e, "AutomationProperties.Name")!.Replace("{Binding NameSuffix, StringFormat={}", "").Replace("{0}}", "")));
        Assert.All(dimmable.Except(outer), e => Assert.True(Attr(e, "Visibility") is null || Attr(e, "Visibility")!.Contains("AllowsAnd")));

        // ラジオボタンの形：編集状況・更新通知あり
        Assert.Equal(2, radios.Count);
        Assert.All(radios, e => Assert.Equal("{StaticResource MatchModeRadios}", Attr(e, "Style")));
        Assert.All(radios, e => Assert.Null(Attr(e, "Visibility")));

    }

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views");
}
