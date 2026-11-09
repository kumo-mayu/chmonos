using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>商品ページの「変換扱い」の欄で、人が変える変え方（<c>docs/spec/conversion.md</c>「画面」）。名前・ID は作り物。</summary>
public class ConversionEditsTests
{
    private static readonly ConversionTarget Suggested = new() { BaseName = "作り物の素体", Forward = true, Slug = "fakebase" };

    private static ConversionSetting WithSuggestion() => new() { Targets = [Suggested] };

    [Fact]
    public void 扱いを変えても_つなぎ先は残す()
    {
        var confirmed = Suggested with { Confirmed = true };
        var notProfile = ConversionEdits.SetMode(ConversionMode.NotProfile)(new ConversionSetting { Targets = [confirmed] });
        var back = ConversionEdits.SetMode(ConversionMode.Auto)(notProfile);

        Assert.Equal(ConversionMode.NotProfile, notProfile!.Mode);
        Assert.Equal([confirmed], back!.Targets);
    }

    [Fact]
    public void 自動に戻して_つなぎ先も無ければ_書き出さない形にする()
    {
        Assert.Null(ConversionEdits.SetMode(ConversionMode.Auto)(new ConversionSetting { Mode = ConversionMode.NotProfile }));
        Assert.Null(ConversionEdits.SetMode(ConversionMode.Auto)(null));
    }

    [Fact]
    public void 確認待ちを確認すると_その行だけ確認済みになる()
    {
        var other = new ConversionTarget { AvatarItemId = "9800401", Reverse = true, Slug = "fakeaoi" };
        var next = ConversionEdits.Confirm(Suggested)(new ConversionSetting { Targets = [Suggested, other] });

        Assert.True(next!.Targets[0].Confirmed);
        Assert.False(next.Targets[1].Confirmed);
    }

    [Fact]
    public void 見回りが書き直して同じ行が無ければ_確認しても何も変えない()
    {
        var current = new ConversionSetting { Targets = [Suggested with { BaseName = "別の作り物の素体" }] };

        Assert.Same(current, ConversionEdits.Confirm(Suggested)(current));
    }

    [Fact]
    public void つなぎ先を選ぶと_そのプロファイルの行を確認済みの1行に置き換える()
    {
        var next = ConversionEdits.Choose("fakebase", forward: true, reverse: false, avatarItemId: "9800402", baseName: null)(WithSuggestion());

        var row = Assert.Single(next!.Targets);
        Assert.Equal("9800402", row.AvatarItemId);
        Assert.Null(row.BaseName);
        Assert.True(row.Confirmed);
        Assert.Equal("fakebase", row.Slug);
    }

    [Fact]
    public void 向きを変えると_確認済みにもなる()
    {
        var next = ConversionEdits.SetDirection(Suggested, forward: true, reverse: true)(WithSuggestion());

        var row = Assert.Single(next!.Targets);
        Assert.True(row.Forward);
        Assert.True(row.Reverse);
        Assert.True(row.Confirmed);
    }

    [Fact]
    public void 最後のつなぎ先を外すと_書き出さない形にする()
    {
        Assert.Null(ConversionEdits.Remove(Suggested)(WithSuggestion()));

        var notProfile = new ConversionSetting { Mode = ConversionMode.NotProfile, Targets = [Suggested] };
        Assert.Empty(ConversionEdits.Remove(Suggested)(notProfile)!.Targets);
    }
}
