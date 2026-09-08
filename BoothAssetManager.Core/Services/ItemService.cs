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

    Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default);

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
            await _store.Items.SaveAsync(
                existing with
                {
                    Local = existing.Local with
                    {
                        ConsecutiveNotFoundCount = count,
                        IsDelisted = count >= _settings.NotFoundThreshold,
                        LastFetchedAt = DateTimeOffset.Now,
                        NextFetchDueAt = NextDue(itemId),
                    },
                },
                cancellationToken);

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

        // booth ブロックだけを差し替える。local はここで触らないので、ユーザ入力が消えることはない。
        await _store.Items.SaveAsync(
            existing with
            {
                Booth = booth,
                Local = existing.Local with
                {
                    ConsecutiveNotFoundCount = 0,
                    IsDelisted = false,
                    LastFetchedAt = DateTimeOffset.Now,
                    NextFetchDueAt = NextDue(itemId),
                    OrderedVariations = MarkMissingVariations(existing.Local.OrderedVariations, booth.Variations),
                },
            },
            cancellationToken);

        if (extraction.DescriptionHtml is not null)
        {
            await _store.Items.SaveDescriptionHtmlAsync(itemId, extraction.DescriptionHtml, cancellationToken);
        }

        await _images.SyncAsync(itemId, booth.Images, cancellationToken);

        return RefreshOutcome.Updated;
    }

    /// <summary>
    /// 未確定ファイルに商品IDを与えて確定させる。確定したファイルはitemへ移し、未確定一覧から取り除く。
    /// </summary>
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
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is not null)
        {
            return ToPreview(itemId, existing.Booth, isAlreadyOwned: true);
        }

        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            return null;
        }

        return ToPreview(itemId, BoothItemMapper.Map(jsonResult.Value, DateTimeOffset.Now, []), isAlreadyOwned: false);
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
            await _store.Items.SaveAsync(
                existing with { Local = existing.Local with { LocalFiles = merged } },
                cancellationToken);
        }
        else
        {
            var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
            if (!jsonResult.IsSuccess || jsonResult.Value is null)
            {
                return false;
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
                    LocalFiles = [record],
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

    /// <summary>
    /// BOOTH側から消えたvariationの購入記録に印を付ける。記録自体は消さない
    /// （実際に払っているので、統計の支出には残す必要がある）。
    /// </summary>
    public static IReadOnlyList<OrderedVariation> MarkMissingVariations(
        IReadOnlyList<OrderedVariation> ordered,
        IReadOnlyList<BoothVariation> current)
    {
        if (ordered.Count == 0)
        {
            return ordered;
        }

        var currentIds = current.Select(variation => variation.Id).ToHashSet();

        return ordered
            .Select(record => record with { ExistsOnBooth = currentIds.Contains(record.VariationId) })
            .ToList();
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
