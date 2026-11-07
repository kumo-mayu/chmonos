using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// バリエーションとアバターの結び付き（ユーザ判断 2026-11-06・docs/spec/conversion.md）。
/// アバターごとにバリエーションで売っている衣装：「ミナト用」「ソラ用」「リク用」「セット」。
/// 名簿は「作り物素体」（ミナト・ユウ）・「別の素体」（ソラ・ハル）と、素体の無いリク。
/// </summary>
public class VariationTargetsTests
{
    private const string ItemId = "9900882";

    private static AvatarRegistryEntry Avatar(string id, string name, string? baseName) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
        BaseName = baseName,
    };

    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            Avatar("9800301", "ミナト", "作り物素体"),
            Avatar("9800302", "ユウ", "作り物素体"),
            Avatar("9800311", "ソラ", "別の素体"),
            Avatar("9800312", "ハル", "別の素体"),
            Avatar("9800321", "リク", null),
        ],
        BaseGroups = [new AvatarBaseGroup { Name = "作り物素体" }, new AvatarBaseGroup { Name = "別の素体" }],
    };

    private static AvatarLink Link(string id, string name) => new()
    {
        AvatarItemId = id,
        Name = name,
        Source = AvatarLinkSource.Variation,
        Confirmed = true,
    };

    private static ItemRecord Outfit(Func<LocalBlock, LocalBlock>? change = null)
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        var local = item.Local with
        {
            Avatars = [Link("9800301", "ミナト"), Link("9800311", "ソラ"), Link("9800321", "リク")],
            AvatarsDetectedAt = DateTimeOffset.Now,
            Purchases = [new Purchase { VariationId = 1, NameSnapshot = "ミナト用", Price = 1500 }],
            DetectedVariationTargets =
            [
                new VariationTarget { VariationId = 1, Avatars = ["9800301"] },
                new VariationTarget { VariationId = 2, Avatars = ["9800311"] },
                new VariationTarget { VariationId = 3, Avatars = ["9800321"] },
            ],
        };
        return item with
        {
            Booth = item.Booth with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "ミナト用", Price = 1500 },
                    new BoothVariation { Id = 2, Name = "ソラ用", Price = 1500 },
                    new BoothVariation { Id = 3, Name = "リク用", Price = 1500 },
                    new BoothVariation { Id = 4, Name = "セット", Price = 3500 },
                ],
            },
            Local = change?.Invoke(local) ?? local,
        };
    }

    private static async Task<ItemViewModel> OpenPageAsync(TestApp app, ItemRecord item, Func<AppSettings, AppSettings>? settings = null)
    {
        await app.Store.Avatars.SaveAsync(Registry());
        if (settings is not null)
        {
            await app.ChangeSettingsAsync(settings);
        }

        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    private static async Task<EditViewModel> OpenEditAsync(TestApp app, ItemRecord item)
    {
        await app.Store.Avatars.SaveAsync(Registry());
        await app.AddItemAsync(item);
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        return Assert.IsType<EditViewModel>(main.CurrentViewModel);
    }

    private static string Faded(ItemViewModel page) => string.Join(" ", page.AvatarTiles
        .OfType<AvatarRow>()
        .Where(row => row.IsOtherVariation)
        .Select(row => row.Name)
        .Order(StringComparer.Ordinal));

    [Fact]
    public Task 商品ページでは_持っていないバリエーションの分の札を薄くし_吹き出しで言う() => TestApp.Run(async app =>
    {
        var page = await OpenPageAsync(app, Outfit());

        // ミナト用の向き先はミナト。素体経由と同じに仲間のユウへ広がるので、ユウは薄くしない
        Assert.Equal("ソラ ハル リク", Faded(page));
        Assert.StartsWith("持っていないバリエーション向けです。", page.AvatarTiles.OfType<AvatarRow>().Single(row => row.Name == "リク").SourceTooltip);
        Assert.DoesNotContain("持っていない", page.AvatarTiles.OfType<AvatarRow>().Single(row => row.Name == "ミナト").SourceTooltip);
    });

    [Fact]
    public Task 名前からの対応を使わない設定では_人が決めていない商品は薄くしない() => TestApp.Run(async app =>
    {
        var page = await OpenPageAsync(app, Outfit(), settings => settings with { UseDetectedVariationTargets = false });

        Assert.Equal(string.Empty, Faded(page));
    });

    [Fact]
    public Task 編集の画面では_全部の行に対応の欄を付け_名前から見つけた対応を確認待ちの札で出す() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app, Outfit(local => local with
        {
            VariationTargets = [new VariationTarget { VariationId = 1, Bases = ["作り物素体"] }],
        }));

        var minato = edit.Variations.Single(row => row.Name == "ミナト用").Targets!;
        Assert.Equal(["作り物素体（共通素体）"], minato.Chips.Select(chip => chip.Text));
        // ミナト用は名前ではミナト。人が素体に決めたので、ミナトは確認待ちの札として残るが効いていない
        Assert.False(Assert.Single(minato.Candidates).IsEffective);

        // 買っていない行にも付けられる。人が決めていなければ、名前から見つけた対応が効いていることを見せる
        var sora = edit.Variations.Single(row => row.Name == "ソラ用").Targets!;
        Assert.False(edit.Variations.Single(row => row.Name == "ソラ用").IsPurchased);
        var candidate = Assert.Single(sora.Candidates);
        Assert.Equal("ソラ", candidate.Text);
        Assert.True(candidate.IsEffective);
        Assert.StartsWith("名前から見つけました（未確認）", candidate.Tooltip);

        // 候補は対応アバター全部・素体・アバター。積んだ物は候補から外れる
        Assert.Equal("対応アバターすべて", minato.Suggestions[0]);
        Assert.DoesNotContain("作り物素体（共通素体）", minato.Suggestions);
        Assert.Contains("別の素体（共通素体）", minato.Suggestions);
        Assert.Contains("ユウ", minato.Suggestions);

        // 「セット」は名前から何も見つからない
        Assert.Empty(edit.Variations.Single(row => row.Name == "セット").Targets!.Candidates);
    });

    [Fact]
    public Task 編集の画面で積んだ対応と_確認待ちから確認した対応を_保存で書く() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app, Outfit());

        edit.Variations.Single(row => row.Name == "セット").Targets!.AddCommand.Execute("対応アバターすべて");
        var sora = edit.Variations.Single(row => row.Name == "ソラ用").Targets!;
        sora.Candidates.Single().ConfirmCommand.Execute(null);
        Assert.Empty(sora.Candidates);
        Assert.Equal(["ソラ"], sora.Chips.Select(chip => chip.Text));
        var riku = edit.Variations.Single(row => row.Name == "リク用").Targets!;
        riku.AddCommand.Execute("リク");
        riku.Chips.Single().RemoveCommand.Execute(null);

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync(ItemId))!.Local.VariationTargets;
        Assert.Equal([2L, 4L], saved.Select(target => target.VariationId).Order());
        Assert.Equal(["9800311"], saved.Single(target => target.VariationId == 2).Avatars);
        Assert.True(saved.Single(target => target.VariationId == 4).All);
    });

    /// <summary>
    /// 触らずに保存したら、向き先を書き込みに入れない（変えた項目だけを書く決まり。並びが変わると、開いている間に別の画面が書いた値を古い写しで戻す）。
    /// 確かめは、保存の前に記録の向き先を書き換えておき、保存の後もそのまま残っていること
    /// </summary>
    [Fact]
    public Task 向き先を触らずに保存しても_記録の向き先を書き戻さない() => TestApp.Run(async app =>
    {
        var edit = await OpenEditAsync(app, Outfit(local => local with
        {
            VariationTargets =
            [
                new VariationTarget { VariationId = 1, Bases = ["作り物素体"], Avatars = ["9800321"] },
                new VariationTarget { VariationId = 4, All = true },
            ],
        }));

        var changed = new VariationTarget { VariationId = 2, Avatars = ["9800312"] };
        await app.Store.Items.ChangeLocalAsync(ItemId, local => local with { VariationTargets = [changed] }, LocalOwners.VariationTargets);

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync(ItemId))!.Local.VariationTargets;
        Assert.Equal([2L], saved.Select(target => target.VariationId));
    });

    [Fact]
    public Task バリエーションが1つの商品には_対応の欄を付けない() => TestApp.Run(async app =>
    {
        var single = Outfit();
        single = single with { Booth = single.Booth with { Variations = [single.Booth.Variations[0]] } };

        var edit = await OpenEditAsync(app, single);

        Assert.All(edit.Variations, row => Assert.False(row.HasTargets));
    });
}
