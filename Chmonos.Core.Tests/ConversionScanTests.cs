using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 手元のプロファイルを見つけ、つなぎ先を確認待ちとして書く見回り（<see cref="ConversionScan"/>）。名前・ID・パスは作り物。
/// </summary>
public sealed class ConversionScanTests : IDisposable
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly UnityPackagePathStore _packages;

    public ConversionScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-conv-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _packages = new UnityPackagePathStore(paths);
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

    private async Task SaveProfileItemAsync(string id, ConversionSetting? conversion = null)
    {
        _packages.Add(Hash, "Profile.unitypackage",
        [
            new UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2kohaku.json"),
            new UnityPackageAsset("00000000000000000000000000000002", "Assets/OutfitRetargetingSystem/Editor/config_kohaku2template.json"),
        ]);
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物のアバター", Category = new BoothCategory { Id = 1, Name = "3Dキャラクター" } },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = Hash,
                        Paths = [@"D:\fake\profile.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
                Conversion = conversion,
            },
        });
    }

    [Fact]
    public async Task 手元のプロファイルから_つなぎ先を確認待ちで書く()
    {
        await SaveProfileItemAsync("9400001");

        var written = await new ConversionScan(_store, _packages).RunAsync();

        Assert.Equal(["9400001"], written);
        var conversion = (await _store.Items.LoadAsync("9400001"))!.Local.Conversion;
        Assert.Equal(
            [new ConversionTarget { AvatarItemId = "9400001", Forward = true, Reverse = true, Slug = "kohaku", Confirmed = false }],
            conversion!.Targets);
        Assert.Equal(ConversionMode.Auto, conversion.Mode);
    }

    [Fact]
    public async Task 人が確認したつなぎ先は残し_同じプロファイルは推し直さない()
    {
        var mine = new ConversionTarget { BaseName = "作り物素体", Forward = true, Slug = "kohaku", Confirmed = true };
        await SaveProfileItemAsync("9400002", new ConversionSetting { Mode = ConversionMode.Profile, Targets = [mine] });

        var written = await new ConversionScan(_store, _packages).RunAsync();

        Assert.Empty(written);
        Assert.Equal([mine], (await _store.Items.LoadAsync("9400002"))!.Local.Conversion!.Targets);
    }

    [Fact]
    public async Task 二度目は変わらなければ書かない()
    {
        await SaveProfileItemAsync("9400003");
        var scan = new ConversionScan(_store, _packages);

        Assert.Single(await scan.RunAsync());
        Assert.Empty(await scan.RunAsync());
    }

    [Fact]
    public void プロファイルが無くなった商品は_確認待ちの値を消し_確認した値は残す()
    {
        var pending = new ConversionTarget { AvatarItemId = "9000001", Forward = true, Slug = "a" };
        var mine = new ConversionTarget { AvatarItemId = "9000002", Reverse = true, Slug = "b", Confirmed = true };

        Assert.Null(ConversionScan.Apply(new ConversionSetting { Targets = [pending] }, []));
        Assert.Equal([mine], ConversionScan.Apply(new ConversionSetting { Targets = [pending, mine] }, [])!.Targets);
    }
}
