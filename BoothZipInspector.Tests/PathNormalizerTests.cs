using BoothZipInspector;
using Xunit;

namespace BoothZipInspector.Tests;

public class PathNormalizerTests
{
    [Fact]
    public void RemovesSurroundingDoubleQuotes()
    {
        var result = PathNormalizer.Normalize("\"C:\\Downloads\\example.zip\"");
        Assert.Equal("C:\\Downloads\\example.zip", result);
    }

    [Fact]
    public void KeepsInternalSpacesWhenQuoted()
    {
        var result = PathNormalizer.Normalize("\"C:\\My Downloads\\example file.zip\"");
        Assert.Equal("C:\\My Downloads\\example file.zip", result);
    }

    [Fact]
    public void LeavesUnquotedPathUnchanged()
    {
        var result = PathNormalizer.Normalize("C:\\Downloads\\example.zip");
        Assert.Equal("C:\\Downloads\\example.zip", result);
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        var result = PathNormalizer.Normalize("  \"C:\\Downloads\\example.zip\"  ");
        Assert.Equal("C:\\Downloads\\example.zip", result);
    }

    [Fact]
    public void HandlesNullInput()
    {
        var result = PathNormalizer.Normalize(null);
        Assert.Equal(string.Empty, result);
    }
}
