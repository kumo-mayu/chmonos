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
    public async Task プロファイルごとに_入っているunitypackageとファイルの名前を返す()
    {
        await SaveProfileItemAsync("9400001");
        var item = (await _store.Items.LoadAsync("9400001"))!;

        var source = Assert.Single(new ConversionScan(_store, _packages).ProfileSourcesOf(item));

        Assert.Equal(new ProfileSource("kohaku", "Profile.unitypackage", "profile.zip"), source);
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

    // ---- プロファイルのファイルが見つからない商品が多いときの帯の知らせ（ユーザ判断 2026-11-09） ----

    /// <summary>変換ツールの置き場を含むが、中のファイルの名前を指定できる商品（ハッシュは商品ごとに変える）。</summary>
    private async Task SaveRootedItemAsync(string id, params string[] paths)
    {
        var hash = new string(id[^1], 64);
        _packages.Add(hash, "Profile.unitypackage",
            paths.Select((path, index) => new UnityPackageAsset($"{index + 1:D32}", path)).ToList());
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物のプロファイル" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [$@"D:\fake\{id}.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
            },
        });
    }

    private NotificationRecord? Alert()
        => (_store.Notifications.Load() ?? []).FirstOrDefault(record => record.Kind == NotificationKind.ProfileFilesUnreadable);

    [Fact]
    public async Task 置き場はあるのにプロファイルが見つからない商品が半分以上_3件以上なら_帯の知らせを出し_読めるようになれば消す()
    {
        // ファイル名の決まりが変わった形（config_*2*.json ではない名前）
        await SaveRootedItemAsync("9400011", "Assets/OutfitRetargetingSystem/Editor/profile_kohaku_forward.json");
        await SaveRootedItemAsync("9400012", "Assets/OutfitRetargetingSystem/Editor/profile_aoi_forward.json");
        await SaveRootedItemAsync("9400013", "Assets/OutfitRetargetingSystem/Editor/profile_momo_reverse.json");
        await SaveRootedItemAsync("9400014", "Assets/OutfitRetargetingSystem/Editor/config_template2kiki.json");

        await new ConversionScan(_store, _packages).RunAsync();

        var alert = Alert();
        Assert.NotNull(alert);
        Assert.False(alert.IsResolved);
        Assert.Equal("変換ツールのフォルダを含む 4 件のうち 3 件で、プロファイルのファイルが見つかりませんでした。", alert.Detail);

        // 読める名前に戻った（アプリの更新で決まりを直した）ら、帯は自分で消える
        await SaveRootedItemAsync("9400011", "Assets/OutfitRetargetingSystem/Editor/config_template2kohaku.json");
        await SaveRootedItemAsync("9400012", "Assets/OutfitRetargetingSystem/Editor/config_template2aoi.json");
        await new ConversionScan(_store, _packages).RunAsync();

        Assert.True(Alert()!.IsResolved);
    }

    [Fact]
    public async Task 見つからない商品が少なければ_帯の知らせを出さない()
    {
        // 変換ツールの本体のように、置き場はあってもプロファイルを含まない商品が1件あるだけ
        await SaveRootedItemAsync("9400021", "Assets/OutfitRetargetingSystem/Editor/Tool.cs");
        await SaveRootedItemAsync("9400022", "Assets/OutfitRetargetingSystem/Editor/config_template2aoi.json");

        await new ConversionScan(_store, _packages).RunAsync();

        Assert.Null(Alert());
    }
}
