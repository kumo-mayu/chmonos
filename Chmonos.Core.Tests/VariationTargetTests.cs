using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// バリエーションとアバターの結び付き（<c>docs/spec/conversion.md</c>「バリエーションとアバターの結び付き」）。
/// アバターごとにバリエーションで売っている衣装で、持っていない分の対応アバターを見分ける。
/// 登録簿は <see cref="AvatarCompatibilityTests.Registry"/>（くうた・まめふれんずの2体・珍飯亭の2体）。
/// </summary>
public class VariationTargetTests
{
    private const long Kuuta = 1;
    private const long Mame = 2;
    private const long Body = 3;

    private static AvatarCompatibilityIndex Index() => AvatarCompatibilityIndex.Build(AvatarCompatibilityTests.Registry());

    /// <summary>くうた用・まめふれんず用（キプフェルの名前）・本体の3つのバリエーションを持つ衣装。</summary>
    private static LocalBlock Outfit(params long[] bought) => AvatarCompatibilityTests.Declaring("kuuta", "kipfel") with
    {
        DetectedVariationTargets =
        [
            new VariationTarget { VariationId = Kuuta, Avatars = ["kuuta"] },
            new VariationTarget { VariationId = Mame, Avatars = ["kipfel"] },
        ],
        Purchases = [.. bought.Select(id => new Purchase { VariationId = id })],
    };

    [Fact]
    public void くうた用だけを持っていれば_まめふれんずの分は持っていない側()
    {
        var unowned = Index().UnownedVariationAvatars(Outfit(Kuuta), includeDetected: true);

        // キプフェルの向き先は、素体経由と同じに仲間のまめひなたへ広がる
        Assert.Equal(["kipfel", "mamehinata"], unowned.Order());
    }

    [Fact]
    public void 持っているバリエーションが分からなければ_何も見分けない()
    {
        Assert.Empty(Index().UnownedVariationAvatars(Outfit(), includeDetected: true));
    }

    /// <summary>「本体」のように向き先の無い行を持っていると、何を持っているか言えない。薄くしすぎる側に倒さない</summary>
    [Fact]
    public void 向き先の無いバリエーションを持っていれば_何も見分けない()
    {
        Assert.Empty(Index().UnownedVariationAvatars(Outfit(Kuuta, Body), includeDetected: true));
    }

    [Fact]
    public void 贈った購入は持っている数に入れず_ファイルに付けたバリエーションは入れる()
    {
        var given = Outfit() with
        {
            Purchases =
            [
                new Purchase { VariationId = Kuuta },
                new Purchase { VariationId = Mame, Kind = PurchaseKind.Given },
            ],
        };
        Assert.Contains("kipfel", Index().UnownedVariationAvatars(given, includeDetected: true));

        var file = Outfit() with
        {
            LocalFiles = [new LocalFileRecord { Hash = "A", Paths = [@"D:\a.zip"], SizeBytes = 1, VariationId = Mame }],
        };
        Assert.Equal(["kuuta"], Index().UnownedVariationAvatars(file, includeDetected: true));
    }

    [Fact]
    public void 人が決めた向き先が_同じバリエーションの検出に勝つ()
    {
        // 名前ではくうた用でも、人が「全部入り」と決めた
        var local = Outfit(Kuuta) with { VariationTargets = [new VariationTarget { VariationId = Kuuta, All = true }] };

        Assert.Empty(Index().UnownedVariationAvatars(local, includeDetected: true));
    }

    [Fact]
    public void 検出を使わない設定では_人が決めた向き先だけで見る()
    {
        Assert.Empty(Index().UnownedVariationAvatars(Outfit(Kuuta), includeDetected: false));

        var decided = Outfit(Kuuta) with
        {
            VariationTargets =
            [
                new VariationTarget { VariationId = Kuuta, Avatars = ["kuuta"] },
                new VariationTarget { VariationId = Mame, Bases = ["まめふれんず"] },
            ],
        };
        Assert.Equal(["kipfel", "mamehinata"], Index().UnownedVariationAvatars(decided, includeDetected: false).Order());
    }

