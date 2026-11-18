using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 通知の「途中で止まった操作」の行のボタン（ユーザ判断 2026-11-19・open 145）。
/// 続けられる物（記録が残る）は「今すぐ続ける」。内容が変わって止めた物は「もう一度操作する」で、前の値を入れて窓を開くだけ（自動では当てない）。
/// 続きそのものの中身は Core の PendingOperationTests が見る。ここは行の出し分けと、押した後の行き先
/// </summary>
public class InboxUnfinishedOperationTests
{
    private const string ItemId = "9900141";
    private const string ToId = "9900142";

    private static NotificationRecord Record(string detail, OperationRetry? retry = null, bool resolved = false) => new()
    {
        Id = PendingOperationRunner.NotificationPrefix + "op-1",
        Kind = NotificationKind.UnfinishedOperation,
        Title = "作り物の操作",
        Detail = detail,
        CreatedAt = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.FromHours(9)),
        IsResolved = resolved,
        Retry = retry,
    };

    private static readonly OperationRetry AttributeRetry = new()
    {
        Kind = PendingOperationKind.RenameAttribute,
        OldName = "作り物の質",
        NewName = "作り物の質感",
    };

    [Fact]
    public void 続けられる行は今すぐ続ける_変わって止めた行はもう一度操作する_ほかは出さない()
    {
        var none = new HashSet<string>();
        Assert.Equal("今すぐ続ける", InboxViewModel.ActionLabel(Record(PendingOperationRunner.FailedDetail), none));
        Assert.Equal("もう一度操作する", InboxViewModel.ActionLabel(Record(PendingOperationRunner.ChangedDetail, AttributeRetry), none));

        // 値を持たない古い知らせ・書き込めなかっただけの行・解消済みの行には出さない
        Assert.Equal(string.Empty, InboxViewModel.ActionLabel(Record(PendingOperationRunner.ChangedDetail), none));
        Assert.Equal(string.Empty, InboxViewModel.ActionLabel(Record("操作は済みましたが、保存先に書き込めませんでした。"), none));
        Assert.Equal(string.Empty, InboxViewModel.ActionLabel(Record(PendingOperationRunner.FailedDetail, resolved: true), none));
    }

    private static async Task<(MainViewModel Main, InboxViewModel Inbox, NotificationRow Row)> OpenAsync(TestApp app, NotificationRecord record)
    {
        await app.Store.Notifications.UpdateAsync(list =>
        {
            list.Add(record);
            return list;
        });
        var main = await app.StartAsync();
        var inbox = new InboxViewModel(app.Services, main);
        await app.SettleAsync();
        await UiThread.Until(() => inbox.Groups.Count == 1, "通知の束が並ぶ");
        return (main, inbox, inbox.Groups[0].Rows[0]);
    }

    [Fact]
    public Task もう一度操作するは属性の管理で前の名前を入れた窓を開き_押すまで何も書かない() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "作り物の質" }] });
        var (main, _, row) = await OpenAsync(app, Record(PendingOperationRunner.ChangedDetail, AttributeRetry));

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => app.RenameDialogs.Count == 1, "名前を変える窓が開く");
        await app.SettleAsync();

        Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        var dialog = app.RenameDialogs[0];
        Assert.Equal("作り物の質", dialog.Name);
        Assert.Equal("作り物の質感", dialog.Input);
        Assert.Equal("作り物の質", Assert.Single(app.Store.Attributes.Load().Attributes).Name);
    });

    [Fact]
    public Task もう一度操作するは小分類の名前の変更も前の名前を入れた窓を開く() => TestApp.Run(async app =>
    {
        await app.Store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "作り物の大分類", Subs = [new UserTagSub { Name = "作り物の小分類" }] }],
        });
        var retry = new OperationRetry
        {
            Kind = PendingOperationKind.RenameUserTag,
            Top = "作り物の大分類",
            Sub = "作り物の小分類",
            NewName = "作り物の小分類2",
        };
        var (main, _, row) = await OpenAsync(app, Record(PendingOperationRunner.ChangedDetail, retry));

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => app.RenameDialogs.Count == 1, "名前を変える窓が開く");

        Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        Assert.Equal("作り物の小分類", app.RenameDialogs[0].Name);
        Assert.Equal("作り物の小分類2", app.RenameDialogs[0].Input);
    });

    [Fact]
    public Task もう一度操作するはIDの変更なら元の商品を開いて移す先を入れた窓を開く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の衣装"));
        var retry = new OperationRetry { Kind = PendingOperationKind.ChangeItemId, FromId = ItemId, ToId = ToId };
        var (main, _, row) = await OpenAsync(app, Record(PendingOperationRunner.ChangedDetail, retry));

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => app.ChangeIdDialogs.Count == 1, "IDを変える窓が開く");
        await app.SettleAsync();

        Assert.Equal(ItemId, Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id);
        Assert.Equal(ToId, app.ChangeIdDialogs[0].IdInput);
        Assert.NotNull(await app.Store.Items.LoadAsync(ItemId));
        Assert.Null(await app.Store.Items.LoadAsync(ToId));
    });

    [Fact]
    public Task もう一度操作するで元の商品が無ければ窓を開かずに言う() => TestApp.Run(async app =>
    {
        var retry = new OperationRetry { Kind = PendingOperationKind.ChangeItemId, FromId = ItemId, ToId = ToId };
        var (main, inbox, row) = await OpenAsync(app, Record(PendingOperationRunner.ChangedDetail, retry));

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => row.ActionNotice.Text.Length > 0, "行の近くに出る");

        Assert.Equal("元の商品がもう無いので、操作し直せません。", row.ActionNotice.Text);
        Assert.Empty(app.ChangeIdDialogs);
        Assert.IsNotType<ItemViewModel>(main.CurrentViewModel);
    });

    [Fact]
    public Task 今すぐ続けるで記録がもう無ければ済んでいたと言って行を片付ける() => TestApp.Run(async app =>
    {
        var (_, inbox, row) = await OpenAsync(app, Record(PendingOperationRunner.FailedDetail));

        row.ActionCommand!.Execute(null);
        await UiThread.Until(() => inbox.ListNotice.Text.Length > 0, "結果が出る");
        await app.SettleAsync();

        Assert.Equal("続きはもう済んでいました。", inbox.ListNotice.Text);
        Assert.True(Assert.Single(app.Store.Notifications.Load()).IsResolved);
    });

    /// <summary>
    /// 起動時の続きが同じ記録を当てている間に押すと、続ける側は飛ばして0件を返し、知らせは解消されずに残る。
    /// それを「もう済んでいました」と言うと、まだ当てている最中の操作を済んだと言ってしまう
    /// </summary>
    [Fact]
    public void 起動時の続きが当てている間は進めている最中と言う()
    {
        var open = new Chmonos.Core.Models.NotificationRecord
        {
            Id = "pending-op:x",
            Kind = Chmonos.Core.Models.NotificationKind.UnfinishedOperation,
            Title = "t",
            Detail = "d",
            CreatedAt = DateTimeOffset.UnixEpoch,
        };

        Assert.Equal("今、続きを進めています。少し待ってから見てください。", ViewModels.InboxViewModel.ResumeOutcomeText(true, open));
        Assert.Equal("続きはもう済んでいました。", ViewModels.InboxViewModel.ResumeOutcomeText(true, open with { IsResolved = true }));
        Assert.Equal("続きはもう済んでいました。", ViewModels.InboxViewModel.ResumeOutcomeText(true, null));
    }
}
