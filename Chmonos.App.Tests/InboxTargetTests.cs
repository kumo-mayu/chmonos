using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品を指して通知の画面を開いたとき（「更新の通知を開く」。メモ51・2026-10-05）の、行の並び・強調・束の見出しの印・送り先。
/// 強調は画面にいる間ずっと持ち、画面を離れれば（画面ごと作り直されて）消える。
/// </summary>
public class InboxTargetTests
{
    private const string Target = "9900001";
    private const string Other = "9900002";

    private static readonly DateTimeOffset Day = new(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(9));

    private static NotificationRecord Record(string id, NotificationKind kind, string itemId, int hoursAgo, bool isRead = false) => new()
    {
        Id = id,
        Kind = kind,
        Title = "作り物の知らせ " + id,
        Detail = string.Empty,
        ItemId = itemId,
        CreatedAt = Day.AddHours(-hoursAgo),
        IsRead = isRead,
    };

    /// <summary>
    /// 「商品ページの変更」：指していない商品の新しい行・指した商品の既読の行・指した商品の未読の古い行。
    /// 「非公開商品の復活」：指していない商品の新しい行・指した商品の行（<paramref name="backTargetRead"/> で既読にできる）。
    /// </summary>
    private static async Task<InboxViewModel> OpenAsync(TestApp app, string? focusItemId, bool backTargetRead = false)
    {
        await app.AddItemAsync(Make.Item(Target, "作り物の衣装"));
        await app.AddItemAsync(Make.Item(Other, "作り物の髪"));
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Record("up-other", NotificationKind.ItemUpdated, Other, 1));
            list.Add(Record("up-read", NotificationKind.ItemUpdated, Target, 2, isRead: true));
            list.Add(Record("up-unread", NotificationKind.ItemUpdated, Target, 3));
            list.Add(Record("back-other", NotificationKind.ItemBackOnBooth, Other, 4));
            list.Add(Record("back-target", NotificationKind.ItemBackOnBooth, Target, 5, backTargetRead));
            return list;
        });

        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main, focusItemId);
        inbox.UnreadOnly = false;
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count == 2, "通知の束が並ぶ");
        return inbox;
    }

    private static NotificationGroup Group(InboxViewModel inbox, NotificationKind kind) => inbox.Groups.Single(group => group.Kind == kind);

    private static string[] Ids(NotificationGroup group) => group.Rows.Select(row => row.Record.Id).ToArray();

    [Fact]
    public Task 指した商品の行は_どの束でも先頭に寄り_未読が先() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, Target);

        Assert.Equal(["up-unread", "up-read", "up-other"], Ids(Group(inbox, NotificationKind.ItemUpdated)));
        Assert.Equal(["back-target", "back-other"], Ids(Group(inbox, NotificationKind.ItemBackOnBooth)));
    });

    [Fact]
    public Task 強調が付くのは_指した商品の行だけ() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, Target);

        var marked = inbox.Groups.SelectMany(group => group.Rows).Where(row => row.IsTarget).Select(row => row.Record.Id).Order().ToArray();
        Assert.Equal(["back-target", "up-read", "up-unread"], marked);
    });

    [Fact]
    public Task 見出しの印は_ほかの束にだけ出て_数は指した商品の行の数() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, Target);

        var back = Group(inbox, NotificationKind.ItemBackOnBooth);
        Assert.True(back.HasTarget);
        Assert.Equal(1, back.TargetCount);
        Assert.Equal("この商品 1", back.TargetText);

        // 「商品ページの変更」の束は、見出しを画面の上へ送る先なので印は出さない（数えてはいる）
        var updated = Group(inbox, NotificationKind.ItemUpdated);
        Assert.Equal(2, updated.TargetCount);
        Assert.False(updated.HasTarget);
    });

    [Fact]
    public Task 未読のみのときは_出ている行だけで数える() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, Target, backTargetRead: true);

        inbox.UnreadOnly = true;
        await UiThread.Until(() => Group(inbox, NotificationKind.ItemUpdated).Rows.All(row => !row.IsRead), "未読だけになる");

        Assert.Equal(["up-unread", "up-other"], Ids(Group(inbox, NotificationKind.ItemUpdated)));
        Assert.Equal(1, Group(inbox, NotificationKind.ItemUpdated).TargetCount);

        // 出ている行に指した商品の行が無い束は、印を出さない（開いても何も無い）
        var back = Group(inbox, NotificationKind.ItemBackOnBooth);
        Assert.Equal(["back-other"], Ids(back));
        Assert.Equal(0, back.TargetCount);
        Assert.False(back.HasTarget);
    });

    [Fact]
    public Task 読み直しても強調と並びは外れない() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, Target);

        await inbox.ReloadAsync();
        await UiThread.Until(() => inbox.Groups.Count == 2, "読み直される");

        Assert.Equal(["up-unread", "up-read", "up-other"], Ids(Group(inbox, NotificationKind.ItemUpdated)));
        Assert.Equal(3, inbox.Groups.SelectMany(group => group.Rows).Count(row => row.IsTarget));
    });

    [Fact]
    public Task 商品を指さずに開いたら_強調も印も無く_新しい順のまま() => TestApp.Run(async app =>
    {
        var inbox = await OpenAsync(app, focusItemId: null);

        Assert.Equal(["up-other", "up-read", "up-unread"], Ids(Group(inbox, NotificationKind.ItemUpdated)));
        Assert.DoesNotContain(inbox.Groups.SelectMany(group => group.Rows), row => row.IsTarget);
        Assert.DoesNotContain(inbox.Groups, group => group.HasTarget);
        Assert.Null(inbox.FocusLine);
    });

    [Fact]
    public Task 画面を離れて開き直したら_強調は消える() => TestApp.Run(async app =>
    {
        var first = await OpenAsync(app, Target);
        Assert.Contains(first.Groups.SelectMany(group => group.Rows), row => row.IsTarget);

        // ナビから開き直すと画面ごと作り直される（指した商品は持ち越さない）
        var second = new InboxViewModel(app.Services, first.Main);
        second.UnreadOnly = false;
        await app.SettleAsync();
        await UiThread.Until(() => second.Groups.Count == 2, "通知の束が並ぶ");

        Assert.DoesNotContain(second.Groups.SelectMany(group => group.Rows), row => row.IsTarget);
    });

    [Fact]
    public Task 指した商品が_商品ページの変更の束に無ければ_印のある最初の束へ送る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(Target, "作り物の衣装"));
        await app.AddItemAsync(Make.Item(Other, "作り物の髪"));
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(Record("up-other", NotificationKind.ItemUpdated, Other, 1));
            list.Add(Record("back-target", NotificationKind.ItemBackOnBooth, Target, 5));
            return list;
        });

        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main, Target);
        inbox.UnreadOnly = false;
        await app.SettleAsync();
        await UiThread.Until(() => inbox.FocusLine is not null, "送り先が決まる");

        var head = Assert.IsType<InboxHeadLine>(inbox.FocusLine);
        Assert.Equal(NotificationKind.ItemBackOnBooth, head.Group.Kind);
        Assert.True(head.Group.IsExpanded);
    });

    [Fact]
    public Task 送り先は_商品ページの変更の束の見出しで_畳んであれば開く() => TestApp.Run(async app =>
    {
        // 畳んだ状態は種類ごとにアプリを閉じるまで覚えるので、先に畳んでおく
        var probe = await OpenAsync(app, focusItemId: null);
        Group(probe, NotificationKind.ItemUpdated).IsExpanded = false;

        var inbox = new InboxViewModel(app.Services, probe.Main, Target);
        inbox.UnreadOnly = false;
        await app.SettleAsync();
        await UiThread.Until(() => inbox.FocusLine is not null, "送り先が決まる");

        var head = Assert.IsType<InboxHeadLine>(inbox.FocusLine);
        Assert.Equal(NotificationKind.ItemUpdated, head.Group.Kind);
        Assert.True(head.Group.IsExpanded);
    });
}
