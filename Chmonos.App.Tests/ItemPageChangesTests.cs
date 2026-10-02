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
        Assert.Equal(string.Empty, changes.OthersText);
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

    [Fact]
    public void ページに無い見出しは_ほかに変わったところの1行にまとめる()
    {
        // 消えた見出しは、もうページに出ていないので印を付ける欄が無い
        var changes = ItemChanges.From(
            [Updated(ItemId, Diff("旧版について", "旧版は配布を終えました", null), Diff("おまけ", "前", "後"))],
            ["更新履歴"]);

        Assert.Equal(["消えた見出し「旧版について」", "見出し「おまけ」"], changes.Others);
        Assert.Equal("ほかに変わったところ：消えた見出し「旧版について」、見出し「おまけ」", changes.OthersText);
        Assert.Empty(changes.Sections);
        Assert.False(changes.Description.IsMarked);
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
                Diff("旧版について", "旧版は配布を終えました", null)));
            list.Add(Updated("1000002", Diff(BoothChanges.PriceField, "¥ 500", "¥ 800")));
            return list;
        });

        var main = await app.StartAsync();
        await UiThread.Until(() => main.UnreadCount == 2, "要確認の未読が2件と数えられる");

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");

        Assert.Equal("変更", Assert.Single(page.NameChange.Marks).Label);
        Assert.False(page.VariationsChange.IsMarked);
        Assert.Equal("変更", Assert.Single(page.Sections[0].Change.Marks).Label);
        Assert.False(page.Sections[1].Change.IsMarked);
        Assert.True(page.DescriptionChange.IsMarked);
        Assert.True(page.HasOtherChanges);
        Assert.Equal("ほかに変わったところ：消えた見出し「旧版について」", page.OtherChangesText);
        Assert.True(page.MarkChangesReadCommand.CanExecute(null));

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると印が消える");

        Assert.False(page.NameChange.IsMarked);
        Assert.False(page.Sections[0].Change.IsMarked);
        Assert.False(page.DescriptionChange.IsMarked);
        Assert.False(page.HasOtherChanges);
        Assert.False(page.MarkChangesReadCommand.CanExecute(null));

        // この商品の知らせだけが既読になり、ナビの要確認の数も合わせて減る
        await UiThread.Until(() => main.UnreadCount == 1, "ナビの要確認の数が1つ減る");
        var saved = app.Services.Notifications.Load();
        Assert.True(saved.Single(record => record.ItemId == ItemId).IsRead);
        Assert.False(saved.Single(record => record.ItemId == "1000002").IsRead);

        // 開き直しても、もう印は付かない
        var reopened = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await app.SettleAsync();
        Assert.False(reopened.HasUnreadChanges);
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
}
