using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 人が足す変換の組（<c>conversion-pairs.json</c>・<c>docs/spec/conversion.md</c>「人が足す組」・ユーザ判断 2026-11-20）。
/// 名前・ID はすべて作り物。
/// </summary>
public sealed class ConversionPairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bam-pairs-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_dir, "store");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static readonly AvatarRegistry Registry = new()
    {
        Entries =
        [
            new AvatarRegistryEntry { ItemId = "9000001", DisplayName = "タユタ", Category = "3Dキャラクター" },
            new AvatarRegistryEntry { ItemId = "9000002", DisplayName = "ミナモ", Category = "3Dキャラクター" },
            new AvatarRegistryEntry { ItemId = "9000003", DisplayName = "ツムギ", Category = "3Dキャラクター", BaseName = "作り物素体" },
            new AvatarRegistryEntry { ItemId = "9000004", DisplayName = "ヒナタ", Category = "3Dキャラクター", BaseName = "作り物素体" },
            new AvatarRegistryEntry { ItemId = "9000005", DisplayName = "コハク", Category = "3Dキャラクター" },
        ],
        BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }],
    };

    private static AvatarCompatibilityIndex Compat => AvatarCompatibilityIndex.Build(Registry);

    private static LocalBlock Clothing(IReadOnlyList<string>? avatars = null, IReadOnlyList<string>? bases = null) => new()
    {
        Avatars = [.. (avatars ?? []).Select(avatar => new AvatarLink { AvatarItemId = avatar, Source = AvatarLinkSource.SupportSection })],
        AvatarBases = [.. (bases ?? []).Select(baseName => new AvatarBaseLink { BaseName = baseName })],
    };

    private static ConversionPair Pair(ConversionEnd from, ConversionEnd to) => new() { From = from, To = to };

    private static IReadOnlyDictionary<string, ConversionRoute> Routes(LocalBlock clothing, IReadOnlyList<ItemRecord> items, params ConversionPair[] pairs)
        => ConversionIndex.Build(Compat, items, includeUnconfirmed: false, includeDetectedVariations: true, pairs).Resolve(clothing);

    // ---- 変換経由の計算 ----

    [Fact]
    public void プロファイルが1つも無くても_組だけで先のアバターが変換経由になる()
    {
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));

        var routes = Routes(Clothing(avatars: ["9000001"]), [], pair);

        Assert.Equal(["9000002"], routes.Keys);
        Assert.Equal(new ConversionRoute("9000001", 0, pair), routes["9000002"]);
    }

    [Fact]
    public void 組は元から先への1方向で_逆向きには使わない()
    {
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));

        Assert.Empty(Routes(Clothing(avatars: ["9000002"]), [], pair));
    }

    [Fact]
    public void 先を素体にした組は仲間全員に効き_既に対応しているアバターは除く()
    {
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Base("作り物素体"));

        Assert.Equal(["9000003", "9000004"], Routes(Clothing(avatars: ["9000001"]), [], pair).Keys.Order());
        // ツムギに直接対応していれば、ツムギとヒナタは素体経由で既に並ぶ
        Assert.Empty(Routes(Clothing(avatars: ["9000001", "9000003"]), [], pair));
    }

    [Fact]
    public void 元を素体にした組は_素体経由で対応したアバターからも使える()
    {
        // 素体経由は元の側に1回まで：衣装は素体に対応 → 仲間のツムギ・ヒナタが S に入り、その素体からの組で先へ
        var pair = Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar("9000005"));

        var routes = Routes(Clothing(bases: ["作り物素体"]), [], pair);

        Assert.Equal(["9000005"], routes.Keys);
        Assert.Equal(1, routes["9000005"].OtherSources);
    }

    [Fact]
    public void 変換は1回まで_組から組へはつながない()
    {
        var first = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));
        var second = Pair(ConversionEnd.Avatar("9000002"), ConversionEnd.Avatar("9000005"));

        Assert.Equal(["9000002"], Routes(Clothing(avatars: ["9000001"]), [], first, second).Keys);
    }

    [Fact]
    public void 組の先がプロファイルの順方向を持っていても_プロファイルへはつながない()
    {
        // 9000002 には順方向・逆方向のプロファイルがあるが、元の 9000001 に逆方向が無いのでプロファイルの道は無い
        var profiles = new[]
        {
            new ItemRecord
            {
                Id = "9200002",
                Booth = new BoothBlock(),
                Local = new LocalBlock
                {
                    Conversion = new ConversionSetting
                    {
                        Targets = [new ConversionTarget { AvatarItemId = "9000005", Forward = true, Confirmed = true }, new ConversionTarget { AvatarItemId = "9000002", Reverse = true, Confirmed = true }],
                    },
                },
            },
        };
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));

        Assert.Equal(["9000002"], Routes(Clothing(avatars: ["9000001"]), profiles, pair).Keys);
    }

    [Fact]
    public void プロファイルと組の両方で届くアバターは_プロファイルの道筋を言う()
    {
        var profiles = new[]
        {
            new ItemRecord
            {
                Id = "9200001",
                Booth = new BoothBlock(),
                Local = new LocalBlock
                {
                    Conversion = new ConversionSetting
                    {
                        Targets = [new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true }, new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true }],
                    },
                },
            },
        };
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));

        Assert.Null(Routes(Clothing(avatars: ["9000001"]), profiles, pair)["9000002"].Pair);
    }

    [Fact]
    public void 元のアバターに対応していない衣装には_組は効かない()
    {
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));

        Assert.Empty(Routes(Clothing(avatars: ["9000005"]), [], pair));
    }

    /// <summary>この機能の主な相手はプロファイルを持たない人。検索の「変換で使える物も含める」も組だけで効く</summary>
    [Fact]
    public void プロファイルが無くても_検索の変換で使える物も含めるは組で当たる()
    {
        var clothing = new ItemRecord { Id = "9300001", Booth = new BoothBlock(), Local = Clothing(avatars: ["9000001"]) };
        var context = new SearchModuleContext(() => Compat, ModificationUsage.Empty, RecentTimes.Empty, null, DateTimeOffset.Now)
        {
            Items = [clothing],
            ConversionPairs = [Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"))],
        };

        bool Hits(bool viaConversion) => SearchModuleConditions.From(new SearchModuleState
        {
            Kind = "Avatar",
            Items = ["avatar:9000002"],
            Flag = true,
            Toggles = new Dictionary<string, bool> { [SearchModuleConditions.ViaConversionToggle] = viaConversion },
        })!.Matches(clothing, context);

        Assert.False(Hits(viaConversion: false));
        Assert.True(Hits(viaConversion: true));
    }

    // ---- 足す・外す・読み替え ----

    [Fact]
    public void 同じ元と先の組は重ねて足さず_元と先が同じ組は足さない()
    {
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Base("作り物素体"));
        var file = ConversionPairs.Add(new ConversionPairFile(), pair);

        file = ConversionPairs.Add(file, pair with { Tool = "別のツール" });
        file = ConversionPairs.Add(file, Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Base("作り物素体")));

        Assert.Equal([pair], file.Pairs);
        Assert.Empty(ConversionPairs.Remove(file, Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Base("作り物素体"))).Pairs);
    }

    [Fact]
    public void 商品IDを移すと組の端も移り_重なった組と自分を指す組は落とす()
    {
        var file = new ConversionPairFile
        {
            Pairs =
            [
                Pair(ConversionEnd.Avatar("local-1"), ConversionEnd.Avatar("9000002")),
                Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002")),
                Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("local-1")),
                Pair(ConversionEnd.Avatar("9000005"), ConversionEnd.Avatar("9000002")),
            ],
        };

        var renamed = ConversionPairs.RenameItem(file, "local-1", "9000001");

        Assert.NotNull(renamed);
        Assert.Equal(
            [Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002")), Pair(ConversionEnd.Avatar("9000005"), ConversionEnd.Avatar("9000002"))],
            renamed!.Pairs);
        Assert.Null(ConversionPairs.RenameItem(renamed, "local-9", "9000009"));
    }

    [Fact]
    public void 素体を改名すると組も追い_消すと素体を指す組を落とす()
    {
        var file = new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar("9000005")), Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"))],
        };

        Assert.Equal(ConversionEnd.Base("新しい素体"), ConversionPairs.RenameBase(file, "作り物素体", "新しい素体")!.Pairs[0].From);
        Assert.Equal([file.Pairs[1]], ConversionPairs.RenameBase(file, "作り物素体", null)!.Pairs);
    }

    [Fact]
    public void メモは同じ元と先の組だけを書き替え_ツールは残し_空白は持たない()
    {
        var target = Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar("9000002")) with { Tool = "作り物のツール" };
        var other = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"));
        var file = new ConversionPairFile { Pairs = [target, other] };

        var written = ConversionPairs.SetMemo(file, Pair(ConversionEnd.Base("作り物素体"), ConversionEnd.Avatar("9000002")), "  袖は直す ");

        Assert.Equal([target with { Memo = "袖は直す" }, other], written.Pairs);
        Assert.Null(ConversionPairs.SetMemo(written, target, "   ").Pairs[0].Memo);

        // 外された組はメモだけで生き返らせない
        Assert.Same(file, ConversionPairs.SetMemo(file, Pair(ConversionEnd.Avatar("9000009"), ConversionEnd.Avatar("9000002")), "メモ"));
    }

    // ---- 保存 ----

    [Fact]
    public async Task メモを書いた組を読み戻せ_メモの無い前の形も読める()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        File.WriteAllText(paths.ConversionPairsFile, """{ "formatVersion": 1, "pairs": [ { "from": { "baseName": "作り物素体" }, "to": { "avatarItemId": "9000002" } } ] }""");
        var store = new DataStore(paths);
        var handler = new CommandHandler(null!, null!) { ConversionPairs = store.ConversionPairs };
        var pair = Assert.Single(store.ConversionPairs.Load().Pairs);
        Assert.Null(pair.Memo);

        await handler.ExecuteAsync(new UiCommand.ChangeConversionPairs(file => ConversionPairs.SetMemo(file, pair, "袖は直す")));

        Assert.Equal("袖は直す", Assert.Single(new DataStore(paths).ConversionPairs.Load().Pairs).Memo);
        Assert.Contains("\"memo\"", File.ReadAllText(paths.ConversionPairsFile), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 書いた組を読み戻せ_欠けた配列と端はnullでなく空として読む()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var handler = new CommandHandler(null!, null!) { ConversionPairs = store.ConversionPairs };
        var pair = Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Base("作り物素体")) with { Tool = "作り物のツール", Memo = "袖は直す" };

        await handler.ExecuteAsync(new UiCommand.ChangeConversionPairs(file => ConversionPairs.Add(file, pair)));

        Assert.Equal([pair], new DataStore(paths).ConversionPairs.Load().Pairs);
        var text = File.ReadAllText(paths.ConversionPairsFile);
        Assert.Contains("\"formatVersion\"", text, StringComparison.Ordinal);

        File.WriteAllText(paths.ConversionPairsFile, """{ "pairs": null }""");
        Assert.Empty(new DataStore(paths).ConversionPairs.Load().Pairs);
        File.WriteAllText(paths.ConversionPairsFile, """{ "pairs": [ { "from": null, "to": { "avatarItemId": "9000002" } } ] }""");
        var broken = Assert.Single(new DataStore(paths).ConversionPairs.Load().Pairs);
        Assert.True(broken.IsEmpty);
    }

    [Fact]
    public async Task 組は引越し_書き出しと戻す_形式を上げる前の控えに付いていく()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        await new DataStore(paths).ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Avatar("9000002"))],
        });
        // 戻すのは商品か設定が入った物だけ（バックアップと見分けるため）
        File.WriteAllText(paths.SettingsFile, "{}");
        var name = Path.GetFileName(paths.ConversionPairsFile);

        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Root, zip, includeImages: false);
        var restored = Path.Combine(_dir, "restored");
        BackupArchive.Restore(zip, restored);
        Assert.True(File.Exists(Path.Combine(restored, name)));

        Assert.True(File.Exists(Path.Combine(StoreFormat.Backup(Root, fromVersion: 1, DateTimeOffset.Now), name)));

        var moved = Path.Combine(_dir, "moved");
        Assert.True(StoreMover.Move(Root, moved).Succeeded);
        Assert.True(File.Exists(Path.Combine(moved, name)));
    }

    [Fact]
    public async Task 素体の改名と削除は_保存した組も書き換える()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var service = new AvatarService(store);
        await service.AddBaseAsync("作り物素体");
        await store.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs = [Pair(ConversionEnd.Avatar("9000001"), ConversionEnd.Base("作り物素体"))],
        });

        await service.RenameBaseAsync("作り物素体", "新しい素体");
        Assert.Equal(ConversionEnd.Base("新しい素体"), Assert.Single(store.ConversionPairs.Load().Pairs).To);

        await service.DeleteBaseAsync("新しい素体");
        Assert.Empty(store.ConversionPairs.Load().Pairs);
    }
}
