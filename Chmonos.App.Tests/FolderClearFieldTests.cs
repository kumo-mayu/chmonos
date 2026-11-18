using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダの画面の2つの入力欄（左の木を探す欄・右の頭の探す欄）の × （open.md 89 行）。
/// × は打った文字を消し、絞る前の並びに戻す。
/// </summary>
public sealed class FolderClearFieldTests
{
    [Fact]
    public Task 左の欄のバツは_打った文字だけを消す() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"lib\a\dress.zip");
        await app.AddItemAsync(Make.Item("9900601", "作り物のドレス").WithFiles(Make.File(path)));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");

        var all = folders.Rows.Count;
        folders.Filter = "当たらない語";
        Assert.True(folders.HasFilter);
        Assert.Empty(folders.Rows);

        // 欄の語は「探す」に揃える（open.md 94 行）。文字で当たらない時は、言葉を変える事を言う
        Assert.Equal("当てはまるものがありません。探す言葉を変えてみてください。", folders.EmptyText);

        folders.ClearFilterCommand.Execute(null);

        Assert.Equal(string.Empty, folders.Filter);
        Assert.False(folders.HasFilter);
        Assert.Equal(all, folders.Rows.Count);
    });

    [Fact]
    public Task 右の頭の欄のバツは_打った文字を消して_直下の並びに戻す() => TestApp.Run(async app =>
    {
        var path = app.NewFile(@"lib\a\dress.zip");
        await app.AddItemAsync(Make.Item("9900602", "作り物のドレス").WithFiles(Make.File(path)));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        folders.Selected = folders.Rows.First(row => row.IsFolderLike);
        var detail = Assert.IsType<FolderViewDetail>(folders.Detail);

        detail.Query = "当たらない語";
        Assert.True(detail.HasQuery);

        detail.ClearQueryCommand.Execute(null);

        Assert.Equal(string.Empty, detail.Query);
        Assert.False(detail.HasQuery);
    });
}
