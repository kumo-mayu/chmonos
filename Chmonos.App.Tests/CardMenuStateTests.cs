using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 右クリックのメニュー（CardMenu）は、項目をいつも全部出し、要る物（商品の JSON・手元のファイル）が無いときは押せなくして理由を言う
/// （ユーザ判断 2026-10-04・メモ20-①）。押せるかと吹き出しの文を、状態ごとに確かめる。
/// </summary>
public class CardMenuStateTests
{
    private static readonly string[] NeedFiles = ["Reveal", "Unpack", "SendToUnity", "SendToUnityWithRecord", "SelectInUnity"];

    private static readonly string[] NeedJson = ["Favorite", "OpenItem", "Edit", "AddToModification", "Hide", "OpenBooth", "OpenShop", "CopyLink"];

    private sealed class Row(ItemCardViewModel? card) : IHasItemCard
    {
        public ItemCardViewModel? Card { get; } = card;
    }

    [Fact]
    public Task ファイルがある商品は_全部の項目が押せる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A"));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles.Concat(NeedJson).Concat(["Select"]))
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task ファイルが無い商品は_ファイルが要る項目だけ押せず_理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A").WithFiles());
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var key in NeedFiles)
        {
            Assert.False(CardMenuState.IsEnabled(key, card), key);
            Assert.Equal("手元にファイルがありません", CardMenuState.Tip(key, card));
        }

        // 使った記録・お気に入り・商品ページなどは、手元のファイルが無くても押せる
        foreach (var key in NeedJson)
        {
            Assert.True(CardMenuState.IsEnabled(key, card), key);
        }
    });

    [Fact]
    public Task 商品が手元に無い行は_商品のJSONが要る項目も押せず_理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A"));
        var main = await app.StartAsync();
        var nameOnly = new Row(null);

        foreach (var key in NeedFiles.Concat(NeedJson))
        {
            Assert.False(CardMenuState.IsEnabled(key, nameOnly), key);
            Assert.Equal("商品の情報がまだありません", CardMenuState.Tip(key, nameOnly));
        }

        // 商品がある行は、カードと同じに押せる
        var withCard = new Row(main.Search.ListItems.Single());
        Assert.True(CardMenuState.IsEnabled("Favorite", withCard));
        Assert.False(CardMenuState.IsEnabled("Select", withCard));
        Assert.Equal("この一覧では選べません", CardMenuState.Tip("Select", withCard));
        Assert.Equal("お気に入りに入れる", CardMenuState.FavoriteHeader(nameOnly));
    });

    [Fact]
    public Task BOOTHに無い商品は_BOOTHとリンクだけ押せず_その理由を言う() => TestApp.Run(async app =>
    {
        var local = Make.Item("1000001", "作り物の衣装A") with { Id = "local-sample-1" };
        await app.AddItemAsync(local);
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.False(CardMenuState.IsEnabled("OpenBooth", card));
        Assert.False(CardMenuState.IsEnabled("CopyLink", card));
        Assert.Equal(card.OpenBoothTip, CardMenuState.Tip("OpenBooth", card));
        Assert.True(CardMenuState.IsEnabled("Edit", card));
    });

    [Fact]
    public Task 子が全部押せない親は_親も押せず_子と同じ理由を言う() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装A").WithFiles());
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var parent in new[] { "OpenParent", "UnityParent" })
        {
            Assert.False(CardMenuState.IsEnabled(parent, card), parent);
            Assert.Equal("手元にファイルがありません", CardMenuState.Tip(parent, card));
            // 商品が手元に無い行は、子の理由（商品の情報がまだありません）がそのまま親の理由になる
            Assert.False(CardMenuState.IsEnabled(parent, new Row(null)), parent);
            Assert.Equal("商品の情報がまだありません", CardMenuState.Tip(parent, new Row(null)));
        }
    });

    [Fact]
    public Task 子が1つでも押せる親は_押せて吹き出しは出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装A"));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        foreach (var parent in new[] { "OpenParent", "UnityParent" })
        {
            Assert.True(CardMenuState.IsEnabled(parent, card), parent);
            Assert.Null(CardMenuState.Tip(parent, card));
        }
    });
}
