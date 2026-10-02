using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Chmonos.App.Tests;

/// <summary>
/// 文字を打つたびに一覧を絞り直す検索欄は、打ち終わって 200ms 待ってからまとめて絞る（検索画面の検索欄と同じ。2026-10-02 のメモ7-④）。
/// 遅らせないと、文字を1つずつ消していく間に、その数だけ絞り直しが走って重くなる。
///
/// 遅らせは XAML の結び付け（<c>Delay=200</c>）に書くので、画面の元の文を読んで確かめる
/// （試験の中では画面の資源が無く、View そのものは作れない）。
/// </summary>
public class FilterFieldDelayTests
{
    [Theory]
    [InlineData("SearchView.xaml", "QueryText")]
    [InlineData("ShopsView.xaml", "FilterText")]
    [InlineData("TagManageView.xaml", "ItemFilter")]
    [InlineData("AttributeManageView.xaml", "ItemFilter")]
    [InlineData("AvatarsView.xaml", "Query")]
    [InlineData("ItemAvatarsPanel.xaml", "AvatarFilter")]
    [InlineData("ResolveView.xaml", "FilterText")]
    public void 打つたびに絞る検索欄は_200ms待ってから絞る(string view, string property)
    {
        var text = File.ReadAllText(Path.Combine(ViewsFolder(), view));
        var bindings = BindingOf(property).Matches(text).Select(match => match.Value).ToList();

        var binding = Assert.Single(bindings);
        Assert.Contains("Delay=200", binding);
    }

    /// <summary>
    /// タグ・属性の管理の左は、検索と追加を1本にした欄（<c>SearchAddBox</c>）。絞り込みの遅らせは部品の中の結び付けに書き、
    /// 画面の側は部品の Text へ FilterText をそのまま結ぶ
    /// </summary>
    [Theory]
    [InlineData("TagManageView.xaml")]
    [InlineData("AttributeManageView.xaml")]
    public void 管理の左の検索と追加の欄は_部品の中で200ms待ってから絞る(string view)
    {
        var screen = File.ReadAllText(Path.Combine(ViewsFolder(), view));
        Assert.Matches(@"<controls:SearchAddBox Text=""\{Binding FilterText, UpdateSourceTrigger=PropertyChanged\}""", screen);

        var box = File.ReadAllText(Path.Combine(ViewsFolder(), "..", "Controls", "SearchAddBox.xaml"));
        Assert.Contains("UpdateSourceTrigger=PropertyChanged, Delay=200", box);
    }

    /// <summary>その名前の値に、打つたびに書き戻す結び付け（UpdateSourceTrigger=PropertyChanged）。</summary>
    private static Regex BindingOf(string property)
        => new($@"\{{Binding {property}, UpdateSourceTrigger=PropertyChanged[^}}]*\}}");

    /// <summary>試験を組んだ所ではなく、元の文の置き場から引く（作業用のフォルダへ組んでも同じ物を読む）。</summary>
    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
