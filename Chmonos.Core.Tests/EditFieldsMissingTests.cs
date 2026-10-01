using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>検索の「編集状況」で見る、編集画面の項目が未入力か（2026-10-01）。</summary>
public class EditFieldsMissingTests
{
    private static ItemRecord Item(LocalBlock local) => new()
    {
        Id = "1000001",
        Booth = new BoothBlock { Name = "作り物" },
        Local = local,
    };

    [Fact]
    public void 何も入れていない商品は_どの項目も未入力()
    {
        var item = Item(new LocalBlock());

        Assert.All(EditFieldsMissing.All, field => Assert.True(EditFieldsMissing.IsMissing(item, field), field.ToString()));
    }

    [Fact]
    public void 入れた項目だけが入力済みになる()
    {
        var item = Item(new LocalBlock
        {
            UserTags = [new UserTagAssignment { Top = "衣装" }],
            Attributes = new Dictionary<string, int> { ["かわいさ"] = 80 },
            Purchases = [new Purchase { Kind = PurchaseKind.ForSelf }],
            AcquiredAt = new DateOnly(2026, 9, 1),
            Memo = "作り物のメモ",
        });

        Assert.All(EditFieldsMissing.All, field => Assert.False(EditFieldsMissing.IsMissing(item, field), field.ToString()));
    }

    [Fact]
    public void 空白だけのメモは未入力()
        => Assert.True(EditFieldsMissing.IsMissing(Item(new LocalBlock { Memo = "  \n" }), EditField.Memo));

    [Fact]
    public void 状態の名前は欄の名前で_知らない名前は飛ばし_残らなければ既定()
    {
        Assert.Equal(["userTags", "attributes", "purchases", "acquiredAt", "memo"], EditFieldsMissing.All.Select(EditFieldsMissing.KeyOf));
        Assert.Equal([EditField.Attributes, EditField.Memo], EditFieldsMissing.Parse(["memo", "知らない", "attributes"]));
        Assert.Equal([EditField.UserTags], EditFieldsMissing.Parse([]));
        Assert.Equal([EditField.UserTags], EditFieldsMissing.Parse(["知らない"]));
    }
}
