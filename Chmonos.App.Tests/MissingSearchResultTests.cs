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

    private static MissingFileOutcome Gone(int i) => new()
    {
        ItemId = $"9900{i:000}",
        ItemName = $"作り物の衣装{i:000}",
        OldPaths = [$@"D:\作り物\{i:000}.zip"],
    };

    private static MissingFolder FolderGone(int i) => new()
    {
        ItemId = $"9901{i:000}",
        ItemName = $"作り物の髪型{i:000}",
        Path = $@"D:\作り物\展開\{i:000}",
        FileCount = 3,
        TotalBytes = 3_000,
    };

    /// <summary>平らな一覧を、見出しは「▶名前 n 件」「▼名前 n 件」、行は商品名、次の手は「…」で書き出す（並びと畳み具合を1行で比べる）。</summary>
    private static string[] LinesOf(ImportViewModel import) => [.. import.MissingResultLines.Select(line => line switch
    {
        MissingResultHeadLine head => $"{(head.IsExpanded ? "▼" : "▶")}{head.Title}{head.CountText}",
        MissingFileResultRow row => row.ItemName,
        MissingFolderRow folder => folder.ItemName,
        MissingResultHintLine hint => "…" + hint.Text,
        _ => line.GetType().Name,
    })];

    [Fact]
    public Task 結果の欄は_見出しと行を1本に並べ_多い見出しは畳み_次の手は畳んでも出す() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();
        var import = main.Import;

        import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = ImportViewModel.ResultFoldOver + 2,
            Relinked = 1,
            Hashed = 0,
            RelinkedFiles = [Gone(0) with { NewPath = @"D:\移した先\000.zip" }],
            NotFoundFiles = [.. Enumerable.Range(1, ImportViewModel.ResultFoldOver + 1).Select(Gone)],
        });
        import.ShowMissingFolders([FolderGone(1)]);

        Assert.True(import.HasMissingResult);
        Assert.True(import.IsRelinkedExpanded);
        Assert.False(import.IsNotFoundExpanded);
        Assert.True(import.IsMissingFoldersExpanded);
        Assert.Equal($"  {ImportViewModel.ResultFoldOver + 1} 件", import.NotFoundCountText);
        Assert.Equal(@"D:\移した先", Assert.Single(import.RelinkedFiles).FolderText);
        Assert.Equal(
        [
            "▼紐付け直したファイル  1 件",
            "作り物の衣装000",
            $"▶見つからなかったファイル  {ImportViewModel.ResultFoldOver + 1} 件",
            "…移した先のフォルダを追加して、もう一度探してください。",
            "▼見つからない登録フォルダ  1 件",
            "作り物の髪型001",
        ], LinesOf(import));

        // 名前の順に並ぶ
        Assert.Equal(
            import.NotFoundFiles.Select(row => row.ItemName).Order(StringComparer.CurrentCulture),
            import.NotFoundFiles.Select(row => row.ItemName));
    });

    [Fact]
    public Task 紐付け直した見出しと行は成功_見つからなかった物と登録フォルダは失敗として色を分ける() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;
        import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = 2,
            Relinked = 1,
            Hashed = 0,
            RelinkedFiles = [Gone(0) with { NewPath = @"D:\移した先\000.zip" }],
            NotFoundFiles = [Gone(1)],
        });
        var heads = import.MissingResultLines.OfType<MissingResultHeadLine>().ToDictionary(line => line.Title);
        Assert.True(heads["紐付け直したファイル"].Succeeded);
        Assert.False(heads["見つからなかったファイル"].Succeeded);

        var rows = import.MissingResultLines.OfType<MissingFileResultRow>().ToList();
        Assert.True(Assert.Single(rows, row => row.HasFolder).Succeeded);
        Assert.False(Assert.Single(rows, row => !row.HasFolder).Succeeded);
    });

    [Fact]
    public Task 見出しを開くと中の行がその見出しの下に入り_畳むと抜ける() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;
        import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = ImportViewModel.ResultFoldOver + 2,
            Relinked = 1,
            Hashed = 0,
            RelinkedFiles = [Gone(0) with { NewPath = @"D:\移した先\000.zip" }],
            NotFoundFiles = [.. Enumerable.Range(1, ImportViewModel.ResultFoldOver + 1).Select(Gone)],
        });
        var head = import.MissingResultLines.OfType<MissingResultHeadLine>().Single(line => line.Title == "見つからなかったファイル");

        // 見出しの印（画面の三角）で開く
        head.IsExpanded = true;
        Assert.True(import.IsNotFoundExpanded);
        var lines = LinesOf(import);
        Assert.Equal(1 + 1 + 1 + (ImportViewModel.ResultFoldOver + 1) + 1, lines.Length);
        Assert.Equal("作り物の衣装001", lines[3]);
        Assert.Equal("…移した先のフォルダを追加して、もう一度探してください。", lines[^1]);
        // 見出しの行は同じ物を使い回す（作り直すと、押した見出しの部品が作り直される）
        Assert.Same(head, import.MissingResultLines[2]);

        import.IsRelinkedExpanded = false;
        Assert.Equal("▶紐付け直したファイル  1 件", LinesOf(import)[0]);
        Assert.Equal($"▼見つからなかったファイル  {ImportViewModel.ResultFoldOver + 1} 件", LinesOf(import)[1]);
    });

    [Fact]
    public Task 境目までは開いて出し_数千件は畳んで出す() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;

        import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = ImportViewModel.ResultFoldOver,
            Relinked = 0,
            Hashed = 0,
            NotFoundFiles = [.. Enumerable.Range(1, ImportViewModel.ResultFoldOver).Select(Gone)],
        });
        Assert.True(import.IsNotFoundExpanded);
        Assert.Equal(1 + ImportViewModel.ResultFoldOver + 1, import.MissingResultLines.Count);

        import.ShowMissingFiles(new MissingFileSearchResult
        {
            MissingBefore = 3000,
            Relinked = 0,
            Hashed = 0,
            NotFoundFiles = [.. Enumerable.Range(1, 3000).Select(Gone)],
        });
        Assert.False(import.IsNotFoundExpanded);
        Assert.Equal(["▶見つからなかったファイル  3000 件", "…移した先のフォルダを追加して、もう一度探してください。"], LinesOf(import));

        import.IsNotFoundExpanded = true;
        Assert.Equal(3000 + 2, import.MissingResultLines.Count);
    });

    [Fact]
    public Task 並べる物も探せなかった場所も無ければ_結果の欄は出ない() => TestApp.Run(async app =>
    {
        var import = (await app.StartAsync()).Import;
        Assert.False(import.HasMissingResult);

        // 探せなかった場所の文だけでも欄は出す（何も並ばないが、探せなかったことは言う）
        import.ShowMissingFiles(new MissingFileSearchResult { MissingBefore = 0, Relinked = 0, Hashed = 0, Unreachable = [@"E:\外付け"] });
        Assert.True(import.HasMissingResult);
        Assert.Empty(import.MissingResultLines);
        Assert.Equal("フォルダ「外付け」はつながっていないため確認できませんでした。", import.MissingSearchNotes);

        // 登録フォルダの候補だけでも出す
        import.ShowMissingFiles(null);
        Assert.False(import.HasMissingResult);
        import.ShowMissingFolders([FolderGone(1)]);
        Assert.True(import.HasMissingResult);
        Assert.Equal(["▼見つからない登録フォルダ  1 件", "作り物の髪型001"], LinesOf(import));

        // 探し直す（窓を開いた時点で前の回を消す）と欄ごと消える
        import.ShowMissingFolders([]);
        Assert.False(import.HasMissingResult);
        Assert.Empty(import.MissingResultLines);
    });

    [Fact]
    public Task 登録フォルダの商品名を押すと商品ページが開く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901001", "作り物の髪型"));
        var main = await app.StartAsync();
        main.Import.ShowMissingFolders([FolderGone(1) with { ItemId = "9901001" }]);

        var row = main.Import.MissingResultLines.OfType<MissingFolderRow>().Single();
        row.OpenItemCommand!.Execute(null);
        await app.SettleAsync();

        Assert.Equal("9901001", Assert.IsType<ItemViewModel>(main.CurrentViewModel).Item.Id);
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
