using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの見つからない行の「探す…」（ユーザ判断 2026-11-20）。範囲を選ぶ窓は取り込み画面と同じで、
/// この商品の分だけを探す。前は「取り込みを開く」で取り込み画面へ移るだけだった。
/// </summary>
public class ItemMissingSearchTests
{
    private const string Costume = "9900801";
    private const string Other = "9900802";

    private static async Task<ItemViewModel> OpenItemAsync(TestApp app, MainViewModel main, string itemId)
    {
        main.ShowItem((await app.Store.Items.LoadAsync(itemId))!);
        var page = Assert.IsType<ItemViewModel>(main.CurrentViewModel);
        await app.SettleAsync();
        return page;
    }

    [Fact]
    public Task 探すは_この商品の分だけを結び直し_同じ中身を持つほかの商品には触らない() => TestApp.Run(async app =>
    {
        var moved = app.NewFile(@"watched\sub\moved.zip", [7, 7, 7, 7]);
        var watched = Path.Combine(app.Root, "files", "watched");
        var gone = Make.File(Path.Combine(watched, "costume.zip")) with { Hash = await FileHasher.ComputeSha256Async(moved), SizeBytes = 4 };
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles(gone));
        await app.AddItemAsync(Make.Item(Other, "作り物の別の衣装").WithFiles(gone));
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var page = await OpenItemAsync(app, main, Costume);

        // 見つからない行は「開く ▾」の代わりに「探す…」
        Assert.True(Assert.Single(page.LocalFiles).CanSearchMissing);

        page.FindMissingCommand.Execute(null);
        await app.SettleAsync();

        // 範囲を選ぶ窓は1回（監視フォルダが既定で入っている）
        Assert.Equal([watched], Assert.Single(app.SearchScopes).SelectedFolders);
        Assert.Equal("1 件を新しい場所に紐付け直しました。", page.FilesNotice.Text);
        Assert.Equal([moved], Assert.Single((await app.Store.Items.LoadAsync(Costume))!.Local.LocalFiles).Paths);
        Assert.Equal([gone.Paths[0]], Assert.Single((await app.Store.Items.LoadAsync(Other))!.Local.LocalFiles).Paths);

        // 行も今の記録に揃う（札「見つかりません」と「探す…」が消える）
        Assert.False(Assert.Single(page.LocalFiles).CanSearchMissing);
    });

    [Fact]
    public Task 窓でやめたら何も探さず_知らせも出さない() => TestApp.Run(async app =>
    {
        var moved = app.NewFile(@"watched\sub\moved.zip", [7, 7, 7, 7]);
        var watched = Path.Combine(app.Root, "files", "watched");
        var gone = Path.Combine(watched, "costume.zip");
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles(
            Make.File(gone) with { Hash = await FileHasher.ComputeSha256Async(moved), SizeBytes = 4 }));
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var page = await OpenItemAsync(app, main, Costume);
        app.PickSearchScope = _ => false;

        page.FindMissingCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(string.Empty, page.FilesNotice.Text);
        Assert.Equal([gone], Assert.Single((await app.Store.Items.LoadAsync(Costume))!.Local.LocalFiles).Paths);
        Assert.True(page.FindMissingCommand.CanExecute(null));
    });

    [Fact]
    public Task 見つからなければ_そう言い_見つからなかった日時を付ける() => TestApp.Run(async app =>
    {
        var watched = Path.Combine(app.Root, "files", "watched");
        Directory.CreateDirectory(watched);
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles(Make.File(Path.Combine(watched, "costume.zip"))));
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var page = await OpenItemAsync(app, main, Costume);

        page.FindMissingCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("1 件を探しましたが、見つかりませんでした。", page.FilesNotice.Text);
        Assert.True(page.FilesNotice.IsWarning);
        Assert.NotNull(Assert.Single((await app.Store.Items.LoadAsync(Costume))!.Local.LocalFiles).MissingSince);
    });

    /// <summary>外したファイルは探す相手に入らないので、「探す…」を出さない（押せない「開く ▾」のまま）。</summary>
    [Fact]
    public Task 外したファイルの行には探すを出さない() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "watched", "costume.zip");
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles(
            Make.File(Path.Combine(app.Root, "files", "watched", "other.zip")),
            Make.File(gone, detached: true)));
        var main = await app.StartAsync();
        var page = await OpenItemAsync(app, main, Costume);

        var detached = Assert.Single(page.LocalFiles, row => row.IsDetached);
        Assert.True(detached.IsMissing);
        Assert.False(detached.CanSearchMissing);
    });

    /// <summary>見つからない登録フォルダは、合うフォルダを候補で並べ、押した物だけに差し替える（推定した値を勝手に入れない）。</summary>
    [Fact]
    public Task 見つからない登録フォルダは候補を並べ_押した場所に差し替える() => TestApp.Run(async app =>
    {
        var watched = Path.Combine(app.Root, "files", "watched");
        var movedTo = Path.Combine(watched, "移した先", "衣装フォルダ");
        Directory.CreateDirectory(movedTo);
        await File.WriteAllBytesAsync(Path.Combine(movedTo, "a.txt"), [1, 2, 3]);
        var oldPath = Path.Combine(app.Root, "files", "元の場所", "衣装フォルダ");
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles() with
        {
            Local = new LocalBlock { LocalFolders = [new LocalFolderRecord { Path = oldPath, FileCount = 1, TotalBytes = 3 }] },
        });
        var main = await app.StartAsync();
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var page = await OpenItemAsync(app, main, Costume);
        Assert.True(Assert.Single(page.LocalFolders).IsMissing);

        page.FindMissingCommand.Execute(null);
        await app.SettleAsync();

        var folder = Assert.Single(page.MissingFolderCandidates);
        var candidate = Assert.Single(folder.Candidates);
        Assert.Equal(movedTo, candidate.Path);

        // 候補は見せるだけ。押すまで記録は変わらない
        Assert.Equal(oldPath, Assert.Single((await app.Store.Items.LoadAsync(Costume))!.Local.LocalFolders).Path);

        page.UseFolderCandidateCommand.Execute(candidate);
        await app.SettleAsync();

        Assert.Equal(movedTo, Assert.Single((await app.Store.Items.LoadAsync(Costume))!.Local.LocalFolders).Path);
        Assert.Empty(folder.Candidates);
        Assert.False(Assert.Single(page.LocalFolders).IsMissing);
    });
}
