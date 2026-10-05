using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページのフォルダの行の「Unity ▾」（ユーザ判断 2026-10-05）。フォルダの中に unitypackage が1つ以上あるときだけ並べ、
/// zip の行と同じ部品（<see cref="UnityPackageRow"/>）・同じ命令で送る。Unity は動かさない（行の出し分けだけを見る）。
/// </summary>
public class ItemFolderUnityTests
{
    private static async Task<ItemViewModel> OpenAsync(TestApp app, LocalFolderRecord folder, bool forEditing = false)
    {
        var item = Make.Item("9900701", "作り物の衣装G").WithFiles();
        await app.AddItemAsync(item with { Local = item.Local with { LocalFolders = [folder] } });
        var main = await app.StartAsync();
        var page = new ItemViewModel(item with { Local = item.Local with { LocalFolders = [folder] } }, app.Services, main, main.Thumbnails, forEditing);
        // 在るかは行を出した後で裏で確かめ、行の一覧ごと差し替える。差し替わるまで待つ
        var first = page.LocalFolders;
        await UiThread.Until(() => !ReferenceEquals(page.LocalFolders, first), "フォルダの行が確かめ終わる");
        return page;
    }

    [Fact]
    public Task フォルダの中にunitypackageがあれば_行に包みが並び_Unityの吹き出しは在るときの文() => TestApp.Run(async app =>
    {
        var file = app.NewFile(@"lib\outfit\Outfit.unitypackage");
        var folder = new LocalFolderRecord { Path = System.IO.Path.GetDirectoryName(file)!, UnityPackages = ["Outfit.unitypackage"] };

        var page = await OpenAsync(app, folder);

        var row = Assert.Single(page.LocalFolders);
        Assert.True(row.HasUnityPackages);
        Assert.False(row.HasManyUnityPackages);
        var package = Assert.Single(row.UnityPackageRows);
        Assert.True(package.Entry.InFolder);
        Assert.Equal("Outfit", package.Name);
        Assert.True(package.FileRow.CanReveal);
        Assert.Equal("開いているUnityへ送るか、Unityのプロジェクトタブで場所を示します。", package.FileRow.UnityMenuTip);
        Assert.True(page.HasAnyUnityPackage);
    });

    [Fact]
    public Task フォルダの中にunitypackageが無ければ_Unityの欄は出ない() => TestApp.Run(async app =>
    {
        var file = app.NewFile(@"lib\plain\readme.txt");
        var folder = new LocalFolderRecord { Path = System.IO.Path.GetDirectoryName(file)! };

        var page = await OpenAsync(app, folder);

        var row = Assert.Single(page.LocalFolders);
        Assert.False(row.HasUnityPackages);
        Assert.Empty(row.UnityPackageRows);
        Assert.False(page.HasAnyUnityPackage);
    });

    [Fact]
    public Task 記録にあっても今は無いunitypackageは_並べない() => TestApp.Run(async app =>
    {
        // 送れない物を並べない（zip の行が zip の在ることを見るのと同じ）
        var file = app.NewFile(@"lib\gone\readme.txt");
        var folder = new LocalFolderRecord { Path = System.IO.Path.GetDirectoryName(file)!, UnityPackages = ["Gone.unitypackage"] };

        var page = await OpenAsync(app, folder);

        Assert.Empty(Assert.Single(page.LocalFolders).UnityPackageRows);
    });

    [Fact]
    public Task 編集画面の中の商品ページには_使う操作なのでUnityを並べない() => TestApp.Run(async app =>
    {
        var file = app.NewFile(@"lib\edit\Edit.unitypackage");
        var folder = new LocalFolderRecord { Path = System.IO.Path.GetDirectoryName(file)!, UnityPackages = ["Edit.unitypackage"] };

        var page = await OpenAsync(app, folder, forEditing: true);

        Assert.Empty(Assert.Single(page.LocalFolders).UnityPackageRows);
    });

    [Fact]
    public void フォルダが見つからないときは_Unityを押せず理由を言う()
    {
        var row = new LocalFolderRow { Path = @"C:\作り物\衣装H", Name = "衣装H", SummaryText = "1 ファイル", IsMissing = true };

        Assert.False(row.CanReveal);
        Assert.Equal("フォルダが見つかりません。", row.UnityMenuTip);
    }
}
