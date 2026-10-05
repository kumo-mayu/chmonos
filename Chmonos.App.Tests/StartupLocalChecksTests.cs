using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 起動時の裏の作業のうち、通信しない確かめ（通知の整理・ページの作りの確認・手で直した JSON の確認）。
/// 設定「起動したとき、裏で取得を始める」は BOOTH へ行く段だけを止める（`background-and-network.md`）。
/// 前は設定を切ると丸ごと戻り、通信しない確かめまで走らなかった（file-lifecycle.md「気になった所」5）。
/// </summary>
public class StartupLocalChecksTests
{
    /// <summary>期限の来た商品（⑦の対象）と、ファイル名と中の商品IDが違う JSON を置く。</summary>
    private static async Task PlaceDueItemAndHandEditAsync(TestApp app)
    {
        var due = Make.Item("9900001", "作り物の衣装");
        await app.AddItemAsync(due with { Local = due.Local with { NextFetchDueAt = DateTimeOffset.UnixEpoch } });

        // 手で名前を変えた JSON：ファイル名は 9900002、中の商品IDは 9900003
        await app.AddItemAsync(Make.Item("9900003", "作り物の髪"));
        File.Move(app.Store.Paths.ItemFile("9900003"), app.Store.Paths.ItemFile("9900002"));
    }

    [Fact]
    public Task 裏の取得を切っていても_手で直したJSONの確認は起動時に走り_BOOTHへは問い合わせない() => TestApp.Run(async app =>
    {
        await PlaceDueItemAndHandEditAsync(app);

        await app.StartAsync();
        await app.SettleAsync();

        var notifications = app.Store.Notifications.Load();
        Assert.Contains(notifications, record => record.Id == "hand-edit" && !record.IsResolved);
        Assert.Empty(app.Booth.Requests);
    });

    /// <summary>上の試験の対になる確かめ：同じ置き方で設定を入れれば⑦が問い合わせる（置き方が⑦の対象になっていること）。</summary>
    [Fact]
    public Task 裏の取得を入れていれば_同じ置き方で期限の来た商品を問い合わせる() => TestApp.Run(async app =>
    {
        await PlaceDueItemAndHandEditAsync(app);
        await app.ChangeSettingsAsync(settings => settings with { ResumeFetchInBackground = true });

        await app.StartAsync();
        await app.SettleAsync();
        await UiThread.Until(() => app.Booth.Requests.Count > 0, "期限の来た商品を問い合わせる");

        var notifications = app.Store.Notifications.Load();
        Assert.Contains(notifications, record => record.Id == "hand-edit");
    });
}
