using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダビューへ戻る（Alt+←）ときの選び直し。
/// 同じファイルを2つの商品が持つと、場所の鍵が同じ行が商品ごとに並ぶ。前は控えが場所だけだったので、
/// 2つ目の商品の行を選んでから別の画面へ行って戻ると、1つ目の商品の行が選ばれていた（2026-10-01 に直した）。
/// </summary>
public class FolderHistoryTests
{
    [Fact]
    public Task 同じファイルを持つ2つ目の商品の行を選んで戻ると_2つ目の行が選ばれている() => TestApp.Run(async app =>
    {
        var shared = app.NewFile(@"lib\shared-bundle.zip");
        await app.AddItemAsync(Make.Item("9900411", "作り物のドレス").WithFiles(Make.File(shared)));
        await app.AddItemAsync(Make.Item("9900412", "作り物の髪型").WithFiles(Make.File(shared)));

        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = await LoadedAsync(main);
        while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
        {
            folders.ToggleCommand.Execute(closed);
        }

        var rows = folders.Rows.Where(row => row.Name == "shared-bundle.zip").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Key, rows[1].Key);
        var second = rows[1];
        var itemId = second.Entry!.Item!.Id;
        Assert.NotEqual(rows[0].Entry!.Item!.Id, itemId);
        folders.Selected = second;
        await app.SettleAsync();

        main.ShowStatsCommand.Execute(null);
        await app.SettleAsync();
        main.GoBack();

        var back = await LoadedAsync(main);
        Assert.NotSame(folders, back);
        await UiThread.Until(() => back.Selected is not null, "戻った先で行が選ばれる");
        Assert.Equal("shared-bundle.zip", back.Selected!.Name);
        Assert.Equal(itemId, back.Selected.Entry?.Item?.Id);

        // 右に出ているのもその商品
        await app.SettleAsync();
        var page = Assert.IsType<ItemViewModel>(back.Detail);
        Assert.Equal(itemId, page.Item.Id);
    });

    private static async Task<FolderViewModel> LoadedAsync(MainViewModel main)
    {
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        return folders;
    }
}