    [Fact]
    public void バリエーションが1つの商品は_バリエーションごとに分けない()
    {
        var index = AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "kuuta", DisplayName = "くうた", Category = "3Dキャラクター" }],
        });

        Assert.Empty(AvatarDetector.ScanVariations(index, [], [(1, "くうた用")]));
    }

    [Fact]
    public void バリエーションの名前ごとに_アバターと共通素体を引く()
    {
        var registry = new AvatarRegistry
        {
            BaseGroups = AvatarCompatibilityTests.Registry().BaseGroups,
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "kuuta", DisplayName = "くうた", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "kipfel", DisplayName = "キプフェル", Category = "3Dキャラクター", BaseName = "まめふれんず" },
            ],
        };
        var index = AvatarNameIndex.Build(registry);

        var found = AvatarDetector.ScanVariations(
            index, registry.BaseGroups, [(1, "✧くうた対応✧"), (2, "まめふれんず素体用"), (3, "本体")]);

        Assert.Equal([1L, 2L], found.Select(target => target.VariationId));
        Assert.Equal(["kuuta"], found[0].Avatars);
        Assert.Equal(["まめふれんず"], found[1].Bases);
    }

    /// <summary>検出が、バリエーションごとの当たりを書く。アバターでない商品（衣装）の名前に当たった物は落とす</summary>
    [Fact]
    public async Task 検出は_バリエーションごとの向き先を書く()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-vartargets-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new Storage.AppPaths(root);
            paths.EnsureCreated();
            var store = new Storage.DataStore(paths);
            await store.Avatars.SaveAsync(new AvatarRegistry
            {
                Entries =
                [
                    new AvatarRegistryEntry { ItemId = "1111", BoothName = "作り物のアバター「しなの」", DisplayName = "しなの", Category = "3Dキャラクター", CheckedAt = DateTimeOffset.Now },
                    new AvatarRegistryEntry { ItemId = "2222", BoothName = "作り物のアバター「マヌカ」", DisplayName = "マヌカ", Category = "3Dキャラクター", CheckedAt = DateTimeOffset.Now },
                    new AvatarRegistryEntry { ItemId = "3333", BoothName = "作り物の衣装「セーラー」", DisplayName = "セーラー", Category = "3D衣装", CheckedAt = DateTimeOffset.Now },
                ],
            });
            await store.Items.SaveAsync(new ItemRecord
            {
                Id = "999",
                Booth = new BoothBlock
                {
                    FetchedAt = DateTimeOffset.Now,
                    Name = "作り物の衣装",
                    Variations =
                    [
                        new BoothVariation { Id = 10, Name = "✧しなの対応✧" },
                        new BoothVariation { Id = 20, Name = "マヌカ用 セーラー" },
                        new BoothVariation { Id = 30, Name = "本体" },
                    ],
                },
                Local = new LocalBlock(),
            });

            // 前の版で検出した保存先（つい先ほど検出していても、版が古ければ起動時に1回だけ検出し直す）
            await store.Avatars.UpdateAsync(registry => new AvatarRegistry
            {
                DetectedAt = DateTimeOffset.Now,
                Entries = registry.Entries,
                BaseGroups = registry.BaseGroups,
            });
            var service = new AvatarService(store, new AppSettings());
            Assert.True(service.IsRedetectDue());

            await service.DetectAsync();

            Assert.Equal(AvatarService.DetectionRevision, store.Avatars.Load().DetectionRevision);
            Assert.False(service.IsRedetectDue());
            var targets = (await store.Items.LoadAsync("999"))!.Local.DetectedVariationTargets;
            Assert.Equal([10L, 20L], targets.Select(target => target.VariationId));
            Assert.Equal(["1111"], targets[0].Avatars);
            Assert.Equal(["2222"], targets[1].Avatars);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void 検索の_持っているバリエーションの分だけ_は持っていない分の商品を落とす()
    {
        var item = new ItemRecord { Id = "outfit", Booth = new BoothBlock { Name = "衣装" }, Local = Outfit(Kuuta) };
        var context = new SearchModuleContext(Index, ModificationUsage.Empty, RecentTimes.Empty, null, DateTimeOffset.Now);

        bool Hits(string key, bool ownedOnly) => SearchModuleConditions.From(new SearchModuleState
        {
            Kind = "Avatar",
            Items = [key],
            Flag = true,
            Toggles = new Dictionary<string, bool> { [SearchModuleConditions.OwnedVariationsToggle] = ownedOnly },
        })!.Matches(item, context);

        Assert.True(Hits("avatar:kipfel", ownedOnly: false));
        Assert.False(Hits("avatar:kipfel", ownedOnly: true));
        Assert.True(Hits("avatar:kuuta", ownedOnly: true));
        Assert.False(Hits("base:まめふれんず", ownedOnly: true));

        // 検出を使わない設定では見分けられないので、落とさない
        var manualOnly = new SearchModuleContext(Index, ModificationUsage.Empty, RecentTimes.Empty, null, DateTimeOffset.Now)
        {
            UseDetectedVariationTargets = false,
        };
        Assert.True(SearchModuleConditions.From(new SearchModuleState
        {
            Kind = "Avatar",
            Items = ["avatar:kipfel"],
            Toggles = new Dictionary<string, bool> { [SearchModuleConditions.OwnedVariationsToggle] = true },
        })!.Matches(item, manualOnly));
    }
}
