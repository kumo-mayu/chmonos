using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページ（編集画面も同じ部品）の対応アバター：共通素体経由の点線の札と、共通素体ごとのまとめ（ユーザ指示 2026-11-04）。
/// 名簿は「作り物素体」（ミナト・ユウ・カイ）・「別の素体」（ソラ・ハル）と、素体の無いリク。持っているのはミナトとハル。
/// </summary>
public class ItemAvatarsViaBaseTests
{
    private const string ItemId = "9900881";

    private static AvatarRegistryEntry Avatar(string id, string name, string? baseName, bool owned = false) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
        BaseName = baseName,
        IsOwnedManually = owned,
    };

    private static AvatarRegistry Registry(IReadOnlyList<string>? order = null) => new()
    {
        Entries =
        [
            Avatar("9800301", "ミナト", "作り物素体", owned: true),
            Avatar("9800302", "ユウ", "作り物素体"),
            Avatar("9800303", "カイ", "作り物素体"),
            Avatar("9800311", "ソラ", "別の素体"),
            Avatar("9800312", "ハル", "別の素体", owned: true),
            Avatar("9800321", "リク", null),
        ],
        BaseGroups =
        [
            new AvatarBaseGroup { Name = "作り物素体", MemberOrder = order },
            new AvatarBaseGroup { Name = "別の素体" },
        ],
    };

    private static AvatarLink Link(string id, string name, bool rejected = false, AvatarLinkSource source = AvatarLinkSource.SupportSection) => new()
    {
        AvatarItemId = id,
        Name = name,
        Source = source,
        Confirmed = source != AvatarLinkSource.H2Link,
        Rejected = rejected,
    };

    private static ItemRecord Item(IReadOnlyList<AvatarLink> links, bool declaresBase)
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        return item with
        {
            Local = item.Local with
            {
                Avatars = links,
                AvatarBases = declaresBase
                    ? [new AvatarBaseLink { BaseName = "作り物素体", Source = AvatarLinkSource.SupportSection, Confirmed = true }]
                    : [],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
    }

    private static async Task<ItemViewModel> OpenAsync(
        TestApp app,
        ItemRecord item,
        IReadOnlyList<string>? order = null,
        Func<AppSettings, AppSettings>? settings = null)
    {
        await app.Store.Avatars.SaveAsync(Registry(order));
        if (settings is not null)
        {
            await app.ChangeSettingsAsync(settings);
        }

        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    /// <summary>並んでいる札を文字にする。見出しは［］、素体経由は ~ を頭に付ける。</summary>
    private static string Shown(ItemViewModel page) => string.Join(" ", page.AvatarTiles
        .OfType<ChipTile>()
        .Where(tile => tile.IsShown)
        .Select(tile => tile switch
        {
            ChipGroupLabel label => $"［{label.Text}］",
            ViaBaseAvatarRow via => "~" + via.Name,
            AvatarRow row => row.Name,
            _ => null,
        })
        .OfType<string>());

    private static readonly AvatarLink[] Recorded = [Link("9800301", "ミナト"), Link("9800311", "ソラ"), Link("9800321", "リク")];

    [Fact]
    public Task 既定では_素体ごとにまとめ_素体経由を点線の札で並べる() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Item(Recorded, declaresBase: true));

        // 持っているアバターを含む素体が先（どちらも含むので名前の順）、素体の中は持っている物が先→名前の順、素体の無い物は最後
        Assert.Equal(
            "［共通素体「作り物素体」］ ミナト ~カイ ~ユウ ［共通素体「別の素体」］ ~ハル ソラ ［共通素体なし］ リク",
            Shown(page));
        Assert.Equal(new HashSet<string> { "カイ", "ユウ", "ハル" }, page.ViaBaseAvatars.Select(row => row.Name).ToHashSet());
        Assert.Equal("（3 体・素体経由 3 体）", page.AvatarsCountText);
        Assert.Equal("出品者が対応と書いているアバターです。点線は共通素体経由です。", page.AvatarSectionNote);
        Assert.Equal("共通素体「作り物素体」経由です。押すとこのアバターを開きます", page.ViaBaseAvatars.Single(row => row.Name == "カイ") is ViaBaseAvatarRow via ? via.ViaTooltip : null);
    });

    [Fact]
    public Task 素体の中は_人が並べた順にし_並べていない仲間は後ろ() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Item(Recorded, declaresBase: true), order: ["9800303", "9800301"]);

        Assert.StartsWith("［共通素体「作り物素体」］ ~カイ ミナト ~ユウ ［", Shown(page));
    });

    [Fact]
    public Task まとめない設定では_記録した札の後ろに素体経由を並べ_見出しを出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Item(Recorded, declaresBase: true), settings: s => s with { GroupAvatarsByBase = false });

        Assert.Equal("ミナト ソラ リク ~ハル ~カイ ~ユウ", Shown(page));
    });

    [Fact]
    public Task 素体経由を出さない設定では_記録した札だけをまとめる() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Item(Recorded, declaresBase: true), settings: s => s with { ShowAvatarsViaBase = false });

        Assert.Equal("［共通素体「作り物素体」］ ミナト ［共通素体「別の素体」］ ソラ ［共通素体なし］ リク", Shown(page));
        Assert.Empty(page.ViaBaseAvatars);
        Assert.Equal("出品者が対応と書いているアバターです。", page.AvatarSectionNote);
    });

    /// <summary>消したアバターは素体の兄弟として計算に戻ってくる。人が消した物を点線で出し直さない。</summary>
    [Fact]
    public Task 消したアバターと_確認待ちの札があるアバターは_素体経由に出さない() => TestApp.Run(async app =>
    {
        var links = new[]
        {
            Link("9800301", "ミナト"),
            Link("9800302", "ユウ", rejected: true),
            Link("9800303", "カイ", source: AvatarLinkSource.H2Link),
        };
        var page = await OpenAsync(app, Item(links, declaresBase: true));

        Assert.Empty(page.ViaBaseAvatars);
        Assert.DoesNotContain("ユウ", Shown(page));
        Assert.Single(page.AvatarTiles.OfType<AvatarRow>(), row => row.Name == "カイ");
    });

    /// <summary>見出しは札の間に混ぜるが、アバターではない。「残り n 体」と最初の 40 体に数えず、札の無いまとまりの見出しは出さない。</summary>
    [Fact]
    public void 多い商品では_見出しを数えずに40体で切り_切った後の見出しは出さない()
    {
        static AvatarRow Row(int index) => new() { ItemId = $"98{index:0000}", Name = $"アバター{index:00}", SourceText = "対応" };

        var first = Enumerable.Range(0, 30).Select(Row).ToList();
        var second = Enumerable.Range(30, 30).Select(Row).ToList();
        var tiles = new List<ChipTile>
        {
            new ChipGroupLabel { Text = "共通素体「A」", Members = first },
        };
        tiles.AddRange(first);
        var secondLabel = new ChipGroupLabel { Text = "共通素体「B」", Members = second };
        tiles.Add(secondLabel);
        tiles.AddRange(second);
        var strip = new ChipStrip<ChipTile>(tile => tile, "体", "対応アバター") { IsCounted = tile => tile is AvatarRow };

        strip.Reset(tiles, expanded: true, showAll: false);

        Assert.Equal(ChipLists.PreviewCount, strip.Tiles.OfType<AvatarRow>().Count(row => row.IsShown));
        Assert.Equal("残り 20 体を表示", strip.Tiles.OfType<ChipToggle>().Single().Text);
        Assert.True(secondLabel.IsShown);

        // 絞って B の札が1枚も当たらなければ、B の見出しは出さない
        strip.Filter(tile => tile is AvatarRow row && row.Name == "アバター00");
        Assert.False(secondLabel.IsShown);
    }

    [Fact]
    public Task 素体が1つも無い商品には_見出しを出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, Item([Link("9800321", "リク")], declaresBase: false));

        Assert.Equal("リク", Shown(page));
    });
}
