using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>価格の「外れ値を無視」：文言と、払った額のときは効かない（押せなくして薄くする）こと。</summary>
public class SearchOutlierTests
{
    // 20個が1,000円、止め値が1個（99,999円）
    private static readonly int[] Values = [.. Enumerable.Repeat(1000, 20), 99999];

    private static RangeModule Price(string? noOutlierSource)
    {
        var module = new RangeModule(
            SearchModuleKind.Price,
            (_, _) => [],
            "円",
            [new ChoiceOption("paid", "購入額"), new ChoiceOption("booth", "BOOTHの価格")])
        {
            AllValuesOf = _ => Values,
            Floor = 100,
            SupportsOutliers = true,
            NoOutlierSource = noOutlierSource,
        };
        module.RefreshBounds();
        return module;
    }

    [Fact]
    public void BOOTHの価格では外れ値の境だけを言う()
    {
        var module = Price("paid");
        module.Source = module.Sources[1];

        Assert.True(module.OutliersApply);
        Assert.Equal("外れ値を無視（5,000円以上を異常値として弾く）", module.OutlierLabel);
        Assert.Equal(1000, (int)module.SliderMaximum);
    }

    [Fact]
    public void 払った額では外れ値を探さず押せない()
    {
        var module = Price("paid");

        Assert.False(module.OutliersApply);
        Assert.Equal("外れ値を無視", module.OutlierLabel);

        // 値は残る：チェックは入ったまま、右端は止め値まで（外れ値を外さない）
        Assert.True(module.IgnoreOutliers);
        Assert.Equal(99999, (int)module.SliderMaximum);
    }

    [Fact]
    public void 払った額からBOOTHの価格へ切り替えると外れ値が効き出す()
    {
        var module = Price("paid");
        var changed = new List<string?>();
        module.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        module.Source = module.Sources[1];

        Assert.Contains(nameof(RangeModule.OutliersApply), changed);
        Assert.True(module.OutliersApply);
        Assert.Equal(1000, (int)module.SliderMaximum);
    }

    [Fact]
    public void 外れ値が無くても境の値の説明を出す()
    {
        var module = Price(null);
        module.AllValuesOf = _ => [.. Enumerable.Repeat(1000, 20)];
        module.RefreshBounds();

        Assert.Equal("外れ値を無視（5,000円以上を異常値として弾く）", module.OutlierLabel);
    }

    [Fact]
    public void 外れ値を使わない元を決めていない条件では常に効く()
    {
        Assert.True(Price(null).OutliersApply);
    }
}
