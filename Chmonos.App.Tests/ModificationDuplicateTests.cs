using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 改変の複製（メモ44・2026-10-05）。左の一覧の行の右クリックと詳細の帯のボタンは同じ道。
/// 名前は窓で聞き、窓には複製元の名前を入れて開く（feedback 2026-11-18・ユーザ判断）。複製したら新しい改変を右に開く。
/// </summary>
public class ModificationDuplicateTests
{
    private static async Task<(ModificationHubViewModel Hub, ModificationRecord Source, MainViewModel Main)> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("9900002", "作り物の衣装"));
        var main = await app.StartAsync();
        var created = Assert.IsType<CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new UiCommand.CreateModification("9900001", "夏の改変")));
        await app.Services.Commands.ExecuteAsync(new UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = "9900002" }));
        await app.Services.Commands.ExecuteAsync(new UiCommand.SetModificationMemo(created.Record.Id, "靴は最後"));

        var hub = new ModificationHubViewModel(app.Services, main, main.Thumbnails, ModificationHubLevel.Modification);
        await UiThread.Until(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
        var source = (await app.Services.Modifications.LoadAsync(created.Record.Id))!;
        return (hub, source, main);
    }

    /// <summary>窓で名前を替えて確定する。</summary>
    private static void AnswerName(TestApp app, string name) => app.NameModification = model =>
    {
        model.Name = name;
        return true;
    };

    [Fact]
    public Task 行の右クリックの複製で_元の名前を入れた窓が開き_付けた名前で複製ができて右に開く() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        AnswerName(app, "冬の改変");

        hub.DuplicateModificationCommand.Execute(source);
        await UiThread.Until(() => hub.Detail is ModificationViewModel { Record.Name: "冬の改変" }, "複製が右に開く");

        // 窓は複製元の名前を入れて開き、同じ名前が既にあることを言う（押せるまま）
        var asked = Assert.Single(app.ModificationNameDialogs);
        Assert.Equal(ModificationNameDialogViewModel.Purpose.Duplicate, asked.For);
        Assert.Equal("作り物のアバター", asked.AvatarName);
        asked.Name = "夏の改変";
        Assert.True(asked.IsTaken);
        Assert.True(asked.CanCommit);

        var detail = Assert.IsType<ModificationViewModel>(hub.Detail);
        Assert.NotEqual(source.Id, detail.Record.Id);
        Assert.True(detail.IsEmbedded);
        Assert.Equal("靴は最後", detail.Record.Memo);
        Assert.Equal("9900002", Assert.Single(detail.Record.Members).ItemId);

        // 保存されている（元と並ぶ）
        var all = (await app.Services.Modifications.LoadAllAsync()).Modifications;
        Assert.Equal(2, all.Count);
    });

    [Fact]
    public Task 詳細のボタンも同じ道で複製し_元の名前のまま確定すれば同じ名前で作る() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        app.NameModification = _ => true;
        hub.ShowModificationCommand.Execute(source);
        var first = Assert.IsType<ModificationViewModel>(hub.Detail);

        first.DuplicateCommand.Execute(null);
        await UiThread.Until(() => hub.Detail is ModificationViewModel copy && copy.Record.Id != source.Id, "複製が開く");

        Assert.Equal("夏の改変", Assert.Single(app.ModificationNameDialogs).Name);
        Assert.Equal("夏の改変", Assert.IsType<ModificationViewModel>(hub.Detail).Record.Name);
        Assert.Equal(2, (await app.Services.Modifications.LoadAllAsync()).Modifications.Count(record => record.Name == "夏の改変"));
    });

    [Fact]
    public Task 窓でキャンセルしたら_複製しない() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);

        // 既定の答えは「キャンセル」
        await hub.DuplicateModificationAsync(source.Id);

        Assert.Single(app.ModificationNameDialogs);
        Assert.Single((await app.Services.Modifications.LoadAllAsync()).Modifications);
        Assert.Null(hub.Detail);
    });

    [Fact]
    public Task 元が無いときは_窓を出さず何も開かず失敗の文を出す() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        await app.Services.Commands.ExecuteAsync(new UiCommand.DeleteModification(source.Id));

        await hub.DuplicateModificationAsync(source.Id);

        Assert.Empty(app.ModificationNameDialogs);
        Assert.Null(hub.Detail);
        Assert.Equal("対象の改変が見つかりませんでした。", hub.Status);
    });

    /// <summary>
    /// 複製を素早く2回押しても、複製は1つだけできる（外部の点検 2026-10-06）。
    /// 書き込みの門を握って1回目を保存の手前で止め、その間に一覧の右クリックと詳細のボタンから2回目を押す
    /// </summary>
    [Fact]
    public Task 複製を素早く2回押しても_複製は1つだけできる() => TestApp.Run(async app =>
    {
        var (hub, source, _) = await OpenAsync(app);
        AnswerName(app, "冬の改変");
        hub.ShowModificationCommand.Execute(source);
        var detail = Assert.IsType<ModificationViewModel>(hub.Detail);

        using (await Chmonos.Core.Storage.StoreWriteGate.HoldAsync())
        {
            detail.DuplicateCommand.Execute(null);
            Assert.False(hub.DuplicateModificationCommand.CanExecute(source));
            detail.DuplicateCommand.Execute(null);
            hub.DuplicateModificationCommand.Execute(source);
        }

        await app.SettleAsync();

        var all = (await app.Store.Modifications.LoadAllAsync()).Modifications;
        Assert.Single(all, modification => modification.Name == "冬の改変");
    });
}
