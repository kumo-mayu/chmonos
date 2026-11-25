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
        // 対応アバターの確認は「確認待ちがある」が未入力。読み取っただけの推定を1つ持たせる
        var item = Item(new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = "9900001", Source = AvatarLinkSource.H2Link }] });

        // バリエーションの対応は買った行の中の欄なので、購入が無ければ欄も無い（下の試験で見る）
        Assert.All(EditFieldsMissing.All.Where(field => field != EditField.VariationTargets),
            field => Assert.True(EditFieldsMissing.IsMissing(item, field), field.ToString()));
        Assert.False(EditFieldsMissing.IsMissing(item, EditField.VariationTargets));
    }

    private static ItemRecord VariationItem(IReadOnlyList<Purchase> purchases, IReadOnlyList<VariationTarget> targets, int variations = 2,
        IReadOnlyList<AvatarLink>? avatars = null, IReadOnlyList<AvatarBaseLink>? bases = null) => new()
    {
        Id = "1000002",
        Booth = new BoothBlock
        {
            Name = "作り物の衣装",
            Variations = [.. Enumerable.Range(1, variations).Select(index => new BoothVariation { Id = index, Name = $"作り物{index}" })],
        },
        Local = new LocalBlock
        {
            Purchases = purchases,
            VariationTargets = targets,
            Avatars = avatars ?? [new AvatarLink { AvatarItemId = "9900001", Source = AvatarLinkSource.SupportSection }],
            AvatarBases = bases ?? [],
        },
    };

    [Fact]
    public void バリエーションの対応は_買った行に決めた向き先が無いと未入力()
    {
        Assert.True(EditFieldsMissing.IsMissing(VariationItem([new Purchase { VariationId = 1 }], []), EditField.VariationTargets));

        // 向き先を決めた行は入力済み（アバター・共通素体・全部のどれでも）
        Assert.False(EditFieldsMissing.IsMissing(
            VariationItem([new Purchase { VariationId = 1 }], [new VariationTarget { VariationId = 1, Avatars = ["9900001"] }]), EditField.VariationTargets));
        Assert.False(EditFieldsMissing.IsMissing(
            VariationItem([new Purchase { VariationId = 1 }], [new VariationTarget { VariationId = 1, Bases = ["作り物の素体"] }]), EditField.VariationTargets));
        Assert.False(EditFieldsMissing.IsMissing(
            VariationItem([new Purchase { VariationId = 1 }], [new VariationTarget { VariationId = 1, All = true }]), EditField.VariationTargets));

        // 外した候補だけの行は何も選んでいない（画面の「未選択」）
        Assert.True(EditFieldsMissing.IsMissing(
            VariationItem([new Purchase { VariationId = 1 }], [new VariationTarget { VariationId = 1, RejectedAvatars = ["9900002"] }]), EditField.VariationTargets));
    }

    [Fact]
    public void バリエーションの対応は_買った行のどれか1つでも決めていなければ未入力()
    {
        var purchases = new[] { new Purchase { VariationId = 1 }, new Purchase { VariationId = 2 } };
        var decidedOne = new[] { new VariationTarget { VariationId = 1, Avatars = ["9900001"] } };
        Assert.True(EditFieldsMissing.IsMissing(VariationItem(purchases, decidedOne), EditField.VariationTargets));

        // 買っていない行（2）に向き先が無くても数えない（画面も買った行にだけ印を付ける）
        Assert.False(EditFieldsMissing.IsMissing(VariationItem([purchases[0]], decidedOne), EditField.VariationTargets));

        // バリエーションを選ばない購入（null）の行も向き先を持てるので、決めていなければ未入力
        Assert.True(EditFieldsMissing.IsMissing(VariationItem([purchases[0], new Purchase { VariationId = null }], decidedOne), EditField.VariationTargets));
    }

    [Fact]
    public void バリエーションの対応は_欄の出ない商品では入力済み()
    {
        var purchases = new[] { new Purchase { VariationId = 1 } };

        // バリエーションが1つ・購入が無い・対応アバターが無い（消した物と説明文から読み取っただけの物は数えない）
        Assert.False(EditFieldsMissing.IsMissing(VariationItem(purchases, [], variations: 1), EditField.VariationTargets));
        Assert.False(EditFieldsMissing.IsMissing(VariationItem([], []), EditField.VariationTargets));
        Assert.False(EditFieldsMissing.IsMissing(VariationItem(purchases, [], avatars: []), EditField.VariationTargets));
        Assert.False(EditFieldsMissing.IsMissing(VariationItem(purchases, [], avatars:
            [
                new AvatarLink { AvatarItemId = "9900001", Source = AvatarLinkSource.SupportSection, Rejected = true },
                new AvatarLink { AvatarItemId = "9900002", Source = AvatarLinkSource.H2Link },
            ]), EditField.VariationTargets));

        // 共通素体だけの宣言でも対応がある（登録簿は見ないので、仲間が登録簿に無い素体でも数える）
        Assert.True(EditFieldsMissing.IsMissing(
            VariationItem(purchases, [], avatars: [], bases: [new AvatarBaseLink { BaseName = "作り物の素体" }]), EditField.VariationTargets));
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
            Avatars = [new AvatarLink { AvatarItemId = "9900001", Source = AvatarLinkSource.H2Link, Confirmed = true }],
        });

        Assert.All(EditFieldsMissing.All, field => Assert.False(EditFieldsMissing.IsMissing(item, field), field.ToString()));
    }

    [Fact]
    public void 空白だけのメモは未入力()
        => Assert.True(EditFieldsMissing.IsMissing(Item(new LocalBlock { Memo = "  \n" }), EditField.Memo));

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void 対応アバターの確認は_確かめても消してもいない推定があるときだけ未入力(bool confirmed, bool rejected, bool missing)
        => Assert.Equal(missing, EditFieldsMissing.IsMissing(
            Item(new LocalBlock { Avatars = [new AvatarLink { AvatarItemId = "9900001", Source = AvatarLinkSource.H2Link, Confirmed = confirmed, Rejected = rejected }] }),
            EditField.AvatarConfirmation));

    [Fact]
    public void 対応アバターの無い商品は_確認の項目では入力済み()
        => Assert.False(EditFieldsMissing.IsMissing(Item(new LocalBlock()), EditField.AvatarConfirmation));

    [Fact]
    public void 状態の名前は欄の名前で_知らない名前は飛ばし_残らなければ既定()
    {
        Assert.Equal(["userTags", "attributes", "purchases", "variationTargets", "acquiredAt", "memo", "avatarConfirmation"], EditFieldsMissing.All.Select(EditFieldsMissing.KeyOf));
        Assert.Equal([EditField.Attributes, EditField.Memo], EditFieldsMissing.Parse(["memo", "知らない", "attributes"]));
        Assert.Equal([EditField.UserTags], EditFieldsMissing.Parse([]));
        Assert.Equal([EditField.UserTags], EditFieldsMissing.Parse(["知らない"]));
    }
}
