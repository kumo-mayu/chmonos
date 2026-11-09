using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 変換で使える対応アバター（<c>docs/spec/conversion.md</c>）の Core：プロファイルの見分け・つなぎ先の推し方・変換経由の計算・データの形。
/// 名前・ID はすべて作り物。
/// </summary>
public class ConversionTests
{
    // ---- プロファイルの見分け ----

    [Fact]
    public void 順と逆のファイルから_slugごとに向きを見分ける()
    {
        var found = ProfileFiles.Scan(
        [
            "Assets/OutfitRetargetingSystem/Editor/config_template2alpha.json",
            "Assets/OutfitRetargetingSystem/Editor/config_alpha2template.json",
            "Assets/OutfitRetargetingSystem/Editor/config_template2Beta.json",
            "Assets/OutfitRetargetingSystem/Editor/sub/config_gamma2template.json",
            "Assets/OutfitRetargetingSystem/Editor/posediff_template_to_gamma.json",
            "Assets/OutfitRetargetingSystem/Editor/avatar_data_alpha.json",
            "Assets/Other/config_template2delta.json",
        ]);

        Assert.Equal(
            [new ProfileSlug("alpha", true, true), new ProfileSlug("Beta", true, false), new ProfileSlug("gamma", false, true)],
            found);
    }

    [Fact]
    public void 大文字小文字の違う同じslugは1つにまとめ_ひな形どうしは数えない()
    {
        var found = ProfileFiles.Scan(
        [
            "Assets/OutfitRetargetingSystem/Editor/config_template2Alpha.json",
            "Assets/outfitretargetingsystem/editor/config_alpha2template.json",
            "Assets/OutfitRetargetingSystem/Editor/config_template2template.json",
        ]);

        Assert.Equal([new ProfileSlug("Alpha", true, true)], found);
    }

    [Fact]
    public void 入る先の一番上から_読む要る包みかを見分ける()
    {
        Assert.True(ProfileFiles.MayHoldProfiles(["Assets/Shop", "Assets/OutfitRetargetingSystem"]));
        Assert.False(ProfileFiles.MayHoldProfiles(["Assets/Shop"]));
    }

    // ---- 名前の照らし ----

    [Theory]
    [InlineData("tayuta", "タユタ")]
    [InlineData("rusk", "ラスク（Rusk）")]
    [InlineData("beryl4", "Beryl")]
    [InlineData("FakeBody", "Fake Body")]
    public void slugは_英字の語とかなに読んだ形で名前と合う(string slug, string name)
        => Assert.True(ConversionNames.Matches(ConversionNames.SlugKeys(slug), ConversionNames.NameKeys([name])));

    [Theory]
    [InlineData("tayuta", "ミナモ")]
    [InlineData("coco", "ココとポポ")]
    public void 違う名前とは合わない(string slug, string name)
        => Assert.False(ConversionNames.Matches(ConversionNames.SlugKeys(slug), ConversionNames.NameKeys([name])));

