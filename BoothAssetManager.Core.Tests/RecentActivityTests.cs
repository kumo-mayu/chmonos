using BoothAssetManager.Core.Services;

namespace BoothAssetManager.Core.Tests;

public sealed class RecentActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 18, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void 初めての商品は行を作る()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);

        var entry = Assert.Single(log);
        Assert.Equal("1", entry.ItemId);
        Assert.Equal(Now, entry.UsedAt);
        Assert.Null(entry.AddedAt);
        Assert.Null(entry.ViewedAt);
    }

    [Fact]
    public void 同じ商品は行を増やさず種類だけ書き換える()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);
        log = RecentActivity.Touch(log, "1", RecentKind.Viewed, Now.AddMinutes(5));

        var entry = Assert.Single(log);
        Assert.Equal(Now, entry.UsedAt);
        Assert.Equal(Now.AddMinutes(5), entry.ViewedAt);
    }

    [Fact]
    public void 同じ種類は上書きする()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Viewed, Now);
        log = RecentActivity.Touch(log, "1", RecentKind.Viewed, Now.AddDays(1));

        Assert.Equal(Now.AddDays(1), Assert.Single(log).ViewedAt);
    }

    [Fact]
    public void 商品が違えば行が増える()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);
        log = RecentActivity.Touch(log, "2", RecentKind.Used, Now);

        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void 商品IDの大文字小文字を区別しない()
    {
        // 仮IDは local-{hash} なので、手で直したファイルから読むと揺れうる
        var log = RecentActivity.Touch([], "local-ABCD1234", RecentKind.Used, Now);
        log = RecentActivity.Touch(log, "local-abcd1234", RecentKind.Viewed, Now);

        Assert.Single(log);
    }

    [Fact]
    public void 種類ごとに時刻を引ける()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);
        log = RecentActivity.Touch(log, "2", RecentKind.Viewed, Now);

        var used = RecentActivity.Times(log, RecentKind.Used);
        var viewed = RecentActivity.Times(log, RecentKind.Viewed);

        Assert.Equal(["1"], used.Keys);
        Assert.Equal(["2"], viewed.Keys);
    }

    [Fact]
    public void 付いていない種類は表に出さない()
    {
        // 「まだ無い」と「古い」を混ぜないため、0ではなく不在にする
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);

        Assert.Empty(RecentActivity.Times(log, RecentKind.Added));
    }

    [Fact]
    public void 手元に無くなった商品の行を落とす()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);
        log = RecentActivity.Touch(log, "2", RecentKind.Used, Now);

        var kept = RecentActivity.KeepOnly(log, new HashSet<string> { "1" });

        Assert.Equal("1", Assert.Single(kept).ItemId);
    }

    [Fact]
    public void 全部残っていれば何も落とさない()
    {
        var log = RecentActivity.Touch([], "1", RecentKind.Used, Now);

        Assert.Single(RecentActivity.KeepOnly(log, new HashSet<string> { "1", "2" }));
    }
}
