using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

public sealed class VideoTitleBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.FromHours(9));

    private static VideoTitleRecord Record(string id, string title, double daysAgo)
        => new() { VideoId = id, Title = title, FetchedAt = Now.AddDays(-daysAgo) };

    [Fact]
    public void 取って30日未満の題はそのまま使う()
        => Assert.Equal("紹介", VideoTitleBook.FreshTitle([Record("a", "紹介", 29.9)], "a", Now));

    [Fact]
    public void 取って30日を過ぎた題は使わず取り直す()
        => Assert.Null(VideoTitleBook.FreshTitle([Record("a", "紹介", 30)], "a", Now));

    [Fact]
    public void 控えの無い動画は取りに行く()
        => Assert.Null(VideoTitleBook.FreshTitle([Record("a", "紹介", 1)], "b", Now));

    [Fact]
    public void 取った日時が先の日付なら使わない()
        => Assert.Null(VideoTitleBook.FreshTitle([Record("a", "紹介", -3)], "a", Now));

    [Fact]
    public void 控えると同じ動画の古い題を置き換え取った日時を今にする()
    {
        var records = VideoTitleBook.Remember([Record("a", "古い題", 10)], "a", "新しい題", Now);

        var only = Assert.Single(records);
        Assert.Equal("新しい題", only.Title);
        Assert.Equal(Now, only.FetchedAt);
    }

    [Fact]
    public void 取れなくなった動画は控えを消す()
        => Assert.Empty(VideoTitleBook.Remember([Record("a", "紹介", 10)], "a", null, Now));

    [Fact]
    public void 控えるついでに30日を過ぎた他の控えを落とす()
    {
        var records = VideoTitleBook.Remember([Record("old", "古い", 31), Record("keep", "残す", 5)], "a", "新しい", Now);

        Assert.Equal(new[] { "keep", "a" }, records.Select(record => record.VideoId));
    }

    [Fact]
    public void 整理は30日を過ぎた物だけを消す()
    {
        var records = new[] { Record("old", "古い", 45), Record("keep", "残す", 2) };

        Assert.True(VideoTitleBook.HasStale(records, Now));
        Assert.Equal(new[] { "keep" }, VideoTitleBook.Prune(records, Now).Select(record => record.VideoId));
    }

    [Fact]
    public void 古い物が無ければ整理は要らない()
        => Assert.False(VideoTitleBook.HasStale([Record("a", "紹介", 1)], Now));
}
