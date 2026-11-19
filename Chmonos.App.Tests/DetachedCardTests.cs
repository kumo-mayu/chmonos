using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>カードの帯「取り外し中」（手元の物が全部、つながっていないドライブの上。ユーザ判断 2026-11-20）。</summary>
public sealed class DetachedCardTests
{
    /// <summary>この PC に無いドライブ文字の上の場所。</summary>
    private static string DetachedPath()
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            var root = $@"{letter}:\";
            if (!Directory.Exists(root))
            {
                return Path.Combine(root, "shop", "a.zip");
            }
        }

        throw new InvalidOperationException("空いているドライブ文字がありません。");
    }

    private static ItemRecord Item(string id, string path) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "item" + id, FetchedAt = DateTimeOffset.Now },
        Local = new LocalBlock { LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = [path], SizeBytes = 1 }] },
    };

    [Fact]
    public Task 全部つながっていないドライブの上の商品のカードだけが取り外し中になり_所持のまま容量は出さない() => TestApp.Run(async app =>
    {
        var here = Path.Combine(app.Root, "here.zip");
        await app.AddItemAsync(Item("1000001", DetachedPath()));
        await app.AddItemAsync(Item("1000002", here));
        var search = (await app.StartAsync()).Search;

        await UiThread.Until(() => search.CardFor("1000001")!.IsDetached || search.IsDetached("1000001"), "取り外し中の確かめ");

        Assert.True(search.IsDetached("1000001"));
        Assert.False(search.IsDetached("1000002"));
        var card = search.CardFor("1000001")!;
        Assert.True(card.IsDetached);
        Assert.True(card.IsOwned);
        Assert.False(card.ShowsSize);
        Assert.False(search.CardFor("1000002")!.IsDetached);
        Assert.True(search.CardFor("1000002")!.ShowsSize);
    });
}
