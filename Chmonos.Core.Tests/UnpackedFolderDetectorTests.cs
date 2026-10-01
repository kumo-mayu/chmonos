using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

public class UnpackedFolderDetectorTests
{
    /// <summary>実測で見つかった形。zipの隣に同名のフォルダが展開されている。</summary>
    [Theory]
    [InlineData("Kipfel_1.2.0", "Kipfel_1.2.0.zip")]
    [InlineData("Milfy_v1.5.0", "Milfy_v1.5.0.zip")]
    [InlineData("hotogiya_Kuuta_ver1.03", "hotogiya_Kuuta_ver1.03.zip")]
    [InlineData("FREYSIA.101", "FREYSIA.101.zip")]
    public void DetectsFolderNamedAfterSiblingArchive(string directoryName, string archiveName)
    {
        var archive = UnpackedFolderDetector.FindMatchingArchive(
            directoryName,
            ["readme.txt", archiveName, "other.zip"]);

        Assert.Equal(archiveName, archive);
    }

    [Fact]
    public void MatchesArchiveNameCaseInsensitively()
    {
        Assert.Equal("KIPFEL_1.2.0.ZIP", UnpackedFolderDetector.FindMatchingArchive(
            "kipfel_1.2.0",
            ["KIPFEL_1.2.0.ZIP"]));
    }

    [Theory]
    [InlineData(".rar")]
    [InlineData(".7z")]
    public void RecognizesOtherArchiveFormats(string extension)
    {
        Assert.NotNull(UnpackedFolderDetector.FindMatchingArchive("Asset_v1", [$"Asset_v1{extension}"]));
    }

    /// <summary>同名のアーカイブが無いフォルダは、普通のフォルダとして取り込む。</summary>
    [Fact]
    public void IgnoresFolderWithoutMatchingArchive()
    {
        Assert.Null(UnpackedFolderDetector.FindMatchingArchive(
            "VRChat_clothes",
            ["Kipfel_1.2.0.zip", "Milfy_v1.5.0.zip"]));
    }

    /// <summary>名前が似ているだけのアーカイブには反応しない。</summary>
    [Fact]
    public void RequiresExactNameMatch()
    {
        Assert.Null(UnpackedFolderDetector.FindMatchingArchive("Kipfel", ["Kipfel_1.2.0.zip"]));
    }

    /// <summary>アーカイブでないファイルと同名でも展開先とはみなさない。</summary>
    [Fact]
    public void IgnoresNonArchiveSiblings()
    {
        Assert.Null(UnpackedFolderDetector.FindMatchingArchive("readme", ["readme.txt", "readme.pdf"]));
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        Assert.Null(UnpackedFolderDetector.FindMatchingArchive(string.Empty, ["a.zip"]));
        Assert.Null(UnpackedFolderDetector.FindMatchingArchive("Kipfel_1.2.0", []));
    }
}
