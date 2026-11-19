using System.IO;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Tests;

/// <summary>設定の画面の監視フォルダの欄の「フォルダを追加…」（ユーザ判断 2026-11-20）。取り込み画面の同じボタンと同じ命令で、取り込まずに足す。</summary>
public class SettingsAddWatchedFolderTests
{
    [Fact]
    public Task 設定の画面から足すと_取り込み画面と同じに監視へ入り_行と文が出る_取り込みはしない() => TestApp.Run(async app =>
    {
        var folder = Path.GetDirectoryName(app.NewFile(@"settings-watch\a.zip", [1, 2, 3, 4]))!;
        var main = await app.StartAsync();
        var settings = new SettingsViewModel(app.Services, main);
        await UiThread.Until(() => !settings.IsLoading, "設定を読み終わる");
        Assert.False(settings.HasWatched);

        await settings.AddWatchedFoldersAsync([folder]);

        Assert.True(settings.HasWatched);
        Assert.Equal(folder, settings.Watched.Single().Path);
        Assert.Equal($"「{folder}」を監視フォルダに追加しました。中のファイル 1 件はまだ取り込んでいません。", settings.WatchAddText);
        Assert.Contains(app.Services.Settings.WatchedFolders, known => string.Equals(known, folder, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(app.Services.Settings.ImportFolders);
        Assert.Contains(folder, main.Import.Watched);
    });
}
