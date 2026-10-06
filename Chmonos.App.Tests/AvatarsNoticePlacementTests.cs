using System.Windows;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの画面とフォルダの画面の、操作の結果の知らせの出し先（2026-10-03 の方針・2026-10-04 担当NA）。
/// 押した欄・ボタンのすぐ下に出す物は上の段（<see cref="AvatarsViewModel.Status"/>）に出さない、自動で保存する欄の成功は出さない、
/// 打ち直したら消す、別のアバターへ移ったら消す、を確かめる。
/// </summary>
public sealed class AvatarsNoticePlacementTests
{
    private const string AvatarId = "2000001";

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app, params AvatarBaseGroup[] bases)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のアバター2", AvatarOverride = true },
            ],
            BaseGroups = bases,
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        return avatars;
    }

    [Fact]
    public Task 共通素体を足す欄の誤りは_欄の下に出て_上の段には出ず_打ち直すと消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowBaseModeCommand.Execute(null);

        avatars.NewBaseName = "  ";
        avatars.AddBaseCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("共通素体の名前を入れてから押してください。", avatars.AddBaseNote.Text);
        Assert.True(avatars.AddBaseNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);

        avatars.NewBaseName = "作り物の素体";
        Assert.Equal(string.Empty, avatars.AddBaseNote.Text);
    });

    [Fact]
    public Task 共通素体を足せたときは_何も出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowBaseModeCommand.Execute(null);

        avatars.NewBaseName = "作り物の素体";
        avatars.AddBaseCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 1, "足した素体が並ぶ");

        Assert.Equal(string.Empty, avatars.AddBaseNote.Text);
        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task 素体の名前を変えた結果は_名前の欄の下に出て_商品の欄の知らせとは別() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, new AvatarBaseGroup { Name = "作り物の素体A" });
        avatars.ShowBaseModeCommand.Execute(null);
        var row = avatars.Bases.Single();

        // 空のまま確定すると、その行の名前の欄の下に言う
        row.StartRenameCommand.Execute(null);
        row.NameInput = " ";
        row.RenameCommand!.Execute(null);
        Assert.Equal("新しい素体の名前を入れてから押してください。", row.NameNote.Text);
        Assert.True(row.NameNote.IsWarning);
        Assert.Equal(string.Empty, row.ItemIdNote.Text);
        Assert.Equal(string.Empty, avatars.Status);

        // 打ち直すと消える
        row.NameInput = "作り物の素体B";
        Assert.Equal(string.Empty, row.NameNote.Text);

        row.RenameCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 1 && avatars.Bases[0].Name == "作り物の素体B", "名前が変わる");

        // 読み直すと行が作り直されるが、変えた素体が選ばれ、その行の名前の欄の下に結果が出る
        var renamed = avatars.SelectedBase!;
        Assert.Equal("作り物の素体B", renamed.Name);
        Assert.Equal("「作り物の素体A」を「作り物の素体B」に変え、商品 0 件を書き換えました。", renamed.NameNote.Text);
        Assert.False(renamed.NameNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task 素体の商品の欄の誤りと結果は_その欄の下に出る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, new AvatarBaseGroup { Name = "作り物の素体A" });
        avatars.ShowBaseModeCommand.Execute(null);
        var row = avatars.Bases.Single();

        row.ItemIdInput = "ふつうの文字";
        row.SetItemIdCommand!.Execute(null);
        await app.SettleAsync();
        Assert.StartsWith("商品IDが読み取れませんでした。", row.ItemIdNote.Text);
        Assert.True(row.ItemIdNote.IsWarning);
        Assert.Equal(string.Empty, row.NameNote.Text);
        Assert.Equal(string.Empty, avatars.Status);

        row.ItemIdInput = "1234567";
        Assert.Equal(string.Empty, row.ItemIdNote.Text);
        row.SetItemIdCommand.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.SelectedBase?.Summary.Group.ItemId == "1234567", "紐付けが読み直される");

        Assert.Equal("商品 1234567 に紐付けました。", avatars.SelectedBase!.ItemIdNote.Text);
        Assert.False(avatars.SelectedBase.ItemIdNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task 素体を削除した結果は_管理の欄ごと切り替わるので上の段に残る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app, new AvatarBaseGroup { Name = "作り物の素体A" }, new AvatarBaseGroup { Name = "作り物の素体B" });
        avatars.ShowBaseModeCommand.Execute(null);
        app.Answer = _ => MessageBoxResult.OK;

        avatars.Bases.Single(row => row.Name == "作り物の素体A").DeleteCommand!.Execute(null);
        await app.SettleAsync();
        await UiThread.Until(() => avatars.Bases.Count == 1, "素体が消える");

        Assert.Equal("「作り物の素体A」を削除し、商品 0 件を書き換えました。", avatars.Status);
    });

    [Fact]
    public Task アバターの欄の知らせは_それぞれの欄の下に出て_上の段には出ない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        // 名前
        avatars.StartRenameCommand.Execute(null);
        avatars.NameInput = " ";
        avatars.RenameCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("名前を入れてから押してください。", avatars.AvatarNameNote.Text);
        avatars.NameInput = "作り物の名前";
        Assert.Equal(string.Empty, avatars.AvatarNameNote.Text);

        // 呼び方
        avatars.AliasInput = "あ";
        avatars.AddAliasCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("呼び方は2文字以上で入れてください。", avatars.AliasNote.Text);
        Assert.True(avatars.AliasNote.IsWarning);
        avatars.AliasInput = "あい";
        Assert.Equal(string.Empty, avatars.AliasNote.Text);

        // 共通素体
        avatars.BaseInput = "  ";
        avatars.SetBaseCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("共通素体の名前を入れてから押してください。", avatars.BaseFieldNote.Text);
        avatars.BaseInput = "作り物の素体";
        Assert.Equal(string.Empty, avatars.BaseFieldNote.Text);

        // ID のコピーは IDの行の下
        avatars.CopyIdCommand.Execute(null);
        Assert.Equal($"{AvatarId} をコピーしました。", avatars.IdNote.Text);
        Assert.False(avatars.IdNote.IsWarning);

        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task 改変を作った結果は_作る欄の下に出る() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        avatars.ModificationNameInput = "普段着";
        avatars.CreateModificationCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("改変「普段着」を作りました。", avatars.ModificationNote.Text);
        Assert.False(avatars.ModificationNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);

        avatars.ModificationNameInput = "次の名前";
        Assert.Equal(string.Empty, avatars.ModificationNote.Text);
    });

    /// <summary>
    /// 「改変を作る」を素早く2回押しても、改変は1つだけできる（外部の点検 2026-10-06）。
    /// 前は作業中の印が無く、同じ名前の確かめと保存を待つ間に2回目が入り、両方が「まだ無い」と判断して2つできた
    /// </summary>
    [Fact]
    public Task 改変を作るを素早く2回押しても_改変は1つだけできる() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        avatars.ModificationNameInput = "普段着";

        // 書き込みの門を握って1回目を保存の手前で止め、その間に2回目を押す
        using (await Chmonos.Core.Storage.StoreWriteGate.HoldAsync())
        {
            avatars.CreateModificationCommand.Execute(null);
            Assert.False(avatars.CreateModificationCommand.CanExecute(null));
            avatars.CreateModificationCommand.Execute(null);
        }

        await app.SettleAsync();

        Assert.Single((await app.Store.Modifications.LoadAllAsync()).Modifications, modification => modification.Name == "普段着");
        Assert.True(avatars.CreateModificationCommand.CanExecute(null) || avatars.ModificationNameInput.Length == 0);
    });

    [Fact]
    public Task メモを保存できたときは何も出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        avatars.MemoInput = "作り物のメモ";
        // 別のアバターへ移ると、待っているメモを今書く
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000002");
        await app.SettleAsync();

        Assert.Equal("作り物のメモ", app.Store.Avatars.Load().Entries.Single(entry => entry.ItemId == AvatarId).Memo);
        Assert.Equal(string.Empty, avatars.MemoNote.Text);
        Assert.Equal(string.Empty, avatars.Status);
    });

    [Fact]
    public Task 別のアバターへ移ると_前のアバターの欄の知らせは消える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);
        avatars.CopyIdCommand.Execute(null);
        avatars.AliasInput = "あ";
        avatars.AddAliasCommand.Execute(null);
        await app.SettleAsync();
        Assert.NotEqual(string.Empty, avatars.IdNote.Text);
        Assert.NotEqual(string.Empty, avatars.AliasNote.Text);

        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000002");

        Assert.Equal(string.Empty, avatars.IdNote.Text);
        Assert.Equal(string.Empty, avatars.AliasNote.Text);
    });

    [Fact]
    public Task 書き込みの共通口は_呼び手の渡した出し先へ失敗を出す() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        // 登録簿のファイルを掴んで書けなくし、呼び方の追加を失敗させる（実際に起きる、ファイルを掴まれた場合と同じ）
        app.AllowLoggedFailures = true;
        using var hold = new System.IO.FileStream(app.Store.Avatars.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
        avatars.AliasInput = "あいう";
        avatars.AddAliasCommand.Execute(null);
        await app.SettleAsync();

        Assert.StartsWith("呼び方を追加できませんでした。", avatars.AliasNote.Text);
        Assert.True(avatars.AliasNote.IsWarning);
        Assert.Equal(string.Empty, avatars.Status);
    });
}
