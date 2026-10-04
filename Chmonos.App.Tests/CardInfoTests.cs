using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索のカードに出すユーザータグ・属性・払った額・対応の数（ユーザ判断 2026-10-04：案A の札と1行・案C の乗せたときの重ねとリストの列）。
/// 出す属性は設定で選び（既定は属性の管理の並びの上から）、並べ替えに使っている属性を先に出す。
/// </summary>
public class CardInfoTests
{
    private static readonly string[] Master = ["かわいい", "かっこいい", "質感", "軽さ"];

    private static ItemRecord Rated(
        string id = "1000001",
        IReadOnlyDictionary<string, int>? attributes = null,
        IReadOnlyList<UserTagAssignment>? tags = null,
        int avatars = 0,
        IReadOnlyList<Purchase>? purchases = null)
    {
        var item = Make.Item(id, "作り物の衣装");
        return item with
        {
            Local = item.Local with
            {
                Attributes = attributes ?? new Dictionary<string, int>(),
                UserTags = tags ?? [],
                Avatars = Enumerable.Range(0, avatars)
                    .Select(n => new AvatarLink { AvatarItemId = (2000001 + n).ToString(), Name = $"作り物アバター{n}" })
                    .ToList(),
                Purchases = purchases ?? [],
            },
        };
    }

    private static UserTagAssignment Tag(string top, params string[] subs) => new() { Top = top, Subs = subs };

    private static CardInfoOptions Defaults(string? sortAttribute = null, IReadOnlyList<string>? chosen = null, bool withSubs = false)
        => CardInfoOptions.Build(chosen ?? [], Master, sortAttribute, withSubs);

    // ---- 札の数と幅 ----

    [Fact]
    public void ユーザータグは札2枚まで出し_残りは数で言う()
    {
        var item = Rated(tags: [Tag("衣装"), Tag("髪"), Tag("小物")]);

        var info = CardInfo.Build(item, Defaults(), narrow: false, owned: true);

        Assert.Equal("衣装", info.Tag1);
        Assert.Equal("髪", info.Tag2);
        Assert.Equal("+1", info.TagMore);
    }

    [Fact]
    public void 幅200未満のカードでは札を1枚にし_1行も短くする()
    {
        var item = Rated(
            attributes: new Dictionary<string, int> { ["かわいい"] = 66, ["質感"] = 42 },
            tags: [Tag("衣装"), Tag("髪"), Tag("小物")],
            avatars: 24,
            purchases: [new Purchase { Price = 3000 }]);

        var wide = CardInfo.Build(item, Defaults(), narrow: false, owned: true);
        var narrow = CardInfo.Build(item, Defaults(), narrow: true, owned: true);

        Assert.Equal("¥3,000・対応 24体", wide.MetaLine);
        Assert.Equal("質感", wide.Attribute2?.Name);
        Assert.Null(narrow.Tag2);
        Assert.Equal("+2", narrow.TagMore);
        Assert.Null(narrow.Attribute2);
        Assert.Equal("かわいい", narrow.Attribute1?.Name);
        Assert.Equal("¥3,000・24体", narrow.MetaLine);
    }

