using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面の「保存して次へ」「スキップ」：押したら保存先に書かれ、次の商品へ進み、ナビの未編集の数が減ること。
///
/// 画面から書き込みまでの道（ViewModel → <c>UiCommand</c> → 保存先 → 検索の写しへの知らせ）を通して確かめる。
/// 前は、保存して JSON を開き、ナビの数を撮って確かめていた。
/// </summary>
public class EditSaveTests
{
    private static async Task<(MainViewModel Main, EditViewModel Edit)> OpenEditAsync(TestApp app, params ItemRecord[] items)
    {
        foreach (var item in items)
        {
            await app.AddItemAsync(item);
        }

        // 最後の1件を保存した後の「n 秒後に戻ります」は時計で進むので、試験では切っておく
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });

        var main = await app.StartAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        return (main, Assert.IsType<EditViewModel>(main.CurrentViewModel));
    }

    [Fact]
    public Task 未編集の商品を_並びの順に1件ずつ出す() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"));

        Assert.True(edit.HasItem);
        Assert.Equal("1 / 2 件", edit.StepText);
        Assert.Empty(edit.Tags);
    });

    [Fact]
    public Task タグを付けて保存すると_保存先に書かれ_次の商品へ進む() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"));
        var first = edit.CurrentItemId!;
        Assert.Equal(2, main.NeedsEditCount);

        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.Memo = "夏に使う";
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        // 保存先に書かれている（商品の記録と、ユーザータグの一覧の両方）
        var saved = await app.Store.Items.LoadAsync(first);
        Assert.Equal("衣装", Assert.Single(saved!.Local.UserTags).Top);
        Assert.Equal("夏に使う", saved.Local.Memo);
        Assert.Contains(app.Store.UserTags.Load().Tops, top => top.Name == "衣装");

        // 次の商品へ進み、ナビの「未:」がその場で減る
        Assert.NotEqual(first, edit.CurrentItemId);
        Assert.Equal("2 / 2 件", edit.StepText);
        Assert.Empty(edit.Tags);
        Assert.Equal(1, main.NeedsEditCount);

        // 検索の写しも、読み直さずに新しい中身になっている
        Assert.Single(main.Search.FindItem(first)!.Local.UserTags);
    });

    [Fact]
    public Task 保存は_この画面で変えた項目だけを書き_ほかの項目は今の値を残す() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"));
        var id = edit.CurrentItemId!;

        // 編集画面を開いている間に、よそ（商品ページ）で星を付けた
        var current = (await app.Store.Items.LoadAsync(id))!;
        await app.AddItemAsync(current with { Local = current.Local with { IsFavorite = true } });

        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        // 開いた時点の姿で丸ごと書き戻すと、星が消える
        var saved = (await app.Store.Items.LoadAsync(id))!;
        Assert.True(saved.Local.IsFavorite);
        Assert.Single(saved.Local.UserTags);
        Assert.Single(saved.Local.LocalFiles);
    });

    [Fact]
    public Task スキップは_保存せずに次の商品へ進む() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"));
        var first = edit.CurrentItemId!;

        edit.SkipCommand.Execute(null);
        await app.SettleAsync();

        Assert.NotEqual(first, edit.CurrentItemId);
        Assert.Empty((await app.Store.Items.LoadAsync(first))!.Local.UserTags);
        Assert.Equal(2, main.NeedsEditCount);
    });

    [Fact]
    public Task 同じ大分類は_表記の大文字小文字が違っても2回は付かない() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenEditAsync(app, Make.Item("1000001", "作り物の衣装"));

        edit.AddTagCommand.Execute("Costume");
        await app.SettleAsync();
        edit.AddTagCommand.Execute("costume");
        await app.SettleAsync();
        edit.AddTagCommand.Execute("   ");
        await app.SettleAsync();

        Assert.Equal("Costume", Assert.Single(edit.Tags).Top);
    });

    [Fact]
    public Task 最後の1件を保存すると_終わりの表示になり_戻る先の名前を言う() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(app, Make.Item("1000001", "作り物の衣装"));

        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(edit.IsFinished);
        Assert.False(edit.HasItem);
        Assert.Equal("検索に戻る", edit.FinishButtonText);
        Assert.Equal(0, main.NeedsEditCount);
    });

    [Fact]
    public Task 商品を指定して入った編集は_終わりの戻る先が入る前の画面になる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        main.ShowItem(main.Search.FindItem("1000001")!);

        // 商品ページの「この商品を編集」
        main.CurrentItemPage!.EditCommand.Execute(null);
        await app.SettleAsync();

        // 前は商品ページから入っても「検索に戻る」と出ていた
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        Assert.Equal("作り物の衣装に戻る", edit.FinishButtonText);
    });
}
