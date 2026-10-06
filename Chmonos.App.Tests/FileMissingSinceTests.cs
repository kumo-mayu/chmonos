using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 使おうとしてファイルが見つからない・見つかったと分かったら、記録の「見つからなくなった日時」に書く（ユーザ判断 2026-10-04）。
/// 前は商品ページだけがその場でディスクを見て「見つかりません」を出し、記録は書かなかったので、
/// 同じ商品がカードの印・検索の条件・統計に出ず、画面どうしで食い違っていた。
/// </summary>
public class FileMissingSinceTests
{
    private static readonly DateTimeOffset First = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static async Task<LocalFileRecord> RecordedAsync(TestApp app, string itemId, string path)
        => (await app.Services.Store.Items.LoadAsync(itemId))!.Local.LocalFiles.Single(file => file.Paths.Contains(path));

    private static ItemCardViewModel CardOf(MainViewModel main, string itemId)
        => main.Search.ListItems.Single(card => card.Item.Id == itemId);

    [Fact]
    public Task 商品ページで無いと分かったら_記録に日時が付き_検索の印と条件に当たる() => TestApp.Run(async app =>
    {
        var present = app.NewFile("present.zip");
        var gone = Path.Combine(app.Root, "files", "deleted-by-hand.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(present), Make.File(gone));
        item = item with { Local = item.Local with { Memo = "自分で書いたメモ" } };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        Assert.False(CardOf(main, "1000001").HasMissingFile);

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.LocalFiles.Single(row => row.FirstPath == gone).IsMissing, "無いファイルに「見つかりません」が付く");
        await app.SettleAsync();

        Assert.NotNull((await RecordedAsync(app, "1000001", gone)).MissingSince);
        Assert.Null((await RecordedAsync(app, "1000001", present)).MissingSince);

        // 場所は消さない。人が入れた値も残る
        var saved = (await app.Services.Store.Items.LoadAsync("1000001"))!;
        Assert.Equal([gone], saved.Local.LocalFiles.Single(file => file.Hash == Make.File(gone).Hash).Paths);
        Assert.Equal("自分で書いたメモ", saved.Local.Memo);

        // 検索の写しにも知らせてある：戻ればカードに印が付き、条件で絞れる
        Assert.True(CardOf(main, "1000001").HasMissingFile);
        // 在るファイルもあるので「一部見つからない」（全部のときと札を分ける。メモ65 の②）
        Assert.Equal("一部見つからない", CardOf(main, "1000001").MissingBadgeText);
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.MissingFile);
        module.Selected = module.Options.Single(option => option.Key == "missing");
        Assert.Equal(["1000001"], main.Search.ListItems.Select(card => card.Item.Id));
    });

    [Fact]
    public Task 商品ページで在ると分かったら_付いていた日時を消す() => TestApp.Run(async app =>
    {
        var back = app.NewFile("put-back.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(back) with { MissingSince = First });
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        Assert.True(CardOf(main, "1000001").HasMissingFile);
        // 持っている物が全部見つからないので、札は「見つからない」
        Assert.Equal("見つからない", CardOf(main, "1000001").MissingBadgeText);

        _ = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => !CardOf(main, "1000001").HasMissingFile, "カードの印が外れる");

        Assert.Null((await RecordedAsync(app, "1000001", back)).MissingSince);
    });

    [Fact]
    public Task 記録と同じなら_商品ページを開いても書かない() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "deleted-by-hand.zip");
        var item = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(gone) with { MissingSince = First });
        await app.AddItemAsync(item);
        var main = await app.StartAsync();

        var page = new ItemViewModel(item, app.Services, main, main.Thumbnails);
        await UiThread.Until(() => page.LocalFiles.Single().IsMissing, "「見つかりません」が付く");
        await app.SettleAsync();

        // 無い間は最初に見た日時のまま
        Assert.Equal(First, (await RecordedAsync(app, "1000001", gone)).MissingSince);
    });

    [Fact]
    public Task カードの右クリックで開こうとして無かったら_記録に日時が付き_カードに印が付く() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "deleted-by-hand.zip");
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(gone)));
        var main = await app.StartAsync();
        var card = CardOf(main, "1000001");
        Assert.False(card.HasMissingFile);

        await ItemFileActions.RevealAsync(app.Services, card.Item, main.Search.NoteItemChanged);

        Assert.Single(app.Notices);
        Assert.NotNull((await RecordedAsync(app, "1000001", gone)).MissingSince);
        Assert.True(CardOf(main, "1000001").HasMissingFile);
    });
}
