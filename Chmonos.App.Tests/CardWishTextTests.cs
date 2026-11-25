using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// カードの星の上のスキ数（ユーザ判断 2026-11-25）。商品ページと同じ「♡ n」。
/// BOOTH から一度も取れていない商品は 0 と書かず空にする（リストの列「スキ数」と同じ）。
/// </summary>
public class CardWishTextTests
{
    private static ItemCardViewModel Card(BoothBlock booth)
        => new(new ItemRecord { Id = "1000005", Booth = booth }, new ThumbnailLoader(), System.IO.Path.GetTempPath(), ThumbnailRole.Default, showRemovedImages: false)
        {
            Name = "作り物",
        };

    [Fact]
    public Task 取れている商品は_商品ページと同じ書き方で出す() => UiThread.Run(() =>
    {
        var booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, WishListsCount = 1234 };
        Assert.Equal("♡ 1,234", Card(booth).WishText);
        Assert.Equal("♡ 0", Card(new BoothBlock { FetchedAt = DateTimeOffset.Now }).WishText);
    });

    [Fact]
    public Task まだ取れていない商品は_0と書かず何も出さない() => UiThread.Run(() =>
    {
        Assert.Equal(string.Empty, Card(new BoothBlock { WishListsCount = 5 }).WishText);
    });
}
