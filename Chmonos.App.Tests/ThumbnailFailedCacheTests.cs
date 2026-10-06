using System.IO;
using Chmonos.App.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 読めなかった画像の覚えも、画像と同じ予算の中で古い順に追い出す（外部の点検 2026-10-07）。
/// 前は大きさ0と数えたので予算を超えず、読めない画像を見て回るほど覚えだけが増え続けた
/// </summary>
public sealed class ThumbnailFailedCacheTests
{
    [Fact]
    public void 読めなかった覚えが増えても_予算の内に収まる()
    {
        var loader = new ThumbnailLoader(budgetMegabytes: 1);
        // 予算は最低でも16MBに切り上げられる。場所の名前を長くして1件を約2KBにし、2万件（約40MB）で予算を超えさせる
        var folder = Path.Combine(Path.GetTempPath(), "bam-thumb-missing-" + new string('x', 1000));

        // 無い場所は読めない画像として覚える
        const int tried = 20_000;
        for (var index = 0; index < tried; index++)
        {
            Assert.Null(loader.Load(Path.Combine(folder, $"missing-{index:00000}.png")));
        }

        Assert.InRange(loader.CachedImageCount, 1, tried / 2);
        Assert.InRange(loader.CachedBytes, 1, 16L * 1024 * 1024);
    }
}
