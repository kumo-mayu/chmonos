using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// カードとリストの商品名の頭の「R-18」（メモ82・ユーザ判断 2026-10-06：ピンク）。
/// 見分けは検索の条件「R-18」と同じ BOOTH の is_adult。
/// </summary>
public class AdultMarkTests
{
    [Fact]
    public Task R18の商品だけ名前の頭に印が付く() => TestApp.Run(async app =>
    {
        var adult = Make.Item("9900881", "作り物の衣装A");
        adult = adult with { Booth = adult.Booth with { IsAdult = true } };
        await app.AddItemAsync(adult);
        await app.AddItemAsync(Make.Item("9900882", "作り物の衣装B"));
        var main = await app.StartAsync();

        var cards = main.Search.ListItems.ToDictionary(card => card.Item.Id);
        Assert.Equal("R-18 ", cards["9900881"].AdultMark);
        Assert.Equal(string.Empty, cards["9900882"].AdultMark);

        // 名前そのものには混ぜない（読み上げ・並べ替え・吹き出しは商品名のまま）
        Assert.Equal("作り物の衣装A", cards["9900881"].Name);
    });
}
