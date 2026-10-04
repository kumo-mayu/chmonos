using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダの画面の、監視を切り替えたときの知らせ（2026-10-03 の決まった事4・2026-10-04 担当NA）。
/// 入れたときは何も出さず（同じ行の「監視中」が答え）、やめたときだけボタンのすぐ下に出す。下の帯（<see cref="FolderViewModel.Status"/>）には出さない。
/// </summary>
public sealed class FolderWatchNoticeTests
{
    private static async Task<(FolderViewModel Folders, FolderViewDetail Detail)> OpenAsync(TestApp app)
    {
        var path = app.NewFile(@"lib\a\dress.zip");
        await app.AddItemAsync(Make.Item("9900501", "作り物のドレス").WithFiles(Make.File(path)));
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");

        // フォルダの行を選んで、その詳細を出す
        foreach (var row in folders.Rows.ToList())
        {
            folders.Selected = row;
            if (folders.Detail is FolderViewDetail { IsOffline: false } found)
            {
                return (folders, found);
            }
        }

        throw new InvalidOperationException("フォルダの詳細が出る行が無い");
    }

    [Fact]
    public Task 監視に入れたときは何も出さず_やめたときだけボタンの下に出す() => TestApp.Run(async app =>
    {
        var (folders, detail) = await OpenAsync(app);
        Assert.False(detail.IsWatched);

        folders.ToggleWatchCommand.Execute(detail);
        await app.SettleAsync();
        Assert.True(detail.IsWatched);
        Assert.Equal(string.Empty, detail.WatchNote.Text);
        Assert.Equal(string.Empty, folders.Status);

        folders.ToggleWatchCommand.Execute(detail);
        await app.SettleAsync();
        Assert.False(detail.IsWatched);
        Assert.Equal("監視をやめました。取り込んだものはそのまま残ります。", detail.WatchNote.Text);
        Assert.False(detail.WatchNote.IsWarning);
        Assert.Equal(string.Empty, folders.Status);

        // 入れ直すと消える
        folders.ToggleWatchCommand.Execute(detail);
        await app.SettleAsync();
        Assert.Equal(string.Empty, detail.WatchNote.Text);
    });
}
