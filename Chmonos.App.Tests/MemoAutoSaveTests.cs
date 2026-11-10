using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// メモの自動保存（<see cref="MemoAutoSave{TOwner}"/>・R2・2026-11-10）。前は6か所に写され、どの値を信じるかが画面ごとに違っていた。
///
/// 取り違えやすいのは「選び直し」の前後：待ちの間に選び直すと、打った文が移った先へ書かれる・前の物に打った文が消える・
/// 開いただけの物へ読んだ文を書き戻す。部品そのものと、選び直しのある3画面（属性・タグ・アバター）の両方で確かめる
/// </summary>
public class MemoAutoSaveTests
{
    private sealed class Row(string name)
    {
        public string Name { get; } = name;
    }

    private static (MemoAutoSave<Row> Memo, List<(string Owner, string Text)> Written) Make()
    {
        var written = new List<(string Owner, string Text)>();
        var memo = new MemoAutoSave<Row>((row, text) =>
        {
            written.Add((row.Name, text));
            return Task.CompletedTask;
        });
        return (memo, written);
    }

    // ---- 部品 ----

    [Fact]
    public Task 続けて打つと_止まってから最後の文を1回だけ書く() => UiThread.Run(async () =>
    {
        var (memo, written) = Make();
        var a = new Row("甲");

        memo.Edited(a, "あ");
        memo.Edited(a, "あい");
        memo.Edited(a, "あいう");
        Assert.Empty(written);

        await UiThread.Until(() => written.Count > 0, "待ちが明けて書く");
        Assert.Equal([("甲", "あいう")], written);
        Assert.False(memo.IsPending);
    });

    [Fact]
    public Task 待ちの間に選び直すと_打った文は元の持ち主へ書き_移った先には書かない() => UiThread.Run(async () =>
    {
        var (memo, written) = Make();
        var a = new Row("甲");
        var b = new Row("乙");

        memo.Edited(a, "甲のメモ");
        memo.Replace(() => memo.Edited(b, "乙の読んだメモ"));
        memo.Edited(b, "乙に打ったメモ");
        await memo.FlushAsync();

        Assert.Equal([("甲", "甲のメモ"), ("乙", "乙に打ったメモ")], written);
    });

    [Fact]
    public Task 欄の入れ替えは打ったことに数えず_開いただけの物へは書かない() => UiThread.Run(async () =>
    {
        var (memo, written) = Make();

        memo.Replace(() => memo.Edited(new Row("乙"), "読んだメモ"));
        Assert.False(memo.IsPending);
        await memo.FlushAsync();

        Assert.Empty(written);
    });

    [Fact]
    public Task 画面を離れる前に書き切ると_待たずに書き_2回目は何もしない() => UiThread.Run(async () =>
    {
        var (memo, written) = Make();
        memo.Edited(new Row("甲"), "打ちかけ");

        await memo.FlushAsync();
        await memo.FlushAsync();

        Assert.Equal([("甲", "打ちかけ")], written);
    });

    [Fact]
    public Task 何も選んでいないときに打った文は書かない() => UiThread.Run(async () =>
    {
        var (memo, written) = Make();
        memo.Edited(null, "持ち主の無い文");
        await memo.FlushAsync();
        Assert.Empty(written);
    });

    // ---- 画面（選び直しのある3つ） ----

    [Fact]
    public Task 属性の管理_打ってすぐ別の属性へ移り続けて打つと_それぞれの属性に書かれる() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲" }, new AttributeDefinition { Name = "作り物の乙", Memo = "乙の元のメモ" }],
        });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count == 2, "属性の管理の読み込みが済む");

        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の甲");
        attributes.MemoDraft = "甲に打ったメモ";
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の乙");
        Assert.Equal("乙の元のメモ", attributes.MemoDraft);
        attributes.MemoDraft = "乙に打ったメモ";
        await attributes.FlushPendingWritesAsync();
        await app.SettleAsync();

        var saved = app.Store.Attributes.Load().Attributes;
        Assert.Equal("甲に打ったメモ", saved.Single(entry => entry.Name == "作り物の甲").Memo);
        Assert.Equal("乙に打ったメモ", saved.Single(entry => entry.Name == "作り物の乙").Memo);
    });

    [Fact]
    public Task タグの管理_打ってすぐ別の大分類へ移り続けて打つと_それぞれの大分類に書かれる() => TestApp.Run(async app =>
    {
        await app.Store.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "作り物の甲" }, new UserTagTop { Name = "作り物の乙", Memo = "乙の元のメモ" }],
        });
        var main = await app.StartAsync();
        main.ShowTagManageCommand.Execute(null);
        var tags = Assert.IsType<TagManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => tags.TopCount == 2, "タグの管理の読み込みが済む");

        tags.Selected = tags.Tops.First(row => row.Name == "作り物の甲");
        tags.MemoDraft = "甲に打ったメモ";
        tags.Selected = tags.Tops.First(row => row.Name == "作り物の乙");
        Assert.Equal("乙の元のメモ", tags.MemoDraft);
        tags.MemoDraft = "乙に打ったメモ";
        await tags.FlushPendingWritesAsync();
        await app.SettleAsync();

        var saved = app.Store.UserTags.Load().Tops;
        Assert.Equal("甲に打ったメモ", saved.Single(top => top.Name == "作り物の甲").Memo);
        Assert.Equal("乙に打ったメモ", saved.Single(top => top.Name == "作り物の乙").Memo);
    });

    [Fact]
    public Task アバター_打ってすぐ別のアバターへ移り続けて打つと_それぞれのアバターに書かれ_戻ると打った文が出る() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "2000001", BoothName = "作り物のアバター甲", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "2000002", BoothName = "作り物のアバター乙", AvatarOverride = true, Memo = "乙の元のメモ" },
            ],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count == 2, "アバターの読み込みが済む");

        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000001");
        avatars.MemoInput = "甲に打ったメモ";
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000002");
        Assert.Equal("乙の元のメモ", avatars.MemoInput);
        avatars.MemoInput = "乙に打ったメモ";
        await avatars.FlushPendingWritesAsync();
        await app.SettleAsync();

        var saved = app.Store.Avatars.Load().Entries;
        Assert.Equal("甲に打ったメモ", saved.Single(entry => entry.ItemId == "2000001").Memo);
        Assert.Equal("乙に打ったメモ", saved.Single(entry => entry.ItemId == "2000002").Memo);

        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "2000001");
        Assert.Equal("甲に打ったメモ", avatars.MemoInput);
    });

    [Fact]
    public Task 選び直しただけでは_どの画面も書かない() => TestApp.Run(async app =>
    {
        await app.Store.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "作り物の甲", Memo = "甲の元のメモ" }, new AttributeDefinition { Name = "作り物の乙" }],
        });
        var main = await app.StartAsync();
        main.ShowAttributeManageCommand.Execute(null);
        var attributes = Assert.IsType<AttributeManageViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => attributes.Rows.Count == 2, "属性の管理の読み込みが済む");
        var file = app.Services.Paths.AttributesFile;
        var before = System.IO.File.GetLastWriteTimeUtc(file);

        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の甲");
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の乙");
        attributes.Selected = attributes.Rows.First(row => row.Name == "作り物の甲");
        await attributes.FlushPendingWritesAsync();
        await app.SettleAsync();

        Assert.Equal(before, System.IO.File.GetLastWriteTimeUtc(file));
    });
}
