using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の画面で改変を作る（feedback 2026-11-18・ユーザ判断）：アバターの見出しの「改変を作る…」と右のアバターの「新しい改変…」は、
/// どちらも名前を窓で聞いて作り、右に開く（前は右の枠の下端の欄に打ち、欄の Enter でも作れなかった）。
/// Enter で決まるのは窓の主のボタン（IsDefault）なので、ここでは窓に答えて確かめる
/// </summary>
public class ModificationCreateTests
{
    private static async Task<ModificationHubViewModel> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物のアバター"));
        var main = await app.StartAsync();
        main.ShowModificationsCommand.Execute(null);
        await app.SettleAsync();
        return Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
    }

    [Fact]
    public Task 窓で付けた名前で作り_右にアバターを出してから聞き_作った改変を右に開く() => TestApp.Run(async app =>
    {
        var hub = await OpenAsync(app);
        HubAvatarDetail? shownWhileAsking = null;
        app.NameModification = model =>
        {
            shownWhileAsking = hub.Detail as HubAvatarDetail;
            model.Name = "  冬の改変 ";
            return true;
        };

        hub.CreateModificationCommand.Execute("1000001");
        await UiThread.Until(() => hub.Detail is ModificationViewModel { Record.Name: "冬の改変" }, "作った改変が右に開く");

        var asked = Assert.Single(app.ModificationNameDialogs);
        Assert.Equal(ModificationNameDialogViewModel.Purpose.Create, asked.For);
        Assert.Equal("作り物のアバター", asked.AvatarName);
        Assert.Equal("1000001", shownWhileAsking?.AvatarItemId);
        Assert.Single((await app.Store.Modifications.LoadAllAsync()).Modifications, record => record.Name == "冬の改変");

        // 同じ名前で作るときは窓の中で言うだけで、押せるまま（別の窓で聞き直さない）
        app.NameModification = model =>
        {
            model.Name = "冬の改変";
            Assert.True(model.IsTaken);
            Assert.True(model.CanCommit);
            return true;
        };
        await hub.CreateModificationAsync("1000001");
        Assert.Equal(2, (await app.Store.Modifications.LoadAllAsync()).Modifications.Count(record => record.Name == "冬の改変"));
        Assert.Empty(app.Notices);
    });

    [Fact]
    public Task 窓でキャンセルしたら作らず_空の名前では押せない() => TestApp.Run(async app =>
    {
        var hub = await OpenAsync(app);

        // 既定の答えは「キャンセル」
        await hub.CreateModificationAsync("1000001");

        var asked = Assert.Single(app.ModificationNameDialogs);
        Assert.Equal(string.Empty, asked.Name);
        Assert.False(asked.CanCommit);
        asked.Name = "   ";
        Assert.False(asked.CanCommit);
        Assert.Empty((await app.Store.Modifications.LoadAllAsync()).Modifications);
        Assert.IsType<HubAvatarDetail>(hub.Detail);
    });

    /// <summary>
    /// 素早く2回押しても、改変は1つだけできる（外部の点検 2026-10-06。アバターの管理と同じ）
    /// </summary>
    [Fact]
    public Task 素早く2回押しても_改変は1つだけできる() => TestApp.Run(async app =>
    {
        var hub = await OpenAsync(app);
        app.NameModification = model =>
        {
            model.Name = "冬の改変";
            return true;
        };

        // 書き込みの門を握って1回目を保存の手前で止め、その間に2回目を押す
        using (await Chmonos.Core.Storage.StoreWriteGate.HoldAsync())
        {
            hub.CreateModificationCommand.Execute("1000001");
            Assert.False(hub.CreateModificationCommand.CanExecute("1000001"));
            hub.CreateModificationCommand.Execute("1000001");
        }

        await app.SettleAsync();

        Assert.Single((await app.Store.Modifications.LoadAllAsync()).Modifications, modification => modification.Name == "冬の改変");
    });
}
