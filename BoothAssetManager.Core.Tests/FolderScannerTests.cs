using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _root;
    private readonly FolderScanner _scanner = new();

    public FolderScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-scan-" + Guid.NewGuid().ToString("N"));
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

    private string Write(string relativePath, int bytes = 8)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    [Fact]
    public void ScansEveryTargetFileUnderAFolder()
    {
        Write("a.zip");
        Write("nested/b.zip");
        Write("notes.txt");

        var result = _scanner.Scan(_root);

        Assert.Equal(2, result.Files.Count);
        Assert.All(result.Files, file => Assert.Equal(".zip", file.Extension));
    }

    /// <summary>
    /// ファイルを直接指定したら、そのファイルだけを対象にする。
    /// 親フォルダへ広げると、ダウンロードフォルダの1件を落としただけで
    /// フォルダ全体が取り込み対象になってしまう。
    /// </summary>
    [Fact]
    public void ScansOnlyTheGivenFileWhenAFileIsSpecified()
    {
        var target = Write("wanted.zip");
        Write("sibling.zip");
        Write("another.psd");

        var result = _scanner.Scan(target);

        Assert.Single(result.Files);
        Assert.Equal(target, result.Files[0].Path);
    }

    [Fact]
    public void ReportsSizeAndTimestampForASingleFile()
    {
        var target = Write("wanted.zip", bytes: 1234);

        var file = _scanner.Scan(target).Files.Single();

        Assert.Equal(1234, file.SizeBytes);
        Assert.Equal(".zip", file.Extension);
        Assert.True(file.ModifiedAtUtc > DateTimeOffset.UnixEpoch);
    }

    /// <summary>対象外の拡張子は、直接指定されても取り込まない。</summary>
    [Fact]
    public void IgnoresANonTargetFileEvenWhenSpecifiedDirectly()
    {
        var target = Write("readme.txt");

        Assert.Empty(_scanner.Scan(target).Files);
    }

    [Fact]
    public void ReturnsNothingForAMissingPath()
    {
        Assert.Empty(_scanner.Scan(Path.Combine(_root, "does-not-exist")).Files);
    }

    /// <summary>展開先フォルダの中身は、フォルダ走査では対象から外す。</summary>
    [Fact]
    public void SkipsFilesInsideAnUnpackedFolder()
    {
        Write("Kipfel_1.2.0.zip");
        Write("Kipfel_1.2.0/inside.psd");

        var result = _scanner.Scan(_root);

        Assert.Single(result.Files);
        Assert.EndsWith("Kipfel_1.2.0.zip", result.Files[0].Path);
        Assert.Single(result.UnpackedFolders);
        Assert.Equal(1, result.SkippedInsideUnpackedFolders);
    }

    /// <summary>
    /// ただし展開先の中のファイルを名指しで落とした場合は、その意思を尊重して取り込む。
    /// フォルダ走査での自動判定と、手で指定した操作は別物。
    /// </summary>
    [Fact]
    public void TakesAFileInsideAnUnpackedFolderWhenSpecifiedDirectly()
    {
        Write("Kipfel_1.2.0.zip");
        var inside = Write("Kipfel_1.2.0/inside.psd");

        var result = _scanner.Scan(inside);

        Assert.Single(result.Files);
        Assert.Equal(inside, result.Files[0].Path);
    }
}
