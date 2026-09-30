using BoothAssetManager.Core.Models;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 検索の条件「壊れたzip」の照合（<see cref="ItemRecord.HasBrokenArchive"/>・ユーザ判断 2026-09-30）。
/// 外していないファイルで、壊れた印の付いた物を1つでも持つ商品が当たる。
/// </summary>
public class BrokenArchiveSearchTests
{
    [Fact]
    public void ItemWithBrokenArchiveMatches()
        => Assert.True(Item(File("AA", broken: true)).HasBrokenArchive);

    /// <summary>壊れていない物と混ざっていても、1つあれば当たる（直す相手がこの商品に在る）。</summary>
    [Fact]
    public void OneBrokenAmongGoodFilesMatches()
        => Assert.True(Item(File("AA"), File("BB", broken: true), File("CC")).HasBrokenArchive);

    [Fact]
    public void ItemWithOnlyGoodFilesDoesNotMatch()
        => Assert.False(Item(File("AA"), File("BB")).HasBrokenArchive);

    /// <summary>ファイルの無い商品（情報だけ・フォルダだけ）は当たらない。</summary>
    [Fact]
    public void ItemWithoutFilesDoesNotMatch()
        => Assert.False(Item().HasBrokenArchive);

    /// <summary>
    /// 外したファイルは数えない。外した物はこの商品の持ち物ではなく（所持・容量と同じ）、
    /// 絞り込みで出ると「ダウンロードし直す」相手がこの商品に見える。
    /// </summary>
    [Fact]
    public void DetachedBrokenArchiveDoesNotMatch()
        => Assert.False(Item(File("AA"), File("BB", broken: true, detached: true)).HasBrokenArchive);

    /// <summary>外した壊れた物のほかに、外していない壊れた物があれば当たる。</summary>
    [Fact]
    public void OwnedBrokenArchiveMatchesNextToDetachedOne()
        => Assert.True(Item(File("AA", broken: true, detached: true), File("BB", broken: true)).HasBrokenArchive);

    /// <summary>
    /// 場所の無い記録（「見つかりません」）でも当たる。**記録だけで決め、ディスクは見ない**——検索は全商品を条件の数だけ照らすので、
    /// ファイルの有無まで確かめると打鍵のたびにディスクを叩く。
    /// </summary>
    [Fact]
    public void RecordWithoutPlacesStillMatches()
        => Assert.True(Item(new LocalFileRecord { Hash = "AA", Paths = [], SizeBytes = 1, ArchiveBroken = true }).HasBrokenArchive);

    private static LocalFileRecord File(string hash, bool broken = false, bool detached = false) => new()
    {
        Hash = hash,
        Paths = [$@"D:\Assets\{hash}.zip"],
        SizeBytes = 1,
        ArchiveBroken = broken,
        Detached = detached,
    };

    private static ItemRecord Item(params LocalFileRecord[] files) => new()
    {
        Id = "1",
        Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.UnixEpoch },
        Local = new LocalBlock { LocalFiles = files },
    };
}
