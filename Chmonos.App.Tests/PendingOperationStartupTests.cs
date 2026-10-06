using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 前の起動で途中で止まった操作（やりかけの記録 pending-operations.json）は、主画面を作ったときに続きを済ませる（ユーザ判断 2026-10-06「A」）。
/// 続けられなかった物は通知の「途中で止まった操作」の束に出る。
/// </summary>
public class PendingOperationStartupTests
{
    private const string Tagged = "9900031";

    [Fact]
    public Task 起動したとき_止まっていたタグの名前の変更の続きを済ませる() => TestApp.Run(async app =>
    {
        // 一覧は書き終え、商品はまだ古い名前のまま止まった様子
        await app.Store.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "服" }] });
        var item = Make.Item(Tagged, "作り物の服");
        await app.AddItemAsync(item with { Local = item.Local with { UserTags = [new UserTagAssignment { Top = "衣装" }] } });
        await app.Store.PendingOperations.SaveAsync(
        [
            new PendingOperation
            {
                Id = "op1",
                Kind = PendingOperationKind.RenameUserTag,
                StartedAt = DateTimeOffset.Now,
                Top = "衣装",
                NewName = "服",
            },
        ]);

        await app.StartAsync();
        await UiThread.Until(() => app.Store.PendingOperations.Load().Count == 0, "やりかけの記録が消える");

        Assert.Equal("服", (await app.Store.Items.LoadAsync(Tagged))!.Local.UserTags.Single().Top);
    });

    [Fact]
    public Task 続けられなかった操作は_通知の束の見出しと説明で出る() => TestApp.Run(async app =>
    {
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(new NotificationRecord
            {
                Id = PendingOperationRunner.NotificationPrefix + "op1",
                Kind = NotificationKind.UnfinishedOperation,
                Title = "タグ「衣装」の名前の変更（→「服」）",
                Detail = "途中で止まっていた操作を続けられませんでした。次に起動したときに、もう一度続けます。",
                CreatedAt = DateTimeOffset.Now,
                IsStrong = true,
            });
            return list;
        });

        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main) { UnreadOnly = false };
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count > 0, "通知の束が並ぶ");

        var group = Assert.Single(inbox.Groups);
        Assert.Equal("途中で止まった操作", group.KindText);
        Assert.Equal("IDや名前の変更が途中で止まり、続きを済ませられなかった操作です。", group.Description);
        Assert.Null(Assert.Single(group.Rows).Picture);
    });
}
