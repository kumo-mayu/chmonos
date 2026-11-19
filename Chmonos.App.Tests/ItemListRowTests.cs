using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// リストの文字の列の中身（ユーザ判断 2026-11-20：列を自分で選ぶ）。値は条件・カードの札・商品ページと同じ言い方にそろえ、
/// 無い物（0・既定の値）は空にする（並んだときに、ある物だけが目に入るように）。
/// </summary>
public class ItemListRowTests
{
    private static readonly ItemListRowSources NoSources = ItemListRowSources.None;

    [Fact]
    public void 何も無い商品は_数や既定の値を書かず空にする()
    {
        var row = new ItemListRow(new ItemRecord { Id = "1000001", Booth = new BoothBlock { Name = "作り物" } }, NoSources, awaitingDetection: false);

        Assert.Equal("1000001", row.Id);
        Assert.Equal(string.Empty, row.WishLists);
        Assert.Equal(string.Empty, row.Variations);
        Assert.Equal(string.Empty, row.Images);
        Assert.Equal(string.Empty, row.Hidden);
        Assert.Equal(string.Empty, row.Notify);
        Assert.Equal(string.Empty, row.Memo);
        Assert.Equal(string.Empty, row.Size);
        Assert.Equal(string.Empty, row.Files);
        Assert.Equal(string.Empty, row.Missing);
        Assert.Equal(string.Empty, row.BrokenZip);
        Assert.Equal(string.Empty, row.Modifications);
        Assert.Equal(string.Empty, row.NewVersion);
        Assert.Equal(string.Empty, row.Profile);
        Assert.Equal("未所持", row.Owned);
        Assert.Equal("公開中", row.Status);
    }

    [Fact]
    public void 自分で入れた値は_条件や商品ページと同じ言い方で出す()
    {
        var item = new ItemRecord
        {
            Id = "1000002",
            Booth = new BoothBlock
            {
                Name = "作り物",
                FetchedAt = DateTimeOffset.Now,
                WishListsCount = 12345,
                IsEndOfSale = true,
                IsAdult = true,
                Tags = ["衣装", "夏"],
            },
            Local = new LocalBlock
            {
                IsHidden = true,
                NotifyOnUpdate = false,
                Memo = "1行目\r\n\r\n2行目",
                UserTags = [new UserTagAssignment { Top = "衣装" }],
                Purchases = [new Purchase { Kind = PurchaseKind.Given }, new Purchase { Kind = PurchaseKind.Received }],
                Avatars = [new AvatarLink { AvatarItemId = "2000001", Name = "作り物の子" }, new AvatarLink { AvatarItemId = "2000002", Name = "消した子", Rejected = true }],
                AvatarBases = [new AvatarBaseLink { BaseName = "作り物素体" }],
            },
        };
        var row = new ItemListRow(item, NoSources, awaitingDetection: false);

        Assert.Equal("12,345", row.WishLists);
        Assert.Equal("販売終了・非公開", row.Status);
        Assert.Equal("R-18", row.Adult);
        Assert.Equal("衣装　夏", row.BoothTags);
        Assert.Equal("非表示", row.Hidden);
        Assert.Equal("通知しない", row.Notify);
        Assert.Equal("1行目 2行目", row.Memo);
        Assert.Equal("ギフトした・ギフトされた", row.Gift);
        Assert.Equal("作り物の子", row.AvatarNames);
        Assert.Equal("作り物素体", row.Bases);

        // 編集状況は、入れていない項目の名前（編集画面の欄の名前）。ユーザータグと購入は入れてある
        Assert.NotEqual(string.Empty, row.EditStatus);
        Assert.DoesNotContain("ユーザータグ", row.EditStatus);
    }

    [Fact]
    public void 取り込みの途中の商品の編集状況は_取り込み中()
    {
        var row = new ItemListRow(new ItemRecord { Id = "1000003" }, NoSources, awaitingDetection: true);
        Assert.Equal("取り込み中", row.EditStatus);
    }

    [Fact]
    public void 使った改変の数と新しい版は_改変の記録から引く()
    {
        var item = new ItemRecord
        {
            Id = "1000004",
            Local = new LocalBlock { ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.Now } },
        };
        var used = new UsedItems(
            [
                Modification("m1", "1000004"),
                Modification("m2", "1000004"),
                Modification("m3", "9999999"),
            ],
            new Dictionary<string, UnityProjectScan>());
        var row = new ItemListRow(item, new ItemListRowSources(() => used, _ => null), awaitingDetection: false);

        Assert.Equal("2", row.Modifications);
        Assert.Equal(item.Local.HasPendingContentUpdate ? "新しい版あり" : string.Empty, row.NewVersion);
        Assert.True(item.Local.HasPendingContentUpdate, "作り物の控えが「確かめていない新しい版」になっている");
    }

    [Fact]
    public Task カードの行は1回だけ作り_使っている所の答えが替わったら作り直す() => UiThread.Run(() =>
    {
        var used = UsedItems.Empty;
        var context = new CardInfoContext { RowSources = new ItemListRowSources(() => used, _ => null) };
        var card = new ItemCardViewModel(new ItemRecord { Id = "1000005" }, new ThumbnailLoader(), System.IO.Path.GetTempPath(), ThumbnailRole.Default, showRemovedImages: false)
        {
            Name = "作り物",
            InfoContext = context,
        };

        var first = card.ListRow;
        Assert.Same(first, card.ListRow);

        used = new UsedItems([Modification("m1", "1000005")], new Dictionary<string, UnityProjectScan>());
        var second = card.ListRow;
        Assert.NotSame(first, second);
        Assert.Equal("1", second.Modifications);
    });

    private static ModificationRecord Modification(string id, string itemId) => new()
    {
        Id = id,
        AvatarItemId = "2000001",
        Name = "作り物の改変",
        Members = [new ModificationMember { ItemId = itemId }],
    };
}
