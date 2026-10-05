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

    /// <summary>
    /// 場所・大きさ・更新日時が同じでも、控えたのと別のディスクの上なら使い回さない（2026-10-05・見つからない・移動の点検の16・ユーザ判断 16-A）。
    /// 2台の外付けが同じ文字を使い、同じ名前・大きさ・日時のファイル（写した物を更新した等）があると、Aで取ったハッシュをBのファイルに当てていた。
    /// </summary>
    [Fact]
    public void 控えたのと別のディスクの上なら_場所と大きさと日時が同じでもハッシュを使い回さない()
    {
        var index = new ScanCacheIndex(
            [new ScanCacheEntry { Path = @"E:\booth\a.zip", SizeBytes = 1000, ModifiedAtUtc = Modified, Hash = "AAAA", Volume = "AAAA0016" }],
            volumeAt: _ => "BBBB0016");

        Assert.False(index.TryGetHash(@"E:\booth\a.zip", 1000, Modified, out _));
    }

    [Fact]
    public void 控えたのと同じディスクの上なら使い回し_取り直した控えには今のディスクを書く()
    {
        var index = new ScanCacheIndex(
            [new ScanCacheEntry { Path = @"E:\booth\a.zip", SizeBytes = 1000, ModifiedAtUtc = Modified, Hash = "AAAA", Volume = "AAAA0016" }],
            volumeAt: _ => "AAAA0016");

        Assert.True(index.TryGetHash(@"E:\booth\a.zip", 1000, Modified, out var hash));
        Assert.Equal("AAAA", hash);

        index.Set(@"E:\booth\b.zip", 5, Modified, "BBBB");
        Assert.Equal("AAAA0016", index.ToList().Single(entry => entry.Hash == "BBBB").Volume);
    }

    /// <summary>
    /// ディスクの欄の無い控えは今までどおり使い回し、そのとき今のディスクを書き足す（取り直すと全部をハッシュし直すことになる。実測で30分近く）。
    /// ディスクの分からない場所（ネットワークの共有）は欄を書かず、今までどおり3点で照らす。
    /// </summary>
    [Fact]
    public void ディスクの欄の無い控えは使い回して今のディスクを書き足し_分からない場所は3点で照らす()
    {
        var index = new ScanCacheIndex(
            [
                new ScanCacheEntry { Path = @"E:\booth\a.zip", SizeBytes = 1000, ModifiedAtUtc = Modified, Hash = "AAAA" },
                new ScanCacheEntry { Path = @"\\nas\share\b.zip", SizeBytes = 7, ModifiedAtUtc = Modified, Hash = "BBBB", Volume = "AAAA0016" },
            ],
            volumeAt: path => path.StartsWith(@"E:", StringComparison.Ordinal) ? "AAAA0016" : null);

        Assert.True(index.TryGetHash(@"E:\booth\a.zip", 1000, Modified, out _));
        Assert.True(index.TryGetHash(@"\\nas\share\b.zip", 7, Modified, out _));
        Assert.Equal("AAAA0016", index.MergeInto([]).Single(entry => entry.Hash == "AAAA").Volume);
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