    [Fact]
    public Task カードの札は結んだ幅で組み_200ちょうどは2枚_199は1枚() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = Master.Select(name => new AttributeDefinition { Name = name }).ToList() });
        await app.AddItemAsync(Rated(tags: [Tag("衣装"), Tag("髪")]));
        var card = (await app.StartAsync()).Search.ListItems.Single();
        var converter = CardInfoForWidthConverter.Instance;

        var at200 = (CardInfo?)converter.Convert([card, card.InfoVersion, 200.0], typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture);
        var at199 = (CardInfo?)converter.Convert([card, card.InfoVersion, 199.0], typeof(object), null!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("髪", at200?.Tag2);
        Assert.Null(at199?.Tag2);
        Assert.Equal("+1", at199?.TagMore);
    });
    [Fact]
    public void 評価していない属性は札を空けず_次の評価した属性を出す()
    {
        var item = Rated(attributes: new Dictionary<string, int> { ["質感"] = 42, ["軽さ"] = 7 });

        var info = CardInfo.Build(item, Defaults(), narrow: false, owned: true);

        Assert.Equal(new CardAttributeChip("質感", 42), info.Attribute1);
        Assert.Equal(new CardAttributeChip("軽さ", 7), info.Attribute2);
        Assert.Equal("質感 42　軽さ 7", info.AttributesLine);
    }

    [Fact]
    public void 何も無い商品は札も1行も空にする()
    {
        var info = CardInfo.Build(Rated(), Defaults(), narrow: false, owned: true);

        Assert.False(info.HasTag1);
        Assert.False(info.HasTagMore);
        Assert.False(info.HasAttribute1);
        Assert.Equal(string.Empty, info.MetaLine);
        Assert.Equal(string.Empty, info.PaidText);
        Assert.Equal(string.Empty, info.AvatarText);
    }

    [Fact]
    public void 未所持のカードは1行を襷のぶん右へ寄せる()
    {
        var item = Rated(avatars: 2);

        Assert.Equal(0, CardInfo.Build(item, Defaults(), narrow: false, owned: true).MetaMargin.Left);
        Assert.Equal(46, CardInfo.Build(item, Defaults(), narrow: false, owned: false).MetaMargin.Left);
    }

    [Fact]
    public void 小分類を出す設定のときは_札に小分類も書く()
    {
        var item = Rated(tags: [Tag("衣装", "夏", "冬"), Tag("髪")]);

        Assert.Equal("衣装：夏・冬", CardInfo.Build(item, Defaults(withSubs: true), narrow: false, owned: true).Tag1);
        Assert.Equal("衣装", CardInfo.Build(item, Defaults(withSubs: false), narrow: false, owned: true).Tag1);
        Assert.Equal("衣装：夏・冬　髪", CardInfo.Build(item, Defaults(withSubs: true), narrow: false, owned: true).TagsLine);
    }

    // ---- 払った額と対応 ----

    [Fact]
    public void 払った額は自分用の合計で_無料は無料_額が無ければ書かない()
    {
        Assert.Equal("¥3,500", CardInfo.PaidTextOf(Rated(purchases: [new Purchase { Price = 3000 }, new Purchase { Price = 500 }])));
        Assert.Equal("無料", CardInfo.PaidTextOf(Rated(purchases: [new Purchase { Price = 0 }])));
        Assert.Equal(string.Empty, CardInfo.PaidTextOf(Rated(purchases: [new Purchase { Price = 800, Kind = PurchaseKind.Received }])));
        Assert.Equal(string.Empty, CardInfo.PaidTextOf(Rated()));
    }

    [Fact]
    public void 対応の数は消したアバターを数えない()
    {
        var item = Rated(avatars: 3);
        item = item with
        {
            Local = item.Local with { Avatars = [.. item.Local.Avatars.Take(2), item.Local.Avatars[2] with { Rejected = true }] },
        };

        Assert.Equal(2, CardInfo.AvatarCountOf(item));
        Assert.Equal("2体", CardInfo.Build(item, Defaults(), narrow: false, owned: true).AvatarText);
    }

    // ---- どの属性をどの順で ----

    [Fact]
    public void 設定で選んでいなければ属性の管理の並びの上から出す()
    {
        var options = Defaults();

        Assert.Equal(Master, options.ChipAttributes);
    }

    [Fact]
    public void 設定で選んだ属性のうち_その商品に付いている順に出す_選んだ順ではない()
    {
        var options = Defaults(chosen: ["軽さ", "かわいい"]);

        var item = Rated(attributes: new Dictionary<string, int> { ["かわいい"] = 10, ["質感"] = 90, ["軽さ"] = 50 });
        var info = CardInfo.Build(item, options, narrow: false, owned: true);
        Assert.Equal("かわいい", info.Attribute1?.Name);
        Assert.Equal("軽さ", info.Attribute2?.Name);
        Assert.Equal("かわいい 10　軽さ 50", info.AttributesLine);
    }

    [Fact]
    public void 選んでいないときも_候補は管理の全部で_出す順は商品に付いている順()
    {
        var item = Rated(attributes: new Dictionary<string, int> { ["軽さ"] = 50, ["質感"] = 90, ["かわいい"] = 10 });

        var info = CardInfo.Build(item, Defaults(), narrow: false, owned: true);

        Assert.Equal("軽さ 50　質感 90　かわいい 10", info.AttributesLine);
    }

    [Fact]
    public void 並べ替えに使っている属性を先に出す_選んでいない属性でも()
    {
        var item = Rated(attributes: new Dictionary<string, int> { ["軽さ"] = 50, ["質感"] = 90, ["かわいい"] = 10 });

        Assert.Equal("質感 90　軽さ 50　かわいい 10", CardInfo.Build(item, Defaults(sortAttribute: "質感"), narrow: false, owned: true).AttributesLine);
        Assert.Equal("質感 90　軽さ 50", CardInfo.Build(item, Defaults(sortAttribute: "質感", chosen: ["軽さ"]), narrow: false, owned: true).AttributesLine);
    }

    [Fact]
    public void 重ねの棒も札と同じ順で_札に出さない属性は後ろに付いている順で出す()
    {
        var item = Rated(attributes: new Dictionary<string, int> { ["質感"] = 3, ["軽さ"] = 4, ["かわいい"] = 1 });

        var peek = CardPeek.Build(item, Defaults(chosen: ["かわいい", "軽さ"]), imageHeight: 400, cardWidth: 228);

        Assert.Equal(["軽さ", "かわいい", "質感"], peek.Bars.Select(bar => bar.Name));
    }

    [Fact]
    public void 決め方が同じなら同じと見なす_カードへ知らせ直さない()
    {
        var context = new CardInfoContext();

        Assert.True(context.Update(Defaults(sortAttribute: "質感")));
        Assert.False(context.Update(Defaults(sortAttribute: "質感")));
        Assert.True(context.Update(Defaults()));
        Assert.Equal(2, context.Version);
    }

    // ---- 乗せたときの重ね ----

    [Fact]
    public void 重ねは絵の高さに入る数だけ棒を出し_入らない分はほかn件にする()
    {
        var item = Rated(
            attributes: new Dictionary<string, int> { ["かわいい"] = 1, ["かっこいい"] = 2, ["質感"] = 3, ["軽さ"] = 4 },
            tags: [Tag("衣装")],
            avatars: 1);

        var tall = CardPeek.Build(item, Defaults(), imageHeight: 200, cardWidth: 228);
        var small = CardPeek.Build(item, Defaults(), imageHeight: 132, cardWidth: 160);

        Assert.Equal(4, tall.Bars.Count);
        Assert.False(tall.HasMore);
        Assert.Equal(CardPeek.TrackWidth, tall.Bars[0].TrackWidth);
        Assert.Single(small.Bars);
        Assert.Equal("ほか 3 件", small.MoreText);
        Assert.Equal(CardPeek.NarrowTrackWidth, small.Bars[0].TrackWidth);
    }

    [Fact]
    public void 重ねには属性の管理に無い属性も後ろに出す()
    {
        var item = Rated(attributes: new Dictionary<string, int> { ["消えた属性"] = 5, ["質感"] = 3 });

        var peek = CardPeek.Build(item, Defaults(), imageHeight: 400, cardWidth: 228);

        Assert.Equal(["質感", "消えた属性"], peek.Bars.Select(bar => bar.Name));
    }

    [Fact]
    public Task 名前の欄に乗せると重ね_絵に乗せている間とキーボードで離れたら下ろす() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = Master.Select(name => new AttributeDefinition { Name = name }).ToList() });
        await app.AddItemAsync(Rated(attributes: new Dictionary<string, int> { ["質感"] = 40 }));
        var card = (await app.StartAsync()).Search.ListItems.Single();

        card.SetPointerOnText(true);
        Assert.True(card.IsPeeking);
        Assert.Equal("質感", card.Peek.Bars.Single().Name);

        // 絵はなぞって送る所なので、絵に乗っている間は重ねない
        card.ShowImageAt(0.5, 200);
        Assert.False(card.IsPeeking);
        card.ResetImage();
        Assert.True(card.IsPeeking);

        card.SetPointerOnText(false);
        Assert.False(card.IsPeeking);
        card.SetKeyboardFocus(true);
        Assert.True(card.IsPeeking);
        card.SetKeyboardFocus(false);
        Assert.False(card.IsPeeking);
    });

    [Fact]
    public Task 何も出す物が無い商品には乗せても重ねない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Rated());
        var card = (await app.StartAsync()).Search.ListItems.Single();

        card.SetPointerOnText(true);

        Assert.False(card.IsPeeking);
    });

    // ---- 画面で ----

    [Fact]
    public Task 検索を属性で並べると_カードとリストの札がその属性を先に出す() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = Master.Select(name => new AttributeDefinition { Name = name }).ToList() });
        await app.AddItemAsync(Rated(attributes: new Dictionary<string, int> { ["かわいい"] = 10, ["質感"] = 90 }));
        var search = (await app.StartAsync()).Search;
        var card = search.ListItems.Single();
        var before = card.InfoVersion;
        Assert.Equal("かわいい", card.ListInfo.Attribute1?.Name);

        search.SortField = search.SortFields.First(field => field.AttributeName == "質感");

        Assert.NotEqual(before, card.InfoVersion);
        Assert.Equal("質感", card.ListInfo.Attribute1?.Name);
        Assert.Equal("質感", card.InfoFor(narrow: true).Attribute1?.Name);
        Assert.Equal("質感 90　かわいい 10", card.ListInfo.AttributesLine);
    });

    [Fact]
    public Task ほかの画面へ渡すカードには検索の並べ替えを持ち込まない() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = Master.Select(name => new AttributeDefinition { Name = name }).ToList() });
        await app.AddItemAsync(Rated(attributes: new Dictionary<string, int> { ["かわいい"] = 10, ["質感"] = 90 }));
        var search = (await app.StartAsync()).Search;

        search.SortField = search.SortFields.First(field => field.AttributeName == "質感");

        Assert.Equal("かわいい", search.CardFor("1000001")!.ListInfo.Attribute1?.Name);
        Assert.Equal("かわいい", search.CreateCard(search.FindItem("1000001")!).ListInfo.Attribute1?.Name);
    });

    [Fact]
    public Task 設定で属性を選ぶと保存され_カードの札がその属性になる() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = Master.Select(name => new AttributeDefinition { Name = name }).ToList() });
        await app.AddItemAsync(Rated(attributes: new Dictionary<string, int> { ["かわいい"] = 10, ["軽さ"] = 30 }));
        var main = await app.StartAsync();
        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        Assert.Equal("選んでいないときは、属性の管理の並びの上から表示します。", settings.CardAttributesNote);

        settings.AddCardAttribute("軽さ");
        settings.AddCardAttribute("存在しない属性");
        await app.SettleAsync();

        Assert.Equal(["軽さ"], app.Services.Settings.CardAttributes);
        Assert.DoesNotContain("軽さ", settings.CardAttributeCandidates);
        Assert.Equal("選んだ属性のうち、商品に付いている順に2つまでカードに表示します。", settings.CardAttributesNote);
        await UiThread.Until(() => main.Search.ListItems.Single().ListInfo.Attribute1?.Name == "軽さ", "札が選んだ属性になる");
        Assert.Null(main.Search.ListItems.Single().ListInfo.Attribute2);

        settings.RemoveCardAttribute("軽さ");
        await app.SettleAsync();
        Assert.Empty(app.Services.Settings.CardAttributes);
    });

    [Fact]
    public Task 検索のリストの足した列は幅を覚え_範囲に収める() => TestApp.Run(async app =>
    {
        var search = (await app.StartAsync()).Search;
        var columns = search.ListColumns;

        Assert.Equal(160, columns.TagsWidth);
        Assert.Equal(160, columns.AttributesWidth);
        Assert.Equal(76, columns.PaidWidth);
        Assert.Equal(56, columns.AvatarsWidth);

        columns.PaidWidth = 9999;
        Assert.Equal(160, columns.PaidWidth);
    });
}
