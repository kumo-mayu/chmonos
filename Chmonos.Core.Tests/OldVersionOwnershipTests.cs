using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 上書きで残った古い版（<see cref="LocalFileRecord.IsOldVersion"/>）は、所持・容量に数えない（ユーザ判断 2026-10-05 ⑤-B）。
/// 古い版だけが残った商品は未所持。所持の定義「ファイルかフォルダを1つ以上持つこと」の「持つ」は手元に在ること：
/// 古い版はもう同じ場所で新しい中身に置き換わっていて、どこにも無いと分かっている物。
/// 記録（商品ページの「古い版」の行と「古い版の記録を片付ける」）は残す。
/// </summary>
public class OldVersionOwnershipTests
{
    private static LocalFileRecord OldVersion(long size = 300) => new()
    {
        Hash = "old-version",
        Paths = [],
        SizeBytes = size,
        Contents = ["v1/old-only.txt"],
        Replaced = new ReplacedVersion(@"D:\a\pack.zip", DateTimeOffset.UnixEpoch),
    };

    private static LocalFileRecord Fresh(long size = 1000) => new()
    {
        Hash = "fresh-version",
        Paths = [@"D:\a\pack.zip"],
        SizeBytes = size,
        Contents = ["v2/new.txt"],
    };

    private static ItemRecord Item(string id, params LocalFileRecord[] files) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物の衣装 " + id, FetchedAt = DateTimeOffset.UnixEpoch },
        Local = new LocalBlock { LocalFiles = files },
    };

    [Fact]
    public void 古い版だけの商品は未所持で_容量も0()
    {
        var item = Item("9900001", OldVersion());

        Assert.False(item.IsOwned);
        Assert.False(item.HasOwnedFiles);
        Assert.Empty(item.Local.OwnedFiles);
        Assert.Equal(0, item.LogicalSizeBytes);
        Assert.Equal(0, item.OwnedSizeBytes);
        Assert.Equal(0, item.ActualDiskBytes);

        // 記録そのものは残っている（商品ページの行・片付ける操作が使う）
        Assert.Single(item.Local.LocalFiles);
    }

    [Fact]
    public void 古い版と新しい版がある商品の容量に古い版は入らない()
    {
        var item = Item("9900001", OldVersion(300), Fresh(1000));

        Assert.True(item.IsOwned);
        Assert.Equal("fresh-version", Assert.Single(item.Local.OwnedFiles).Hash);
        Assert.Equal(1000, item.LogicalSizeBytes);
        Assert.Equal(1000, item.OwnedSizeBytes);
    }

    /// <summary>古い版の印があっても、場所がまた足された物（古い版を別の所で見つけた）は持ち物。</summary>
    [Fact]
    public void 古い版の印があっても場所があれば所持に数える()
    {
        var found = OldVersion(300) with { Paths = [@"D:\b\pack-old.zip"] };
        var item = Item("9900001", found);

        Assert.True(item.IsOwned);
        Assert.Equal(300, item.OwnedSizeBytes);
    }

    [Fact]
    public void 統計の所持の数と容量に古い版は入らない()
    {
        var snapshot = StatsService.Build(
            [Item("9900001", OldVersion(300)), Item("9900002", OldVersion(300), Fresh(1000))],
            new AvatarRegistry(),
            unresolvedCount: 0);

        Assert.Equal(1, snapshot.OwnedCount);
        Assert.Equal(1000, snapshot.LogicalBytes);
        Assert.Equal(1000, snapshot.PhysicalBytes);
    }

    [Fact]
    public void 容量の並びで古い版だけの商品は未所持の側に回る()
    {
        var oldOnly = Item("old", OldVersion(5000));
        var small = Item("small", Fresh(10));

        Assert.Equal(["small", "old"], ItemOrder.BySize([oldOnly, small], descending: true).Select(item => item.Id));
    }

    [Fact]
    public void 中身の検索は古い版の中身を見ない()
    {
        var values = SearchText.RawValues(Item("9900001", OldVersion(), Fresh()), SearchField.Content);

        Assert.Contains("v2/new.txt", values);
        Assert.DoesNotContain("v1/old-only.txt", values);
    }
}
