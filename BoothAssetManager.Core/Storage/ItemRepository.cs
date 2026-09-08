using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Storage;

/// <summary>
/// <c>items/{id}.json</c> の読み書き。1商品につき1ファイルにしているのは、
/// 壊れた時の被害を1件に閉じ込め、差分バックアップと直接編集をしやすくするため。
/// </summary>
public sealed class ItemRepository
{
    private readonly AppPaths _paths;

    public ItemRepository(AppPaths paths)
    {
        _paths = paths;
    }

    public bool Exists(string itemId) => File.Exists(_paths.ItemFile(itemId));

    public async Task<ItemRecord?> LoadAsync(string itemId, CancellationToken cancellationToken = default)
        => Migrate(await JsonStore.ReadAsync<ItemRecord>(_paths.ItemFile(itemId), cancellationToken));

    /// <summary>
    /// 古い形で書かれた記録を今の形に読み替える。
    ///
    /// ここで済ませておくと、読んだ先はどこも新しい形だけを見ればよくなる。
    /// ファイルは次に保存したときに自然に書き換わるので、一括変換は走らせない
    /// （全件を一度に書き換えると、途中で失敗したときにどこまで進んだか分からなくなる）。
    /// </summary>
    private static ItemRecord? Migrate(ItemRecord? item)
    {
        if (item?.Local.LegacyOrderedVariations is not { Count: > 0 } legacy)
        {
            return item;
        }

        // 新しい形が既にあるなら、そちらが正。古い方は捨てるだけ
        var purchases = item.Local.Purchases.Count > 0
            ? item.Local.Purchases
            : legacy.Select(Purchase.FromLegacy).ToList();

        return item with
        {
            Local = item.Local with
            {
                Purchases = purchases,
                LegacyOrderedVariations = null,
            },
        };
    }

    public Task SaveAsync(ItemRecord item, CancellationToken cancellationToken = default)
        => JsonStore.WriteAsync(_paths.ItemFile(item.Id), item, cancellationToken);

    public IReadOnlyList<string> EnumerateItemIds()
    {
        if (!Directory.Exists(_paths.ItemsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(_paths.ItemsDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();
    }

    /// <summary>
    /// 全件をメモリへ読み込む。説明の生HTMLは含まないので、件数が増えても軽いまま。
    /// 壊れた1件で全体が止まらないよう、読めなかったファイルはスキップして呼び出し側へ返す。
    /// </summary>
    public async Task<ItemLoadResult> LoadAllAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<ItemRecord>();
        var failures = new List<string>();
        var loaded = 0;

        foreach (var itemId in EnumerateItemIds())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = await LoadAsync(itemId, cancellationToken);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
            {
                failures.Add(itemId);
            }

            progress?.Report(++loaded);
        }

        return new ItemLoadResult { Items = items, FailedItemIds = failures };
    }

    /// <summary>表示用の説明HTML。商品ページを開いた時だけ読む。</summary>
    public async Task<string?> LoadDescriptionHtmlAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var path = _paths.ItemHtmlFile(itemId);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path, cancellationToken)
            : null;
    }

    public Task SaveDescriptionHtmlAsync(string itemId, string html, CancellationToken cancellationToken = default)
        => JsonStore.WriteTextAsync(_paths.ItemHtmlFile(itemId), html, cancellationToken);

    /// <summary>管理対象から外す。保存ファイルの実体には触らない。</summary>
    public void Delete(string itemId)
    {
        DeleteIfExists(_paths.ItemFile(itemId));
        DeleteIfExists(_paths.ItemHtmlFile(itemId));

        var imagesDir = _paths.ItemImagesDir(itemId);
        if (Directory.Exists(imagesDir))
        {
            Directory.Delete(imagesDir, recursive: true);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class ItemLoadResult
{
    public required IReadOnlyList<ItemRecord> Items { get; init; }

    /// <summary>読み込めなかったitem。手編集で壊れた場合などに、黙って消えないよう返す。</summary>
    public required IReadOnlyList<string> FailedItemIds { get; init; }
}
