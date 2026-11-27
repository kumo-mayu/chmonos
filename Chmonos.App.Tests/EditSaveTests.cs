using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

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
    public Task 保存して次へを押すと_続きの記録に保存した印と進めた位置が残る() => TestApp.Run(async app =>
    {
        var (_, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"), Make.Item("1000003", "作り物の靴"));
        var first = edit.CurrentItemId!;

        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();

        var session = app.Store.EditSession.Load();
        Assert.Equal(3, session.ItemIds.Count);
        Assert.Equal(1, session.Index);
        Assert.Equal([first], session.SavedItemIds);
    });

    /// <summary>
    /// 入り直したときは、保存した商品を外して詰めた順番と位置を1回で書く（前は2回に分けて書いていた）。
    /// </summary>
    [Fact]
    public Task 離れて入り直すと_保存した商品を外し_続きの位置から開く() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"), Make.Item("1000003", "作り物の靴"));
        var first = edit.CurrentItemId!;

        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        var skipped = edit.CurrentItemId!;
        edit.SkipCommand.Execute(null);
        await app.SettleAsync();
        var third = edit.CurrentItemId!;

        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();

        var again = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        Assert.Equal(third, again.CurrentItemId);
        Assert.Equal("2 / 2 件", again.StepText);

        var session = app.Store.EditSession.Load();
        Assert.Equal([skipped, third], session.ItemIds);
        Assert.Equal(1, session.Index);
        Assert.Empty(session.SavedItemIds);
        Assert.DoesNotContain(first, session.ItemIds);
    });

    /// <summary>
    /// 入り直したとき、後から取り込んだ未編集の商品を順番の後ろへ足す（ユーザ判断 2026-11-17）。
    /// 足す順は作り直し（BuildDefaultQueueAsync）と同じ。保存した商品・ユーザータグのある商品は足さない。位置は前の続き。
    /// </summary>
    [Fact]
    public Task 入り直すと_後から取り込んだ未編集の商品を作り直しと同じ順で後ろへ足し_位置は前の続きのまま() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"), Make.Item("1000003", "作り物の靴"));
        var first = edit.CurrentItemId!;
        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        var second = edit.CurrentItemId!;

        // 編集の外で取り込んだ商品：未編集2件（名前の順と商品IDの順を食い違わせる）と、タグのある1件
        var lateA = Make.Item("1000009", "作り物のあ");
        var lateB = Make.Item("1000004", "作り物のん");
        var tagged = Make.Item("1000005", "作り物のタグ付き");
        tagged = tagged with { Local = tagged.Local with { UserTags = [new UserTagAssignment { Top = "衣装" }] } };
        await app.AddItemAsync(lateA);
        await app.AddItemAsync(lateB);
        await app.AddItemAsync(tagged);

        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();

        var again = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        var session = app.Store.EditSession.Load();

        // 作り直しと同じ順（ItemOrder.ByAcquired）で、前の順番の後ろに付く
        var expectedTail = ItemOrder.ByAcquired(
            [(await app.Store.Items.LoadAsync("1000009"))!, (await app.Store.Items.LoadAsync("1000004"))!], descending: true)
            .Select(item => item.Id).ToList();
        Assert.Equal(2, expectedTail.Count);
        Assert.Equal(expectedTail, session.ItemIds.Skip(2).ToList());
        Assert.Equal(4, session.ItemIds.Count);
        Assert.DoesNotContain(first, session.ItemIds);
        Assert.DoesNotContain("1000005", session.ItemIds);

        // 位置は前の続き（保存して外した1件の分だけ詰める）。順番と位置は同じ記録に書かれている
        Assert.Equal(second, again.CurrentItemId);
        Assert.Equal(0, session.Index);
        Assert.Equal("1 / 4 件", again.StepText);
    });

    [Fact]
    public Task スキップした商品の打ちかけは_前へで戻ると残っていて_保存先には書かれない() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), Make.Item("1000002", "作り物の髪型"));
        var first = edit.CurrentItemId!;

        edit.Memo = "あとで決める";
        edit.SkipCommand.Execute(null);
        await app.SettleAsync();
        Assert.NotEqual(first, edit.CurrentItemId);
        Assert.True(main.Drafts.Contains(first));

        edit.BackCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(first, edit.CurrentItemId);
        Assert.Equal("あとで決める", edit.Memo);
        Assert.NotEqual("あとで決める", (await app.Store.Items.LoadAsync(first))!.Local.Memo);
        Assert.Equal(0, app.Store.EditSession.Load().Index);
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

    // ---- 所持している商品だけ（設定 EditOwnedOnly・ユーザ判断 2026-11-27） ----

    private static ItemRecord InfoOnly(string id, string name) => Make.Item(id, name).WithFiles();

    [Fact]
    public Task 既定では_ファイルの無い商品を順番にもナビの数にも入れない() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), InfoOnly("1000002", "作り物の情報だけ"), Make.Item("1000003", "作り物の靴"));

        Assert.True(edit.OwnedOnly);
        Assert.Equal("1 / 2 件", edit.StepText);
        Assert.DoesNotContain("1000002", app.Store.EditSession.Load().ItemIds);
        Assert.Equal(2, main.NeedsEditCount);
    });

    [Fact]
    public Task 切ると情報だけの商品が後ろへ足され_入に戻すと書きかけの無い物だけ外れる() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), InfoOnly("1000002", "作り物の情報だけ"), InfoOnly("1000003", "作り物の下書き"));
        Assert.Equal("1 / 1 件", edit.StepText);
        var current = edit.CurrentItemId;

        await edit.SetOwnedOnlyAsync(false);
        await app.SettleAsync();
        Assert.False(app.Services.Settings.EditOwnedOnly);
        Assert.Equal(3, app.Store.EditSession.Load().ItemIds.Count);
        Assert.Equal(current, edit.CurrentItemId);
        Assert.Equal(3, main.NeedsEditCount);

        // 帯の絵は、情報だけの商品にだけ未所持の印を出す（ユーザ指示 2026-11-27）
        Assert.Equal(
            [("1000001", false), ("1000002", true), ("1000003", true)],
            edit.QueueTiles.Select(tile => (tile.ItemId, tile.IsUnowned)).OrderBy(pair => pair.ItemId).ToList());
        Assert.StartsWith("未所持・", edit.QueueTiles.Single(tile => tile.ItemId == "1000002").StateText);

        // 情報だけの商品の1件に書きかけを作る（未保存の入力）
        edit.SkipCommand.Execute(null);
        await app.SettleAsync();
        var draftId = edit.CurrentItemId!;
        edit.Memo = "あとで書く";
        edit.SkipCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(main.Drafts.Contains(draftId));

        await edit.SetOwnedOnlyAsync(true);
        await app.SettleAsync();

        // 書きかけのある商品と、いま開いている商品は残る。残りの情報だけの商品が外れる
        var ids = app.Store.EditSession.Load().ItemIds;
        Assert.Contains(draftId, ids);
        Assert.Contains(edit.CurrentItemId!, ids);
        Assert.Contains("1000001", ids);
        Assert.DoesNotContain(
            ids, id => id != draftId && id != edit.CurrentItemId && id != "1000001");

        // 「未:」は所持している商品だけ（書きかけのある情報だけの商品も、順番には残るが数えない）
        Assert.Equal(1, main.NeedsEditCount);
    });

    [Fact]
    public Task 入り直すと_順番に残っていた情報だけの商品を外し_切のままなら残す() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { EditOwnedOnly = false });
        var (main, _) = await OpenEditAsync(
            app, Make.Item("1000001", "作り物の衣装"), InfoOnly("1000002", "作り物の情報だけ"));
        Assert.Equal(2, app.Store.EditSession.Load().ItemIds.Count);

        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(2, app.Store.EditSession.Load().ItemIds.Count);

        await app.ChangeSettingsAsync(settings => settings with { EditOwnedOnly = true });
        main.ShowSearchCommand.Execute(null);
        await app.SettleAsync();
        main.ShowEditCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(["1000001"], app.Store.EditSession.Load().ItemIds);
    });

    [Fact]
    public void 設定ファイルに項目が無ければ_所持している商品だけが入で読まれる()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(settings.EditOwnedOnly);
    }
}
