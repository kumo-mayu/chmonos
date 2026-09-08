using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class RegisteredFolderSetTests
{
    [Fact]
    public void ContainsFilesUnderARegisteredFolder()
    {
        var set = new RegisteredFolderSet([@"D:\dl\rurune_v1.1.3"]);

        Assert.True(set.Contains(@"D:\dl\rurune_v1.1.3\rurune\texture\hair.psd"));
        Assert.True(set.Contains(@"D:\dl\rurune_v1.1.3"));
    }

    /// <summary>
    /// 前方一致だけで判定すると "rurune_v1" が "rurune_v1.1.3" を巻き込む。
    /// 区切りまで見て判定していることを確かめる。
    /// </summary>
    [Fact]
    public void DoesNotMatchAFolderThatMerelySharesAPrefix()
    {
        var set = new RegisteredFolderSet([@"D:\dl\rurune_v1"]);

        Assert.False(set.Contains(@"D:\dl\rurune_v1.1.3\rurune\texture\hair.psd"));
    }

    [Fact]
    public void DoesNotMatchFilesOutsideAnyRegisteredFolder()
    {
        var set = new RegisteredFolderSet([@"D:\dl\rurune_v1.1.3"]);

        Assert.False(set.Contains(@"D:\dl\Kipfel_1.2.0.zip"));
    }

    /// <summary>末尾の区切りや向きの違いで取りこぼさない。</summary>
    [Fact]
    public void IgnoresTrailingSeparatorsAndSlashDirection()
    {
        var set = new RegisteredFolderSet([@"D:\dl\rurune_v1.1.3\"]);

        Assert.True(set.Contains(@"D:/dl/rurune_v1.1.3/rurune/texture/hair.psd"));
    }

    [Fact]
    public void IgnoresBlankEntriesAndDuplicates()
    {
        var set = new RegisteredFolderSet([@"D:\dl\a", "  ", @"D:\dl\a", @"D:\dl\b"]);

        Assert.Equal(2, set.Count);
    }

    [Fact]
    public void MeasuresFileCountAndTotalBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "bam-measure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllBytes(Path.Combine(root, "a.psd"), new byte[100]);
        File.WriteAllBytes(Path.Combine(root, "sub", "b.png"), new byte[50]);

        try
        {
            var (count, bytes) = RegisteredFolderSet.Measure(root);

            Assert.Equal(2, count);
            Assert.Equal(150, bytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MeasuresZeroForAMissingFolder()
    {
        var (count, bytes) = RegisteredFolderSet.Measure(Path.Combine(Path.GetTempPath(), "bam-missing-" + Guid.NewGuid()));

        Assert.Equal(0, count);
        Assert.Equal(0, bytes);
    }
}
