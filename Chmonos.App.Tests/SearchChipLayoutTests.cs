namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件の札（メモ70）。長い名前を2つ積むと2つ目の × が欄で切れた。横に並べる StackPanel は幅を無限に測るので、
/// 名前を縮められなかった。名前の列だけ縮む Grid にしてある。見た目は ViewShot の catalog-module-shop-set
/// （部品を組んで測るには検索画面の資源が要るので、原文を読む）。
/// </summary>
public class SearchChipLayoutTests
{
    private static string SearchViewPath([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", "Chmonos.App", "Views", "SearchView.xaml");

    [Fact]
    public void 札の名前は縮む列に置き_件数と外すボタンは残す()
    {
        var chip = System.Xml.Linq.XDocument.Load(SearchViewPath()).Descendants()
            .Single(element => element.Name.LocalName == "DataTemplate"
                && element.Attributes().Any(attribute => attribute.Name.LocalName == "Key" && attribute.Value == "SearchChipTemplate"));

        var remove = chip.Descendants().Single(element => element.Name.LocalName == "Button");
        Assert.Equal("Grid", remove.Parent!.Name.LocalName);

        var name = chip.Descendants().First(element => element.Name.LocalName == "TextBlock");
        Assert.Equal("CharacterEllipsis", name.Attribute("TextTrimming")?.Value);
        Assert.Equal("0", name.Attribute("Grid.Column")?.Value);
    }
}
