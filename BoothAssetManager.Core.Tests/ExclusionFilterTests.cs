using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class ExclusionFilterTests
{
    private const string ExcludedPath = @"D:\storage\VRChat_material\unity_project_backup.zip";
    private static readonly DateTimeOffset Modified = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static ExclusionFilter CreateFilter() => new(
    [
        new ExcludedEntry
        {
            Hash = "AAAA",
            Paths = [ExcludedPath],
            ExcludedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Reason = "BOOTH商品ではない",
        },
    ]);

    private static ScannedFile Scanned(string path, long size = 100, DateTimeOffset? modified = null) => new()
    {
        Path = path,
        SizeBytes = size,
        ModifiedAtUtc = modified ?? Modified,
        Extension = ".zip",
    };

    /// <summary>外したときの走査の控え。大きさ・更新日時・ハッシュの3点。</summary>
    private static ScanCacheIndex CacheOf(string hash) => new(
    [
        new ScanCacheEntry { Path = ExcludedPath, SizeBytes = 100, ModifiedAtUtc = Modified, Hash = hash },
    ]);

    /// <summary>パスと控えで弾ければハッシュ計算そのものが不要になる。これが省略の第一段。</summary>
    [Fact]
    public void ExcludesByPathWithoutHashingWhenTheCacheMatches()
    {
        Assert.True(CreateFilter().IsExcludedWithoutHashing(Scanned(ExcludedPath), CacheOf("AAAA")));
    }

    [Fact]
    public void MatchesPathCaseInsensitively()
    {
        Assert.True(CreateFilter().IsExcludedWithoutHashing(
            Scanned(@"d:\STORAGE\vrchat_material\UNITY_PROJECT_BACKUP.ZIP"),
            CacheOf("aaaa")));
    }

    /// <summary>
    /// 外したのは中身で、場所ではない（ユーザ判断 2026-09-23）。
    /// 同じ名前で落とし直した更新版は大きさか日時が控えと違うので、第一段では弾かずハッシュに回す。
    /// </summary>
    [Theory]
    [InlineData(200, 0)]
    [InlineData(100, 1)]
    public void DoesNotSkipHashingWhenTheFileChangedAtTheSamePath(long size, int hoursLater)
    {
        var changed = Scanned(ExcludedPath, size, Modified.AddHours(hoursLater));

        Assert.False(CreateFilter().IsExcludedWithoutHashing(changed, CacheOf("AAAA")));
    }

    /// <summary>控えが無ければ中身が同じとは言えない。ハッシュを取って決める。</summary>
    [Fact]
    public void DoesNotSkipHashingWithoutACacheEntry()
    {
        Assert.False(CreateFilter().IsExcludedWithoutHashing(Scanned(ExcludedPath), new ScanCacheIndex()));
    }

    /// <summary>控えのハッシュが外した物と違えば（外した後に中身が入れ替わって、既に読み直した）外さない。</summary>
    [Fact]
    public void DoesNotExcludeWhenTheCachedHashIsAnotherContent()
    {
        Assert.False(CreateFilter().IsExcludedWithoutHashing(Scanned(ExcludedPath), CacheOf("BBBB")));
    }

    /// <summary>移動されるとパスでは弾けないので、ハッシュ側の判定が効く必要がある。</summary>
    [Fact]
    public void StillExcludesMovedFileByHash()
    {
        var filter = CreateFilter();

        Assert.False(filter.IsExcludedWithoutHashing(Scanned(@"E:\moved\unity_project_backup.zip"), CacheOf("AAAA")));
        Assert.True(filter.IsExcludedByHash("AAAA"));
    }

    [Fact]
    public void DoesNotExcludeUnknownFile()
    {
        var filter = CreateFilter();

        Assert.False(filter.IsExcludedWithoutHashing(Scanned(@"D:\storage\VRChat_clothes\other.zip"), CacheOf("AAAA")));
        Assert.False(filter.IsExcludedByHash("BBBB"));
    }

    [Fact]
    public void HandlesEmptyExclusionList()
    {
        var filter = new ExclusionFilter();

        Assert.False(filter.IsExcludedWithoutHashing(Scanned(@"D:\anything.zip"), new ScanCacheIndex()));
        Assert.False(filter.IsExcludedByHash("AAAA"));
    }
}
