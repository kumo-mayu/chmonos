using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// 編集画面から離れる時（ユーザ判断 2026-11-18・11-19）：保存を聞く窓は出さず、書きかけを控えた事を下の帯で一言知らせる。
/// 上の帯の「〇〇に戻る」は来た画面を名乗る。検索の「まとめて編集する」から入って戻ると、選んでいた物が残る。
/// </summary>
public class EditLeaveTests
{
    private static ItemCardViewModel Card(SearchViewModel search, string id) => search.ListItems.Single(card => card.Item.Id == id);

    private static async Task<(MainViewModel Main, EditViewModel Edit)> OpenFromSearchSelectionAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪型"));
        await app.AddItemAsync(Make.Item("1000003", "作り物の靴"));
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
        var main = await app.StartAsync();
        var search = main.Search;
        Card(search, "1000001").IsSelected = true;
        Card(search, "1000002").IsSelected = true;

        search.SendSelectionToEditCommand.Execute(null);
        await app.SettleAsync();
        return (main, Assert.IsType<EditViewModel>(main.CurrentViewModel));
    }

    [Fact]
    public Task まとめて編集から入ると_上の帯は検索に戻ると名乗り_押すと選んでいた物が残っている() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenFromSearchSelectionAsync(app);
        Assert.Equal("検索に戻る", edit.FinishButtonText);

        // 1件目を保存して2件目まで進んでも、1手で検索へ戻る（履歴を辿らない）。
        // 保存した商品は検索のカードが作り直され、終えると一覧を読み直すが、どちらでも選択は外れない
        edit.AddTagCommand.Execute("衣装");
        await app.SettleAsync();
        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        edit.FinishCommand.Execute(null);
        await app.SettleAsync();

        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
        Assert.Equal(2, main.Search.SelectedCount);
        Assert.True(Card(main.Search, "1000001").IsSelected);
        Assert.True(Card(main.Search, "1000002").IsSelected);
        Assert.False(Card(main.Search, "1000003").IsSelected);
    });

    [Fact]
    public Task 保存していない入力を持って編集の外へ出ると_控えた事を知らせ_続きを編集で書きかけの商品だけを開く() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenFromSearchSelectionAsync(app);
        var first = edit.CurrentItemId!;
        Assert.False(main.HasDraftNotice);

        edit.Memo = "あとで決める";
        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(main.HasDraftNotice);
        Assert.Equal("保存していない入力を控えました。ナビの「編:1」が控えている商品の数です。", main.DraftNoticeText);

        main.OpenDraftsFromNoticeCommand.Execute(null);
        await app.SettleAsync();

        var reopened = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        Assert.Equal(first, reopened.CurrentItemId);
        Assert.Equal("あとで決める", reopened.Memo);
        Assert.False(main.HasDraftNotice);
    });

    [Fact]
    public Task 編集の中で商品を移る時は知らせず_外へ出た時に途中で控えた分を知らせる() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenFromSearchSelectionAsync(app);

        // 順番の中の移動では出さない（そこでは上の帯の「編集途中 n 件」が見えている）
        edit.Memo = "あとで決める";
        edit.SkipCommand.Execute(null);
        await app.SettleAsync();
        Assert.False(main.HasDraftNotice);

        // 出る時の商品（2件目）は手付かずでも、1件目の入力を控えているので知らせる
        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();

        Assert.True(main.HasDraftNotice);
    });

    [Fact]
    public Task 何も入力せずに編集の外へ出ると_知らせない() => TestApp.Run(async app =>
    {
        var (main, _) = await OpenFromSearchSelectionAsync(app);

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();

        Assert.False(main.HasDraftNotice);
    });

    [Fact]
    public Task 控えた入力が保存で無くなったら_知らせを消す() => TestApp.Run(async app =>
    {
        var (main, edit) = await OpenFromSearchSelectionAsync(app);
        edit.Memo = "あとで決める";
        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(main.HasDraftNotice);

        main.Drafts.Remove("1000001");

        Assert.False(main.HasDraftNotice);
    });
}
