using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 「見つからないファイルを探す」の結果を、何が起きたか分かる形で並べる（手触りの確認 2026-10-06・メモ73）。
/// 前は1行の文だけで、監視していない場所を足して探した回に結び直っていたのに、画面では見つからなかったように見えた。
/// </summary>
public class MissingSearchResultTests
{
    private static async Task<(string Path, LocalFileRecord File)> FileOfAsync(TestApp app, string relative, byte[] content)
    {
        var path = app.NewFile(relative, content);
        return (path, new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(path),
            Paths = [path],
            SizeBytes = content.Length,
        });
    }

    [Fact]
    public Task 監視していない場所を足して探すと_紐付け直した物と見つからなかった物が商品とファイルで並ぶ() => TestApp.Run(async app =>
    {
        var watched = Path.GetDirectoryName(app.NewFile(@"watched\keep.txt"))!;
        var (movedFrom, movedFile) = await FileOfAsync(app, @"watched\sample-move.zip", [1, 2, 3, 4]);
        var (deleted, deletedFile) = await FileOfAsync(app, @"watched\sample-gone.zip", [5, 6, 7, 8, 9]);
        await app.AddItemAsync(Make.Item("9900001", "作り物の移動テスト").WithFiles(movedFile));
        await app.AddItemAsync(Make.Item("9900002", "作り物の消したテスト").WithFiles(deletedFile));
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var main = await app.StartAsync();

        var elsewhere = Path.Combine(app.Root, "files", "保管");
        Directory.CreateDirectory(elsewhere);
        File.Move(movedFrom, Path.Combine(elsewhere, "sample-move.zip"));
        File.Delete(deleted);
        app.PickSearchScope = model =>
        {
            model.AddFolders([elsewhere]);
            return true;
        };

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.HasRelinkedFiles, "探した結果が出る");
        await app.SettleAsync();

        Assert.Equal("1 件を新しい場所に紐付け直し、1 件は見つかりませんでした。", main.Import.MissingSearchText);

        var relinked = Assert.Single(main.Import.RelinkedFiles);
        Assert.Equal(("作り物の移動テスト", "sample-move.zip", elsewhere), (relinked.ItemName, relinked.FileName, relinked.FolderText));
        Assert.True(main.Import.IsRelinkedExpanded);

        var notFound = Assert.Single(main.Import.NotFoundFiles);
        Assert.Equal(("作り物の消したテスト", "sample-gone.zip"), (notFound.ItemName, notFound.FileName));
        Assert.False(notFound.HasFolder);
        Assert.Equal(string.Empty, main.Import.MissingSearchNotes);

        // 探し直すと前の回の結果は消える（結び直した後なので、見つからないのは消した方だけ）
        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => !main.Import.HasRelinkedFiles && main.Import.MissingSearchText.StartsWith("1 件を探しましたが", StringComparison.Ordinal), "探し直した結果が出る");
        Assert.Equal("作り物の消したテスト", Assert.Single(main.Import.NotFoundFiles).ItemName);
    });

    [Fact]
    public Task 結果の商品名を押すと商品ページが開く() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "library", "moved-away.zip");
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装").WithFiles(Make.File(gone)));
        var main = await app.StartAsync();

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.HasNotFoundFiles, "探した結果が出る");
        Assert.Single(main.Import.NotFoundFiles).OpenItemCommand!.Execute(null);
        await app.SettleAsync();

        Assert.Equal("9900001", Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id);
    });

    [Fact]
    public Task 押す前に商品が消えていれば_その行に開けないと出る() => TestApp.Run(async app =>
    {
        var gone = Path.Combine(app.Root, "files", "library", "moved-away.zip");
        await app.AddItemAsync(Make.Item("9900001", "作り物の衣装").WithFiles(Make.File(gone)));
        var main = await app.StartAsync();

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.HasNotFoundFiles, "探した結果が出る");
        await app.SettleAsync();
        await app.Store.Items.DeleteAsync("9900001");

        var row = Assert.Single(main.Import.NotFoundFiles);
        row.OpenItemCommand!.Execute(null);
        await UiThread.Until(() => row.HasStatus, "行に理由が出る");

        Assert.IsNotType<ItemViewModel>(main.CurrentViewModel);
        Assert.Equal("この商品は見つかりませんでした。商品IDを変えたか、管理対象から除外した可能性があります。", row.StatusText);
    });

    [Fact]
    public Task 多ければ畳んで出し_少なければ開いて出す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        MissingFileOutcome Gone(int i) => new()
        {
            ItemId = $"99000{i:00}",
            ItemName = $"作り物の衣装{i:00}",
            OldPaths = [$@"D:\作り物\{i:00}.zip"],
        };

        main.Import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = ImportViewModel.ResultFoldOver + 2,
            Relinked = 1,
            Hashed = 0,
            RelinkedFiles = [Gone(0) with { NewPath = @"D:\移した先\00.zip" }],
            NotFoundFiles = [.. Enumerable.Range(1, ImportViewModel.ResultFoldOver + 1).Select(Gone)],
        });

        Assert.True(main.Import.IsRelinkedExpanded);
        Assert.False(main.Import.IsNotFoundExpanded);
        Assert.Equal($"  {ImportViewModel.ResultFoldOver + 1} 件", main.Import.NotFoundCountText);
        Assert.Equal(@"D:\移した先", Assert.Single(main.Import.RelinkedFiles).FolderText);

        // 名前の順に並ぶ
        Assert.Equal(
            main.Import.NotFoundFiles.Select(row => row.ItemName).Order(StringComparer.CurrentCulture),
            main.Import.NotFoundFiles.Select(row => row.ItemName));
    });

    [Fact]
    public void 場所が全部外れたファイルは_名前の分からないファイルと出す()
    {
        var row = ImportViewModel.ResultRowOf(new MissingFileOutcome { ItemId = "9900001", ItemName = "作り物の衣装", OldPaths = [] });

        Assert.Equal("名前の分からないファイル", row.FileName);
    }

    [Fact]
    public void 紐付け直した行は_新しい場所の名前とフォルダを出す()
    {
        var row = ImportViewModel.ResultRowOf(new MissingFileOutcome
        {
            ItemId = "9900001",
            ItemName = "作り物の衣装",
            OldPaths = [@"D:\元\衣装.zip"],
            NewPath = @"E:\保管\衣装_v2.zip",
        });

        Assert.Equal(("衣装_v2.zip", @"E:\保管", @"E:\保管\衣装_v2.zip"), (row.FileName, row.FolderText, row.PathTip));
    }

    [Fact]
    public void 要約は1行で_紐付け直した数と見つからなかった数を言う()
    {
        static string Summary(int before, int relinked)
            => ImportViewModel.MissingSearchSummary(new MissingFileSearchResult { MissingBefore = before, Relinked = relinked, Hashed = 0 });

        Assert.Equal("2 件を新しい場所に紐付け直しました。", Summary(2, 2));
        Assert.Equal("2 件を新しい場所に紐付け直し、1 件は見つかりませんでした。", Summary(3, 2));
        Assert.Equal("3 件を探しましたが、見つかりませんでした。", Summary(3, 0));
    }
}
