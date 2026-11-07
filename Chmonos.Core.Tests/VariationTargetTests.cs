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

    /// <summary>種類の分からない購入でも、人が何のアバター向けかを付けていれば、持っている分に数える（ユーザ指摘 2026-11-07）</summary>
    [Fact]
    public void バリエーションを選ばない購入に付けた向き先も_持っている分に数える()
    {
        var local = Outfit() with
        {
            Purchases = [new Purchase { VariationId = null }],
            VariationTargets = [new VariationTarget { VariationId = null, Avatars = ["kuuta"] }],
        };
        Assert.Equal(["kipfel", "mamehinata"], Index().UnownedVariationAvatars(local, includeDetected: true).Order());

        // 付けていなければ前と同じく数えない（持っているバリエーションが分からない）
        Assert.Empty(Index().UnownedVariationAvatars(local with { VariationTargets = [] }, includeDetected: true));

        // 贈った購入は持っている分に入れない
        var given = local with { Purchases = [new Purchase { VariationId = null, Kind = PurchaseKind.Given }] };
        Assert.Empty(Index().UnownedVariationAvatars(given, includeDetected: true));
    }

    /// <summary>名前から見つけた物を全部外した行は人が決めた行なので、検出の行を使わない（向き先が無いので、持っていれば見分けない）</summary>
    [Fact]
    public void 名前から見つけた物を外しただけの行も_検出に勝つ()
    {
        var local = Outfit(Mame) with
        {
            VariationTargets = [new VariationTarget { VariationId = Mame, RejectedAvatars = ["kipfel"] }],
        };

        Assert.Empty(Index().UnownedVariationAvatars(local, includeDetected: true));
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

    /// <summary>
    /// 1つのバリエーション名に複数のアバター（「〇〇・△△対応」）。区切りの「・」は Unicode のカタカナの区画に入るので、
    /// 前後がカタカナでない名前だけ当てる決まりで両方とも落ちていた（2026-11-07・友人の写しで見つけた）。「先行リリース」の中の「リース」は今までどおり当てない
    /// </summary>
    [Fact]
    public void 中黒でつないだ複数のアバター名を_両方とも拾う()
    {
        var index = AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "a", DisplayName = "シフォン", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "b", DisplayName = "ショコラ", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "c", DisplayName = "リース", Category = "3Dキャラクター" },
            ],
        });

        var found = AvatarDetector.ScanVariations(index, [], [(1, "シフォン・ショコラ対応"), (2, "先行リリース版"), (3, "ショコラ用")]);

        Assert.Equal(["a", "b"], found.Single(target => target.VariationId == 1).Avatars.Order());
        Assert.DoesNotContain(found, target => target.VariationId == 2);
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

    /// <summary>「フルセット」などアバター名の無い全部入りの行は、対応アバター全部向け（ユーザ指示 2026-11-07）。「セット」だけは当てない</summary>
    [Fact]
    public void フルセットの行は_対応アバター全部向けにする()
    {
        var index = AvatarNameIndex.Build(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "kuuta", DisplayName = "くうた", Category = "3Dキャラクター" }],
        });

        var found = AvatarDetector.ScanVariations(index, [],
        [
            (1, "くうた用"),
            (2, "✦ Full set"),
            (3, "フルパック -full pack-"),
            (4, "All 20 + Hair"),
            (5, "くうた用 Fullset"),
            (6, "カジュアルセット"),
            (7, "Small"),
        ]);

        Assert.Equal([2L, 3L, 4L], found.Where(target => target.All).Select(target => target.VariationId));
        Assert.Equal(["kuuta"], found.Single(target => target.VariationId == 5).Avatars);
        Assert.False(found.Single(target => target.VariationId == 5).All);
        Assert.DoesNotContain(found, target => target.VariationId is 6 or 7);
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