    // ---- つなぎ先の推し方 ----

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
        BaseGroups = [new AvatarBaseGroup { Name = "作り物素体", Aliases = [new AvatarAlias { Text = "FakeBody" }] }],
    };

    private static ItemRecord Item(string id, string? category = null, IReadOnlyList<string>? avatars = null, IReadOnlyList<string>? bases = null, ConversionSetting? conversion = null)
        => new()
        {
            Id = id,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物", Category = category is null ? null : new BoothCategory { Id = 1, Name = category } },
            Local = new LocalBlock
            {
                Avatars = [.. (avatars ?? []).Select(avatar => new AvatarLink { AvatarItemId = avatar, Source = AvatarLinkSource.SupportSection })],
                AvatarBases = [.. (bases ?? []).Select(baseName => new AvatarBaseLink { BaseName = baseName })],
                Conversion = conversion,
            },
        };

    private static AvatarCompatibilityIndex Compat => AvatarCompatibilityIndex.Build(Registry);

    [Fact]
    public void アバター本体の商品は_その商品自身につなぐ()
    {
        var suggestion = ConversionInference.Suggest(Item("9000005", category: "3Dキャラクター"), [new ProfileSlug("kohaku", true, true)], Registry, Compat).Single();

        Assert.False(suggestion.Conflicting);
        Assert.Equal(new ConversionTarget { AvatarItemId = "9000005", Forward = true, Reverse = true, Slug = "kohaku", Confirmed = false }, suggestion.Target);
    }

    [Fact]
    public void 対応アバターが1体なら_そのアバターにつなぎ_確認待ちにする()
    {
        var suggestion = ConversionInference.Suggest(Item("9100001", avatars: ["9000002"]), [new ProfileSlug("xyz", true, false)], Registry, Compat).Single();

        Assert.Equal("9000002", suggestion.Target?.AvatarItemId);
        Assert.False(suggestion.Target!.Confirmed);
    }

    [Fact]
    public void 対応アバターが2体以上なら_slugと名前の合う1体だけに絞る()
    {
        var suggestions = ConversionInference.Suggest(
            Item("9100002", avatars: ["9000001", "9000002"]),
            [new ProfileSlug("tayuta", true, false), new ProfileSlug("minamo", true, false), new ProfileSlug("unknown", true, false)],
            Registry,
            Compat);

        Assert.Equal(["9000001", "9000002", null], suggestions.Select(suggestion => suggestion.Target?.AvatarItemId));
        Assert.All(suggestions, suggestion => Assert.False(suggestion.Conflicting));
    }

    [Fact]
    public void 素体の手掛かりと_その素体の仲間のアバターが当たったら_素体用にまとめる()
    {
        var suggestion = ConversionInference.Suggest(
            Item("9100003", avatars: ["9000003"], bases: ["作り物素体"]),
            [new ProfileSlug("fakebody", true, true)],
            Registry,
            Compat).Single();

        Assert.False(suggestion.Conflicting);
        Assert.Equal("作り物素体", suggestion.Target?.BaseName);
        Assert.Null(suggestion.Target?.AvatarItemId);
    }

    [Fact]
    public void 手掛かりが食い違えば推さずに印を付ける()
    {
        // アバター本体の商品が、ほかのアバター1体を対応に挙げている（同梱か、別のアバターのプロファイルか分からない）
        var suggestion = ConversionInference.Suggest(
            Item("9000005", category: "3Dキャラクター", avatars: ["9000002"]),
            [new ProfileSlug("xyz", true, false)],
            Registry,
            Compat).Single();

        Assert.True(suggestion.Conflicting);
        Assert.Null(suggestion.Target);
    }

    // ---- 変換経由の計算 ----

    private static ConversionSetting Profile(params ConversionTarget[] targets) => new() { Targets = targets };

    [Fact]
    public void 元のアバターに逆_先のアバターに順があれば_先のアバターは変換経由で使える()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true })),
            Item("9200002", conversion: Profile(new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true })),
        };
        var index = ConversionIndex.Build(Compat, items, includeUnconfirmed: false, includeDetectedVariations: true);
        var clothing = Item("9300001", avatars: ["9000001"]).Local;

        var routes = index.Resolve(clothing);

        Assert.Equal(["9000002"], routes.Keys);
        Assert.Equal(new ConversionRoute("9000001", 0), routes["9000002"]);
    }

    [Fact]
    public void 人が外したつなぎ先は_変換経由に使わない()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true })),
            Item("9200002", conversion: Profile(new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true, Rejected = true })),
        };

        var routes = ConversionIndex.Build(Compat, items, includeUnconfirmed: true, includeDetectedVariations: true)
            .Resolve(Item("9300001", avatars: ["9000001"]).Local);

        Assert.Empty(routes);
    }

    [Fact]
    public void 素体につないだ順は仲間全員に効き_アバターにつないだ順はそのアバターだけ()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true })),
            Item("9200003", conversion: Profile(new ConversionTarget { BaseName = "作り物素体", Forward = true, Confirmed = true })),
        };
        var viaBase = ConversionIndex.Build(Compat, items, false, true).Resolve(Item("9300001", avatars: ["9000001"]).Local);
        Assert.Equal(["9000003", "9000004"], viaBase.Keys.Order());

        var avatarOnly = ConversionIndex.Build(
            Compat,
            [items[0], Item("9200004", conversion: Profile(new ConversionTarget { AvatarItemId = "9000003", Forward = true, Confirmed = true }))],
            false,
            true).Resolve(Item("9300001", avatars: ["9000001"]).Local);
        Assert.Equal(["9000003"], avatarOnly.Keys);
    }

    [Fact]
    public void 直接対応と素体経由で既に並ぶアバターは_変換経由に出さない()
    {
        var items = new[]
        {
            Item("9200005", conversion: Profile(new ConversionTarget { BaseName = "作り物素体", Forward = true, Reverse = true, Confirmed = true })),
        };

        // 衣装は素体の仲間の1体に対応：素体経由で仲間全員に並ぶので、変換経由は空
        var routes = ConversionIndex.Build(Compat, items, false, true).Resolve(Item("9300002", avatars: ["9000003"]).Local);

        Assert.Empty(routes);
    }

    [Fact]
    public void 確認していないつなぎ先は_設定で含めたときだけ使う()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = false })),
            Item("9200002", conversion: Profile(new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true })),
        };
        var clothing = Item("9300001", avatars: ["9000001"]).Local;

        Assert.Empty(ConversionIndex.Build(Compat, items, includeUnconfirmed: false, true).Resolve(clothing));
        Assert.Equal(["9000002"], ConversionIndex.Build(Compat, items, includeUnconfirmed: true, true).Resolve(clothing).Keys);
    }

    [Fact]
    public void プロファイルとして扱わないにした商品は使わない()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true })),
            Item("9200002", conversion: Profile(new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true }) with { Mode = ConversionMode.NotProfile }),
        };

        Assert.Empty(ConversionIndex.Build(Compat, items, false, true).Resolve(Item("9300001", avatars: ["9000001"]).Local));
    }

    [Fact]
    public void 元にできるアバターが何体もあれば_1体とほかの数を返す()
    {
        var items = new[]
        {
            Item("9200001", conversion: Profile(
                new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true },
                new ConversionTarget { AvatarItemId = "9000002", Reverse = true, Confirmed = true },
                new ConversionTarget { AvatarItemId = "9000005", Forward = true, Confirmed = true })),
        };

        var routes = ConversionIndex.Build(Compat, items, false, true).Resolve(Item("9300003", avatars: ["9000001", "9000002"]).Local);

        Assert.Equal(new ConversionRoute("9000001", 1), routes["9000005"]);
    }

    // ---- 検索の「変換で使える物も含める」 ----

    [Fact]
    public void 検索の_変換で使える物も含める_を入れたときだけ_変換経由のアバターで当たる()
    {
        var clothing = Item("9300001", avatars: ["9000001"]);
        var items = new[]
        {
            clothing,
            Item("9200001", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true })),
            Item("9200003", conversion: Profile(new ConversionTarget { BaseName = "作り物素体", Forward = true, Confirmed = true })),
        };
        var context = new SearchModuleContext(() => Compat, ModificationUsage.Empty, RecentTimes.Empty, null, DateTimeOffset.Now) { Items = items };

        bool Hits(string key, bool viaConversion) => SearchModuleConditions.From(new SearchModuleState
        {
            Kind = "Avatar",
            Items = [key],
            Flag = true,
            Toggles = new Dictionary<string, bool> { [SearchModuleConditions.ViaConversionToggle] = viaConversion },
        })!.Matches(clothing, context);

        Assert.False(Hits("avatar:9000003", viaConversion: false));
        Assert.True(Hits("avatar:9000003", viaConversion: true));
        Assert.True(Hits("base:作り物素体", viaConversion: true));
        Assert.False(Hits("avatar:9000002", viaConversion: true));
    }

    // ---- 検索の条件「プロファイル」（ユーザ判断 2026-11-09・案B：変換元は逆方向、変換先は順方向） ----

    private static bool ProfileHits(
        ItemRecord item,
        IReadOnlyList<string> from,
        IReadOnlyList<string> to,
        bool viaBase = true,
        bool includeUnconfirmed = true,
        bool counterpart = false)
    {
        var context = new SearchModuleContext(() => Compat, ModificationUsage.Empty, RecentTimes.Empty, null, DateTimeOffset.Now) { Items = [item] };
        var condition = SearchModuleConditions.From(new SearchModuleState
        {
            Kind = "Profile",
            Items = [.. from.Select(key => SearchModuleConditions.SourceKey + key), .. to.Select(key => SearchModuleConditions.DestinationKey + key)],
            Flag = viaBase,
            Toggles = new Dictionary<string, bool>
            {
                [SearchModuleConditions.IncludeUnconfirmedToggle] = includeUnconfirmed,
                [SearchModuleConditions.CounterpartToggle] = counterpart,
            },
        })!;
        condition.Prepare(context);
        return condition.IsActive && condition.Matches(item, context);
    }

    private static readonly ItemRecord ForwardToTsumugi = Item("9200011", conversion: Profile(new ConversionTarget { AvatarItemId = "9000003", Forward = true, Confirmed = true }));
    private static readonly ItemRecord ReverseFromTayuta = Item("9200012", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Reverse = true, Confirmed = true }));
    private static readonly ItemRecord BothForBase = Item("9200013", conversion: Profile(new ConversionTarget { BaseName = "作り物素体", Forward = true, Reverse = true, Confirmed = true }));

    [Fact]
    public void 条件プロファイルは_変換先には順方向_変換元には逆方向で当たる()
    {
        // 変換先だけ：ツムギに着せる順方向（ツムギ自身と、ツムギの素体の物）
        Assert.True(ProfileHits(ForwardToTsumugi, [], ["avatar:9000003"]));
        Assert.True(ProfileHits(BothForBase, [], ["avatar:9000003"]));
        Assert.False(ProfileHits(ReverseFromTayuta, [], ["avatar:9000003"]));

        // 変換元だけ：タユタ用の衣装を持っていく逆方向
        Assert.True(ProfileHits(ReverseFromTayuta, ["avatar:9000001"], []));
        Assert.False(ProfileHits(ForwardToTsumugi, ["avatar:9000001"], []));

        // 逆方向しか無い物は、そのアバターを変換先に入れても当たらない
        var reverseOnlyTsumugi = Item("9200014", conversion: Profile(new ConversionTarget { AvatarItemId = "9000003", Reverse = true, Confirmed = true }));
        Assert.False(ProfileHits(reverseOnlyTsumugi, [], ["avatar:9000003"]));
    }

    [Fact]
    public void 条件プロファイルは_両方入れると_変換元の逆方向と変換先の順方向のどちらかに当たる物を出す()
    {
        Assert.True(ProfileHits(ReverseFromTayuta, ["avatar:9000001"], ["avatar:9000003"]));
        Assert.True(ProfileHits(ForwardToTsumugi, ["avatar:9000001"], ["avatar:9000003"]));
        Assert.False(ProfileHits(Item("9200015", conversion: Profile(new ConversionTarget { AvatarItemId = "9000002", Forward = true, Confirmed = true })),
            ["avatar:9000001"], ["avatar:9000003"]));
    }

    [Fact]
    public void 条件プロファイルは_素体経由を切ると素体のプロファイルを当てない()
    {
        Assert.False(ProfileHits(BothForBase, [], ["avatar:9000003"], viaBase: false));
        Assert.True(ProfileHits(BothForBase, [], ["base:作り物素体"], viaBase: false));
        Assert.True(ProfileHits(ForwardToTsumugi, [], ["base:作り物素体"]));
        Assert.False(ProfileHits(ForwardToTsumugi, [], ["base:作り物素体"], viaBase: false));
    }

    [Fact]
    public void 条件プロファイルは_もう一方の候補も出すと_空の側を持っている物全部として反対の向きも出す()
    {
        // 変換先だけ入れたとき：元として使える（逆方向を持つ）物も出す。場面「どのアバター用の衣装でも B に」
        Assert.False(ProfileHits(ReverseFromTayuta, [], ["avatar:9000003"]));
        Assert.True(ProfileHits(ReverseFromTayuta, [], ["avatar:9000003"], counterpart: true));

        // 変換元だけ入れたとき：先として使える（順方向を持つ）物も出す。場面「A 用の衣装をどれに着せられるか」
        Assert.True(ProfileHits(ForwardToTsumugi, ["avatar:9000001"], [], counterpart: true));
    }

    [Fact]
    public void 条件プロファイルは_確認待ちを既定で含め_切ると確認済みだけ_外した物はどちらでも当てない()
    {
        var suggested = Item("9200021", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Forward = true }));
        var rejected = Item("9200022", conversion: Profile(new ConversionTarget { AvatarItemId = "9000001", Forward = true, Confirmed = true, Rejected = true }));

        Assert.True(ProfileHits(suggested, [], ["avatar:9000001"]));
        Assert.False(ProfileHits(suggested, [], ["avatar:9000001"], includeUnconfirmed: false));
        Assert.False(ProfileHits(rejected, [], ["avatar:9000001"]));
    }

    [Fact]
    public void 条件プロファイルは_何も選ばないと_プロファイルの商品すべてに絞り_扱わない物は除く()
    {
        var notProfile = Item("9200032", conversion: new ConversionSetting
        {
            Mode = ConversionMode.NotProfile,
            Targets = [new ConversionTarget { AvatarItemId = "9000001", Forward = true }],
        });

        Assert.True(ProfileHits(ForwardToTsumugi, [], []));
        Assert.False(ProfileHits(notProfile, [], []));
        Assert.False(ProfileHits(Item("9300031", avatars: ["9000001"]), [], []));
    }

    // ---- データの形 ----

    [Fact]
    public void 既定の商品には変換扱いを書き出さず_決めた値は読める形で書く()
    {
        Assert.DoesNotContain("conversion", JsonSerializer.Serialize(new LocalBlock(), JsonStore.Options), StringComparison.Ordinal);

        var local = new LocalBlock
        {
            Conversion = new ConversionSetting
            {
                Mode = ConversionMode.Profile,
                Targets = [new ConversionTarget { BaseName = "作り物素体", Forward = true, Slug = "fakebody", Confirmed = true }],
            },
        };
        var json = JsonSerializer.Serialize(local, JsonStore.Options);

        Assert.Contains("\"mode\": \"profile\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("avatarItemId", json, StringComparison.Ordinal);
        Assert.Equal(local.Conversion, JsonSerializer.Deserialize<LocalBlock>(json, JsonStore.Options)!.Conversion with { Targets = local.Conversion.Targets });
        Assert.Equal(local.Conversion.Targets, JsonSerializer.Deserialize<LocalBlock>(json, JsonStore.Options)!.Conversion!.Targets);
    }

    [Fact]
    public void 変換扱いは_名指しした保存だけが書く()
    {
        var current = new LocalBlock();
        var incoming = new LocalBlock { Conversion = new ConversionSetting { Mode = ConversionMode.NotProfile } };

        Assert.Null(LocalFields.Merge(current, incoming, LocalOwners.EditScreen).Conversion);
        Assert.Equal(ConversionMode.NotProfile, LocalFields.Merge(current, incoming, LocalOwners.Conversion).Conversion?.Mode);
    }
}
