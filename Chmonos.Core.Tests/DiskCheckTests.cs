using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>手元の物が在るか（技術的負債 4-2）。画面から呼ぶので、どんなパスでも投げないこと。</summary>
public sealed class DiskCheckTests
{
    [Fact]
    public async Task 在る物と無い物を見分ける()
    {
        var folder = Path.GetTempPath();
        var file = Path.GetTempFileName();
        try
        {
            Assert.True(await DiskCheck.FolderExistsAsync(folder));
            Assert.True(await DiskCheck.FileExistsAsync(file));
            Assert.False(await DiskCheck.FileExistsAsync(folder));
            Assert.False(await DiskCheck.FolderExistsAsync(Path.Combine(folder, Guid.NewGuid().ToString("N"))));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\a\0b")]
    [InlineData(@"\\?\")]
    public void 読めないパスは無いと答え投げない(string? path)
    {
        Assert.False(DiskCheck.FileExists(path));
        Assert.False(DiskCheck.FolderExists(path));
    }
}
