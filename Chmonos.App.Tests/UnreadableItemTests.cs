using System.IO;
using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 読めない商品の記録を通知に出し、行のボタンで直す（ユーザ判断 2026-10-05「前提と 1 と 3 の案」）。
/// 控えがあれば「1つ前の版に戻す」、無ければ「BOOTHから作り直す」。BOOTH に無い商品には出さない。
/// </summary>
public class UnreadableItemTests
{
    private const string ItemId = "9900011";

    private static void Break(TestApp app, string itemId)
        => File.WriteAllText(app.Services.Paths.ItemFile(itemId), "{\n  \"id\": \"" + itemId + "\",\n  \"booth\": {\n");

    private static async Task<(MainViewModel Main, InboxViewModel Inbox, NotificationRow Row)> OpenInboxAsync(TestApp app)
    {
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Any(group => group.Kind == NotificationKind.UnreadableItem), "読めない記録の束が並ぶ");
        var group = inbox.Groups.Single(group => group.Kind == NotificationKind.UnreadableItem);
        return (main, inbox, group.Rows.Single());
    }

    [Fact]
    public Task 起動すると_裏の取得を切っていても_読めない記録が何行目かを添えて通知に出る() => TestApp.Run(async app =>
    {
        // TestApp は裏の取得（ResumeFetchInBackground）を切って始める
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装"));
        Break(app, ItemId);

        await app.StartAsync();
        await app.SettleAsync();

        var notice = Assert.Single(app.Services.Notifications.Load(), record => record.Kind == NotificationKind.UnreadableItem);
        Assert.Equal($"{ItemId}.json", notice.Title);
        Assert.Equal("壊れている場所：4 行目", notice.Detail);
        Assert.Empty(app.Booth.Requests);
    });

    [Fact]
    public Task 控えがあれば_一つ前の版に戻すを出し_確かめて押すと戻って検索に並ぶ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装"));
        Break(app, ItemId);
        var (main, inbox, row) = await OpenInboxAsync(app);

        Assert.Equal("1つ前の版に戻す", row.ActionText);
        Assert.Equal("0 件", main.Search.ResultSummary);

        app.Answer = _ => MessageBoxResult.OK;
        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => inbox.ListNotice.Text.Length > 0, "直した知らせが出る");
        await app.SettleAsync();

        var confirm = Assert.Single(app.Notices);
        Assert.Equal("1つ前の版に戻す", confirm.Caption);
        Assert.Contains("_broken", confirm.Text);
        Assert.Equal("作り物の衣装", (await app.Store.Items.LoadAsync(ItemId))!.Booth.Name);
        Assert.Single(Directory.GetFiles(app.Services.Paths.BrokenItemsDir));
        Assert.Empty(app.Booth.Requests);
        await UiThread.Until(() => main.Search.ResultSummary == "1 件", "戻した商品が検索に並ぶ");
        Assert.DoesNotContain(inbox.Groups, group => group.Kind == NotificationKind.UnreadableItem);
    });

    [Fact]
    public Task 控えが無ければ_BOOTHから作り直すを出し_確認の窓で戻らない物を言う() => TestApp.Run(async app =>
    {
        Break(app, ItemId);
        app.Booth.HasItem(ItemId, "作り直した衣装");
        var (_, inbox, row) = await OpenInboxAsync(app);

        Assert.Equal("BOOTHから作り直す", row.ActionText);

        // やめれば何も動かない
        row.ActionCommand!.Execute(null);
        await app.SettleAsync();
        Assert.Empty(app.Booth.Requests);
        Assert.False(Directory.Exists(app.Services.Paths.BrokenItemsDir));

        var confirm = Assert.Single(app.Notices);
        Assert.Contains("タグ・メモ・購入記録などは戻りません。", confirm.Text);
        Assert.Contains("手元のファイルは、そのフォルダを取り込み直すと付け直します。", confirm.Text);

        app.Answer = _ => MessageBoxResult.OK;
        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => inbox.ListNotice.Text.Length > 0, "作り直した知らせが出る");
        await app.SettleAsync();

        Assert.Equal("作り直した衣装", (await app.Store.Items.LoadAsync(ItemId))!.Booth.Name);
        Assert.NotEmpty(app.Booth.Requests);
        Assert.Single(Directory.GetFiles(app.Services.Paths.BrokenItemsDir));
    });

    [Fact]
    public Task BOOTHに無い商品で控えも無ければ_ボタンを出さない() => TestApp.Run(async app =>
    {
        Break(app, "local-9900012");
        var (_, _, row) = await OpenInboxAsync(app);

        Assert.False(row.HasAction);
    });
}
