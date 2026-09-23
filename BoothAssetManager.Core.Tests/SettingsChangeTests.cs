using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>設定の書き手を1つにし、変え方だけを渡す（技術的負債 1-1・1-4、2026-09-14）。</summary>
public sealed class SettingsChangeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-settings-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public SettingsChangeTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 前は、取り込み画面で足した取り込み元が、別の画面の保存（起動時に読んだ古い写し）で消えていた。
    /// 変え方を渡せば、別の画面が書いた項目は残る。
    /// </summary>
    [Fact]
    public async Task 別の画面が書いた項目を消さない()
    {
        var settings = new SettingsService(_store);

        await settings.UpdateAsync(current => current with { ImportFolders = [@"D:\BOOTH"] });
        await settings.UpdateAsync(current => current with { ShowAdult = false });

        var saved = _store.Settings.Load();
        Assert.Equal([@"D:\BOOTH"], saved.ImportFolders);
        Assert.False(saved.ShowAdult);
        Assert.Equal(saved.ImportFolders, settings.Current.ImportFolders);
    }

    /// <summary>画面が使う道（UiCommand.ChangeSettings → CommandHandler）でも、変え方が今の設定に当たり、書いた値が返る。</summary>
    [Fact]
    public async Task 命令を通しても変え方が今の設定に当たる()
    {
        var settings = new SettingsService(_store);
        await settings.UpdateAsync(current => current with { ImportFolders = [@"D:\BOOTH"] });
        var handler = new BoothAssetManager.Core.Commands.CommandHandler(null!, null!, settings: settings);

        var result = await handler.ExecuteAsync(
            new BoothAssetManager.Core.Commands.UiCommand.ChangeSettings(current => current with { SaveImages = false }));

        var changed = Assert.IsType<BoothAssetManager.Core.Commands.CommandResult.SettingsChanged>(result).Settings;
        Assert.False(changed.SaveImages);
        Assert.Equal([@"D:\BOOTH"], changed.ImportFolders);
        Assert.False(_store.Settings.Load().SaveImages);
    }

    /// <summary>同時に書いても、一時ファイルがぶつからず、どちらの変更も残る（前は投げっぱなしの保存が重なりえた）。</summary>
    [Fact]
    public async Task 同時に書いても両方残る()
    {
        var settings = new SettingsService(_store);

        await Task.WhenAll(
            Task.Run(() => settings.UpdateAsync(current => current with { ShowAdult = false })),
            Task.Run(() => settings.UpdateAsync(current => current with { ShowSubTagsInList = true })),
            Task.Run(() => settings.UpdateAsync(current => current with { WatchedFolders = [@"D:\DL"] })));

        var saved = _store.Settings.Load();
        Assert.False(saved.ShowAdult);
        Assert.True(saved.ShowSubTagsInList);
        Assert.Equal([@"D:\DL"], saved.WatchedFolders);
    }

    /// <summary>ドラッグで変えた画面の幅は、画面の状態のファイルに人が読める形で残る（ユーザ判断 2026-09-14）。</summary>
    [Fact]
    public async Task 画面の幅を場所ごとに残す()
    {
        var settings = new SettingsService(_store);

        await settings.UpdateUiStateAsync(state => state with
        {
            PaneWidths = new Dictionary<string, double> { ["folder.list"] = 480, ["edit.right"] = 520 },
        });

        var saved = _store.UiState.Load();
        Assert.Equal(480, saved.PaneWidths["folder.list"]);
        Assert.Equal(520, saved.PaneWidths["edit.right"]);
        Assert.Contains("\"folder.list\": 480", File.ReadAllText(_store.UiState.Path));
    }

    /// <summary>画面の状態は設定とは別のファイルに書き、設定ファイルを書き直さない（技術的負債 3-2）。</summary>
    [Fact]
    public async Task 画面の状態は設定とは別のファイルに書く()
    {
        var settings = new SettingsService(_store);

        await settings.UpdateUiStateAsync(state => state with
        {
            NavCollapsed = true,
            SearchModules = [new SearchModuleState { Kind = "Path", Items = [@"D:\Assets"] }],
        });

        Assert.False(File.Exists(_store.Settings.Path));
        var saved = _store.UiState.Load();
        Assert.True(saved.NavCollapsed);
        Assert.Equal([@"D:\Assets"], Assert.Single(saved.SearchModules!).Items);
        Assert.True(settings.UiState.NavCollapsed);
    }

    [Fact]
    public async Task 取得の間隔は約束の範囲に戻して書く()
    {
        var settings = new SettingsService(_store);

        await settings.UpdateAsync(current => current with { FetchIntervalMs = 100 });

        Assert.True(settings.Current.FetchIntervalMs >= 1500);
    }
}
