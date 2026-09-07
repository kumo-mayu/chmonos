using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class ExclusionFilterTests
{
    private static ExclusionFilter CreateFilter() => new(
    [
        new ExcludedEntry
        {
            Hash = "AAAA",
            Paths = [@"D:\storage\VRChat_material\unity_project_backup.zip"],
            ExcludedAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            Reason = "BOOTH商品ではない",
        },
    ]);

    /// <summary>パスで弾ければハッシュ計算そのものが不要になる。これが省略の第一段。</summary>
    [Fact]
    public void ExcludesByPathWithoutHashing()
    {
        var filter = CreateFilter();

        Assert.True(filter.IsExcludedByPath(@"D:\storage\VRChat_material\unity_project_backup.zip"));
    }

    [Fact]
    public void MatchesPathCaseInsensitively()
    {
        var filter = CreateFilter();

        Assert.True(filter.IsExcludedByPath(@"d:\STORAGE\vrchat_material\UNITY_PROJECT_BACKUP.ZIP"));
    }

    /// <summary>移動されるとパスでは弾けないので、ハッシュ側の判定が効く必要がある。</summary>
    [Fact]
    public void StillExcludesMovedFileByHash()
    {
        var filter = CreateFilter();

        Assert.False(filter.IsExcludedByPath(@"E:\moved\unity_project_backup.zip"));
        Assert.True(filter.IsExcludedByHash("AAAA"));
    }

    [Fact]
    public void DoesNotExcludeUnknownFile()
    {
        var filter = CreateFilter();

        Assert.False(filter.IsExcludedByPath(@"D:\storage\VRChat_clothes\other.zip"));
        Assert.False(filter.IsExcludedByHash("BBBB"));
    }

    [Fact]
    public void HandlesEmptyExclusionList()
    {
        var filter = new ExclusionFilter();

        Assert.False(filter.IsExcludedByPath(@"D:\anything.zip"));
        Assert.False(filter.IsExcludedByHash("AAAA"));
    }
}
