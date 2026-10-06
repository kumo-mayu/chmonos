using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 属性は1%刻み（ユーザ判断 2026-10-06・メモ82・判断9）。検索の条件はヒストグラムとスライダで選び、編集画面の5%刻みも1%にそろえる。
/// </summary>
public class AttributeStepTests
{
    [Theory]
    [InlineData(37.4, 37)]
    [InlineData(37.5, 38)]
    [InlineData(62.0, 62)]
    public void 検索の属性のスライダは1刻みで受ける(double position, int expected)
    {
        var row = new AttributeFilter { Name = "質感" };

        row.LowPosition = position;
        row.HighPosition = position;

        Assert.Equal(expected, row.Min);
        Assert.Equal(expected, row.Max);
        Assert.Equal($"{expected}〜{expected}%", row.RangeText);
    }

    [Fact]
    public void 下限は上限を越えられず_つまみは止まった値へ戻る()
    {
        var row = new AttributeFilter { Name = "質感" };
        row.Max = 40;
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.LowPosition = 55;

        Assert.Equal(40, row.Min);
        Assert.Equal(40, row.LowPosition);
        Assert.Contains(nameof(AttributeFilter.LowPosition), changed);
    }

    [Fact]
    public void 分布の帯は評価した値を5ずつの棒に数える()
    {
        var row = new AttributeFilter { Name = "質感" };

        row.SetValues([0, 3, 4, 37, 100, 100, 100]);

        Assert.Equal(AttributeFilter.HistogramBuckets, row.Histogram.Count);
        Assert.Equal(RangeModule.HistogramHeight, row.Histogram[^1].Height);
        Assert.Equal(RangeModule.HistogramHeight, row.Histogram[0].Height);
        Assert.True(row.Histogram[7].Height > 0);
        Assert.Equal(0, row.Histogram[8].Height);
    }

    [Fact]
    public void 評価した商品が無ければ帯を出さない()
    {
        var row = new AttributeFilter { Name = "質感" };

        row.SetValues([]);

        Assert.False(row.HasHistogram);
    }

    [Fact]
    public Task 検索で属性を足すと_手元の値から帯が出る() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "質感" }] });
        await app.AddItemAsync(Rated("9900601", 12));
        await app.AddItemAsync(Rated("9900602", 87));
        await app.AddItemAsync(Make.Item("9900603", "作り物（未評価）"));
        var search = (await app.StartAsync()).Search;

        var module = (AttributeModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Attribute);
        module.AddRow("質感");

        var row = Assert.Single(module.Rows);
        Assert.True(row.HasHistogram);
        Assert.Equal(2, row.Histogram.Count(bar => bar.Height > 0));
    });

    private static ItemRecord Rated(string id, int value)
    {
        var item = Make.Item(id, "作り物 " + id);
        return item with { Local = item.Local with { Attributes = new Dictionary<string, int> { ["質感"] = value } } };
    }

    [Fact]
    public void 編集画面の属性のスライダは1刻みで吸い付く()
    {
        var slider = Slider("EditView.xaml", "EditAttributeSlider");

        Assert.Equal("1", (string?)slider.Attribute("TickFrequency"));
        Assert.Equal("True", (string?)slider.Attribute("IsSnapToTickEnabled"));
        Assert.Equal("1", (string?)slider.Attribute("SmallChange"));
    }

    [Theory]
    [InlineData("SearchAttributeMinSlider")]
    [InlineData("SearchAttributeMaxSlider")]
    public void 検索の属性のスライダは目盛に吸い付けず_キーは1ずつ動く(string automationId)
    {
        var slider = Slider("SearchView.xaml", automationId);

        Assert.Equal("False", (string?)slider.Attribute("IsSnapToTickEnabled"));
        Assert.Equal("1", (string?)slider.Attribute("SmallChange"));
    }

    /// <summary>画面の元の文（XAML）を読む（試験では View を作れない。<c>DeleteIsRedTests</c> と同じ）。</summary>
    private static XElement Slider(string view, string automationId)
        => Assert.Single(
            XDocument.Load(Path.Combine(ViewsFolder(), view)).Descendants(),
            e => e.Name.LocalName == "Slider"
                && (string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "AutomationProperties.AutomationId") == automationId);

    private static string ViewsFolder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(here))!, "Chmonos.App", "Views");
}
