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

    private static NotificationLine Removed(string text, string? follows = null)
        => new() { Kind = NotificationLineKind.Removed, Text = text, Follows = follows };

    /// <summary>消えた行を「差し込む位置:文」で並べる。</summary>
    private static string Spots(ChangedLineMarks marks) => string.Join(" | ", marks.RemovedLines.Select(line => $"{line.Before}:{line.Text}"));

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
        Assert.Equal("0:旧い行", Spots(marks));
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
    public void 消えた行は知らせに残した分を全部並べ_残さなかった数だけを1行で言う()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "注意事項",
            Before = "x",
            After = "y",
            Lines = [Removed("1"), Added("足した"), Removed("2", "足した"), Removed("3", "足した"), Removed("4", "足した")],
            MoreAdded = 5,
            MoreRemoved = 2,
        });

        var marks = ChangedLineMarks.For(lines, "足した");

        Assert.Equal("0:1 | 1:2 | 1:3 | 1:4", Spots(marks));

        // 足した行の残さなかった数は数えない（本文の上で見えている）
        Assert.Equal("ほかに消えた行が 2 行あります。", marks.RemovedMoreText);
        Assert.True(marks.HasRemovedMore);
    }

    // ---- 消えた行の位置（メモ17：札ではなく帯で、元の位置に並べる） ----

    [Fact]
    public void 消えた行は_今の本文で直前にあった行のすぐ後ろへ差し込む()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "注意事項",
            Before = "x",
            After = "y",
            Lines = [Removed("先頭で消えた"), Removed("再配布は禁止です。", "作り物の注意書きです。"), Removed("末尾で消えた", "改変は自由です。")],
        });

        // 番号は本文を改行で分けたときの番号（空の行も数える）。差し込むのは「その番号の行の前」
        var marks = ChangedLineMarks.For(lines, "作り物の注意書きです。\n\n改変は自由です。");

        Assert.Equal("0:先頭で消えた | 1:再配布は禁止です。 | 3:末尾で消えた", Spots(marks));
    }

    [Fact]
    public void 同じ文の行が2つあれば_前に置いた所から先を先に探す()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "更新履歴",
            Before = "x",
            After = "y",
            Lines = [Added("v1.1"), Removed("旧い注記", "---")],
        });

        // 足した行（v1.1）より後ろの「---」の後ろ。前の「---」に付くと、差の順と食い違う
        var marks = ChangedLineMarks.For(lines, "---\nv1.0\n---\nv1.1\n---\n末尾");

        Assert.Equal("5:旧い注記", Spots(marks));
    }

    [Fact]
    public void 重ねた知らせで_直前の行が後で消えていたら_その消えた行のすぐ後ろ()
    {
        // 1回目に「B」が消え（直前は A）、2回目に「A」が消えた（直前は C）。B は今の本文に無い A の後ろ＝C の後ろに並ぶ
        // （手掛かりの無い物の置き方＝先頭とは違う所になる）
        var stacked = ChangeStack.StackLines([Removed("B", "A")], [Removed("A", "C")]);
        var lines = ChangedLines.From(new NotificationDiff { Field = "注意事項", Before = "x", After = "y", Lines = stacked });

        var marks = ChangedLineMarks.For(lines, "C\nD");

        Assert.Equal("1:A | 1:B", Spots(marks));
    }

    [Fact]
    public void 見出しごと消えた物は_本文が無く_消えた行を並びの順に全部並べる()
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "旧版について",
            Before = "旧版は配布を終えました",
            Lines = [Removed("旧版は配布を終えました"), Removed("問い合わせは受けません", "旧版は配布を終えました")],
        });

        Assert.True(lines.WholeRemoved);
        Assert.Equal("0:旧版は配布を終えました | 0:問い合わせは受けません", Spots(ChangedLineMarks.For(lines, null)));
    }

    [Fact]
    public Task 本文の文書は_足した行と消えた行を種類ごとの段落にし_消えた行を元の位置に置く() => UiThread.Run(() =>
    {
        var lines = ChangedLines.From(new NotificationDiff
        {
            Field = "注意事項",
            Before = "x",
            After = "y",
            Lines = [Removed("消えた1", "残る"), Removed("消えた2", "残る"), Added("足した")],
        });
        var marks = ChangedLineMarks.For(lines, "残る\n足した\n最後");

        var document = Chmonos.App.Controls.SelectableText.Build("残る\n足した\n最後", null, marks);
        var paragraphs = document.Blocks.OfType<System.Windows.Documents.Paragraph>().ToList();

        Assert.Equal(
            ["残る", "消えた1\n消えた2", "足した", "最後"],
            paragraphs.Select(paragraph => new System.Windows.Documents.TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Replace("\r\n", "\n")));

        // 消えた行の段落だけに、既読にするときに画面が高さを測る印が付く
        Assert.Equal([false, true, false, false], paragraphs.Select(paragraph => ReferenceEquals(paragraph.Tag, Chmonos.App.Controls.SelectableText.RemovedLineTag)));
        Assert.Equal(new System.Windows.Thickness(3, 0, 0, 0), paragraphs[2].BorderThickness);
        Assert.Equal(new System.Windows.Thickness(0), paragraphs[0].BorderThickness);
    });

    [Fact]
    public void 行を持たない知らせでは_本文に何も付けない()
    {
        var marks = ChangedLineMarks.For(ChangedLines.From(new NotificationDiff { Field = "注意事項", Before = "前", After = "後" }), "後");

        Assert.Same(ChangedLineMarks.None, marks);
        Assert.Empty(marks.AddedLines);
        Assert.Empty(marks.RemovedLines);
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
                Lines = [Removed("v1.1 予定", "v1.0 公開"), Added("v1.1 直しました")],
            }));
            return list;
        });

        var main = await app.StartAsync();
        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.HasUnreadChanges, "知らせを読んで印が付く");

        Assert.Equal([1], page.Sections[0].Lines.AddedLines);
        Assert.Equal("1:v1.1 予定", Spots(page.Sections[0].Lines));
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
        Assert.Empty(page.DescriptionLines.RemovedLines);

        page.MarkChangesReadCommand.Execute(null);
        await UiThread.Until(() => !page.HasUnreadChanges, "既読にすると印が消える");
        Assert.Same(ChangedLineMarks.None, page.DescriptionLines);
    });
}
