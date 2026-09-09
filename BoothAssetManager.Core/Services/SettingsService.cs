using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>保存先の使用状況。設定画面に出す。</summary>
public sealed record StorageUsage
{
    public required string Root { get; init; }

    public required long ImageBytes { get; init; }

    public required int ImageCount { get; init; }

    public required long ItemBytes { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>非表示にした商品1件。設定画面から戻せるようにする。</summary>
public sealed record HiddenItem
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }
}

/// <summary>管理から外したファイル1件。</summary>
public sealed record ExcludedFile
{
    public required string Hash { get; init; }

    public required string Path { get; init; }

    public string? Reason { get; init; }

    public required DateTimeOffset ExcludedAt { get; init; }
}

public interface ISettingsService
{
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default);

    Task UnhideAsync(string itemId, CancellationToken cancellationToken = default);

    IReadOnlyList<ExcludedFile> LoadExcluded();

    Task RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default);
}

/// <summary>
/// 設定の保存と、設定画面に出す集計。
///
/// 「非表示にした商品」と「管理から外したファイル」をここから戻せるようにしているのは、
/// どちらも普段の画面からは見えなくなる操作で、設定画面以外に取り消す場所が無いため。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly DataStore _store;

    public SettingsService(DataStore store)
    {
        _store = store;
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => _store.Settings.SaveAsync(settings, cancellationToken);

    /// <summary>保存先が何をどれだけ使っているか。画像は実ファイルを数える。</summary>
    public Task<StorageUsage> LoadUsageAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var (imageBytes, imageCount) = Measure(_store.Paths.ImagesDir);
                var (itemBytes, itemCount) = Measure(_store.Paths.ItemsDir);

                return new StorageUsage
                {
                    Root = _store.Paths.Root,
                    ImageBytes = imageBytes,
                    ImageCount = imageCount,
                    ItemBytes = itemBytes,
                    ItemCount = itemCount,
                };
            },
            cancellationToken);

    private static (long Bytes, int Count) Measure(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return (0, 0);
            }

            long bytes = 0;
            var count = 0;

            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(path).Length;
                    count++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 触れないファイルは数えないだけ
                }
            }

            return (bytes, count);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    public async Task<IReadOnlyList<HiddenItem>> LoadHiddenAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .Where(item => item.Local.IsHidden)
            .Select(item => new HiddenItem
            {
                ItemId = item.Id,
                Name = item.Booth.Name ?? item.Id,
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task UnhideAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null || !item.Local.IsHidden)
        {
            return;
        }

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { IsHidden = false },
            LocalOwners.Visibility,
            cancellationToken: cancellationToken);
    }

    public IReadOnlyList<ExcludedFile> LoadExcluded()
        => _store.Excluded.Load()
            .Select(entry => new ExcludedFile
            {
                Hash = entry.Hash,
                Path = entry.Paths.FirstOrDefault() ?? entry.Hash,
                Reason = entry.Reason,
                ExcludedAt = entry.ExcludedAt,
            })
            .OrderByDescending(entry => entry.ExcludedAt)
            .ToList();

    /// <summary>
    /// 除外を解除する。次の取り込みでまた未確定として出てくる。
    /// ここで解除しないと、一度除外したファイルは二度と現れない。
    /// </summary>
    public async Task RestoreExcludedAsync(string hash, CancellationToken cancellationToken = default)
    {
        var entries = _store.Excluded.Load()
            .Where(entry => !string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await _store.Excluded.SaveAsync(entries, cancellationToken);
    }
}
