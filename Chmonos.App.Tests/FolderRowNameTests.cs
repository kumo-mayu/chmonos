using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダビューの木の、ファイル名と商品名の切り替え（ユーザ判断 2026-10-01：画面は作り替えず、ファイル名の所を商品名にする）。
/// 商品名にするのは商品に結び付いた行だけで、未確定とフォルダは名前のまま。元のファイル名は2行目に回す。
/// 切り替えで行を作り直さない（並び・選んだ行がそのまま）ことと、閉じても覚えることも見る。
/// </summary>
public class FolderRowNameTests
{
    private static UnresolvedFile Unresolved(string path) => new()
    {
        Hash = Make.HashOf(path),
        Paths = [path],
        SizeBytes = 3,
        ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    };

    /// <summary>作り物の置き方：商品2つ（うち1つはフォルダごと登録も持つ）、2つの商品が同じ zip を持つ、未確定1つ。</summary>
    private static async Task<(MainViewModel Main, FolderViewModel Folders)> OpenAsync(TestApp app)
    {
        var shared = app.NewFile(@"lib\shared-bundle.zip");
        var dress = app.NewFile(@"lib\dress_v2.zip");
        var folder = System.IO.Path.GetDirectoryName(app.NewFile(@"lib\unpacked-hair\readme.txt"))!;
        var stray = app.NewFile(@"lib\unknown.zip");

        await app.AddItemAsync(Make.Item("9900401", "作り物のドレス").WithFiles(Make.File(dress), Make.File(shared)));
        var hair = Make.Item("9900402", "作り物の髪型").WithFiles(Make.File(shared));
        await app.AddItemAsync(hair with { Local = hair.Local with { LocalFolders = [new LocalFolderRecord { Path = folder }] } });
        await app.Store.Unresolved.SaveAsync([Unresolved(stray)]);

        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        ExpandAll(folders);
        return (main, folders);
    }

