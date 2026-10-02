using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 説明文の見出しの変更を、変わった行で見せる（メモ13-②・ユーザ指示 2026-10-02）。
/// 要確認の札は変わった行を並べ、商品ページは本文の足した行に地を付け、消えた行を見出しの下に並べる。
/// 頭の抜き出しだけでは、見出しの後ろの方が変わったときに「同じ → 同じ」に見えていた。
/// </summary>
public class ChangedLinesTests
{
    private const string ItemId = "1000001";

    private static NotificationLine Added(string text) => new() { Kind = NotificationLineKind.Added, Text = text };

    private static NotificationLine Removed(string text) => new() { Kind = NotificationLineKind.Removed, Text = text };

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

    private static NotificationRow Row(params NotificationDiff[] diffs) => new() { Record = Updated(ItemId, diffs), KindText = "商品ページの変更" };

    private static string Show(IEnumerable<ChangedLineRow> rows) => string.Join(" | ", rows.Select(row => $"{row.Label}:{row.Text}"));

    // ---- 要確認の札 ----

    [Fact]
    public void 要確認の札は_変わった行を並べ_頭の抜き出しは出さない()
    {
        var card = Assert.Single(Row(new NotificationDiff
        {
            Field = "更新履歴",
            Before = "v1.0 公開",
            After = "v1.0 公開",
            Lines = [Removed("v1.1 予定"), Added("v1.1 直しました")],
        }).Cards);

        Assert.Equal("更新履歴", card.Field);
        Assert.Equal(string.Empty, card.Text);
        Assert.Equal("削除:v1.1 予定 | 追加:v1.1 直しました", Show(card.Lines));
        Assert.Equal(string.Empty, card.MoreText);
    }

    [Fact]
    public void 行が多いときは_4行で切って_ほか_n_行()
    {
        var card = Assert.Single(Row(new NotificationDiff
        {
            Field = "同梱物",
            After = "a",
            Lines = [Added("1"), Added("2"), Added("3"), Added("4"), Added("5"), Removed("6")],
            MoreAdded = 3,
            MoreRemoved = 1,
        }).Cards);

        Assert.Equal("追加:1 | 追加:2 | 追加:3 | 追加:4", Show(card.Lines));

        // 札に入らなかった2行と、知らせに残さなかった4行
        Assert.Equal("ほか 6 行", card.MoreText);
    }

    [Fact]
    public void 短い値と_行を持たない前の形の知らせは_今までどおり前から後()
    {
        var cards = Row(
            new NotificationDiff { Field = BoothChanges.PriceField, Before = "¥ 500", After = "¥ 800" },
            new NotificationDiff { Field = "注意事項", Before = "前の頭", After = "後の頭" },
            new NotificationDiff { Field = "同梱物", After = "足した見出しの頭" }).Cards;

        Assert.Equal(["¥ 500 → ¥ 800", "前の頭 → 後の頭", "足した見出しの頭"], cards.Select(card => card.Text));
        Assert.All(cards, card => Assert.Empty(card.Lines));
    }

    // ---- 商品ページの本文の印 ----

