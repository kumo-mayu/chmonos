using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 確定前の下見。名前とショップが分かれば「これで合っているか」は判断できる。
/// 画像までは取りに行かない（確定しないかもしれないものに取得の間隔を使わない）。
/// </summary>
public sealed class ItemPreview
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? ShopName { get; init; }

    public string? CategoryText { get; init; }

    public int? Price { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>既にライブラリにあるitemか。あればBOOTHへは取りに行っていない。</summary>
    public bool IsAlreadyOwned { get; init; }
}

public interface IItemService
{
    Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default);

    Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>取得できなかった理由まで返す版。画面はこちらを使う。</summary>
    Task<(ItemPreview? Preview, string? Error)> PreviewWithReasonAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>この商品の未取得の画像を、行列の先頭で取る。</summary>
    Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default);

    Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default);

    Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default);

    Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default);

    Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default);

    Task ExcludeAsync(string hash, IReadOnlyList<string> paths, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>1件のitemに対する操作。UIに依存しないので、そのまま単体テストできる。</summary>
public sealed class ItemService : IItemService
{
    private readonly DataStore _store;
    private readonly IBoothClient _client;
    private readonly ImagePipeline _images;
    private readonly AppSettings _settings;

    public ItemService(DataStore store, IBoothClient client, ImagePipeline images, AppSettings? settings = null)
    {
        _store = store;
        _client = client;
        _images = images;
        _settings = settings ?? new AppSettings();
    }

    /// <summary>
    /// BOOTHから取り直して <c>booth</c> ブロックだけを差し替える。
    /// <c>local</c> は触らないので、ユーザの入力が上書きで消えることがない。
    ///
    /// 404が続いた場合だけ非公開と判定する。一時エラーは回数に数えず、次回に回す。
    /// </summary>
    public async Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return RefreshOutcome.Missing;
        }

        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);

        if (jsonResult.Status == BoothFetchStatus.NotFound)
        {
            var count = existing.Local.ConsecutiveNotFoundCount + 1;
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with
                {
                    ConsecutiveNotFoundCount = count,
                    IsDelisted = count >= _settings.NotFoundThreshold,
                    LastFetchedAt = DateTimeOffset.Now,
                    NextFetchDueAt = NextDue(itemId),
                },
                LocalOwners.Fetch,
                cancellationToken: cancellationToken);

            return count >= _settings.NotFoundThreshold ? RefreshOutcome.Delisted : RefreshOutcome.NotFound;
        }

        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            // BOOTH側の一時的な不調。カウントも更新予定も動かさず、そのまま次回へ回す。
            return RefreshOutcome.TemporaryFailure;
        }

        var htmlResult = await _client.GetItemHtmlAsync(itemId, cancellationToken);
        var extraction = htmlResult.IsSuccess && htmlResult.Value is not null
            ? H2SectionExtractor.Extract(htmlResult.Value)
            : new H2ExtractionResult();

        var booth = BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now, extraction.Sections);

        // booth を差し替え、local は取得の記録だけを書く。
        //
        // ここに来るまでにBOOTHへ2回問い合わせている（3秒以上）。その間にユーザが
        // 同じ商品を編集していることがあるので、上の existing.Local を丸ごと書き戻すと
        // その入力が消える。**読み直しは保存側で行われる。**
        // Purchases の ExistsOnBooth も、新しいvariation一覧からそこで入れ直される。
        await _store.Items.SaveLocalAsync(
            itemId,
            existing.Local with
            {
                ConsecutiveNotFoundCount = 0,
                IsDelisted = false,
                LastFetchedAt = DateTimeOffset.Now,
                NextFetchDueAt = NextDue(itemId),
            },
            LocalOwners.Fetch,
            booth,
            cancellationToken);

        if (extraction.DescriptionHtml is not null)
        {
            await _store.Items.SaveDescriptionHtmlAsync(itemId, extraction.DescriptionHtml, cancellationToken);
        }

        await _images.SyncAsync(itemId, booth.Images, cancellationToken);

        if (booth.Shop is { } shop)
        {
            await _images.SyncShopIconAsync(shop.Subdomain, shop.ThumbnailUrl, cancellationToken);
        }

        return RefreshOutcome.Updated;
    }

    /// <summary>
    /// この商品の未取得の画像を、行列の先頭で取る。
    ///
    /// 自動で「見えたものを優先」にはしない。間隔が1,500msなので取れるのは1分あたり40枚で、
    /// 勢いよくスクロールされると順番待ちが「もう見ていないもの」で埋まる。
    /// **押した意思の方が、見えたという事実より確か。**
    ///
    /// 優先度は <see cref="BoothPriority.PinnedImage"/>。人が押した操作より下なのは、
    /// 押した直後に別のボタンを押されたとき、そちらを待たせないため。
    /// 逆に取り込みの段より上なので、**指名した商品は最後まで通ってから梯子に戻る**。
    /// </summary>
    /// <returns>この呼び出しで落とせた枚数。</returns>
    public async Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null || item.Booth.Images.Count == 0)
        {
            return 0;
        }

        using var priority = BoothClient.Prioritize(BoothPriority.PinnedImage);

        var result = await _images.SyncAsync(itemId, item.Booth.Images, cancellationToken);
        return result.Downloaded;
    }

    /// <summary>
    /// 未確定ファイルに商品IDを与えて確定させる。確定したファイルはitemへ移し、未確定一覧から取り除く。
    /// </summary>
    /// <summary>
    /// フォルダを商品に紐付ける。zipが手元に無く、展開したものだけが残っている場合に使う。
    ///
    /// 紐付けたフォルダの配下は、以降のスキャンで見に行かなくなる。
    /// その場で未確定からも取り除く。次のスキャンまで残しても、
    /// 既に行き先の決まったファイルを作業として見せることになるだけなので。
    /// 商品がまだ手元に無ければBOOTHから取得する（ファイル確定と同じ扱い）。
    /// </summary>
    public async Task<bool> RegisterFolderAsync(
        string itemId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        var item = await _store.Items.LoadAsync(itemId, cancellationToken) ?? await FetchNewItemAsync(itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var (count, bytes) = RegisteredFolderSet.Measure(folderPath);
        var normalized = Path.TrimEndingDirectorySeparator(folderPath);

        var folders = item.Local.LocalFolders
            .Where(folder => !string.Equals(
                Path.TrimEndingDirectorySeparator(folder.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        folders.Add(new LocalFolderRecord
        {
            Path = normalized,
            FileCount = count,
            TotalBytes = bytes,
            RegisteredAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
        });

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { LocalFolders = folders },
            LocalOwners.Import,
            cancellationToken: cancellationToken);

        await RemoveUnresolvedUnderAsync(normalized, cancellationToken);
        return true;
    }

    /// <summary>
    /// フォルダの紐付けを解除する。ファイルには触らない。
    ///
    /// zipを後から手に入れたときに要る。zipを取り込むと展開先は自動で対象から外れるが、
    /// フォルダ登録は残るので、容量が二重に乗ったままになる。
    /// </summary>
    public async Task<bool> UnregisterFolderAsync(
        string itemId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return false;
        }

        var normalized = Path.TrimEndingDirectorySeparator(folderPath);
        var remaining = item.Local.LocalFolders
            .Where(folder => !string.Equals(
                Path.TrimEndingDirectorySeparator(folder.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count == item.Local.LocalFolders.Count)
        {
            return false;
        }

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { LocalFolders = remaining },
            LocalOwners.Import,
            cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// まだ手元に無い商品をBOOTHから取ってきて保存する。説明HTMLと画像もここで揃える。
    /// ファイル確定とフォルダ登録の両方から使う（どちらも「新しい商品が増える」点は同じ）。
    /// </summary>
    private async Task<ItemRecord?> FetchNewItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            return null;
        }

        var htmlResult = await _client.GetItemHtmlAsync(itemId, cancellationToken);
        var extraction = htmlResult.IsSuccess && htmlResult.Value is not null
            ? H2SectionExtractor.Extract(htmlResult.Value)
            : new H2ExtractionResult();

        var item = new ItemRecord
        {
            Id = itemId,
            Booth = BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now, extraction.Sections),
            Local = new LocalBlock
            {
                NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                LastFetchedAt = DateTimeOffset.Now,
                NextFetchDueAt = NextDue(itemId),
            },
        };

        await _store.Items.SaveAsync(item, cancellationToken);

        if (extraction.DescriptionHtml is not null)
        {
            await _store.Items.SaveDescriptionHtmlAsync(itemId, extraction.DescriptionHtml, cancellationToken);
        }

        await _images.SyncAsync(itemId, item.Booth.Images, cancellationToken);

        if (item.Booth.Shop is { } shop)
        {
            await _images.SyncShopIconAsync(shop.Subdomain, shop.ThumbnailUrl, cancellationToken);
        }

        return item;
    }

    /// <summary>登録したフォルダの配下にあった未確定を取り除く。行き先が決まったため。</summary>
    private async Task RemoveUnresolvedUnderAsync(string folderPath, CancellationToken cancellationToken)
    {
        var unresolved = _store.Unresolved.Load();
        var registered = new RegisteredFolderSet([folderPath]);

        var inside = unresolved
            .Where(file => file.Paths.Any(registered.Contains))
            .ToList();

        if (inside.Count == 0)
        {
            return;
        }

        foreach (var file in inside)
        {
            unresolved.Remove(file);
        }

        await _store.Unresolved.SaveAsync(unresolved, cancellationToken);
    }

    /// <summary>
    /// 既にどこかのitemが持っているファイルを、未確定の一覧から取り除く。
    ///
    /// 確定は「itemを保存」→「未確定から削除」の2段階で、その間に落ちると
    /// 両方に存在する状態が残る。順序を逆にはできない（先に消してitemの保存に
    /// 失敗すると、ファイルの記録ごと失う方が明らかに悪い）。
    /// 重複は害が小さく後から均せるので、開くたびにここで均す。
    /// </summary>
    public async Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default)
    {
        var unresolved = _store.Unresolved.Load();
        if (unresolved.Count == 0)
        {
            return 0;
        }

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var owned = loaded.Items
            .SelectMany(item => item.Local.LocalFiles)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = unresolved.Where(file => owned.Contains(file.Hash)).ToList();
        if (stale.Count == 0)
        {
            return 0;
        }

        foreach (var file in stale)
        {
            unresolved.Remove(file);
        }

        await _store.Unresolved.SaveAsync(unresolved, cancellationToken);
        return stale.Count;
    }

    /// <summary>
    /// 確定する前に、そのIDが何なのかを見る。
    /// 既に持っているitemならローカルから読み、BOOTHへは行かない。
    /// </summary>
    public async Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default)
        => (await PreviewWithReasonAsync(itemId, cancellationToken)).Preview;

    /// <summary>
    /// 失敗の理由まで返す。
    ///
    /// 前は null を返すだけで、存在しないIDも通信の失敗もタイムアウトも
    /// 呼び出し側からは区別できなかった。最大100秒待たされた末に
    /// 「取得できませんでした」の1行だけ、という状態だったので理由を渡す。
    /// </summary>
    public async Task<(ItemPreview? Preview, string? Error)> PreviewWithReasonAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is not null)
        {
            return (ToPreview(itemId, existing.Booth, isAlreadyOwned: true), null);
        }

        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);

        if (jsonResult.Status == BoothFetchStatus.NotFound)
        {
            return (null, $"商品ID {itemId} はBOOTHに見つかりませんでした。IDが違うか、販売が終わって非公開になっています。");
        }

        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            var detail = string.IsNullOrWhiteSpace(jsonResult.Error) ? string.Empty : $"（{jsonResult.Error}）";
            return (null, $"BOOTHに問い合わせできませんでした{detail}。通信を確かめて、もう一度お試しください。");
        }

        return (ToPreview(itemId, BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now, []), isAlreadyOwned: false), null);
    }

    private static ItemPreview ToPreview(string itemId, BoothBlock booth, bool isAlreadyOwned) => new()
    {
        Id = itemId,
        Name = booth.Name ?? itemId,
        ShopName = booth.Shop?.Name,
        CategoryText = booth.Category is null
            ? null
            : booth.Category.ParentName is null
                ? booth.Category.Name
                : $"{booth.Category.ParentName} / {booth.Category.Name}",
        Price = booth.Variations.Count > 0 ? booth.Variations.Min(variation => variation.Price) : null,
        PublishedAt = booth.PublishedAt,
        IsAlreadyOwned = isAlreadyOwned,
    };

    public async Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default)
    {
        var unresolved = _store.Unresolved.Load();
        var target = unresolved.FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return false;
        }

        var record = new LocalFileRecord
        {
            Hash = target.Hash,
            Paths = target.Paths,
            SizeBytes = target.SizeBytes,
            Contents = target.Contents,
        };

        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is not null)
        {
            var merged = LocalFileMerger.Merge(existing.Local.LocalFiles, [record]);
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with { LocalFiles = merged },
                LocalOwners.Import,
                cancellationToken: cancellationToken);
        }
        else
        {
            // 取得に数秒かかるので、その間に人が触っていることがある。
            // 作った直後でも、書くのは取り込みが持つ項目だけにする
            var created = await FetchNewItemAsync(itemId, cancellationToken);
            if (created is null)
            {
                return false;
            }

            await _store.Items.SaveLocalAsync(
                itemId,
                created.Local with { LocalFiles = [record] },
                LocalOwners.Import,
                cancellationToken: cancellationToken);
        }

        unresolved.Remove(target);
        await _store.Unresolved.SaveAsync(unresolved, cancellationToken);
        return true;
    }

    /// <summary>ファイルを管理対象から外す。未確定一覧からも取り除く。</summary>
    public async Task ExcludeAsync(
        string hash,
        IReadOnlyList<string> paths,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var excluded = _store.Excluded.Load();
        if (!excluded.Any(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)))
        {
            excluded.Add(new ExcludedEntry
            {
                Hash = hash,
                Paths = paths,
                ExcludedAt = DateTimeOffset.Now,
                Reason = reason,
            });
            await _store.Excluded.SaveAsync(excluded, cancellationToken);
        }

        var unresolved = _store.Unresolved.Load();
        if (unresolved.RemoveAll(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            await _store.Unresolved.SaveAsync(unresolved, cancellationToken);
        }
    }

    private DateTimeOffset NextDue(string itemId)
    {
        var jitterDays = _settings.RefreshJitterDays;
        var offset = jitterDays <= 0
            ? 0
            : Math.Abs(itemId.GetHashCode(StringComparison.Ordinal)) % ((jitterDays * 2) + 1) - jitterDays;

        return DateTimeOffset.Now.AddDays(_settings.RefreshIntervalDays + offset);
    }

}

public enum RefreshOutcome
{
    Updated,
    NotFound,
    Delisted,
    TemporaryFailure,
    Missing,
}
