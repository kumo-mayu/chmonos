using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 起動したときの裏の見回り（ユーザ判断 2026-10-05）。記録しているファイルとフォルダの場所を全部見て、
/// 見つからなくなった日時を付け外しし、検索の写しとカードの印へ知らせる。
/// 前は取り込みと使おうとした画面でしか書かず、取り込まずに使っている間は、手で消した zip が印にも条件にも出なかった。
/// </summary>
public class MissingMarksSweepTests
{
    private static readonly DateTimeOffset First = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static ItemCardViewModel CardOf(MainViewModel main, string itemId)
        => main.Search.ListItems.Single(card => card.Item.Id == itemId);

    private static string Gone(TestApp app, string name) => Path.Combine(app.Root, "files", name);

    [Fact]
    public Task 起動時の見回りで_手で消したファイルとフォルダに印が付き_戻した物の印が外れる() => TestApp.Run(async app =>
    {
        var deleted = Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(Gone(app, "deleted-by-hand.zip")));
        deleted = deleted with { Local = deleted.Local with { Memo = "自分で書いたメモ" } };
        await app.AddItemAsync(deleted);
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪").WithFiles(Make.File(app.NewFile("put-back.zip")) with { MissingSince = First }));
        var folderItem = Make.Item("1000003", "作り物の小物").WithFiles();
        await app.AddItemAsync(folderItem with
        {
            Local = folderItem.Local with
            {
                LocalFolders = [new LocalFolderRecord { Path = Gone(app, "deleted-folder"), RegisteredAt = DateTimeOffset.UnixEpoch }],
            },
        });

        var main = await app.StartAsync();
        Assert.False(CardOf(main, "1000001").HasMissingFile);
        Assert.True(CardOf(main, "1000002").HasMissingFile);
        Assert.False(CardOf(main, "1000003").HasMissingFile);

        main.StartMissingMarksSweep();
        await app.SettleAsync();
        await UiThread.Until(() => CardOf(main, "1000001").HasMissingFile, "消したファイルの商品に印が付く");

        Assert.False(CardOf(main, "1000002").HasMissingFile);
        Assert.True(CardOf(main, "1000003").HasMissingFile);

        // 場所は消さない。人が入れた値も残る
        var saved = (await app.Store.Items.LoadAsync("1000001"))!;
        Assert.NotNull(saved.Local.LocalFiles.Single().MissingSince);
        Assert.Equal([Gone(app, "deleted-by-hand.zip")], saved.Local.LocalFiles.Single().Paths);
        Assert.Equal("自分で書いたメモ", saved.Local.Memo);

        // 検索の条件「見つからないファイル」でも絞れる
        var module = (ChoiceModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.MissingFile);
        module.Selected = module.Options.Single(option => option.Key == "missing");
        Assert.Equal(["1000001", "1000003"], main.Search.ListItems.Select(card => card.Item.Id).Order());

        // BOOTH には問い合わせない
        Assert.Empty(app.Booth.Requests);
    });

    /// <summary>初めて見回る保存先や、外付けを付け直した回は数百件が変わる。1件ずつ差し替えず全件を読み直しても、印は同じく付く。</summary>
    [Fact]
    public Task 多くの商品が変わったら_全件を読み直して印を付ける() => TestApp.Run(async app =>
    {
        var count = MainViewModel.SweepNoteOneByOneLimit + 1;
        for (var i = 0; i < count; i++)
        {
            var id = (1000001 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await app.AddItemAsync(Make.Item(id, $"作り物の衣装{i}").WithFiles(Make.File(Gone(app, $"deleted-{i}.zip"))));
        }

        var main = await app.StartAsync();
        Assert.DoesNotContain(main.Search.ListItems, card => card.HasMissingFile);

        main.StartMissingMarksSweep();
        await app.SettleAsync();
        await UiThread.Until(() => main.Search.ListItems.Count(card => card.HasMissingFile) == count, "全部の商品に印が付く");
    });

    [Fact]
    public Task 記録と同じなら_見回りは何も書かない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(Gone(app, "deleted-by-hand.zip")) with { MissingSince = First }));
        await app.AddItemAsync(Make.Item("1000002", "作り物の髪").WithFiles(Make.File(app.NewFile("here.zip"))));
        var main = await app.StartAsync();

        var written = await app.Services.MissingMarks.SweepAsync();

        Assert.Empty(written);
        Assert.Equal(First, (await app.Store.Items.LoadAsync("1000001"))!.Local.LocalFiles.Single().MissingSince);
        Assert.True(CardOf(main, "1000001").HasMissingFile);
    });
}
