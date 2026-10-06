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

        // 名前は省ける唯一の文字で、縮む（*）列に置く。頭に改変の絵の列（Auto）が入っても、名前の列が縮む列であること
        var name = chip.Descendants().Single(element => element.Name.LocalName == "TextBlock" && element.Attribute("TextTrimming") is not null);
        Assert.Equal("CharacterEllipsis", name.Attribute("TextTrimming")?.Value);
        var columns = remove.Parent!.Descendants().Where(element => element.Name.LocalName == "ColumnDefinition").ToList();
        var column = int.Parse(name.Attribute("Grid.Column")?.Value ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("*", columns[column].Attribute("Width")?.Value);
    }
}
