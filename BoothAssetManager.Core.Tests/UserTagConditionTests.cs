using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

public sealed class UserTagConditionTests
{
    private static IReadOnlyList<UserTagAssignment> Tags(params (string Top, string[] Subs)[] tags)
        => tags.Select(tag => new UserTagAssignment { Top = tag.Top, Subs = tag.Subs }).ToList();

    private static readonly IReadOnlyList<UserTagAssignment> 衣装に上着と靴 = Tags(("衣装", ["上着", "靴"]));
    private static readonly IReadOnlyList<UserTagAssignment> 衣装だけ = Tags(("衣装", []));
    private static readonly IReadOnlyList<UserTagAssignment> 小物に帽子 = Tags(("小物", ["帽子"]));

    [Fact]
    public void 大分類だけなら付いていれば小分類を問わず当たる()
    {
        var condition = new UserTagCondition { Top = "衣装" };

        Assert.True(condition.Matches(衣装に上着と靴));
        Assert.True(condition.Matches(衣装だけ));
        Assert.False(condition.Matches(小物に帽子));
    }

    [Fact]
    public void 大分類の名前は大文字小文字を区別しない()
        => Assert.True(new UserTagCondition { Top = "outfit" }.Matches(Tags(("Outfit", []))));

    [Fact]
    public void 小分類のORはどれか1つが付いていれば当たる()
    {
        var condition = new UserTagCondition { Top = "衣装", Subs = ["上着", "下着"] };

        Assert.True(condition.Matches(衣装に上着と靴));
        Assert.False(condition.Matches(Tags(("衣装", ["靴"]))));
    }

    [Fact]
    public void 小分類のANDは全部が付いていなければ外れる()
    {
        var both = new UserTagCondition { Top = "衣装", Subs = ["上着", "靴"], MatchAll = true };
        var withMissing = both with { Subs = ["上着", "下着"] };

        Assert.True(both.Matches(衣装に上着と靴));
        Assert.False(withMissing.Matches(衣装に上着と靴));
    }

    [Fact]
    public void 小分類なしは大分類が付いていて小分類が1つも無い商品にだけ当たる()
    {
        var condition = new UserTagCondition { Top = "衣装", NoSub = true };

        Assert.True(condition.Matches(衣装だけ));
        Assert.False(condition.Matches(衣装に上着と靴));

        // 大分類そのものが付いていない商品は「小分類なし」ではない
        Assert.False(condition.Matches(小物に帽子));
        Assert.False(condition.Matches([]));
    }

    [Fact]
    public void 小分類なしと小分類のORはどちらかで当たる()
    {
        var condition = new UserTagCondition { Top = "衣装", Subs = ["上着"], NoSub = true };

        Assert.True(condition.Matches(衣装だけ));
        Assert.True(condition.Matches(衣装に上着と靴));
        Assert.False(condition.Matches(Tags(("衣装", ["靴"]))));
    }

    [Fact]
    public void 小分類なしと小分類のANDは両立しないので何にも当たらない()
    {
        var condition = new UserTagCondition { Top = "衣装", Subs = ["上着"], NoSub = true, MatchAll = true };

        Assert.False(condition.Matches(衣装だけ));
        Assert.False(condition.Matches(衣装に上着と靴));
    }

    [Fact]
    public void 大分類どうしのORはどれかの条件に当たれば通す()
    {
        UserTagCondition[] conditions = [new() { Top = "衣装", Subs = ["上着"] }, new() { Top = "小物" }];

        Assert.True(UserTagCondition.MatchesAll(conditions, matchAll: false, 衣装に上着と靴));
        Assert.True(UserTagCondition.MatchesAll(conditions, matchAll: false, 小物に帽子));
        Assert.False(UserTagCondition.MatchesAll(conditions, matchAll: false, 衣装だけ));
    }

    [Fact]
    public void 大分類どうしのANDは全部の条件に当たらなければ外す()
    {
        UserTagCondition[] conditions = [new() { Top = "衣装", Subs = ["上着"] }, new() { Top = "小物" }];
        var both = Tags(("衣装", ["上着"]), ("小物", []));

        Assert.True(UserTagCondition.MatchesAll(conditions, matchAll: true, both));
        Assert.False(UserTagCondition.MatchesAll(conditions, matchAll: true, 衣装に上着と靴));
    }

    [Fact]
    public void 条件が無ければ絞らない()
        => Assert.True(UserTagCondition.MatchesAll([], matchAll: true, []));

    [Fact]
    public void 状態の指紋は足した順を問わず小分類の違いは見分ける()
    {
        SearchModuleState State(params UserTagCondition[] tags) => new() { Kind = "UserTag", UserTags = tags };
        var a = new UserTagCondition { Top = "衣装", Subs = ["上着", "靴"] };
        var b = new UserTagCondition { Top = "小物" };

        Assert.Equal(State(a, b).Fingerprint, State(b, a with { Subs = ["靴", "上着"] }).Fingerprint);
        Assert.NotEqual(State(a).Fingerprint, State(a with { NoSub = true }).Fingerprint);
        Assert.NotEqual(State(a).Fingerprint, State(a with { MatchAll = true }).Fingerprint);
        Assert.NotEqual(State(a).Fingerprint, State(a with { Subs = ["上着"] }).Fingerprint);
    }
}
