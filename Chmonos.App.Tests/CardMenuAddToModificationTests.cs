using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品のカード・リストの右クリックの「改変に追加…」（ユーザ指示 2026-10-03）。
/// 選んでいる最中に選んだ物の上で押したら選んだ全部、選んでいない物の上なら押した1件だけ。窓は出さないので、渡す商品の決め方を確かめる。
/// </summary>
public class CardMenuAddToModificationTests
{
    [Fact]
    public Task 選んだ物の上で押すと選んだ全部_選んでいない物の上なら押した1件だけ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の衣装B"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の衣装C"));
        var main = await app.StartAsync();

        var cards = main.Search.ListItems.ToList();
        Assert.Equal(3, cards.Count);
        cards[0].IsSelected = true;
        cards[1].IsSelected = true;
        var selected = new List<ItemCardViewModel> { cards[0], cards[1] };

        Assert.Equal(selected, ItemSelectionActions.CardsForMenu(cards[1], selected));
        Assert.Equal([cards[2]], ItemSelectionActions.CardsForMenu(cards[2], selected));
        Assert.Empty(ItemSelectionActions.CardsForMenu(null, selected));

        // 選んでいない時は押した1件
        Assert.Equal([cards[0]], ItemSelectionActions.CardsForMenu(cards[0], []));
    });

    [Fact]
    public Task 右クリックの改変に追加は_カードを渡したときだけ押せる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装A"));
        var main = await app.StartAsync();
        var card = main.Search.ListItems.Single();

        Assert.True(main.Search.CardAddToModificationCommand.CanExecute(card));
        Assert.False(main.Search.CardAddToModificationCommand.CanExecute(null));
    });
}
