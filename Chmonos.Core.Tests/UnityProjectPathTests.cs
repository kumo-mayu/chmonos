using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// Unity プロジェクトの場所の正規化（Unity の API-GAPS 3-11：Unity は <c>/</c> 区切りで返すので、そのまま渡すと検索の条件が0件だった）。
/// 場所は作り物
/// </summary>
public sealed class UnityProjectPathTests
{
    [Theory]
    [InlineData(@"D:\unity\Sample", @"D:\unity\Sample")]
    [InlineData("D:/unity/Sample", @"D:\unity\Sample")]
    [InlineData(@"D:\unity\Sample\", @"D:\unity\Sample")]
    [InlineData("D:/unity/Sample/", @"D:\unity\Sample")]
    [InlineData(@"  D:\unity\other\..\Sample  ", @"D:\unity\Sample")]
    [InlineData(@"D:\", @"D:\")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void 区切りの向きと末尾の区切りを揃える(string? path, string? expected)
        => Assert.Equal(expected, UnityProjectPath.Normalize(path));

    /// <summary>画面の条件も API の条件も同じ索引（<see cref="ModificationUsage.InProject"/>）を引くので、どちらの綴りでも当たる。</summary>
    [Fact]
    public void 検索の条件は_綴りだけ違う同じ場所に当たる()
    {
        var usage = ModificationUsage.From(
        [
            new ModificationRecord
            {
                Id = "mod-0000b001", AvatarItemId = "9000001", Name = "作り物の改変", UnityProject = "D:/unity/Sample/",
                Members = [new ModificationMember { ItemId = "1000001" }],
            },
            new ModificationRecord
            {
                Id = "mod-0000b002", AvatarItemId = "9000001", Name = "作り物の改変2", UnityProject = @"d:\unity\sample",
                Members = [new ModificationMember { ItemId = "1000002" }],
            },
        ]);

        Assert.Single(usage.ItemIdsByProject);
        foreach (var key in new[] { @"D:\unity\Sample", "D:/unity/Sample", @"D:\unity\Sample\", "d:/UNITY/sample/" })
        {
            Assert.True(usage.InProject(key, "1000001"), key);
            Assert.True(usage.InProject(key, "1000002"), key);
        }

        Assert.False(usage.InProject(@"D:\unity\Other", "1000001"));
    }
}
