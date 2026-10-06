using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Xunit;

namespace Chmonos.App.Tests;

/// <summary>
/// 監視フォルダ・取り込み元の名前を変えた・移したとき、外付けを外しているのと分けて「見つかりません」と言う
/// （2026-10-05・見つからない・移動の点検 9）。前は外付けと同じに黙る・「つながっていないため探せませんでした」と言っていた。
/// 外付けを外している側（ドライブの根が無い）は実マシンのドライブの構成に左右されるので、文の出し分けは値を渡して確かめる。
/// </summary>
public sealed class WatchedFolderMissingTests
{
    [Fact]
    public Task 名前を変えた監視フォルダで探すと_見つからないと言い_つながっていないとは言わない() => TestApp.Run(async app =>
    {
        var gone = System.IO.Path.Combine(app.Root, "files", "library", "moved-away.zip");
        await app.AddItemAsync(Make.Item("9900401", "作り物の衣装").WithFiles(Make.File(gone)));
        var renamed = System.IO.Path.Combine(app.Root, "名前を変える前");
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [renamed] });
        var main = await app.StartAsync();

        main.Import.FindMissingFilesCommand.Execute(null);
        await UiThread.Until(() => main.Import.MissingSearchNotes.Contains("確認できませんでした", StringComparison.Ordinal), "探した結果が出る");

        Assert.Equal("1 件を探しましたが、見つかりませんでした。", main.Import.MissingSearchText);
        // 結果の欄は何が起きたかだけ（どの監視フォルダか）。直し方は監視対象の欄の注意が言う（ユーザ 2026-10-06）
        Assert.Equal("監視フォルダ「名前を変える前」は見つからなかったため確認できませんでした。", main.Import.MissingSearchNotes);
    });

    [Fact]
    public void 外付けの監視フォルダは_名前でつながっていないと言い_足したフォルダは監視フォルダと言わず_多ければ数でまとめる()
    {
        var lines = ImportViewModel.MissingSearchNoteLines(new Core.Services.MissingFileSearchResult
        {
            MissingBefore = 1,
            Relinked = 1,
            Hashed = 1,
            Unreachable = [@"E:\作り物\監視"],
        }, [@"E:\作り物\監視"]);

        Assert.Equal(["監視フォルダ「監視」はつながっていないため確認できませんでした。"], lines);

        var added = ImportViewModel.MissingSearchNoteLines(new Core.Services.MissingFileSearchResult
        {
            MissingBefore = 1, Relinked = 0, Hashed = 0, NotFoundFolders = [@"D:\保管"],
        }, [@"E:\作り物\監視"]);
        Assert.Equal(["フォルダ「保管」は見つからなかったため確認できませんでした。"], added);

        var many = ImportViewModel.MissingSearchNoteLines(new Core.Services.MissingFileSearchResult
        {
            MissingBefore = 1, Relinked = 0, Hashed = 0, Unreachable = [@"E:\a", @"E:\b", @"E:\c", @"E:\d"],
        }, []);
        Assert.Equal(["4 個のフォルダはつながっていないため確認できませんでした。"], many);
    }

    [Fact]
    public Task 起動時に監視フォルダが見つからなければ_取り込み画面で名前を出して言う() => TestApp.Run(async app =>
    {
        var renamed = System.IO.Path.Combine(app.Root, "名前を変える前");
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [renamed] });
        var main = await app.StartAsync();

        await UiThread.Until(() => main.HasWatchedMissing, "見つからない監視フォルダが届く");

        Assert.Equal("監視フォルダ「名前を変える前」が見つかりません。", main.WatchedMissingText);
    });

    [Fact]
    public void 見つからない監視フォルダが複数なら数で言う()
    {
        Assert.Equal(string.Empty, MainViewModel.WatchedMissingSummary([]));
        Assert.Equal("監視フォルダが 2 個見つかりません。", MainViewModel.WatchedMissingSummary([@"D:\作り物\甲", @"D:\作り物\乙"]));
    }

    [Fact]
    public Task 設定の一覧で_名前を変えたフォルダは見つからないと言う() => TestApp.Run(async app =>
    {
        var renamed = System.IO.Path.Combine(app.Root, "名前を変える前");
        await app.ChangeSettingsAsync(settings => settings with { WatchedFolders = [renamed], ImportFolders = [renamed] });
        var main = await app.StartAsync();

        main.ShowSettingsCommand.Execute(null);
        var settings = Assert.IsType<SettingsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => settings.Watched.Count == 1 && settings.Folders.Count == 1, "設定の一覧が並ぶ");

        Assert.Equal("見つかりません", settings.Watched[0].StatusText);
        Assert.Equal("見つかりません", settings.Folders[0].StatusText);
    });

    [Fact]
    public void 設定の一覧で_ドライブがつながっていなければ今つながっていないと言う()
    {
        Assert.Equal("今つながっていません", new ImportFolderRow { Path = @"E:\作り物", Exists = false, DriveConnected = false }.StatusText);
        Assert.Equal(string.Empty, new ImportFolderRow { Path = @"D:\作り物", Exists = true, DriveConnected = true }.StatusText);
    }
}
