using System.IO;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;

namespace Chmonos.App.Tests;

/// <summary>
/// 同梱の辞書などが見つからないまま起動したときの、主の窓の帯（2026-10-07）。
/// zip の中から直に開く・exe だけを取り出すと、検索の一部が黙って効かなかった。
/// </summary>
public class MissingAssetsBandTests
{
    [Fact]
    public void 空のフォルダでは同梱の物が全部無いと答える()
    {
        var folder = Directory.CreateTempSubdirectory("chmonos-assets-").FullName;
        try
        {
            Assert.Equal(BundledAssets.FileNames, BundledAssets.MissingIn(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void 全部そろっていれば無い物は無い()
    {
        var folder = Directory.CreateTempSubdirectory("chmonos-assets-").FullName;
        try
        {
            foreach (var name in BundledAssets.FileNames)
            {
                File.WriteAllText(Path.Combine(folder, name), string.Empty);
            }

            Assert.Empty(BundledAssets.MissingIn(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public Task 一つでも無ければ下の帯に開き直し方を出す() => TestApp.Run(async app =>
    {
        app.Services.FindMissingAssets = () => ["JMdict_e.gz"];
        var main = await app.StartAsync();

        Assert.True(main.HasMissingAssets);
        Assert.Equal(BundledAssets.MissingText, main.MissingAssetsText);
    });

    [Fact]
    public Task そろっていれば帯を出さない() => TestApp.Run(async app =>
    {
        app.Services.FindMissingAssets = () => [];
        var main = await app.StartAsync();

        Assert.False(main.HasMissingAssets);
        Assert.Equal(string.Empty, main.MissingAssetsText);
    });
}
