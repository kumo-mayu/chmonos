using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-settings-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new SettingsService(_store);
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

    private Task SaveItemAsync(string id, bool hidden)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { IsHidden = hidden },
        });

    [Fact]
    public async Task ListsOnlyHiddenItems()
    {
        await SaveItemAsync("1", hidden: true);
        await SaveItemAsync("2", hidden: false);

        var hidden = await _service.LoadHiddenAsync();

        Assert.Equal(["1"], hidden.Select(item => item.ItemId));
    }

    /// <summary>非表示は設定からしか戻せないので、ここが効かないと二度と出てこない。</summary>
    [Fact]
    public async Task UnhideBringsTheItemBack()
    {
        await SaveItemAsync("1", hidden: true);

        await _service.UnhideAsync("1");

        Assert.Empty(await _service.LoadHiddenAsync());
        var item = await _store.Items.LoadAsync("1");
        Assert.False(item!.Local.IsHidden);
    }

    [Fact]
    public async Task RestoringAnExcludedFileRemovesTheEntry()
    {
        await _store.Excluded.SaveAsync(
        [
            new ExcludedEntry { Hash = "AAAA", Paths = ["x.zip"], ExcludedAt = DateTimeOffset.Now, Reason = "BOOTH商品ではない" },
            new ExcludedEntry { Hash = "BBBB", Paths = ["y.zip"], ExcludedAt = DateTimeOffset.Now },
        ]);

        await _service.RestoreExcludedAsync("AAAA");

        Assert.Equal(["BBBB"], _service.LoadExcluded().Select(entry => entry.Hash));
    }

    [Fact]
    public async Task SavesAndReloadsSettings()
    {
        await _service.SaveAsync(new AppSettings { ShowAdult = false, RefreshIntervalDays = 21 });

        var reloaded = _store.Settings.Load();

        Assert.False(reloaded.ShowAdult);
        Assert.Equal(21, reloaded.RefreshIntervalDays);
    }

    /// <summary>保存先が空でも落ちない（初回起動でディレクトリが無い）。</summary>
    [Fact]
    public async Task ReportsZeroUsageOnAnEmptyStore()
    {
        var usage = await _service.LoadUsageAsync();

        Assert.Equal(0, usage.ImageCount);
        Assert.Equal(_root, usage.Root);
    }
}
