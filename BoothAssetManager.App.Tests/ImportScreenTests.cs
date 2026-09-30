using BoothAssetManager.App.Tests.Support;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.App.Tests;

/// <summary>
/// 取り込みの画面の、結果の文のほかの文言と、対象の出し入れ。
/// （読めなかった物・壊れた zip・BOOTH で取れなかった物の文は <see cref="ImportResultTextTests"/>）
/// </summary>
public class ImportScreenTests
{
    // ---- 対応アバターの検出の結果 ----

    [Fact]
    public void 検出が走らなかった回は_見つからなかったと言わない()
        // 走らなかったのに「見つかりませんでした」と言うと、自動で走っていないと受け取られる
        => Assert.Equal(
            "BOOTHから新しく取得した商品が無いため、対応アバターの検出はしていません。",
            ImportViewModel.DescribeDetection(new ImportSummary { AvatarDetectRan = false }));

    [Fact]
    public void 検出して見つからなければ_見つからなかったと言う()
        => Assert.Equal(
            "対応アバターは見つかりませんでした。",
            ImportViewModel.DescribeDetection(new ImportSummary { AvatarDetectRan = true }));

    [Fact]
    public void 検出して見つかれば_体数と記録した商品の数を言う()
        => Assert.Equal(
            "対応アバターを 3 体検出し、5 件の商品に記録しました。",
            ImportViewModel.DescribeDetection(
                new ImportSummary { AvatarDetectRan = true, AvatarsFound = 3, AvatarItemsUpdated = 5 }));

    [Fact]
    public void 検出が途中で止まった回は_やり直す道を言う()
    {
        var text = ImportViewModel.DescribeDetection(new ImportSummary
        {
            AvatarDetectRan = true,
            AvatarItemsUpdated = 5,
            AvatarDetectError = "保存先に書けませんでした。",
        });

        // 取り込みは済んでいる。検出はやり直せるので、その道を言う（止まったのに「記録しました」と言わない）
        Assert.Equal(
            "対応アバターの検出は途中で止まりました。保存先に書けませんでした。アバターの管理の「対応アバターを検出する」からやり直せます。",
            text);
    }

    // ---- 残りの時間の言い方 ----

    [Theory]
    [InlineData(0, "1分以内")]
    [InlineData(59, "1分以内")]
    [InlineData(60, "約 1 分")]
    [InlineData(61, "約 2 分")]
    [InlineData(3540, "約 59 分")]
    [InlineData(3600, "約 1 時間 0 分")]
    [InlineData(3900, "約 1 時間 5 分")]
    public void 残りの時間は_分で言う(double seconds, string expected)
        // 秒まで出すと毎回変わって読めない
        => Assert.Equal(expected, ImportViewModel.Duration(seconds));

    // ---- 対象の出し入れ ----

    [Fact]
    public Task 履歴から対象に積むと_始めるが押せるようになる() => TestApp.Run(async app =>
    {
        var folder = System.IO.Path.GetDirectoryName(app.NewFile(@"downloads\costume.zip"))!;
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [folder] });
        var import = (await app.StartAsync()).Import;

        // 履歴は「何を読んだかを確かめる一覧」で、対象ではない。開いただけでは対象は空
        Assert.Equal([folder], import.History);
        Assert.True(import.HasHistory);
        Assert.Empty(import.Folders);
        Assert.False(import.StartCommand.CanExecute(null));

        import.TakeFromHistoryCommand.Execute(folder);