    [Fact]
    public void 本文の足した行に印を付け_空白の違いは同じ行と見る()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "更新履歴",
            Before = "x",
            After = "y",
            Lines = [Removed("旧い行"), Added("v1.2 足しました")],
        });

        // 行の番号は本文を改行で分けたときの番号（空の行も数える）。全角の空白・連続した空白は差を作るときと同じく詰めて比べる
        var marks = ChangedLineMarks.For(lines, "v1.0 公開\n\nv1.1 直しました\r\n v1.2　　足しました ");

        Assert.Equal([3], marks.AddedLines.Order());
        Assert.Equal("削除:旧い行", Show(marks.Removed));
        Assert.Equal(string.Empty, marks.RemovedMoreText);
    }

    [Fact]
    public void 本文に足した行を_差の順に一つずつ当てる()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "更新履歴",
            Before = "x",
            After = "y",
            Lines = [Added("v1.1"), Added("v1.2")],
        });

        Assert.Equal([1, 2], ChangedLineMarks.For(lines, "v1.0\nv1.1\nv1.2\nv1.1").AddedLines.Order());
    }

    [Fact]
    public void 見出しごと足された本文は_空でない行を全部印にする()
    {
        // 上限で知らせに残さなかった行にも付くように、行の中身ではなく「前が無い」で決める
        var lines = ChangedLines.From(new NotificationDiff { Field = "同梱物", After = "a", Lines = [Added("a")], MoreAdded = 40 });

        Assert.Equal([0, 2], ChangedLineMarks.For(lines, "a\n\nb").AddedLines.Order());
    }

    [Fact]
    public void 長くて切った行も_切った所までで当てる()
    {
        var full = new string('長', LineDiff.MaxLineLength + 20);
        var stored = Assert.Single(LineDiff.Compare("", full));

        var lines = ChangedLines.From(new NotificationDiff { Field = "注意事項", Before = "x", After = "y", Lines = [stored] });

        Assert.Equal([1], ChangedLineMarks.For(lines, $"前からの行\n{full}").AddedLines);
    }

    [Fact]
    public void 消えた行は_3行で切って_残さなかった数も足して_ほか_n_行()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "注意事項",
            Before = "x",
            After = "y",
            Lines = [Removed("1"), Added("足した"), Removed("2"), Removed("3"), Removed("4")],
            MoreAdded = 5,
            MoreRemoved = 2,
        });

        var marks = ChangedLineMarks.For(lines, "足した");

        Assert.Equal("削除:1 | 削除:2 | 削除:3", Show(marks.Removed));

        // 足した行の残さなかった数は数えない（本文の上で見えている）
        Assert.Equal("ほか 3 行", marks.RemovedMoreText);
        Assert.True(marks.HasRemoved);
    }

    [Fact]
    public void 行を持たない知らせでは_本文に何も付けない()
    {
        var marks = ChangedLineMarks.For(ChangedLines.From(new NotificationDiff { Field = "注意事項", Before = "前", After = "後" }), "後");

        Assert.Same(ChangedLineMarks.None, marks);
        Assert.Empty(marks.AddedLines);
        Assert.False(marks.HasRemoved);
    }

    // ---- 商品ページを通して ----

    [Fact]
    public Task 商品ページは_見出しの本文に変わった行の印を付け_既読にするで消す() => TestApp.Run(async app =>
    {
        var history = new H2Section { Heading = "★更新履歴★", Text = "v1.0 公開\nv1.1 直しました" };
        var notes = new H2Section { Heading = "注意事項", Text = "作り物の注意" };
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with { Booth = item.Booth with { H2Sections = [history, notes] } };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, new NotificationDiff
            {
                Field = history.NormalizedHeading,
                Before = "v1.0 公開 v1.1 予定",
                After = "v1.0 公開 v1.1 直しました",
                Lines = [Removed("v1.1 予定"), Added("v1.1 直しました")],
            }));
            return list;
        });

        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");

        Assert.Equal([1], page.Sections[0].Lines.AddedLines);
        Assert.Equal("削除:v1.1 予定", Show(page.Sections[0].Lines.Removed));
        Assert.Same(ChangedLineMarks.None, page.Sections[1].Lines);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると印が消える");

        Assert.Same(ChangedLineMarks.None, page.Sections[0].Lines);
        Assert.Empty(page.Sections[0].Lines.AddedLines);
    });

    [Fact]
    public Task 見出しの無い商品は_説明文の本文に印を付ける() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        item = item with { Booth = item.Booth with { Description = "説明\n対応：A\n対応：B", H2Sections = [] } };
        await app.AddItemAsync(item);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(ItemId, new NotificationDiff
            {
                Field = BoothChanges.DescriptionField,
                Before = "説明 対応：A",
                After = "説明 対応：A 対応：B",
                Lines = [Added("対応：B")],
            }));
            return list;
        });

        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");

        Assert.Equal([2], page.DescriptionLines.AddedLines);
        Assert.False(page.DescriptionLines.HasRemoved);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると印が消える");
        Assert.Same(ChangedLineMarks.None, page.DescriptionLines);
    });
}
