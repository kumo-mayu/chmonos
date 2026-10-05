using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

public class ScanCacheIndexTests
{
    private static readonly DateTimeOffset Modified = new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);

    private static ScanCacheIndex CreateIndex() => new(
    [
        new ScanCacheEntry
        {
            Path = @"D:\storage\VRChat_clothes\a.zip",
            SizeBytes = 1000,
            ModifiedAtUtc = Modified,
            Hash = "AAAA",
        },
    ]);

    /// <summary>名前の大文字小文字だけを変えた物は、控えから引けて、控えの綴りも今の名前になる（2026-10-05・点検の14）。</summary>
    [Fact]
    public void FollowsACaseOnlyRenameWhenReusingTheHash()
    {
        var index = CreateIndex();

        Assert.True(index.TryGetHash(@"D:\storage\VRChat_clothes\A.zip", 1000, Modified, out var hash));

        Assert.Equal("AAAA", hash);
        Assert.Equal(@"D:\storage\VRChat_clothes\A.zip", Assert.Single(index.ToList()).Path);
        Assert.Equal(@"D:\storage\VRChat_clothes\A.zip", Assert.Single(index.MergeInto(CreateIndex().ToList())).Path);
    }

    [Fact]
    public void ReusesHashWhenPathSizeAndModifiedAllMatch()
    {
        var index = CreateIndex();

        var reused = index.TryGetHash(@"D:\storage\VRChat_clothes\a.zip", 1000, Modified, out var hash);

        Assert.True(reused);
        Assert.Equal("AAAA", hash);
    }

    /// <summary>サイズだけの判定では中身の差し替えを見逃すので、サイズが違えば計算し直す。</summary>
    [Fact]
    public void RecomputesWhenSizeDiffers()
    {
        var index = CreateIndex();

        Assert.False(index.TryGetHash(@"D:\storage\VRChat_clothes\a.zip", 1001, Modified, out _));
    }

    [Fact]
    public void RecomputesWhenModifiedDiffers()
    {
        var index = CreateIndex();

        Assert.False(index.TryGetHash(@"D:\storage\VRChat_clothes\a.zip", 1000, Modified.AddSeconds(1), out _));
    }

    [Fact]
    public void RecomputesForUnknownPath()
    {
        var index = CreateIndex();

        Assert.False(index.TryGetHash(@"E:\backup\a.zip", 1000, Modified, out _));
    }

    /// <summary>同じファイルを別のドライブへコピーした場合、パスが違うので別エントリとして計算する。</summary>
    [Fact]
    public void TreatsSamePathCaseInsensitively()
    {
        var index = CreateIndex();

        Assert.True(index.TryGetHash(@"d:\STORAGE\vrchat_clothes\A.ZIP", 1000, Modified, out _));
    }

    [Fact]
    public void SetOverwritesExistingEntry()
    {
        var index = CreateIndex();

        index.Set(@"D:\storage\VRChat_clothes\a.zip", 2000, Modified, "BBBB");

        Assert.Equal(1, index.Count);
        Assert.True(index.TryGetHash(@"D:\storage\VRChat_clothes\a.zip", 2000, Modified, out var hash));
        Assert.Equal("BBBB", hash);
    }

    [Fact]
    public void RoundTripsThroughList()
    {
        var index = CreateIndex();

        var restored = new ScanCacheIndex(index.ToList());

        Assert.True(restored.TryGetHash(@"D:\storage\VRChat_clothes\a.zip", 1000, Modified, out var hash));
        Assert.Equal("AAAA", hash);
    }
}
