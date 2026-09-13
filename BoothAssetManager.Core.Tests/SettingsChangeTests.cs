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
        await settings.UpdateAsync(current => current with { FilterPanelCollapsed = true });

        var saved = _store.Settings.Load();
        Assert.Equal([@"D:\BOOTH"], saved.ImportFolders);
        Assert.True(saved.FilterPanelCollapsed);
        Assert.Equal(saved.ImportFolders, settings.Current.ImportFolders);
    }

    /// <summary>同時に書いても、一時ファイルがぶつからず、どちらの変更も残る（前は投げっぱなしの保存が重なりえた）。</summary>
    [Fact]
    public async Task 同時に書いても両方残る()
    {
        var settings = new SettingsService(_store);

        await Task.WhenAll(
            Task.Run(() => settings.UpdateAsync(current => current with { NavCollapsed = true })),
            Task.Run(() => settings.UpdateAsync(current => current with { FilterPanelCollapsed = true })),
            Task.Run(() => settings.UpdateAsync(current => current with { SearchExtraFilters = ["Folder"] })));

        var saved = _store.Settings.Load();
        Assert.True(saved.NavCollapsed);
        Assert.True(saved.FilterPanelCollapsed);
        Assert.Equal(["Folder"], saved.SearchExtraFilters);
    }

    [Fact]
    public async Task 取得の間隔は約束の範囲に戻して書く()
    {
        var settings = new SettingsService(_store);

        await settings.UpdateAsync(current => current with { FetchIntervalMs = 100 });

        Assert.True(settings.Current.FetchIntervalMs >= 1500);
    }
}
