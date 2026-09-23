using BoothAssetManager.Core.Services;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>失敗を画面で言うときの原因の見当。.NET の例外の文をそのまま出さない。</summary>
public class FailureTextTests
{
    [Fact]
    public void DoesNotLeakTheExceptionMessage()
    {
        var text = FailureText.Cause(new IOException(@"The process cannot access the file 'C:\secret\a.zip'"));

        Assert.DoesNotContain("process", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\", text, StringComparison.Ordinal);
    }

    /// <summary>別のアプリが掴んでいるときは、それと分かる言い方にする（閉じれば直る）。</summary>
    [Fact]
    public void SharingViolationSaysAnotherAppHasIt()
    {
        var sharing = new IOException("locked", unchecked((int)0x80070020));

        Assert.Contains("別のアプリ", FailureText.Cause(sharing));
    }

    [Fact]
    public void DiskFullSaysSo()
    {
        var full = new IOException("full", unchecked((int)0x80070070));

        Assert.Contains("空き", FailureText.Cause(full));
    }

    [Fact]
    public void MissingFileAndPermissionAreToldApart()
    {
        Assert.Contains("見つかりません", FailureText.Cause(new DirectoryNotFoundException()));
        Assert.Contains("権限", FailureText.Cause(new UnauthorizedAccessException()));
    }

    /// <summary>見当が付かない種類でも空にしない（画面の文が「〜できませんでした。」で切れないように）。</summary>
    [Fact]
    public void UnknownKindStillSaysSomething()
        => Assert.NotEmpty(FailureText.Cause(new InvalidOperationException("x")));
}
