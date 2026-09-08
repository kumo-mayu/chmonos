using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class UnpackedFileResolverTests : IDisposable
{
    private readonly string _root;

    public UnpackedFileResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-origin-" + Guid.NewGuid().ToString("N"));
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

    private string Write(string relativePath)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[8]);
        return full;
    }

    [Fact]
    public void FindsTheArchiveForAFileInsideItsUnpackedFolder()
    {
        var archive = Write("Kipfel_1.2.0.zip");
        var inside = Write("Kipfel_1.2.0/texture.psd");

        Assert.Equal(archive, UnpackedFileResolver.FindArchiveFor(inside));
    }

    /// <summary>展開先の奥に置かれていても、親をたどって元のzipへ行き着く。</summary>
    [Fact]
    public void WalksUpToFindTheArchive()
    {
        var archive = Write("Kipfel_1.2.0.zip");
        var deep = Write("Kipfel_1.2.0/textures/body/skin.psd");

        Assert.Equal(archive, UnpackedFileResolver.FindArchiveFor(deep));
    }

    /// <summary>zipが無ければ、そのファイル自身を使うしかない。</summary>
    [Fact]
    public void FindsNothingWhenTheArchiveIsGone()
    {
        var inside = Write("Kipfel_1.2.0/texture.psd");

        Assert.Null(UnpackedFileResolver.FindArchiveFor(inside));
    }

    /// <summary>ただ同じ場所に置かれているだけのファイルは、展開先ではない。</summary>
    [Fact]
    public void FindsNothingForAFileBesideAnArchive()
    {
        Write("Kipfel_1.2.0.zip");
        var beside = Write("other.psd");

        Assert.Null(UnpackedFileResolver.FindArchiveFor(beside));
    }

    /// <summary>名前が食い違えば対応とはみなさない。</summary>
    [Fact]
    public void FindsNothingWhenTheFolderNameDiffers()
    {
        Write("Kipfel_1.2.0.zip");
        var inside = Write("Kipfel_old/texture.psd");

        Assert.Null(UnpackedFileResolver.FindArchiveFor(inside));
    }

    [Fact]
    public void ReportsOnlyTheFilesThatHaveAnOrigin()
    {
        var archive = Write("Kipfel_1.2.0.zip");
        var inside = Write("Kipfel_1.2.0/texture.psd");
        var standalone = Write("standalone.psd");

        var origins = UnpackedFileResolver.FindOrigins([inside, standalone, _root]);

        Assert.Single(origins);
        Assert.Equal(inside, origins[0].FilePath);
        Assert.Equal(archive, origins[0].ArchivePath);
    }
}
