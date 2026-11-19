using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// フォルダビューの右に未確定を出しているときに落とした物も、未確定の画面と同じ規則で振り分ける（ユーザのメモ 2026-11-18）。
/// 前は未確定の画面だけを見ていて、右でファイルを選んでいてもブラウザの商品ページが「登録しますか」の問いになった。
/// </summary>
public class FolderResolveDropTests
{
    private const string Url = "https://booth.pm/ja/items/1234567";

    private static async Task<(MainViewModel Main, ResolveViewModel Resolve)> OpenEmbeddedResolveAsync(TestApp app)
    {
        var path = app.NewFile(@"materials\costume.zip", [1, 2, 3]);
        await app.Store.Unresolved.SaveAsync([new UnresolvedFile
        {
            Hash = Make.HashOf(path),
            Paths = [path],
            SizeBytes = 3,
            ModifiedAtUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            FirstSeenAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        }]);
        var main = await app.StartAsync();
        main.ShowFoldersCommand.Execute(null);
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");

        var detail = folders.Rows.ToList()
            .Select(row => { folders.Selected = row; return folders.Detail; })
            .OfType<FolderViewDetail>()
            .First(found => found.Unresolved.Count == 1);
        folders.OpenUnresolvedCommand.Execute(detail);
        var resolve = Assert.IsType<ResolveViewModel>(folders.Detail);
        await UiThread.Until(() => resolve.IsLoaded, "右の未確定の読み込みが済む");
        await app.SettleAsync();
        return (main, resolve);
    }

    [Fact]
    public Task ファイルを選んでいれば_商品ページはそのファイルの商品IDとして入り_登録は聞かない() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenEmbeddedResolveAsync(app);
        resolve.Selected ??= resolve.Files.First();
        Assert.True(resolve.HasSelection);

        await main.HandleDropAsync(null, Url);
        await app.SettleAsync();

        Assert.Equal("1234567", resolve.ItemIdInput);
        Assert.DoesNotContain(app.Notices, notice => notice.Caption == "BOOTHのURLを受け取りました");
        Assert.Null(await app.Store.Items.LoadAsync("1234567"));
    });

    /// <summary>何も選んでいなければ、未確定の画面と同じく普段の振り分け（持っていない商品は登録するかを聞く）</summary>
    [Fact]
    public Task ファイルを選んでいなければ_未確定の画面と同じく登録するかを聞く() => TestApp.Run(async app =>
    {
        var (main, resolve) = await OpenEmbeddedResolveAsync(app);
        resolve.Selected = null;
        Assert.False(resolve.HasSelection);
        app.Answer = _ => System.Windows.MessageBoxResult.No;

        await main.HandleDropAsync(null, Url);
        await app.SettleAsync();

        Assert.Contains(app.Notices, notice => notice.Caption == "BOOTHのURLを受け取りました");
        Assert.True(string.IsNullOrEmpty(resolve.ItemIdInput));
    });
}
