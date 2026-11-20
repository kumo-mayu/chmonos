using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Chmonos.App.Tests;

/// <summary>
/// 探す欄の内側の案内（薄い字）は、欄に打った字が入ったらすぐ消す（ユーザ判断 2026-11-20・①：
/// 「入力から検索実行までかけている遅延でプレイスホルダーと入力文字がかぶっている」）。
/// 絞りに使う値は <c>Delay=200</c> で遅れて入るので、それが空かで案内を出すと、打ってから 200ms の間は打った字と重なる。
/// 案内は欄そのものの字（<c>ElementName</c> の <c>Text.IsEmpty</c>）で出し分ける。
///
/// 出し分けは XAML の結び付けに書くので、画面の元の文を読んで確かめる（<c>FilterFieldDelayTests</c> と同じ。
/// 試験の中では画面の資源が無く、View そのものは作れない）。アバターの管理で直した後、ほかの画面（除外の一覧・ショップ・未確定・属性の管理・フォルダ）も直した（ユーザ指示 2026-11-20「12は直してしまってください。」）
/// </summary>
public class PlaceholderFollowsFieldTests
{
    public static TheoryData<string> XamlFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(ViewsFolder(), "*.xaml").Concat(Directory.GetFiles(ControlsFolder(), "*.xaml")))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void 探す欄の案内は_遅れて入る値で出し分けない(string file)
    {
        var path = Path.Combine(ViewsFolder(), file);
        var text = File.ReadAllText(File.Exists(path) ? path : Path.Combine(ControlsFolder(), file));

        // 遅れて入る値（Delay の付いた結び付けの元）
        var delayed = new Regex(@"\{Binding ([\w.]+), UpdateSourceTrigger=PropertyChanged, Delay=\d+")
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // 値が空かで案内を出す結び付け（EmptyToVisibility・空の文字との DataTrigger）
        var byValue = new Regex(@"Visibility=""\{Binding ([\w.]+), Converter=\{x:Static vm:EmptyToVisibilityConverter\.Instance\}\}""")
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Concat(new Regex(@"<DataTrigger Binding=""\{Binding ([\w.]+)\}"" Value="""">").Matches(text).Select(match => match.Groups[1].Value));

        Assert.DoesNotContain(byValue, delayed.Contains);
    }

    /// <summary>空かを VM の bool（HasQuery など）で出していた所は、上の試験の形では拾えないので名指しで見る</summary>
    [Theory]
    [InlineData("AvatarsView.xaml", "ConversionPairSearchBox")]
    [InlineData("AvatarsView.xaml", "AvatarSearchBox")]
    [InlineData("AttributeManageView.xaml", "AttributeItemFilterBox")]
    [InlineData("FolderBrowserView.xaml", "QueryBox")]
    [InlineData("FolderView.xaml", "FolderFilterBox")]
    public void 探す欄の案内は_欄そのものの字が空の時だけ出す(string view, string box)
    {
        var text = File.ReadAllText(Path.Combine(ViewsFolder(), view));

        Assert.Contains($@"<TextBox x:Name=""{box}""", text);
        Assert.Contains($@"Visibility=""{{Binding Text.IsEmpty, ElementName={box}, Converter={{x:Static vm:BoolToVisibilityConverter.Instance}}}}""", text);
    }

    /// <summary>試験を組んだ所ではなく、元の文の置き場から引く（作業用のフォルダへ組んでも同じ物を読む）。</summary>
    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");

    private static string ControlsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Controls");
}
