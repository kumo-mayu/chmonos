using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

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

    // ---- 何日以内か ----

    [Fact]
    public void 記録が無ければ当てはまらない()
    {
        // 「値が小さい」ではなく「値が無い」ので、何日以内にも入らない
        Assert.False(RecentActivity.IsWithin(null, 30, Now));
    }

    [Fact]
    public void 日数が0以下なら絞っていない扱いで全部通す()
    {
        Assert.True(RecentActivity.IsWithin(null, 0, Now));
        Assert.True(RecentActivity.IsWithin(null, -1, Now));
    }

    [Fact]
    public void 期間の中にあれば当てはまる()
        => Assert.True(RecentActivity.IsWithin(Now.AddDays(-3), 30, Now));

    [Fact]
    public void 期間より古ければ外れる()
        => Assert.False(RecentActivity.IsWithin(Now.AddDays(-31), 30, Now));

    [Fact]
    public void ちょうど境目は含める()
    {
        // 「30日以内」に30日前を入れないと、境目の1日が誰にも当たらなくなる
        Assert.True(RecentActivity.IsWithin(Now.AddDays(-30), 30, Now));
    }

    [Fact]
    public void 未来の時刻も当てはまる()
    {
        // 時計を戻した後などに起こりうる。除くと「使ったのに出ない」になる
        Assert.True(RecentActivity.IsWithin(Now.AddDays(1), 30, Now));
    }

    /// <summary>
    /// 商品のIDを変えるとき、移し先にも足跡があれば1行にまとめ、種類ごとに新しい方の日時を残す（外部の点検 2026-10-06）。
    /// 前は ID を書き換えるだけで同じ商品の行が2つ残り、打つ側は先の行・引く側は後の行を見て、新しい閲覧が検索に出なくなった
    /// </summary>
    [Fact]
    public void IDを変えると_移し元と移し先の足跡は1行にまとまり_新しい方の日時が残る()
    {
        var entries = new[]
        {
            new RecentEntry { ItemId = "9900001", ViewedAt = Now, UsedAt = Now.AddDays(-9) },
            new RecentEntry { ItemId = "9900003", ViewedAt = Now.AddDays(-1) },
            new RecentEntry { ItemId = "9900002", ViewedAt = Now.AddDays(-5), UsedAt = Now.AddDays(-2), AddedAt = Now.AddDays(-30) },
        };

        var renamed = RecentActivity.Renamed(entries, "9900001", "9900002");

        Assert.Equal(["9900002", "9900003"], renamed.Select(entry => entry.ItemId));
        var merged = renamed[0];
        Assert.Equal(Now, merged.ViewedAt);
        Assert.Equal(Now.AddDays(-2), merged.UsedAt);
        Assert.Equal(Now.AddDays(-30), merged.AddedAt);

        // その後に閲覧すると、検索で引く日時も変わる
        var touched = RecentActivity.Touch(renamed, "9900002", RecentKind.Viewed, Now.AddHours(1));
        Assert.Equal(Now.AddHours(1), RecentActivity.Times(touched, RecentKind.Viewed)["9900002"]);
    }

    [Fact]
    public void IDを変える先に足跡が無ければ_行の場所はそのままで付け替わる()
    {
        var entries = new[] { new RecentEntry { ItemId = "9900009" }, new RecentEntry { ItemId = "9900001", ViewedAt = Now } };

        var renamed = RecentActivity.Renamed(entries, "9900001", "9900002");

        Assert.Equal(["9900009", "9900002"], renamed.Select(entry => entry.ItemId));
        Assert.Equal(Now, renamed[1].ViewedAt);
    }

    [Fact]
    public void 同じ商品の行が2つあっても_日時は新しい方を引く()
    {
        // 手で直した JSON や前の版で重なった行に、古い日時で引きずられない
        var entries = new[]
        {
            new RecentEntry { ItemId = "9900001", ViewedAt = Now },
            new RecentEntry { ItemId = "9900001", ViewedAt = Now.AddDays(-7) },
        };

        Assert.Equal(Now, RecentActivity.Times(entries, RecentKind.Viewed)["9900001"]);
    }
}
