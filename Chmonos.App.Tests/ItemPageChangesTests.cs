using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「BOOTHで変わった所」の印（メモ7-①・ユーザ判断 2026-10-02：「既読にする」を押すまで印を残す）。
///
/// 未読の更新の知らせの差（欄の名前・前・後）から、どの欄にどの種類の印を付けるかを決める。
/// 前は要確認・ショップの「変更あり」から開いても、ページのどこが変わったのか分からなかった。
/// </summary>
public class ItemPageChangesTests
{
    private const string ItemId = "1000001";

    private static NotificationDiff Diff(string field, string? before, string? after)
        => new() { Field = field, Before = before, After = after };

    private static NotificationRecord Updated(string itemId, params NotificationDiff[] diffs) => new()
    {
        Id = $"item-updated:{itemId}",
        Kind = NotificationKind.ItemUpdated,
        ItemId = itemId,
        Title = "作り物の衣装",
        Detail = BoothChanges.Summarize(diffs),
        Diffs = diffs,
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)),
    };

    // ---- 欄への振り分け ----

    [Fact]
    public void 商品名_価格_販売終了は_それぞれの欄に種類の色と文字で付く()
    {
        var changes = ItemChanges.From(
            [Updated(ItemId,
                Diff(BoothChanges.NameField, "作り物の衣装", "作り物の衣装 改"),
                Diff(BoothChanges.PriceField, "¥ 500", "¥ 800"),
                Diff(BoothChanges.SaleField, "販売中", "販売終了"))],
            []);

        var name = Assert.Single(changes.Name.Marks);
        Assert.Equal((ChangeTone.Changed, "変更", "前の値：作り物の衣装"), (name.Tone, name.Label, name.Tip));
        Assert.Equal(ChangeTone.Changed, changes.Name.Edge);

        // 値段が出ているのはバリエーションの行だけなので、価格はバリエーションの欄に付く
        var price = Assert.Single(changes.Variations.Marks);
        Assert.Equal((ChangeTone.Price, "価格変更", "前の値：¥ 500"), (price.Tone, price.Label, price.Tip));

        var sale = Assert.Single(changes.Sale.Marks);
        Assert.Equal((ChangeTone.Removed, "販売終了", "前の値：販売中"), (sale.Tone, sale.Label, sale.Tip));

        // 変わっていない欄には付けない
        Assert.False(changes.Gallery.IsMarked);
        Assert.Null(changes.Gallery.Edge);
        Assert.False(changes.Description.IsMarked);
        Assert.Empty(changes.Others);
    }

    [Fact]
    public void 販売再開は_足された色()
    {
        var changes = ItemChanges.From([Updated(ItemId, Diff(BoothChanges.SaleField, "販売終了", "販売中"))], []);

        var sale = Assert.Single(changes.Sale.Marks);
        Assert.Equal((ChangeTone.Added, "販売再開"), (sale.Tone, sale.Label));
    }

    [Theory]
    [InlineData("3 件", "5 件", ChangeTone.Added, "追加")]
    [InlineData("5 件", "3 件", ChangeTone.Removed, "削除")]
    public void バリエーションと画像の数は_増えたら追加_減ったら削除(string before, string after, ChangeTone tone, string label)
    {
        var changes = ItemChanges.From(
            [Updated(ItemId,
                Diff(BoothChanges.VariationsField, before, after),
                Diff(BoothChanges.ImagesField, before.Replace("件", "枚"), after.Replace("件", "枚")))],
            []);

        var variations = Assert.Single(changes.Variations.Marks);
        Assert.Equal((tone, label, $"前の値：{before}"), (variations.Tone, variations.Label, variations.Tip));

        var gallery = Assert.Single(changes.Gallery.Marks);
        Assert.Equal((tone, label), (gallery.Tone, gallery.Label));
    }

    [Fact]
    public void 価格と数が両方変わったら_バリエーションの欄に札を2枚並べ_線は先の印の色()
    {
        var changes = ItemChanges.From(
            [Updated(ItemId, Diff(BoothChanges.PriceField, "¥ 500", "¥ 800"), Diff(BoothChanges.VariationsField, "2 件", "3 件"))],
            []);

        Assert.Equal(["価格変更", "追加"], changes.Variations.Marks.Select(mark => mark.Label));
        Assert.Equal(ChangeTone.Price, changes.Variations.Edge);
    }

    [Fact]
    public void 説明文の見出しは_見出しごとに付け_商品説明の見出しにもまとめて出す()
    {
        var changes = ItemChanges.From(
            [Updated(ItemId,
                Diff("更新履歴", "v1.0 公開", "v1.1 公開"),
                Diff("同梱物", null, "テクスチャ一式"))],
            ["更新履歴", "同梱物", "注意事項"]);

        var history = Assert.Single(changes.Sections["更新履歴"].Marks);
        Assert.Equal((ChangeTone.Changed, "変更", "前の値：v1.0 公開"), (history.Tone, history.Label, history.Tip));

        // 前回の取得に無かった見出しは、前の値の代わりに足されたと言う
        var added = Assert.Single(changes.Sections["同梱物"].Marks);
        Assert.Equal((ChangeTone.Added, "追加", "前回の取得の後に追加されました。"), (added.Tone, added.Label, added.Tip));

        Assert.False(changes.Sections.ContainsKey("注意事項"));

        // 欄を畳むと見出しの印が隠れるので、欄の見出しにも出す
        var description = Assert.Single(changes.Description.Marks);
        Assert.Equal("変わった見出し：更新履歴、同梱物", description.Tip);
    }

    [Fact]
    public void 見出しの無い商品の説明文の変化は_商品説明の見出しに付く()
    {
        var changes = ItemChanges.From([Updated(ItemId, Diff(BoothChanges.DescriptionField, "前の説明", "今の説明"))], []);

        var description = Assert.Single(changes.Description.Marks);
        Assert.Equal((ChangeTone.Changed, "前の値：前の説明"), (description.Tone, description.Tip));
        Assert.Empty(changes.Sections);
    }

    /// <summary>
    /// 消えた見出しは、前は上の帯の1行「ほかに変わったところ」にまとめていた。元の位置に見出しごと赤の帯で並べる（メモ17②）。
    /// 今のページに無いのに「変わった」とある見出し（ページを取り直す前の知らせ）だけは、見せる欄が無いので上の帯の押せない語にする
    /// </summary>
    [Fact]
    public void 消えた見出しは元の位置へ並べる物として渡し_ページに無い変わった見出しだけを押せない語にする()
    {
        var changes = ItemChanges.From(
            [Updated(ItemId,
                new NotificationDiff { Field = "旧版について", Before = "旧版は配布を終えました", Follows = "更新履歴" },
                Diff("おまけ", "前", "後"))],
            ["更新履歴"]);

        var section = Assert.Single(changes.RemovedSections);
        Assert.Equal(("旧版について", "更新履歴"), (section.Key, section.Follows));
        Assert.Equal((ChangeTone.Removed, "削除", "前の値：旧版は配布を終えました"), (section.Mark.Tone, section.Mark.Label, section.Mark.Tip));
        Assert.Equal(["見出し「おまけ」"], changes.Others);
        Assert.Empty(changes.Sections);

        // 欄を畳むと見えないので、商品説明の見出しにもまとめて出す
        Assert.Equal("変わった見出し：旧版について", Assert.Single(changes.Description.Marks).Tip);
    }

    [Fact]
    public void 既読_解消済み_ほかの商品_ほかの種類の知らせには印を付けない()
    {
        var mine = Updated(ItemId, Diff(BoothChanges.NameField, "前", "後"));

        Assert.True(ItemChanges.IsUnreadUpdateOf(mine, ItemId));
        Assert.False(ItemChanges.IsUnreadUpdateOf(mine with { IsRead = true }, ItemId));
        Assert.False(ItemChanges.IsUnreadUpdateOf(mine with { IsResolved = true }, ItemId));
        Assert.False(ItemChanges.IsUnreadUpdateOf(mine, "1000002"));
        Assert.False(ItemChanges.IsUnreadUpdateOf(mine with { Kind = NotificationKind.ItemBackOnBooth }, ItemId));
    }

    [Fact]
    public void 未読が2件あれば_欄ごとにいちばん古い前の値を見せる()
    {
        var older = Updated(ItemId, Diff(BoothChanges.PriceField, "¥ 500", "¥ 600")) with { Id = "older" };
        var newer = Updated(ItemId, Diff(BoothChanges.PriceField, "¥ 600", "¥ 800")) with
        {
            Id = "newer",
            CreatedAt = older.CreatedAt.AddDays(1),
        };

        var changes = ItemChanges.From([newer, older], []);

        Assert.Equal("前の値：¥ 500", Assert.Single(changes.Variations.Marks).Tip);
        Assert.Equal(["older", "newer"], changes.NotificationIds);
    }

    /// <summary>
    /// 保存側と同じ重ね方（<see cref="ChangeStack"/>）でまとめる（ユーザ判断 2026-10-02「重ねましょう」）。
    /// 前は前後を並べるだけで、上がって戻った価格に「前の値：¥ 500」の印が付き、足して消した行も「足した」と出ていた
    /// </summary>
    [Fact]
    public void 未読が2件で_戻った欄と打ち消し合った行には印を付けない()
    {
        static NotificationLine Line(NotificationLineKind kind, string text) => new() { Kind = kind, Text = text };

        var older = Updated(ItemId,
            Diff(BoothChanges.PriceField, "¥ 500", "¥ 600"),
            new NotificationDiff
            {
                Field = "更新履歴",
                Before = "v1.0",
                After = "v1.0",
                Lines = [Line(NotificationLineKind.Added, "v1.1 予告"), Line(NotificationLineKind.Added, "v1.1 公開")],
            }) with { Id = "older" };
        var newer = Updated(ItemId,
            Diff(BoothChanges.PriceField, "¥ 600", "¥ 500"),
            new NotificationDiff
            {
                Field = "更新履歴",
                Before = "v1.0",
                After = "v1.0",
                Lines = [Line(NotificationLineKind.Removed, "v1.1 予告")],
            }) with { Id = "newer", CreatedAt = older.CreatedAt.AddDays(1) };

        var changes = ItemChanges.From([newer, older], ["更新履歴"]);

        Assert.False(changes.Variations.IsMarked);
        var line = Assert.Single(changes.SectionLines["更新履歴"].Lines);
        Assert.Equal((NotificationLineKind.Added, "v1.1 公開"), (line.Kind, line.Text));
    }

    [Fact]
    public void 知らせが無ければ_何も付けない()
    {
        var changes = ItemChanges.From([], ["更新履歴"]);

        Assert.False(changes.HasAny);
        Assert.Same(ItemChanges.None, changes);
    }

    // ---- 商品ページを通して ----

    [Fact]
    public Task 商品ページは_未読の更新の欄に印を付け_既読にするで印を消し_要確認の数も減る() => TestApp.Run(async app =>
    {
        var history = new H2Section { Heading = "★更新履歴★", Text = "v1.1 公開" };
        var notes = new H2Section { Heading = "注意事項", Text = "作り物の注意" };
        var item = Make.Item(ItemId, "作り物の衣装 改");
        item = item with { Booth = item.Booth with { H2Sections = [history, notes] } };
        await app.AddItemAsync(item);
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));

        // 見出しは知らせを作る側と同じく、正規化した名前で差に入っている
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId,
                Diff(BoothChanges.NameField, "作り物の衣装", "作り物の衣装 改"),
                Diff(history.NormalizedHeading, "v1.0 公開", "v1.1 公開"),
                new NotificationDiff
                {
                    Field = "旧版について",
                    Before = "旧版は配布を終えました",
                    Follows = history.NormalizedHeading,
                    Lines = [new NotificationLine { Kind = NotificationLineKind.Removed, Text = "旧版は配布を終えました" }],
                }));
            list.Add(Updated("1000002", Diff(BoothChanges.PriceField, "¥ 500", "¥ 800")));
            return list;
        });

        var main = await app.StartAsync();
        await UiThread.Until(() => main.UnreadCount == 2, "要確認の未読が2件と数えられる");

        // 開いた時点で印が付いている（後から付くと、上の帯が後から現れて本文を押し下げる。メモ17）
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.True(page.HasUnreadChanges);
        Assert.True(page.ShowsChangesBar);

        Assert.Equal("変更", Assert.Single(page.NameChange.Marks).Label);
        Assert.Equal("作り物の衣装", page.PreviousName);
        Assert.False(page.VariationsChange.IsMarked);
        Assert.Equal("変更", Assert.Single(page.Sections[0].Change.Marks).Label);
        Assert.True(page.DescriptionChange.IsMarked);

        // 消えた見出しは、前のページで直前にあった見出しの後ろに、見出しごと赤の帯で並ぶ
        Assert.Equal(["更新履歴", "旧版について", "注意事項"], page.Sections.Select(section => section.Key));
        var removed = page.Sections[1];
        Assert.True(removed.IsRemoved);
        Assert.Equal(ChangeTone.Removed, removed.Band);
        Assert.Equal("削除", Assert.Single(removed.Change.Marks).Label);
        Assert.Equal("旧版は配布を終えました", Assert.Single(removed.Lines.RemovedLines).Text);
        Assert.False(page.Sections[2].Change.IsMarked);
        Assert.True(page.MarkChangesReadCommand.CanExecute(null));

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると印が消える");

        Assert.False(page.NameChange.IsMarked);
        Assert.False(page.HasPreviousName);
        Assert.False(page.Sections[0].Change.IsMarked);
        Assert.False(page.DescriptionChange.IsMarked);
        Assert.Empty(page.ChangeTargets);
        Assert.False(page.MarkChangesReadCommand.CanExecute(null));

        // 消えた見出しの行は一覧から抜かずに隠す（一覧を作り直すと見ている所が動く）。帯は「既読にしました」として同じ高さで残す
        Assert.False(removed.IsShown);
        Assert.Same(removed, page.Sections[1]);
        Assert.True(page.ShowsChangesBar);
        Assert.True(page.IsChangesRead);

        // この商品の知らせだけが既読になり、ナビの要確認の数も合わせて減る
        await UiThread.Until(() => main.UnreadCount == 1, "ナビの要確認の数が1つ減る");
        var saved = app.Services.Notifications.Load();
        Assert.True(saved.Single(record => record.ItemId == ItemId).IsRead);
        Assert.False(saved.Single(record => record.ItemId == "1000002").IsRead);

        // 開き直しても、もう印は付かない
        var reopened = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await app.SettleAsync();
        Assert.False(reopened.HasUnreadChanges);
        Assert.False(reopened.ShowsChangesBar);
        Assert.Equal(["更新履歴", "注意事項"], reopened.Sections.Select(section => section.Key));
    });

    /// <summary>
    /// 未読のうちに2回変わった商品は、重ねた1件の知らせ（<see cref="ChangeStack"/>）になる。
    /// ナビの数は1つ、印は最初の前の値で付き、要確認の行の日時の吹き出しは最初と最後を並べる
    /// </summary>
    [Fact]
    public Task 重ねた知らせは_ナビで1件と数え_最初の前の値で印を付ける() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装 改2");
        await app.AddItemAsync(item);

        var first = Updated(ItemId, Diff(BoothChanges.NameField, "作り物の衣装", "作り物の衣装 改"));
        var stacked = ChangeStack.Stack(first.Diffs, [Diff(BoothChanges.NameField, "作り物の衣装 改", "作り物の衣装 改2")]);
        var record = first with
        {
            Diffs = stacked,
            Detail = BoothChanges.Summarize(stacked),
            UpdatedAt = first.CreatedAt.AddDays(2),
        };
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(record);
            return list;
        });

        var main = await app.StartAsync();
        await UiThread.Until(() => main.UnreadCount == 1, "重ねた知らせは1件と数える");

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");
        Assert.Equal("前の値：作り物の衣装", Assert.Single(page.NameChange.Marks).Tip);

        var row = new NotificationRow { Record = record, KindText = "更新" };
        Assert.Equal("2026-10-01 12:00 〜 2026-10-03 12:00", row.CreatedTip);
    });

    [Fact]
    public Task 開いただけでは既読にしない() => TestApp.Run(async app =>
    {
        // 読み終える前に別の画面へ移ると印が消えてしまう（ユーザ判断 2026-10-02：「既読にする」を押すまで残す）
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, Diff(BoothChanges.ImagesField, "3 枚", "4 枚")));
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");
        await app.SettleAsync();

        Assert.Equal("追加", Assert.Single(page.GalleryChange.Marks).Label);
        Assert.False(Assert.Single(app.Services.Notifications.Load()).IsRead);
        Assert.Equal(1, main.UnreadCount);
    });

    [Fact]
    public Task 編集画面の中の商品ページには_印も既読にするも出さない() => TestApp.Run(async app =>
    {
        // 編集画面は入力の場。既読の操作を混ぜない
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, Diff(BoothChanges.NameField, "前", "後")));
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails, forEditing: true);
        await app.SettleAsync();

        Assert.False(page.HasUnreadChanges);
        Assert.False(page.NameChange.IsMarked);
    });

    // ---- 上の帯の並び（メモ17⑤：変わった所を並べ、押すとそこまで流す） ----

    [Fact]
    public void 上の帯には_変わった所をページの上から並べ_種類の色を持たせる()
    {
        var changes = ItemChanges.From(
            [Updated(ItemId,
                Diff(BoothChanges.ImagesField, "3 枚", "4 枚"),
                Diff(BoothChanges.NameField, "前", "後"),
                Diff(BoothChanges.PriceField, "¥ 500", "¥ 800"),
                Diff(BoothChanges.SaleField, "販売中", "販売終了"),
                Diff("注意事項", "前", "後"),
                Diff("おまけ", "前", "後"))],
            ["更新履歴", "注意事項"]);
        var sections = new[] { "★更新履歴★", "注意事項" }
            .Select(heading => new SectionRow(new H2Section { Heading = heading, Text = "本文" }))
            .ToList();
        sections[1].Change = changes.Sections["注意事項"];

        var targets = ItemViewModel.Targets(changes, sections);

        Assert.Equal(
            ["商品名", "価格", "販売状況", "画像", "説明文：注意事項", "見出し「おまけ」"],
            targets.Select(target => target.Label));
        Assert.Equal(
            [ChangeTone.Changed, ChangeTone.Price, ChangeTone.Removed, ChangeTone.Added, ChangeTone.Changed, ChangeTone.Changed],
            targets.Select(target => target.Tone));
        Assert.Same(sections[1], targets[4].Section);

        // ページに見せる欄の無い物は押せない
        Assert.Equal([true, true, true, true, true, false], targets.Select(target => target.CanGo));
    }

    [Fact]
    public void 上の帯の見出しの名前は_長ければ切る()
    {
        var heading = new string('長', 30);
        var changes = ItemChanges.From([Updated(ItemId, Diff(heading, "前", "後"))], [heading]);
        var section = new SectionRow(new H2Section { Heading = heading, Text = "本文" }) { Change = changes.Sections[heading] };

        Assert.Equal($"説明文：{new string('長', 16)}…", Assert.Single(ItemViewModel.Targets(changes, [section])).Label);
    }

    [Fact]
    public Task 押すと_畳んだ欄を開いてから_画面に流すよう頼む() => TestApp.Run(async app =>
    {
        var notes = new H2Section { Heading = "注意事項", Text = "作り物の注意" };
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with { Booth = item.Booth with { H2Sections = [notes] } };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, Diff("注意事項", "前の注意", "作り物の注意"), Diff(BoothChanges.VariationsField, "1 件", "2 件")));
            return list;
        });
        var main = await app.StartAsync();

        var description = SectionFolds.DescriptionExpanded;
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        var variations = page.IsVariationsExpanded;
        try
        {
            page.IsDescriptionExpanded = false;
            page.IsVariationsExpanded = false;
            page.Sections[0].IsOpen = false;
            var asked = new List<ChangeTarget>();
            page.ChangeRevealRequested += target =>
            {
                // 頼まれた時点で、行き先の欄はもう開いている（画面は開いた後に位置を測る）
                Assert.True(page.IsDescriptionExpanded);
                Assert.True(page.Sections[0].IsOpen);
                asked.Add(target);
            };

            var section = page.ChangeTargets.Single(target => target.Place == ChangePlace.Section);
            Assert.True(page.GoToChangeCommand.CanExecute(section));
            page.GoToChangeCommand.Execute(section);

            Assert.Equal([section], asked);
            Assert.Equal("すべて折りたたむ", page.ToggleAllSectionsText);
            Assert.False(page.IsVariationsExpanded);

            page.GoToChangeCommand.Execute(page.ChangeTargets.Single(target => target.Place == ChangePlace.Variations));
            Assert.True(page.IsVariationsExpanded);
            Assert.Equal(2, asked.Count);
        }
        finally
        {
            SectionFolds.DescriptionExpanded = description;
            page.IsVariationsExpanded = variations;
        }
    });

    // ---- 帯（メモ17⑥：バリエーション・見出しごと） ----

    [Fact]
    public Task 消えたバリエーションは元の位置に赤の帯の行で残し_足したバリエーションは緑の帯() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with
        {
            Booth = item.Booth with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "フルセット", Price = 1500 },
                    new BoothVariation { Id = 3, Name = "新色", Price = 800 },
                    new BoothVariation { Id = 4, Name = "テクスチャのみ", Price = 300 },
                ],
            },
        };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, new NotificationDiff
            {
                Field = BoothChanges.VariationsField,
                Before = "3 件",
                After = "3 件",
                Lines =
                [
                    new NotificationLine { Kind = NotificationLineKind.Removed, Text = "旧色", Follows = "フルセット" },
                    new NotificationLine { Kind = NotificationLineKind.Added, Text = "新色" },
                ],
            }));
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Equal(
            ["フルセット:", "旧色:Removed", "新色:Added", "テクスチャのみ:"],
            page.Variations.Select(row => $"{row.Name}:{row.Band}"));
        Assert.Equal("BOOTHで削除されたバリエーションです。", page.Variations[1].BandTip);
        Assert.Equal(string.Empty, page.Variations[1].PriceText);
        Assert.Equal("バリエーション", page.ChangeTargets.Single().Label);

        // 知らせるために差し込んだ行は、欄の件数に数えない
        Assert.Equal("3 件", page.VariationsCountText);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると帯が消える");

        Assert.Equal(["フルセット:", "新色:", "テクスチャのみ:"], page.Variations.Select(row => $"{row.Name}:{row.Band}"));
        Assert.Equal("3 件", page.VariationsCountText);
    });

    [Fact]
    public void 買っていて現存しない行があれば_その行に赤の帯を付け_同じバリエーションを2行に出さない()
    {
        var rows = new List<VariationRow>
        {
            new() { Name = "フルセット", PriceText = "¥1,500", Key = "フルセット" },
            new() { Name = "旧色", PriceText = "¥800 で購入", IsPurchased = true, IsGone = true, Key = "旧色" },
        };
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = BoothChanges.VariationsField,
            Before = "2 件",
            After = "1 件",
            Lines = [new NotificationLine { Kind = NotificationLineKind.Removed, Text = "旧色", Follows = "フルセット" }],
        });

        var result = ItemViewModel.WithBands(rows, boothCount: 1, lines);

        Assert.Equal(["フルセット:", "旧色:Removed"], result.Select(row => $"{row.Name}:{row.Band}"));
        Assert.True(result[1].IsPurchased);
    }

    [Fact]
    public Task 見出しごと足された見出しは_見出しの行から緑の帯() => TestApp.Run(async app =>
    {
        var added = new H2Section { Heading = "同梱物", Text = "unitypackage\nテクスチャ" };
        var notes = new H2Section { Heading = "注意事項", Text = "作り物の注意" };
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with { Booth = item.Booth with { H2Sections = [added, notes] } };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, new NotificationDiff
            {
                Field = "同梱物",
                After = "unitypackage テクスチャ",
                Lines =
                [
                    new NotificationLine { Kind = NotificationLineKind.Added, Text = "unitypackage" },
                    new NotificationLine { Kind = NotificationLineKind.Added, Text = "テクスチャ" },
                ],
            }));
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Equal(ChangeTone.Added, page.Sections[0].Band);
        Assert.Equal([0, 1], page.Sections[0].Lines.AddedLines.Order());
        Assert.Null(page.Sections[1].Band);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると帯が消える");
        Assert.Null(page.Sections[0].Band);
    });

    // ---- バリエーションの値段の変化（メモ27-⑤） ----

    private static NotificationDiff PriceDiff(string before, string after, params NotificationPrice[] prices)
        => new() { Field = BoothChanges.PriceField, Before = before, After = after, Prices = prices };

    private static NotificationPrice Price(long id, string name, int before, int after)
        => new() { Id = id, Name = name, Before = before, After = after };

    [Fact]
    public void 値段の変わったバリエーションをIDで引けるようにし_商品の価格の文字が同じなら前の値を言わない()
    {
        var changes = ItemChanges.From([Updated(ItemId, PriceDiff("¥ 500~", "¥ 500~", Price(2, "支援版", 1000, 1500)))], []);

        Assert.Equal(1500, changes.VariationPrices[2].After);
        var mark = Assert.Single(changes.Variations.Marks);
        Assert.Equal((ChangeTone.Price, "価格変更", "バリエーションの価格が変更されました。"), (mark.Tone, mark.Label, mark.Tip));
    }

    [Fact]
    public Task 値段の変わったバリエーションの行に青の帯を付け_前の値段から今の値段を行に出す() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with
        {
            Booth = item.Booth with
            {
                Variations =
                [
                    new BoothVariation { Id = 1, Name = "通常版", Price = 500 },
                    new BoothVariation { Id = 2, Name = "支援版", Price = 1500 },
                    new BoothVariation { Id = 3, Name = "おまけ付き", Price = 2000 },
                ],
            },
            Local = item.Local with { Purchases = [new Purchase { VariationId = 3, NameSnapshot = "おまけ付き", Price = 1800 }] },
        };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, PriceDiff("¥ 500~", "¥ 500~", Price(2, "支援版", 1000, 1500), Price(3, "おまけ付き", 1800, 2000))));
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);

        Assert.Equal(
            ["通常版:|¥500|", "支援版:Price|¥1,000 → ¥1,500|", "おまけ付き:Price|¥1,800 で買った|BOOTHの価格 ¥1,800 → ¥2,000"],
            page.Variations.Select(row => $"{row.Name}:{row.Band}|{row.PriceText}|{row.PriceChangeText}"));
        Assert.Equal("BOOTHで価格が変更されたバリエーションです。", page.Variations[1].BandTip);
        Assert.Equal("価格", page.ChangeTargets.Single().Label);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると帯が消える");

        Assert.Equal(
            ["通常版:|¥500|", "支援版:|¥1,500|", "おまけ付き:|¥1,800 で買った|"],
            page.Variations.Select(row => $"{row.Name}:{row.Band}|{row.PriceText}|{row.PriceChangeText}"));
    });

    [Fact]
    public void 要確認の札は_値段の変わったバリエーションを商品ページと同じ形で1行ずつ出す()
    {
        var row = new NotificationRow
        {
            Record = Updated(ItemId, PriceDiff("¥ 500~", "¥ 500~", Price(2, "支援版", 1000, 1500), Price(3, "おまけ付き", 1800, 2000))),
            KindText = "更新",
        };

        var card = Assert.Single(row.Cards);
        Assert.Equal(("価格", "支援版 ¥1,000 → ¥1,500\nおまけ付き ¥1,800 → ¥2,000"), (card.Field, card.Text));
    }
}
