using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面と改変の詳細の、操作の結果の知らせの置き場（メモ20-③）。
/// 押した欄・ボタン・行のすぐ下へ出し、上の帯・左の欄の下の帯（Status）には出さない。
/// 自動保存の成功は出さない。
/// </summary>
public class ModificationNoticeTests
{
    private static async Task<ModificationViewModel> OpenDetailAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の衣装"));
        var main = await app.StartAsync();

        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("1000001", "夏の改変")));
        var modification = new ModificationViewModel(created.Record, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => modification.ItemSuggestions.Count > 0, "候補が出る");
        return modification;
    }

    [Fact]
    public Task 名前を自動で保存できたときは_何も言わない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.NameInput = "冬の改変";
        await modification.FlushPendingWritesAsync();

        Assert.Equal("冬の改変", modification.Record.Name);
        Assert.Equal(string.Empty, modification.NameNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task メモを自動で保存できたときは_何も言わない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.MemoInput = "貫通は袖だけ直す";
        await modification.FlushPendingWritesAsync();

        Assert.Equal("貫通は袖だけ直す", modification.Record.Memo);
        Assert.Equal(string.Empty, modification.MemoNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 候補に無い名前で足そうとすると_足す欄の下に警告を出し_上の帯には出さない() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.AddMemberCommand.Execute("どこにも無い商品");
        await UiThread.Until(() => modification.AddNotice.HasText, "足す欄の下に出る");

        Assert.True(modification.AddNotice.IsWarning);
        Assert.Contains("手元に見つかりません", modification.AddNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 使ったものを足すと_足す欄の下に済んだことを出す() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);

        modification.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => modification.Members.Count == 1, "足される");

        Assert.False(modification.AddNotice.IsWarning);
        Assert.Equal("「作り物の衣装」を追加しました。", modification.AddNotice.Text);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 行を外すと_使ったものの一覧の見出しの近くに出す() => TestApp.Run(async app =>
    {
        var modification = await OpenDetailAsync(app);
        modification.AddMemberCommand.Execute("作り物の衣装");
        await UiThread.Until(() => modification.Members.Count == 1, "足される");

        modification.RemoveMemberCommand.Execute(modification.Members.Single());
        await UiThread.Until(() => modification.MembersNotice.Text.Contains("外しました"), "一覧の近くに出る");

        Assert.False(modification.MembersNotice.IsWarning);
        Assert.Equal(string.Empty, modification.Status);
    });

    [Fact]
    public Task 改変の画面で改変を作る案内は_アバターの右の詳細の欄の下に出し_左の帯には出さない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowModificationsCommand.Execute(null);
        await app.SettleAsync();
        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);

        hub.StartCreateCommand.Execute("1000001");

        var detail = Assert.IsType<HubAvatarDetail>(hub.Detail);
        Assert.Contains("改変を作る", detail.Notice.Text);
        Assert.Equal(string.Empty, hub.Status);

        // 右の詳細を開き直しても、同じアバターの詳細には同じ知らせが出る（一覧を読み直したときに消えない）
        hub.ShowAvatarCommand.Execute("1000001");
        Assert.Contains("改変を作る", Assert.IsType<HubAvatarDetail>(hub.Detail).Notice.Text);

        // ほかのアバターの詳細には出ない
        await app.AddItemAsync(Make.Item("1000009", "作り物の別のアバター"));
        hub.ShowAvatarCommand.Execute("1000009");
        Assert.Equal(string.Empty, Assert.IsType<HubAvatarDetail>(hub.Detail).Notice.Text);
    });

    /// <summary>
    /// 改変の画面で「改変を作る」を素早く2回押しても、改変は1つだけできる（外部の点検 2026-10-06。アバターの管理と同じ）
    /// </summary>
    [Fact]
    public Task 改変の画面で改変を作るを素早く2回押しても_改変は1つだけできる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowModificationsCommand.Execute(null);
        await app.SettleAsync();
        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        hub.StartCreateCommand.Execute("1000001");
        Assert.IsType<HubAvatarDetail>(hub.Detail).NameInput = "冬の改変";

        // 書き込みの門を握って1回目を保存の手前で止め、その間に2回目を押す
        using (await Chmonos.Core.Storage.StoreWriteGate.HoldAsync())
        {
            hub.CreateModificationCommand.Execute(null);
            Assert.False(hub.CreateModificationCommand.CanExecute(null));
            hub.CreateModificationCommand.Execute(null);
        }

        await app.SettleAsync();

        Assert.Single((await app.Store.Modifications.LoadAllAsync()).Modifications, modification => modification.Name == "冬の改変");
    });
}
