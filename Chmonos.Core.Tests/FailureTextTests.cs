using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

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

    /// <summary>
    /// 同期のアプリ（OneDrive など）のファイルをダウンロードできなかったときは、それと分かる言い方にする（2026-10-07）。
    /// 番号は OneDrive を止めて実際に出た「クラウド ファイル プロバイダーが実行されていません」（362）と、ネットが無いとき（388）
    /// </summary>
    [Theory]
    [InlineData(362)]
    [InlineData(388)]
    public void CloudFileErrorSaysTheSyncAppOrNetwork(int code)
    {
        var cloud = new IOException("cloud", unchecked((int)0x80070000) | code);

        var text = FailureText.Cause(cloud);

        Assert.Contains("同期のアプリ", text);
        Assert.DoesNotContain("別のアプリが開いて", text);
    }

    /// <summary>同じ下16ビットでも Windows の番号でなければ、クラウドの話にしない。</summary>
    [Fact]
    public void SameLowBitsFromAnotherFacilityIsNotCloud()
    {
        var other = new IOException("other", unchecked((int)0x80040000) | 362);

        Assert.DoesNotContain("同期のアプリ", FailureText.Cause(other));
    }

    /// <summary>見当が付かない種類でも空にしない（画面の文が「〜できませんでした。」で切れないように）。</summary>
    [Fact]
    public void UnknownKindStillSaysSomething()
        => Assert.NotEmpty(FailureText.Cause(new InvalidOperationException("x")));
}
