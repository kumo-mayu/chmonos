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
/// 試験の中では画面の資源が無く、View そのものは作れない）。直したのはアバターの管理の画面だけ（ほかの画面の同じ作りは残してある）
/// </summary>
public class PlaceholderFollowsFieldTests
{
    [Fact]
    public void アバターの管理の探す欄の案内は_遅れて入る値で出し分けない()
    {
        var text = File.ReadAllText(Path.Combine(ViewsFolder(), "AvatarsView.xaml"));

        // 遅れて入る値（Delay の付いた結び付けの元）
        var delayed = new Regex(@"\{Binding ([\w.]+), UpdateSourceTrigger=PropertyChanged, Delay=\d+")
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(delayed);

        // 値が空かで案内を出す結び付け（EmptyToVisibility・空の文字との DataTrigger）
        var byValue = new Regex(@"Visibility=""\{Binding ([\w.]+), Converter=\{x:Static vm:EmptyToVisibilityConverter\.Instance\}\}""")
            .Matches(text)
            .Select(match => match.Groups[1].Value)
            .Concat(new Regex(@"<DataTrigger Binding=""\{Binding ([\w.]+)\}"" Value="""">").Matches(text).Select(match => match.Groups[1].Value));

        Assert.DoesNotContain(byValue, delayed.Contains);
    }

    [Theory]
    [InlineData("ConversionPairSearchBox")]
    [InlineData("AvatarSearchBox")]
    public void 探す欄の案内は_欄そのものの字が空の時だけ出す(string box)
    {
        var text = File.ReadAllText(Path.Combine(ViewsFolder(), "AvatarsView.xaml"));

        Assert.Contains($@"<TextBox x:Name=""{box}""", text);
        Assert.Contains($@"Visibility=""{{Binding Text.IsEmpty, ElementName={box}, Converter={{x:Static vm:BoolToVisibilityConverter.Instance}}}}""", text);
    }

    /// <summary>試験を組んだ所ではなく、元の文の置き場から引く（作業用のフォルダへ組んでも同じ物を読む）。</summary>
    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
