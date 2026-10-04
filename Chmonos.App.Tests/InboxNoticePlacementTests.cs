using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 要確認の行のボタンの結果が、失敗なら押した行の知らせ、成功（行が片付く）なら一覧の見出しの下の知らせへ出ること
/// （2026-10-03 のユーザの方針）。行は一覧を読み直すたびに作り直されるので、知らせは作り直した行にも残る。
/// </summary>
public class InboxNoticePlacementTests
{
    private const string ItemId = "1000001";

    private static NotificationRecord BackOnBooth() => new()
    {
        Id = "back-1",
        Kind = NotificationKind.ItemBackOnBooth,
        Title = "作り物の復活",
        Detail = string.Empty,
        ItemId = ItemId,
        CreatedAt = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(9)),
    };

    private static async Task<(InboxViewModel Inbox, NotificationRow Row)> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装"));
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(BackOnBooth());
            return list;
        });

        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count == 1, "要確認の束が並ぶ");
        return (inbox, inbox.Groups[0].Rows[0]);
    }

    [Fact]
    public Task 取り直しに失敗したら_押した行の下に出て_一覧の上には出ない() => TestApp.Run(async app =>
    {
        var (inbox, row) = await OpenAsync(app);

        // 作り物の BOOTH は何も教えなければ 404 を返す
        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => row.ActionNotice.IsWarning, "失敗が行に出る");
        await app.SettleAsync();

        Assert.NotEqual(string.Empty, row.ActionNotice.Text);
        Assert.Equal(string.Empty, inbox.ListNotice.Text);
        Assert.Equal(string.Empty, inbox.StatusText);

        // 一覧を読み直して行が作り直されても、同じ行の知らせとして残る
        await inbox.ReloadAsync();
        await UiThread.Until(() => inbox.Groups.Count == 1, "束が並び直る");
        Assert.Same(row.ActionNotice, inbox.Groups[0].Rows[0].ActionNotice);
        Assert.True(inbox.Groups[0].Rows[0].ActionNotice.IsWarning);
    });

    [Fact]
    public Task 取り直しに成功したら_一覧の見出しの下に出て_行の知らせは消える() => TestApp.Run(async app =>
    {
        var (inbox, row) = await OpenAsync(app);
        app.Booth.HasItem(ItemId, "作り物の衣装");

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => inbox.ListNotice.Text.Length > 0, "成功が一覧の見出しの下に出る");
        await app.SettleAsync();

        Assert.Equal("BOOTHの商品ページから情報を取り直しました。", inbox.ListNotice.Text);
        Assert.False(inbox.ListNotice.IsWarning);
        Assert.Equal(string.Empty, row.ActionNotice.Text);
        Assert.Equal(string.Empty, inbox.StatusText);
    });
}
