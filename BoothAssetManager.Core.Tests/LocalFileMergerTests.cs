using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class LocalFileMergerTests
{
    private static LocalFileRecord Record(string hash, params string[] paths) => new()
    {
        Hash = hash,
        Paths = paths,
        SizeBytes = 100,
    };

    /// <summary>すべてのパスが実在するものとして扱う。</summary>
    private static bool AllExist(string path) => true;

    /// <summary>同じ中身が2箇所にある状態。1レコードが複数のパスを持つ形になる。</summary>
    [Fact]
    public void UnionsPathsForTheSameHash()
    {
        var merged = LocalFileMerger.Merge(
            [Record("AAAA", @"D:\storage\a.zip")],
            [Record("AAAA", @"E:\backup\a.zip")],
            AllExist);

        var record = Assert.Single(merged);
        Assert.Equal(2, record.Paths.Count);
        Assert.Contains(@"D:\storage\a.zip", record.Paths);
        Assert.Contains(@"E:\backup\a.zip", record.Paths);
    }

    [Fact]
    public void DoesNotDuplicatePathsThatDifferOnlyInCase()
    {
        var merged = LocalFileMerger.Merge(
            [Record("AAAA", @"D:\storage\a.zip")],
            [Record("AAAA", @"d:\STORAGE\A.ZIP")],
            AllExist);

        Assert.Single(Assert.Single(merged).Paths);
    }

    /// <summary>中身が違えば別レコード（v1.0とv1.2を両方持っている状態）。</summary>
    [Fact]
    public void KeepsDifferentHashesAsSeparateRecords()
    {
        var merged = LocalFileMerger.Merge(
            [Record("AAAA", @"D:\storage\v1.0.zip")],
            [Record("BBBB", @"D:\storage\v1.2.zip")],
            AllExist);

        Assert.Equal(2, merged.Count);
    }

    /// <summary>variationの紐付けはユーザ入力なので、再スキャンで消えてはいけない。</summary>
    [Fact]
    public void PreservesExistingVariationLink()
    {
        var existing = Record("AAAA", @"D:\storage\a.zip") with { VariationId = 12826082 };

        var merged = LocalFileMerger.Merge([existing], [Record("AAAA", @"D:\storage\a.zip")], AllExist);

        Assert.Equal(12826082, Assert.Single(merged).VariationId);
    }

    [Fact]
    public void PreservesExistingContents()
    {
        var existing = Record("AAAA", @"D:\storage\a.zip") with { Contents = ["a/readme.txt"] };

        var merged = LocalFileMerger.Merge([existing], [Record("AAAA", @"D:\storage\a.zip")], AllExist);

        Assert.Equal(["a/readme.txt"], Assert.Single(merged).Contents);
    }

    /// <summary>ファイルを移動した場合。古いパスが消え、新しいパスだけが残る。</summary>
    [Fact]
    public void DropsPathsThatNoLongerExist()
    {
        var merged = LocalFileMerger.Merge(
            [Record("AAAA", @"D:\storage\a.zip")],
            [Record("AAAA", @"E:\moved\a.zip")],
            path => path.StartsWith(@"E:\", StringComparison.OrdinalIgnoreCase),
            NoVolumeMissing);

        var record = Assert.Single(merged);
        Assert.Equal([@"E:\moved\a.zip"], record.Paths);
    }

    private static bool NoVolumeMissing(string path) => false;

    /// <summary>
    /// 外付けを外している間に、同じ商品へ別のファイルを足した。外付けの上の記録は「見えない」だけなので残す
    /// （点検 2026-09-23：落としていたので、つなぎ直しても商品から外付けのファイルが消えていた）。
    /// </summary>
    [Fact]
    public void KeepsPathsOnDisconnectedVolume()
    {
        var merged = LocalFileMerger.Merge(
            [Record("AAAA", @"F:\external\a.zip")],
            [Record("BBBB", @"D:\storage\b.zip")],
            path => path.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase),
            path => path.StartsWith(@"F:\", StringComparison.OrdinalIgnoreCase));

        Assert.Equal([@"F:\external\a.zip"], merged.Single(record => record.Hash == "AAAA").Paths);
        Assert.Equal([@"D:\storage\b.zip"], merged.Single(record => record.Hash == "BBBB").Paths);
    }

    /// <summary>既定では、実際につながっていないドライブ文字を「外している」と見る。</summary>
    [Fact]
    public void DefaultTreatsUnmountedDriveAsDisconnected()
    {
        var free = Enumerable.Range('F', 'Z' - 'F' + 1)
            .Select(letter => $"{(char)letter}:\\")
            .FirstOrDefault(root => !Directory.Exists(root));
        if (free is null)
        {
            return;
        }

        var path = free + @"external\a.zip";
        var merged = LocalFileMerger.Merge([Record("AAAA", path)], [], _ => false);

        Assert.Equal([path], Assert.Single(merged).Paths);
    }

    /// <summary>
    /// どこにも実体が無くなっても、レコード自体は残す。
    /// 「ファイルが見つからない」状態として扱い、再スキャンでの復旧に繋げるため。
    /// </summary>
    [Fact]
    public void KeepsRecordWithNoRemainingPaths()
    {
        var merged = LocalFileMerger.Merge([Record("AAAA", @"D:\storage\a.zip")], [], _ => false, NoVolumeMissing);

        var record = Assert.Single(merged);
        Assert.Empty(record.Paths);
        Assert.Equal("AAAA", record.Hash);
    }

    [Fact]
    public void ReturnsDiscoveredRecordsWhenNothingExistedBefore()
    {
        var merged = LocalFileMerger.Merge([], [Record("AAAA", @"D:\storage\a.zip")], AllExist);

        Assert.Equal("AAAA", Assert.Single(merged).Hash);
    }
}
