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

/// <summary>
/// フォルダ登録はzipが手元に無い場合の受け皿なので、zipが現れたら役目を終える。
/// 気付かずに置いておくと容量が二重に数えられる。
/// </summary>
public class ArchiveForFolderTests : IDisposable
{
    private readonly string _root;

    public ArchiveForFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-archfor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void FindsTheArchiveThatAppearedBesideTheFolder()
    {
        var folder = Path.Combine(_root, "rurune_v1.1.3");
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(_root, "rurune_v1.1.3.zip");
        File.WriteAllBytes(archive, new byte[8]);

        Assert.Equal(archive, RegisteredFolderSet.FindArchiveFor(folder));
    }

    [Fact]
    public void FindsNothingWhileTheArchiveIsAbsent()
    {
        var folder = Path.Combine(_root, "rurune_v1.1.3");
        Directory.CreateDirectory(folder);

        Assert.Null(RegisteredFolderSet.FindArchiveFor(folder));
    }

    /// <summary>名前が違うアーカイブは対応とみなさない。</summary>
    [Fact]
    public void IgnoresAnUnrelatedArchive()
    {
        var folder = Path.Combine(_root, "rurune_v1.1.3");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(_root, "Kipfel_1.2.0.zip"), new byte[8]);

        Assert.Null(RegisteredFolderSet.FindArchiveFor(folder));
    }
}
