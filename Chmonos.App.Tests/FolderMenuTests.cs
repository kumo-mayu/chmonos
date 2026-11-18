using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダの右クリック（open.md 90・92 行）。左の木のフォルダの行・右の子フォルダのカード・リストのフォルダの行で同じメニューを出し、
/// 右の頭のボタンと同じ命令を、右クリックした物の場所で動かす。開閉の項目は木の行だけ。
/// メニューは別の窓に出て撮れないので、出し分け・押せるか・押した結果を ViewModel で確かめる
/// </summary>
public sealed class FolderMenuTests
{
    /// <summary>lib の下に a（その下に b・c）と d。商品は b・c・d に1つずつ。</summary>
    private static async Task<(MainViewModel Main, FolderViewModel Folders)> OpenAsync(TestApp app)
    {
        await app.AddItemAsync(Make.Item("9900701", "作り物のドレス").WithFiles(Make.File(app.NewFile(@"lib\a\b\dress.zip"))));
        await app.AddItemAsync(Make.Item("9900702", "作り物の髪型").WithFiles(Make.File(app.NewFile(@"lib\a\c\hair.zip"))));
        await app.AddItemAsync(Make.Item("9900703", "作り物の靴").WithFiles(Make.File(app.NewFile(@"lib\d\shoes.zip"))));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        return (main, folders);
    }

    private static FolderViewRow Folder(FolderViewModel folders, string name)
        => folders.Rows.Single(row => row.IsFolderLike && row.Name == name);

    private static FolderViewRow Root(FolderViewModel folders)
        => folders.Rows.First(row => row.Kind == FolderViewRowKind.Root);

    [Fact]
    public Task 開閉の項目は木のフォルダの行だけに出し_カードには出さない() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        var root = Root(folders);
        folders.Selected = root;
        var detail = Assert.IsType<FolderViewDetail>(folders.Detail);
        var card = detail.Rows.SelectMany(row => row.Cards).OfType<FolderBrowserFolderCard>().First();

        Assert.Equal(true, folders.FolderMenuValue("TreeOnly", root));
        Assert.Equal(false, folders.FolderMenuValue("TreeOnly", card));
        Assert.Equal("折りたたむ", folders.FolderMenuValue("Header.Toggle", root));

        // 商品のファイルの行と未確定の行は、フォルダの右クリックの対象ではない
        Assert.Null(FolderTarget.Of(folders.Rows.FirstOrDefault(row => row.IsItemFile)));

        // 木の行もカードも詳細も、同じ入口から命令を引く
        Assert.Same(folders, folders.FolderMenu);
        Assert.Same(folders, detail.FolderMenu);
    });

    [Fact]
    public Task 下の階層まで広げると_下のフォルダが全部開き_折りたたむと畳んで覚える() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        var root = Root(folders);

        folders.ExpandAllCommand.Execute(root);
        Assert.DoesNotContain(folders.Rows, row => row.IsFolderLike && row.CanExpand && !row.IsExpanded);
        Assert.Contains(folders.Rows, row => row.Name == "dress.zip");
        Assert.Contains(folders.Rows, row => row.Name == "shoes.zip");

        folders.CollapseAllCommand.Execute(Root(folders));
        Assert.False(Root(folders).IsExpanded);
        Assert.DoesNotContain(folders.Rows, row => row.Name == "a");

        // 根を三角で開き直しても、下は畳んだまま（下まで畳んだ事を覚えている）
        folders.ToggleCommand.Execute(Root(folders));
        Assert.False(Folder(folders, "a").IsExpanded);
        Assert.DoesNotContain(folders.Rows, row => row.Name == "dress.zip");
    });

    [Fact]
    public Task 探している間は開閉の項目を押せず_理由を出す() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        Assert.Equal(true, folders.FolderMenuValue("CanToggle", Root(folders)));
        Assert.Null(folders.FolderMenuValue("Tip.Toggle", Root(folders)));

        var version = folders.FolderMenuVersion;
        folders.Filter = "dress";
        Assert.NotEqual(version, folders.FolderMenuVersion);

        var root = Root(folders);
        Assert.Equal(false, folders.FolderMenuValue("CanToggle", root));
        Assert.Equal("探す欄の文字を消すと、開閉できます。", folders.FolderMenuValue("Tip.Toggle", root));

        // 押されても何も変えない
        var count = folders.Rows.Count;
        folders.CollapseAllCommand.Execute(root);
        Assert.Equal(count, folders.Rows.Count);
    });

    [Fact]
    public Task カードから監視を切り替えると_見出しが変わり_右に出ている同じフォルダの監視も合う() => TestApp.Run(async app =>
    {
        var (main, folders) = await OpenAsync(app);
        folders.Selected = Root(folders);
        var parent = Assert.IsType<FolderViewDetail>(folders.Detail);
        var card = parent.Rows.SelectMany(row => row.Cards).OfType<FolderBrowserFolderCard>().First();
        Assert.Equal("監視フォルダにする", folders.FolderMenuValue("Header.Watch", card));

        // 右に出ていないフォルダ（子フォルダのカード）を右クリックで監視に入れる
        var version = folders.FolderMenuVersion;
        folders.ToggleWatchCommand.Execute(card);
        await app.SettleAsync();
        Assert.Contains(card.Path, app.Services.Settings.WatchedFolders, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("監視をやめる", folders.FolderMenuValue("Header.Watch", card));
        Assert.NotEqual(version, folders.FolderMenuVersion);
        Assert.False(parent.IsWatched);

        // そのフォルダへ移ると、右の頭も「監視中」。右の頭から外すと、右クリックの見出しも戻る
        folders.OpenFolder(card);
        var shown = Assert.IsType<FolderViewDetail>(folders.Detail);
        Assert.True(shown.IsWatched);
        folders.ToggleWatchCommand.Execute(shown);
        await app.SettleAsync();
        Assert.False(shown.IsWatched);
        Assert.Equal("監視フォルダにする", folders.FolderMenuValue("Header.Watch", card));
        Assert.Equal("監視をやめました。取り込んだものはそのまま残ります。", shown.WatchNote.Text);

        // 右に出ている物を木の行の右クリックで切り替えても、右の頭が合う
        var row = folders.Rows.Single(candidate => candidate.IsFolderLike && string.Equals(candidate.Path, shown.Path, StringComparison.OrdinalIgnoreCase));
        folders.ToggleWatchCommand.Execute(row);
        await app.SettleAsync();
        Assert.True(shown.IsWatched);
        _ = main;
    });

    [Fact]
    public Task 木のどのフォルダの行も_右クリックの宛先は選んだ時の右の頭と同じ() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        folders.ExpandAllCommand.Execute(Root(folders));

        // ドライブ・根・フォルダの行。右の頭のボタンと場所・押せるかがずれると、右クリックだけ別の場所を取り込む
        foreach (var row in folders.Rows.Where(candidate => candidate.IsFolderLike).ToList())
        {
            folders.Selected = row;
            Assert.Equal(FolderTarget.Of(folders.Detail), FolderTarget.Of(row));
            Assert.False(row.IsItemFile);
        }

        Assert.Contains(folders.Rows, row => row.Kind == FolderViewRowKind.Volume);
    });

    [Fact]
    public Task 木の行から検索で開くと_そのフォルダで絞った検索へ移る() => TestApp.Run(async app =>
    {
        var (main, folders) = await OpenAsync(app);
        folders.ExpandAllCommand.Execute(Root(folders));

        folders.SearchHereCommand.Execute(Folder(folders, "d"));
        await app.SettleAsync();

        Assert.IsType<SearchViewModel>(main.CurrentViewModel);
    });
}
