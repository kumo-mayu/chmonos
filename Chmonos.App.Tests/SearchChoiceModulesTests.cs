using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件の監修（ユーザ判断 2026-10-06・メモ82〜84）で決めた、選ぶ形と候補から積む形の条件。
/// 選択肢の並び・既定・当たる商品を確かめる。商品の番号は作り物（9900000番台）。
/// </summary>
public class SearchChoiceModulesTests
{
    private static void Pick(ChoiceModule module, string key) => module.Selected = module.Options.Single(option => option.Key == key);

    private static List<string> Shown(SearchViewModel search) => [.. search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal)];

    private static BoothVariation Variation(long id, int price) => new() { Id = id, Price = price };

    [Fact]
    public Task 選ぶ形の条件は_どれも両方を並びの最後に置き_先頭が既定() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        foreach (var kind in SearchModuleCatalog.All.Select(info => info.Kind))
        {
            if (SearchModuleMenuTests.Add(search, kind) is not ChoiceModule choice)
            {
                continue;
            }

            var labels = choice.Options.Select(option => option.Label).ToList();
            if (labels.Contains("両方"))
            {
                Assert.True(labels[^1] == "両方", $"{kind} の「両方」が最後にない：{string.Join("／", labels)}");
            }

            Assert.Equal(choice.Options[0], choice.Selected);
        }
    });

    [Fact]
    public Task 所持_非表示_対応アバターの確認の選択肢() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;

        Assert.Equal(["所持のみ", "未所持のみ", "両方"], ((ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Owned)).Options.Select(option => option.Label));
        Assert.Equal(["非表示のみ", "表示している商品のみ", "両方"], ((ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Hidden)).Options.Select(option => option.Label));
        Assert.Equal(["確認待ちあり", "確認待ちなし", "両方"], ((ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.AvatarUnconfirmed)).Options.Select(option => option.Label));
    });

    private static async Task<SearchViewModel> StartEndOfSaleAsync(TestApp app)
    {
        var selling = Make.Item("9900001", "公開中の商品");
        await app.AddItemAsync(selling);
        await app.AddItemAsync(selling with { Id = "9900002", Booth = selling.Booth with { IsEndOfSale = true } });
        await app.AddItemAsync(selling with { Id = "9900003", Local = selling.Local with { IsDelisted = true, ConsecutiveNotFoundCount = 3 } });

        // 見つからないのが続いているが、まだ非公開と確定していない（確かめ中）
        await app.AddItemAsync(selling with { Id = "9900004", Local = selling.Local with { ConsecutiveNotFoundCount = 1 } });

        // 仮IDの商品（BOOTHに無い商品）。印が付いていても BOOTH の状態とは見ない
        await app.AddItemAsync(Make.Item(LocalItemId.For("abcdef0123456789"), "BOOTHに無い商品") is var local
            ? local with { Local = local.Local with { IsDelisted = true } }
            : local);
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task 販売終了は_本物のIDの商品だけを分け_仮IDの商品は両方のときだけ出す() => TestApp.Run(async app =>
    {
        var search = await StartEndOfSaleAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.EndOfSale);

        Assert.Equal(["販売終了・非公開", "公開中", "両方"], module.Options.Select(option => option.Label));
        Assert.False(module.HasFlag);

        Pick(module, "ended");
        Assert.Equal(["9900002", "9900003"], Shown(search));

        Pick(module, "selling");
        Assert.Equal(["9900001", "9900004"], Shown(search));

        Pick(module, "both");
        Assert.Equal(5, search.ListItems.Count);
        Assert.Equal([2, 2, 5], module.Options.Select(option => option.Count));
    });

    private static async Task<SearchViewModel> StartGiftAsync(TestApp app)
    {
        var item = Make.Item("9900011", "作り物");
        Purchase Bought(PurchaseKind kind) => new() { Kind = kind, Price = 500 };
        await app.AddItemAsync(item with { Local = item.Local with { Purchases = [Bought(PurchaseKind.Given)] } });
        await app.AddItemAsync(item with { Id = "9900012", Local = item.Local with { Purchases = [Bought(PurchaseKind.Received)] } });
        await app.AddItemAsync(item with { Id = "9900013", Local = item.Local with { Purchases = [Bought(PurchaseKind.ForSelf)] } });
        await app.AddItemAsync(item with { Id = "9900014", Local = item.Local with { Purchases = [Bought(PurchaseKind.Received), Bought(PurchaseKind.ForSelf)] } });
        await app.AddItemAsync(item with { Id = "9900015" });
        return (await app.StartAsync()).Search;
    }

    [Fact]
    public Task ギフトは3つで_両方を持たず_既定はギフトした() => TestApp.Run(async app =>
    {
        var search = await StartGiftAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Gift);

        Assert.Equal(["ギフトした", "ギフトされた", "購入した"], module.Options.Select(option => option.Label));
        Assert.Equal(["9900011"], Shown(search));

        Pick(module, "received");
        Assert.Equal(["9900012", "9900014"], Shown(search));

        // 貰って自分でも買った商品は、どちらにも出る
        Pick(module, "bought");
        Assert.Equal(["9900013", "9900014"], Shown(search));
    });

    [Fact]
    public Task ギフトの購入記録の無い商品も含めるは_既定で切り_どの選択肢でも足せる() => TestApp.Run(async app =>
    {
        var search = await StartGiftAsync(app);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Gift);

        Assert.False(module.Flag);
        foreach (var key in new[] { "given", "received", "bought" })
        {
            Pick(module, key);
            Assert.True(module.HasFlag);
            module.Flag = true;
            Assert.Contains("9900015", Shown(search));
            module.Flag = false;
            Assert.DoesNotContain("9900015", Shown(search));
        }

        module.Flag = true;
        Assert.Equal("ギフト：購入した・購入記録の無い商品も含める", module.SummaryText);
    });

    [Fact]
    public Task 有料無料は_BOOTHのバリエーションの価格だけで3つに分け_最後に両方を持つ() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900021", "作り物");
        await app.AddItemAsync(item with { Booth = item.Booth with { Variations = [Variation(1, 0)] } });
        await app.AddItemAsync(item with { Id = "9900022", Booth = item.Booth with { Variations = [Variation(1, 0), Variation(2, 500)] } });

        // 払った額は見ない（無料版だけの商品に支援で払っても「すべて無料」）
        await app.AddItemAsync(item with
        {
            Id = "9900023",
            Booth = item.Booth with { Variations = [Variation(1, 1000)] },
        });
        await app.AddItemAsync(item with
        {
            Id = "9900024",
            Booth = item.Booth with { Variations = [Variation(1, 0)] },
            Local = item.Local with { Purchases = [new Purchase { Price = 300 }] },
        });
        await app.AddItemAsync(item with { Id = "9900025" });
        var search = (await app.StartAsync()).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.FreePaid);

        Assert.Equal(["すべて無料", "無料版と有料版がある", "有料のみ", "両方"], module.Options.Select(option => option.Label));
        Assert.Equal(["9900021", "9900024"], Shown(search));

        Pick(module, "mixed");
        Assert.Equal(["9900022"], Shown(search));

        Pick(module, "paid");
        Assert.Equal(["9900023"], Shown(search));

        // バリエーションの分からない商品はどれにも入らない。両方は絞らない
        Assert.Equal([2, 1, 1, 5], module.Options.Select(option => option.Count));
        Pick(module, "both");
        Assert.Equal(["9900021", "9900022", "9900023", "9900024", "9900025"], Shown(search));
        Assert.False(module.IsActive);

        // 条件をクリアすると、ほかの選ぶ形と同じく「両方」に戻る（条件ごと切らない）
        Pick(module, "free");
        module.Clear();
        Assert.Equal("both", module.SelectedKey);
        Assert.True(module.IsEnabled);
    });

    [Fact]
    public Task 有料無料の価格が設定されていない商品も表示は_既定で切り_両方の間は隠す() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900026", "作り物");
        await app.AddItemAsync(item with { Booth = item.Booth with { Variations = [Variation(1, 0)] } });
        await app.AddItemAsync(item with { Id = "9900027", Booth = item.Booth with { Variations = [Variation(1, 500)] } });
        await app.AddItemAsync(item with { Id = "9900028" });
        var search = (await app.StartAsync()).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.FreePaid);

        Assert.True(module.HasFlag);
        Assert.False(module.Flag);
        Assert.Equal("価格が設定されていない商品も表示", module.FlagLabel);
        Assert.Equal(["9900026"], Shown(search));

        // どの選択肢でも、バリエーションの価格が1つも無い商品を足す
        module.Flag = true;
        Assert.Equal(["9900026", "9900028"], Shown(search));
        Assert.Equal("有料・無料：すべて無料・価格が設定されていない商品も表示", module.SummaryText);
        Pick(module, "paid");
        Assert.Equal(["9900027", "9900028"], Shown(search));

        // 両方は何も絞らないので、切り替えを隠す
        Pick(module, "both");
        Assert.False(module.HasFlag);
    });

    [Fact]
    public Task 所持のすべてのファイルが見つからなければ未所持とするは_既定で切り_入れると未所持に数える() => TestApp.Run(async app =>
    {
        var gone = Make.File(@"D:\files\gone.zip") with { MissingSince = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) };
        await app.AddItemAsync(Make.Item("9900081", "一部だけ見つからない").WithFiles(Make.File(@"D:\files\here.zip"), gone));
        await app.AddItemAsync(Make.Item("9900082", "すべて見つからない").WithFiles(gone with { Hash = Make.HashOf("other") }));
        await app.AddItemAsync(Make.Item("9900083", "見つかる"));
        await app.AddItemAsync(Make.Item("9900084", "持っていない").WithFiles());
        var search = (await app.StartAsync()).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Owned);

        Assert.True(module.HasFlag);
        Assert.False(module.Flag);
        Assert.Equal("すべてのファイルが見つからなければ未所持とする", module.FlagLabel);

        // 既定は所持の定義のまま（ファイルの記録があれば所持）
        Assert.Equal(["9900081", "9900082", "9900083"], Shown(search));
        Pick(module, "unowned");
        Assert.Equal(["9900084"], Shown(search));

        // 入れると、すべて見つからない商品を未所持に数える（所持のみから外れ、未所持のみに入る）
        module.Flag = true;
        Assert.Equal(["9900082", "9900084"], Shown(search));
        Assert.Equal("所持：未所持のみ・すべて見つからない商品は未所持", module.SummaryText);
        Pick(module, "owned");
        Assert.Equal(["9900081", "9900083"], Shown(search));

        // 件数も同じ数え方。両方は何も絞らないので隠す
        Assert.Equal([2, 2, 4], module.Options.Select(option => option.Count));
        Pick(module, "both");
        Assert.False(module.HasFlag);
        Assert.True(module.Save().Flag);
    });

    [Fact]
    public Task 見つからないファイルの未所持も含めるは_既定で含め_切るとすべて見つからない商品を外す() => TestApp.Run(async app =>
    {
        var gone = Make.File(@"D:\files\gone.zip") with { MissingSince = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero) };
        await app.AddItemAsync(Make.Item("9900031", "一部だけ見つからない").WithFiles(Make.File(@"D:\files\here.zip"), gone));
        await app.AddItemAsync(Make.Item("9900032", "すべて見つからない").WithFiles(gone with { Hash = Make.HashOf("other") }));
        await app.AddItemAsync(Make.Item("9900033", "見つかる"));
        var search = (await app.StartAsync()).Search;
        var module = (ChoiceModule)SearchModuleMenuTests.Add(search, SearchModuleKind.MissingFile);

        Assert.True(module.HasFlag);
        Assert.True(module.Flag);
        Assert.Equal("未所持も含める", module.FlagLabel);
        Assert.Equal(["9900031", "9900032"], Shown(search));

        module.Flag = false;
        Assert.Equal(["9900031"], Shown(search));
        Assert.Equal("見つからないファイル：見つからないファイルがある・すべて見つからない商品を除く", module.SummaryText);

        // 見つからないファイルが無い商品・両方には効かないので、切り替えを隠す
        Pick(module, "none");
        Assert.False(module.HasFlag);
        Assert.Equal(["9900033"], Shown(search));
    });

    [Fact]
    public Task カテゴリとBOOTHタグは_ORだけで_ANDの切り替えを出さない() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900041", "作り物");
        await app.AddItemAsync(item with { Booth = item.Booth with { Tags = ["タグA"] } });
        await app.AddItemAsync(item with { Id = "9900042", Booth = item.Booth with { Tags = ["タグB"] } });
        var search = (await app.StartAsync()).Search;

        foreach (var kind in new[] { SearchModuleKind.Category, SearchModuleKind.BoothTag })
        {
            var module = (ListModule)SearchModuleMenuTests.Add(search, kind);
            module.AddKey("A");
            module.AddKey("B");
            Assert.False(module.AllowsAnd);
            Assert.False(module.ShowsMatchMode);
            module.RemoveCommand!.Execute(null);
        }

        var tags = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.BoothTag);
        tags.AddKey("タグA");
        tags.AddKey("タグB");
        tags.MatchAll = true;
        Assert.Equal(["9900041", "9900042"], Shown(search));
        Assert.False(tags.Save().MatchAll);
    });

    [Fact]
    public void ANDとORの言い方は1か所にある()
    {
        Assert.Equal("すべてを満たす商品のみ（AND）", MatchModeText.All);
        Assert.Equal("いずれかを満たす商品（OR）", MatchModeText.Any);
        Assert.Equal("すべての大分類を満たす商品のみ（AND）", new UserTagModule().TopMatchAllText);
    }

    [Fact]
    public void ユーザータグのANDの中の小分類なしは_色を変える印が立つ()
    {
        var row = new UserTagModule();
        row.SetMasters([("衣装", (IReadOnlyList<string>)["上着", "靴"])]);
        var top = row.AddTop("衣装")!;
        top.AddCommand.Execute(UserTagTopRow.NoSubText);
        top.AddCommand.Execute("上着");

        Assert.False(top.HasNoSubConflict);
        Assert.All(top.Chips, chip => Assert.False(chip.IsConflict));

        top.MatchAll = true;
        Assert.True(top.HasNoSubConflict);
        Assert.True(top.Chips.Single(chip => chip.Text == UserTagTopRow.NoSubText).IsConflict);
        Assert.False(top.Chips.Single(chip => chip.Text == "上着").IsConflict);
        Assert.Contains("AND", top.Chips[0].ToolTipText);

        // 小分類を外して「小分類なし」だけになれば矛盾しない
        top.Chips.Single(chip => chip.Text == "上着").RemoveCommand!.Execute(null);
        Assert.False(top.Chips.Single().IsConflict);
    }

    [Fact]
    public Task ショップの候補は_お気に入りを先に区切って並べ_チェックの文はすべて表示() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900051", "作り物A", shop: "aaa-shop"));
        await app.AddItemAsync(Make.Item("9900052", "作り物B", shop: "bbb-shop"));
        await app.AddItemAsync(Make.Item("9900053", "作り物C", shop: "ccc-shop"));
        await app.Store.ShopNotes.SaveAsync([new ShopNoteRecord { Subdomain = "ccc-shop", IsFavorite = true }]);
        var search = (await app.StartAsync()).Search;
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Shop);

        Assert.Equal(["ccc-shop（ccc-shop）", "aaa-shop（aaa-shop）", "bbb-shop（bbb-shop）"], module.Suggestions);
        var rows = SuggestBox.Arrange(module.Suggestions.ToList(), string.Empty, 0, module.InfoSelector);
        Assert.Equal([false, true, false], rows.Select(row => row.DividerAbove));
        Assert.StartsWith("お気に入りのショップはすべて表示", module.IncludeText);

        // ショップ画面で星を変えたら並びも変わる
        search.NoteShopNotesChanged([new ShopNoteRecord { Subdomain = "bbb-shop", IsFavorite = true }]);
        Assert.Equal("bbb-shop（bbb-shop）", module.Suggestions[0]);
    });

    [Fact]
    public Task 対応アバターの札は吹き出しに絵を添え_ほかの条件の札は添えない() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;
        var avatar = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Avatar);
        var category = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Category);
        avatar.AddKey("avatar:9900061", text: "作り物のアバター（9900061）");
        category.AddKey("衣装");

        Assert.Equal("名前、ID、素体で絞り込む", avatar.Placeholder);
        Assert.True(avatar.Flag);
        Assert.True(avatar.Chips.Single().HasIconSource);
        Assert.False(category.Chips.Single().HasIconSource);

        // 札そのものに絵を出すのは改変の札だけ。対応アバターの札は吹き出しだけの今の形（ユーザ判断 2026-10-06）
        Assert.False(avatar.Chips.Single().ShowsIcon);
        Assert.False(category.Chips.Single().ShowsIcon);
    });

    [Fact]
    public void ユーザータグは_大分類も小分類も全部選んだ後も欄を出したまま()
    {
        var module = new UserTagModule();
        Assert.False(module.ShowsInput);
        module.SetMasters([("衣装", (IReadOnlyList<string>)["上着"])]);
        Assert.True(module.ShowsInput);

        var top = module.AddTop("衣装")!;
        top.AddCommand.Execute(UserTagTopRow.NoSubText);
        top.AddCommand.Execute("上着");

        // 候補は尽きても、欄は消さない（消すと下の枠が上へずれる・ユーザ指摘 2026-10-06）
        Assert.Empty(module.Suggestions);
        Assert.Empty(top.Suggestions);
        Assert.True(module.ShowsInput);
        Assert.True(top.ShowsInput);
        Assert.False(module.IsMasterEmpty);
    }

    [Fact]
    public Task 編集状況は複数置け_同じ種類どうしはANDで結ぶ() => TestApp.Run(async app =>
    {
        var item = Make.Item("9900071", "作り物");
        await app.AddItemAsync(item);
        await app.AddItemAsync(item with { Id = "9900072", Local = item.Local with { Memo = "メモあり" } });
        var search = (await app.StartAsync()).Search;

        Assert.True(SearchModuleCatalog.Of(SearchModuleKind.Unedited).AllowsMany);
        var tags = (UneditedModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Unedited);
        var memo = (UneditedModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Unedited);
        Assert.NotSame(tags, memo);

        // 2つ目は「メモが未入力」だけを見る
        memo.Fields.Single(field => field.Field == Core.Services.EditField.Memo).IsOn = true;
        memo.Fields.Single(field => field.Field == Core.Services.EditField.UserTags).IsOn = false;

        Assert.Equal(["9900071"], Shown(search));
        Assert.Equal("SearchModule.Unedited-2.Field.Memo", memo.Fields.Single(field => field.Field == Core.Services.EditField.Memo).AutomationId);
    });

    [Fact]
    public Task ファイルの場所とUnityプロジェクトは_候補と札の長い文の間を省き_ほかは末尾を切る() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;
        var path = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Path);
        var project = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.UnityProject);
        var category = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Category);
        path.AddKey(@"D:\作り物\とても長い名前のフォルダ\さらに奥のフォルダ\最後のフォルダ");
        category.AddKey("衣装");

        Assert.True(path.TrimsMiddle);
        Assert.True(project.TrimsMiddle);
        Assert.False(category.TrimsMiddle);
        Assert.True(path.Chips.Single().TrimsMiddle);
        Assert.False(category.Chips.Single().TrimsMiddle);

        // 札は頭のドライブと最後のフォルダ名を残し、間を「…」にする。吹き出しは全文
        var chip = path.Chips.Single();
        Assert.StartsWith(@"D:\", chip.DisplayText);
        Assert.EndsWith("…最後のフォルダ", chip.DisplayText);
        Assert.True(chip.DisplayText.Length <= 18);
        Assert.Equal(chip.Text, chip.ToolTipText);
        Assert.Equal("衣装", category.Chips.Single().DisplayText);
    });
}