    private static void ExpandAll(FolderViewModel folders)
    {
        while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } row)
        {
            folders.ToggleCommand.Execute(row);
        }
    }

    private static FolderViewRow Row(FolderViewModel folders, string fileName, string? itemId = null)
        => folders.Rows.Single(row => row.Name == fileName && (itemId is null || row.Entry?.Item?.Id == itemId));

    [Fact]
    public Task 既定はファイル名で_2行目に商品名() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);

        Assert.False(folders.ShowsItemNames);
        var dress = Row(folders, "dress_v2.zip");
        Assert.Equal("dress_v2.zip", dress.Title);
        Assert.Equal("作り物のドレス", dress.SubText);
    });

    [Fact]
    public Task 商品名にすると_商品のファイルの行だけ商品名になり_ファイル名は2行目へ() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);

        folders.ShowItemNamesCommand.Execute(null);

        Assert.True(folders.ShowsItemNames);
        Assert.False(folders.ShowsFileNames);
        var dress = Row(folders, "dress_v2.zip");
        Assert.Equal("作り物のドレス", dress.Title);
        Assert.Equal("dress_v2.zip", dress.SubText);

        // 鍵・並び・絞り込みに使う名前は替えない
        Assert.Equal("dress_v2.zip", dress.Name);

        // フォルダごと登録した商品も商品名に。2行目はフォルダの名前と、フォルダごとの登録であること
        var hairFolder = folders.Rows.Single(row => row.Kind == FolderViewRowKind.ItemFolder);
        Assert.Equal("作り物の髪型", hairFolder.Title);
        Assert.Equal("unpacked-hair（フォルダごと登録した商品）", hairFolder.SubText);

        // 未確定（結び付いた商品が無い）とフォルダの行は名前のまま
        var stray = Row(folders, "unknown.zip");
        Assert.Equal("unknown.zip", stray.Title);
        Assert.Equal("未確定", stray.SubText);
        Assert.All(folders.Rows.Where(row => row.IsFolderLike), row => Assert.Equal(row.Name, row.Title));
    });

    [Fact]
    public Task 二行目が商品名のときだけ_本文の色で出す印が立つ() => TestApp.Run(async app =>
    {
        // ファイル名で出しているとき、2行目の商品名は灰色の小さな字だと読み飛ばされる（メモ6-①）。
        // 商品名で出しているときの2行目（元のファイル名）と、未確定・フォルダの2行目は補足なので灰色のまま
        var (_, folders) = await OpenAsync(app);

        var dress = Row(folders, "dress_v2.zip");
        var hairFolder = folders.Rows.Single(row => row.Kind == FolderViewRowKind.ItemFolder);
        var stray = Row(folders, "unknown.zip");
        Assert.True(dress.SubTextIsItemName);
        Assert.True(hairFolder.SubTextIsItemName);
        Assert.False(stray.SubTextIsItemName);
        Assert.All(folders.Rows.Where(row => row.IsFolderLike), row => Assert.False(row.SubTextIsItemName));

        var changed = new List<string?>();
        dress.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        folders.ShowItemNamesCommand.Execute(null);

        Assert.False(dress.SubTextIsItemName);
        Assert.False(hairFolder.SubTextIsItemName);
        Assert.Contains(nameof(FolderViewRow.SubTextIsItemName), changed);
    });

    [Fact]
    public Task 二つの商品が持つファイルは_商品ごとの行にそれぞれの商品名を出す() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);

        folders.ShowItemNamesCommand.Execute(null);

        var shared = folders.Rows.Where(row => row.Name == "shared-bundle.zip").ToList();
        Assert.Equal(["作り物のドレス", "作り物の髪型"], shared.Select(row => row.Title).Order());
        Assert.All(shared, row => Assert.Equal("shared-bundle.zip", row.SubText));
    });

    [Fact]
    public Task 切り替えても行は作り直さず_並びと選んだ行と右の商品はそのまま() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);

        // 同じファイルの2つ目の行（鍵が1つ目と同じ）を選ぶ
        var second = folders.Rows.Where(row => row.Name == "shared-bundle.zip").Last();
        folders.Selected = second;
        await app.SettleAsync();
        var page = Assert.IsType<ItemViewModel>(folders.Detail);
        var before = folders.Rows.ToList();

        folders.ShowItemNamesCommand.Execute(null);
        folders.ShowFileNamesCommand.Execute(null);
        folders.ShowItemNamesCommand.Execute(null);

        Assert.Equal(before, folders.Rows);
        Assert.Same(second, folders.Selected);
        Assert.Same(page, folders.Detail);
    });

    [Fact]
    public Task 作り直した行も今の切り替えに従い_選んだ行は同じ商品の行に戻る() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        folders.ShowItemNamesCommand.Execute(null);

        // 絞り込みは行を作り直す。商品名で出していても、ファイル名で当たる
        folders.Filter = "dress_v2";
        Assert.Equal("作り物のドレス", Row(folders, "dress_v2.zip").Title);
        folders.Filter = string.Empty;
        ExpandAll(folders);

        var second = folders.Rows.Where(row => row.Name == "shared-bundle.zip").Last();
        folders.Selected = second;
        await app.SettleAsync();
        var itemId = second.Entry!.Item!.Id;

        // 選んだ行の商品名で絞る（同じファイルの、もう1つの商品の行は外れる）→ 戻す
        folders.Filter = second.Title;
        Assert.Equal(itemId, folders.Selected?.Entry?.Item?.Id);
        folders.Filter = string.Empty;
        ExpandAll(folders);

        Assert.All(folders.Rows.Where(row => row.Entry?.Item is not null), row => Assert.Equal(row.Entry!.Item!.DisplayName, row.Title));
        Assert.Equal(itemId, folders.Selected?.Entry?.Item?.Id);
        Assert.Equal("shared-bundle.zip", folders.Selected?.Name);
    });

    [Fact]
    public Task 切り替えは閉じても覚え_次に開いたときも商品名で始まる() => TestApp.Run(async app =>
    {
        var (main, folders) = await OpenAsync(app);

        folders.ShowItemNamesCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(app.Services.UiState.FolderRowsShowItemName);

        main.ShowFolders();
        var reopened = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        Assert.NotSame(folders, reopened);
        await UiThread.Until(() => reopened.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        ExpandAll(reopened);
        Assert.True(reopened.ShowsItemNames);
        Assert.Equal("作り物のドレス", Row(reopened, "dress_v2.zip").Title);

        reopened.ShowFileNamesCommand.Execute(null);
        await app.SettleAsync();
        Assert.False(app.Services.UiState.FolderRowsShowItemName);
    });

    [Fact]
    public Task 管理しているファイルを木に出していない間は_切り替えを押せない() => TestApp.Run(async app =>
    {
        var (_, folders) = await OpenAsync(app);
        try
        {
            Assert.True(folders.CanChooseRowNames);
            folders.ShowManaged = false;
            Assert.False(folders.CanChooseRowNames);
            folders.ShowManaged = true;
            folders.ShowItems = false;
            Assert.False(folders.CanChooseRowNames);
        }
        finally
        {
            // 出す物の切り替えはアプリを閉じるまで覚える（静的）。ほかの試験へ持ち越さない
            folders.ShowItems = true;
            folders.ShowManaged = true;
        }
    });
}
