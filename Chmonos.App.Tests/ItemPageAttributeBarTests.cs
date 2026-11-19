using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの属性の並び（ユーザ判断 2026-11-20）。値のある属性を高い順に出し、その後ろに値はまだの属性を「—」で出す。
/// 値はまだは 0% ではないので、値の文字も棒の幅も 0 にしない。
/// </summary>
public class ItemPageAttributeBarTests
{
    [Fact]
    public void 値のある属性の後ろに_値はまだの属性が_を値にして並ぶ()
    {
        var local = new LocalBlock
        {
            Attributes = new Dictionary<string, int> { ["ゼロ"] = 0, ["高い"] = 80 },
            PendingAttributes = ["まだ1", "まだ2"],
        };

        var bars = AttributeBar.Build(local);

        Assert.Equal(["高い", "ゼロ", "まだ1", "まだ2"], bars.Select(bar => bar.Name));
        Assert.Equal(["80", "0", "—", "—"], bars.Select(bar => bar.ValueText));
        Assert.Equal([false, false, true, true], bars.Select(bar => bar.IsPending));
    }

    [Fact]
    public void 値はまだが無ければ今までどおり値のある物だけ()
    {
        var bars = AttributeBar.Build(new LocalBlock { Attributes = new Dictionary<string, int> { ["a"] = 5 } });

        Assert.Equal("a", Assert.Single(bars).Name);
        Assert.Empty(AttributeBar.Build(new LocalBlock()));
    }
}
