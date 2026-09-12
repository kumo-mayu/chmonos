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

    /// <summary>
    /// 1件を読む。**古い形の読み替えはしない**（公開前は、今の形に合わないデータの側を問題にする・ユーザ判断 2026-09-12）。
    /// 以前は旧形式の購入記録（orderedVariations）を読むたびに purchases へ移していた
    /// </summary>
    public Task<ItemRecord?> LoadAsync(string itemId, CancellationToken cancellationToken = default)
        => JsonStore.ReadAsync<ItemRecord>(_paths.ItemFile(itemId), cancellationToken);

    public Task SaveAsync(ItemRecord item, CancellationToken cancellationToken = default)
        => JsonStore.WriteAsync(_paths.ItemFile(item.Id), item, cancellationToken);

    /// <summary>
    /// <c>local</c> のうち、<paramref name="owns"/> で名指しした項目だけを書く。
    ///
    /// 保存の直前に読み直すので、**名指ししなかった項目は今の値がそのまま残る。**
    /// 画面は開いた時点の写しを抱えているため、丸ごと書き戻すと
    /// 開いている間に取り込み・検出・再取得が書いた項目まで古い値で潰してしまう。
    ///
    /// itemが消えていれば **何も書かずに false**。
    /// 取り込み中でも削除を塞がないと決めたので、書く直前に確かめる必要がある。
    ///
    /// <paramref name="booth"/> を渡すと <c>booth</c> ブロックも入れ替える（再取得のため）。
    /// 取得には数秒かかるので、その間に人が入力していることがある。
    /// **読み直しはここで行うので、呼ぶ側は取得の前に読んだ写しをそのまま渡してよい。**
    /// </summary>
    public async Task<bool> SaveLocalAsync(
        string itemId,
        LocalBlock local,
        IReadOnlyCollection<LocalField> owns,
        BoothBlock? booth = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        var merged = LocalFields.Merge(existing.Local, local, owns);

        // ExistsOnBooth は導ける値なので、どの経路から保存しても同じ式で入れ直す。
        // booth を入れ替えるときは、当然そちらの新しい一覧が正
        var variations = (booth ?? existing.Booth).Variations;
        merged = merged with { Purchases = Purchase.Reconcile(merged.Purchases, variations) };

        await SaveAsync(
            existing with { Booth = booth ?? existing.Booth, Local = merged },
            cancellationToken);

        return true;
    }

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
