using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「BOOTHでの変化の履歴」（ユーザ判断 2026-11-20）：新しい順に日付・未読の印・変わった項目の札を並べ、
/// 押すとその件の差分を本文と同じ帯と線で出す。履歴が無い商品ではメニューを押せない。
/// </summary>
public class ChangeHistoryTests
{
    private const string ItemId = "1000001";

    private static DateTimeOffset At(int month, int day, int hour = 12) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(9));

    private static NotificationRecord Updated(DateTimeOffset createdAt, params NotificationDiff[] diffs) => new()
    {
        Id = $"item-updated:{ItemId}",
        Kind = NotificationKind.ItemUpdated,
        ItemId = ItemId,
        Title = "作り物の衣装",
        Detail = BoothChanges.Summarize(diffs),
        Diffs = diffs,
        CreatedAt = createdAt,
        IsRead = true,
    };

    private static NotificationLine Added(string text) => new() { Kind = NotificationLineKind.Added, Text = text };

    private static NotificationLine Removed(string text) => new() { Kind = NotificationLineKind.Removed, Text = text };

    // ---- 差分の本文と帯 ----

    /// <summary>過去の差の行は今の本文に無いことが多い。今の本文に当てずに、差そのものから本文を組む</summary>
    [Fact]
    public void 変わった行は_足した行を本文にして全部に帯を付け_消えた行は差の順の位置に差し込む()
    {
        var (text, marks) = HistoryLineMarks.From([Removed("旧1"), Added("新1"), Removed("旧2"), Added("新2")]);

        Assert.Equal("新1\n新2", text);
        Assert.Equal([0, 1], marks.AddedLines.Order());
        Assert.Equal([new InsertedLine(0, "旧1"), new InsertedLine(1, "旧2")], marks.RemovedLines);
    }

    [Fact]
    public void 消えた行だけの差は_本文が空で消えた行だけが並ぶ()
    {
        var (text, marks) = HistoryLineMarks.From([Removed("旧1"), Removed("旧2")]);

        Assert.Equal(string.Empty, text);
        Assert.Empty(marks.AddedLines);
        Assert.Equal(["旧1", "旧2"], marks.RemovedLines.Select(line => line.Text));
    }

    [Fact]
    public void 前と後だけの欄は_前を消えた行_後を足した行として出す()
    {
        var field = Assert.Single(ChangeHistoryEntry.BuildFields(Updated(At(10, 1),
            new NotificationDiff { Field = BoothChanges.NameField, Before = "作り物の衣装", After = "作り物の衣装 改" })));

        Assert.Equal((BoothChanges.NameField, "作り物の衣装 改"), (field.Heading, field.Text));
        Assert.Equal([0], field.Marks!.AddedLines);
        Assert.Equal([new InsertedLine(0, "作り物の衣装")], field.Marks.RemovedLines);
    }

    [Fact]
    public void 値段はバリエーションごとに前と後を1行ずつ_帯は付けない()
    {
        var field = Assert.Single(ChangeHistoryEntry.BuildFields(Updated(At(10, 1), new NotificationDiff
        {
            Field = BoothChanges.PriceField,
            Before = "¥ 500~",
            After = "¥ 500~",
            Prices = [new NotificationPrice { Id = 1, Name = "通常版", Before = 500, After = 800 }, new NotificationPrice { Id = 2, Before = 1000, After = 900 }],
        })));

        Assert.Equal("通常版 ¥500 → ¥800\n¥1,000 → ¥900", field.Text);
        Assert.Null(field.Marks);
    }

    [Fact]
    public void 上限で残さなかった行は_足した行と消えた行の数を1文で言う()
    {
        var field = Assert.Single(ChangeHistoryEntry.BuildFields(Updated(At(10, 1), new NotificationDiff
        {
            Field = "注意事項",
            Lines = [Added("新しい注意")],
            MoreAdded = 3,
            MoreRemoved = 2,
        })));

        Assert.Equal("新しい注意", field.Text);
        Assert.Equal("ほかに追加された行が 3 行、削除された行が 2 行あります。", field.MoreText);
    }

    [Fact]
    public void 中身の記録が無い欄と_差を持たない前の形の知らせは_そう言う()
    {
        var empty = Assert.Single(ChangeHistoryEntry.BuildFields(Updated(At(10, 1), new NotificationDiff { Field = BoothChanges.ImagesField })));
        Assert.Equal("変わった内容は記録されていません。", empty.Text);

        var old = Assert.Single(ChangeHistoryEntry.BuildFields(Updated(At(10, 1)) with { Detail = "商品ページが変わりました" }));
        Assert.Equal(("変更", "商品ページが変わりました"), (old.Heading, old.Text));
    }

    // ---- 並びの行（日付・札・未読） ----

    [Theory]
    [InlineData(null, "2026-10-01")]
    [InlineData("2026-10-01T20:00+09:00", "2026-10-01")]          // 同じ日に重ねた物は1つの日付
    [InlineData("2026-10-05T09:00+09:00", "2026-10-01〜10-05")]   // 重ねた物は最初〜最後
    [InlineData("2027-01-03T09:00+09:00", "2026-10-01〜2027-01-03")] // 年をまたぐと後ろも年から
    public void 日付は_重ねた物を最初の日から最後の日の幅で言う(string? updated, string expected)
    {
        DateTimeOffset? last = updated is null ? null : DateTimeOffset.Parse(updated, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, ChangeHistoryEntry.DateRange(At(10, 1), last));
    }

    [Fact]
    public void 札は変わった項目の名前で_説明文の見出しは頭に説明文と付けて長い物を切り_色は足した消したで分ける()
    {
        var entry = new ChangeHistoryEntry(Updated(At(10, 1),
            new NotificationDiff { Field = BoothChanges.PriceField, Before = "¥ 500", After = "¥ 800" },
            new NotificationDiff { Field = "とても長い見出しの名前がここに続いています", After = "新しい見出し", Lines = [Added("新しい見出し")] },
            new NotificationDiff { Field = "旧版について", Before = "旧版", Lines = [Removed("旧版")] }));

        Assert.Equal(
            [("価格", ChangeTone.Changed), ("説明文：とても長い見出しの名前がここに続…", ChangeTone.Added), ("説明文：旧版について", ChangeTone.Removed)],
            entry.Labels.Select(label => (label.Text, label.Tone)));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]   // 解消済みは未読に数えない（ナビの数と同じ）
    public void 未読の印は未読かつ未解消の知らせだけ(bool read, bool resolved, bool unread)
    {
        var entry = new ChangeHistoryEntry(Updated(At(10, 1)) with { IsRead = read, IsResolved = resolved });

        Assert.Equal(unread, entry.IsUnread);
    }

    // ---- 商品ページのメニュー ----

    [Fact]
    public Task 変化の記録が無い商品では_メニューを押せず_理由を言う() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await app.SettleAsync();

        Assert.Equal(ChangeHistoryAvailability.None, page.ChangeHistory);
        Assert.False(page.ShowChangeHistoryCommand.CanExecute(null));
        Assert.Equal("この商品には、BOOTHでの変化の記録がありません。", page.ChangeHistoryTip);
    });

    [Fact]
    public Task 今の通知と保管した通知を新しい順に並べ_最初の件の差分を開く() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        await app.Store.NotificationArchive.AppendAsync(
        [
            Updated(At(9, 3), new NotificationDiff { Field = BoothChanges.PriceField, Before = "¥ 500", After = "¥ 800" }),
            Updated(At(9, 4)) with { Id = "item-updated:1000002", ItemId = "1000002" },
        ]);
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Updated(At(11, 1), new NotificationDiff { Field = BoothChanges.NameField, Before = "作り物の服", After = "作り物の衣装" }) with { IsRead = false });
            return list;
        });
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.Equal(ChangeHistoryAvailability.Present, page.ChangeHistory);
        page.ShowChangeHistoryCommand.Execute(null);
        await app.SettleAsync();

        var history = Assert.Single(app.ChangeHistoryDialogs);
        Assert.Equal(["2026-11-01", "2026-09-03"], history.Entries.Select(entry => entry.DateText));
        Assert.Equal([true, false], history.Entries.Select(entry => entry.IsUnread));
        Assert.Same(history.Entries[0], history.Selected);
        Assert.Equal(BoothChanges.NameField, Assert.Single(history.SelectedFields).Heading);

        history.Selected = history.Entries[1];
        Assert.Equal(BoothChanges.PriceField, Assert.Single(history.SelectedFields).Heading);
    });

    /// <summary>保管庫は目録を作るまで分からない。確かめている間は「無い」と言わず、分かったら押せるようにする</summary>
    [Fact]
    public Task 保管した通知にだけ記録がある商品は_確かめ終えてから押せるようになる() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        await app.Store.NotificationArchive.AppendAsync([Updated(At(9, 3), new NotificationDiff { Field = BoothChanges.SaleField, Before = "販売中", After = "販売終了" })]);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        Assert.NotEqual(ChangeHistoryAvailability.None, page.ChangeHistory);

        await UiThread.Until(() => page.ChangeHistory == ChangeHistoryAvailability.Present, "保管庫を確かめ終える");
        Assert.True(page.ShowChangeHistoryCommand.CanExecute(null));
    });

    [Fact]
    public Task 保管した通知がほかの商品の物だけなら_確かめ終えて押せないままにする() => TestApp.Run(async app =>
    {
        var item = Make.Item(ItemId, "作り物の衣装");
        await app.AddItemAsync(item);
        await app.Store.NotificationArchive.AppendAsync([Updated(At(9, 3)) with { Id = "item-updated:1000002", ItemId = "1000002" }]);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.ChangeHistory == ChangeHistoryAvailability.None, "保管庫を確かめ終える");
        Assert.False(page.ShowChangeHistoryCommand.CanExecute(null));
    });
}