        Assert.Equal([folder], import.Folders);
        Assert.True(import.HasFolders);
        Assert.True(import.StartCommand.CanExecute(null));
    });

    [Fact]
    public Task 同じ場所を2回積んでも_対象は1つ() => TestApp.Run(async app =>
    {
        var folder = System.IO.Path.GetDirectoryName(app.NewFile(@"downloads\costume.zip"))!;
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [folder] });
        var import = (await app.StartAsync()).Import;

        import.TakeFromHistoryCommand.Execute(folder);
        import.TakeFromHistoryCommand.Execute(folder.ToUpperInvariant());

        Assert.Single(import.Folders);
    });

    [Fact]
    public Task 対象から外すと_始めるは押せなくなる() => TestApp.Run(async app =>
    {
        var folder = System.IO.Path.GetDirectoryName(app.NewFile(@"downloads\costume.zip"))!;
        await app.ChangeSettingsAsync(settings => settings with { ImportFolders = [folder] });
        var import = (await app.StartAsync()).Import;
        import.TakeFromHistoryCommand.Execute(folder);

        import.RemoveFolderCommand.Execute(folder);
        await app.SettleAsync();

        Assert.Empty(import.Folders);
        Assert.False(import.StartCommand.CanExecute(null));
    });

    // ---- 前回の続き ----

    [Fact]
    public Task 前回が途中で終わっていなければ_続きの知らせは出さない() => TestApp.Run(async app =>
    {
        var main = await app.StartAsync();

        Assert.False(main.Import.IsRunning);
        Assert.False(main.IsImporting);
    });

    // ---- 見つからないファイルを探す ----

    [Fact]
    public Task 見つからないファイルが無ければ_無かったと言う() => TestApp.Run(async app =>
    {
        var file = app.NewFile(@"library\costume.zip");
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(file)));
        var import = (await app.StartAsync()).Import;

        import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        // 押しても何も起きなかったときこそ、結果の1行が要る
        Assert.Equal("見つからないファイルはありませんでした。", import.MissingSearchText);
        Assert.True(import.FindMissingFilesCommand.CanExecute(null));
    });

    [Fact]
    public Task 移したファイルが監視フォルダに無ければ_次にやることを言う() => TestApp.Run(async app =>
    {
        var gone = System.IO.Path.Combine(app.Root, "files", "library", "moved-away.zip");
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(Make.File(gone)));
        var import = (await app.StartAsync()).Import;

        import.FindMissingFilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(
            "紐付け直せたものはありませんでした。1 件は監視フォルダの中に見つかりませんでした。"
            + "移した先を監視フォルダに追加してから、もう一度押してください。",
            import.MissingSearchText);
    });

    /// <summary>
    /// 探す処理は丸ごと裏で走る（2026-09-30）。画面の側は、押した直後の文・押せない間・結果の文を画面のスレッドで入れ、
    /// 裏から届く進み具合の文が結果の文の後に残らないこと。
    /// </summary>
    [Fact]
    public Task 監視フォルダの中で移したファイルは_紐付け直して件数を言う() => TestApp.Run(async app =>
    {
        var moved = app.NewFile(@"watched\sub\moved.zip", [7, 7, 7, 7]);
        var watched = System.IO.Path.Combine(app.Root, "files", "watched");
        var gone = System.IO.Path.Combine(watched, "costume.zip");
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装").WithFiles(
            Make.File(gone) with { Hash = await FileHasher.ComputeSha256Async(moved), SizeBytes = 4 }));
        var main = await app.StartAsync();

        // 監視フォルダは主画面を作った後に入れる（先に入れると、起動時の新着の確かめが同じフォルダを見に行く）
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [watched] });
        var import = main.Import;

        import.FindMissingFilesCommand.Execute(null);

        // 押した直後：探している間は押せない
        Assert.Equal("見つからないファイルを調べています…", import.MissingSearchText);
        Assert.False(import.FindMissingFilesCommand.CanExecute(null));

        await app.SettleAsync();

        Assert.Equal("1 件を新しい場所に紐付け直しました。", import.MissingSearchText);
        Assert.True(import.FindMissingFilesCommand.CanExecute(null));
        var item = await app.Store.Items.LoadAsync("1000001");
        Assert.Equal(moved, Assert.Single(Assert.Single(item!.Local.LocalFiles).Paths));
    });
}
