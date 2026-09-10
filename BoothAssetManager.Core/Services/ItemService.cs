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

    /// <summary>ファイルを持たない商品として登録する。既にあれば何もしない。</summary>
    Task<bool> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default);

    Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default);

    Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default);

    Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default);

    Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 未確定のファイルを「BOOTHに無い商品」として登録する。
    /// 仮ID（<see cref="LocalItemId"/>）を与えるので、BOOTHへは一切問い合わせない。
    /// </summary>
    /// <returns>作った商品のID。対象のファイルが無ければ null。</returns>
    Task<string?> RegisterLocalItemAsync(
        string hash,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<DetachOutcome> DetachFileAsync(
        string itemId,
        string hash,
        bool deleteItemWhenEmpty,
        CancellationToken cancellationToken = default);

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
        // 仮IDはBOOTHに存在しない。叩けば404が返るだけなので、通信する前に降りる
        if (LocalItemId.IsLocal(itemId))
        {
            return RefreshOutcome.NotOnBooth;
        }

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
                    NextFetchDueAt = NextDue(itemId, count),
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

        // 取り直せたので、404だった印を全部落とす。
        //
        // **ここが印を外す唯一のきっかけ。**取り直した瞬間に新しい画像URLの一覧が
        // 手に入るので、日数で外す仕組みを別に持たなくてよい。
        // 作者がたまたま商品ページを非公開にしていただけ、という場合はこれで復活する。
        _images.ClearMissingMarkers(itemId);

        await NoteBackOnBoothAsync(existing, booth, cancellationToken);
        await NoteChangesAsync(existing, booth, cancellationToken);

        // **画像はここで落とさない。**梯子の規則をここだけ破らないため。
        // 落とすと「①②が画像より先」の外側に画像の取得が生まれる。
        // 増えた画像は ImageBacklog が拾い、人が押した取り直しでは
        // 呼び出し側が優先ボタンと同じ経路で取りに行く。
        return RefreshOutcome.Updated;
    }

    /// <summary>
    /// 変わっていたら要確認へ書く。
    ///
    /// 「知らせる」を商品ごとに切れるようにしてあるので、切っている商品には出さない。
    /// 何が変わったかを列挙するのは、**開かなくても判断できるようにする**ため。
    /// </summary>
    private async Task NoteChangesAsync(ItemRecord existing, BoothBlock booth, CancellationToken cancellationToken)
    {
        if (!existing.Local.NotifyOnUpdate)
        {
            return;
        }

        var diffs = BoothChanges.Describe(existing.Booth, booth);
        if (diffs.Count == 0)
        {
            return;
        }

        var notifications = _store.Notifications.Load();

        // 同じ商品の未読が既にあれば差し替える。溜めても読む手間が増えるだけ
        var id = $"item-updated:{existing.Id}";
        notifications.RemoveAll(entry => entry.Id == id && !entry.IsRead);

        notifications.Add(new NotificationRecord
        {
            Id = id,
            Kind = NotificationKind.ItemUpdated,
            ItemId = existing.Id,
            Title = $"{booth.Name ?? existing.Id}：商品ページが変わりました",
            Detail = BoothChanges.Summarize(diffs),
            Diffs = diffs,
            CreatedAt = DateTimeOffset.Now,
        });

        await _store.Notifications.SaveAsync(notifications, cancellationToken);
    }

    /// <summary>
    /// 非公開と見なしていた商品が戻ってきたことを要確認に出す。
    ///
    /// 黙って埋めると、画像が急に増え、価格が入り、印が消える。
    /// 説明が無いと「壊れた」と読まれる。
    ///
    /// **名前を切り替えるかは聞かない。**自分で付けた名前を優先すると決めてあるので、
    /// そこを毎回問い直す理由がない（編集画面で変えられることだけ言う）。
    /// 「知らせる」を切っている商品にも出す——これは更新の知らせではなく、
    /// **こちらが「もう無い」と判断していたのが誤りだったという訂正**だから。
    /// </summary>
    private async Task NoteBackOnBoothAsync(
        ItemRecord existing,
        BoothBlock booth,
        CancellationToken cancellationToken)
    {
        if (!existing.Local.IsDelisted)
        {
            return;
        }

        var notifications = _store.Notifications.Load();

        var id = $"item-back:{existing.Id}";
        notifications.RemoveAll(entry => entry.Id == id && !entry.IsRead);

        var name = existing.Local.DisplayName;
        var detail = name is { Length: > 0 }
            ? $"「販売終了」の印を外しました。名前は自分で付けた「{name}」のままです（編集画面で変えられます）。"
            : "「販売終了」の印を外しました。";

        notifications.Add(new NotificationRecord
        {
            Id = id,
            Kind = NotificationKind.ItemBackOnBooth,
            ItemId = existing.Id,
            Title = $"{name ?? booth.Name ?? existing.Id}：BOOTHに現れました",
            Detail = detail,
            CreatedAt = DateTimeOffset.Now,
        });

        await _store.Notifications.SaveAsync(notifications, cancellationToken);
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
    /// ファイルを持たない商品として登録する。
    ///
    /// 「情報だけあって所持していない」itemは既に成立している状態なので、
    /// 新しい状態を作らない。贈った商品や、気になっている未購入品がここに入る
    /// ——どちらもファイルが手元に来ないので、取り込みからは入れない。
    /// </summary>
    /// <returns>登録できたか。既に持っている商品なら true（何もしない）。</returns>
    public async Task<bool> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (await _store.Items.LoadAsync(itemId, cancellationToken) is not null)
        {
            return true;
        }

        return await FetchNewItemAsync(itemId, cancellationToken) is not null;
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
    /// <summary>
    /// BOOTHから取って新しいitemを作る。**仮IDでは何もしない**——
    /// 存在しないIDなので、通信するだけ無駄になる。
    /// </summary>
    private async Task<ItemRecord?> FetchNewItemAsync(string itemId, CancellationToken cancellationToken)
    {
        if (LocalItemId.IsLocal(itemId))
        {
            return null;
        }

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

        // 前に「この商品のものではない」と外していたなら、その記録は捨てる。
        // ユーザが改めて選び直したのだから、こちらが覚えていて弾き続ける方がおかしい
        var detached = _store.Detached.Load();
        if (detached.RemoveAll(entry =>
                string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)
                && entry.ItemId == itemId) > 0)
        {
            await _store.Detached.SaveAsync(detached, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// 未確定のファイルを「BOOTHに無い商品」として登録する。
    ///
    /// 非公開・削除済みの商品は、買っていて手元にファイルがあってもIDが分からない。
    /// 未確定に置き続けると**二度と復活しないものが永久に溜まり、作業一覧が
    /// 「終わらない仕事」で埋まる。**除外もできない——除外は「これはBOOTH商品ではない」
    /// に使う語で、入れると統計からも検索からも消えてしまう。
    ///
    /// **BOOTHへは一切問い合わせない。**存在しないIDなので、叩けば404が返るだけ。
    /// <c>Booth.FetchedAt</c> は null のまま——観測していないので、それが正しい。
    /// <c>NextFetchDueAt</c> も入れない（⑦の対象から自然に外れる）。
    /// </summary>
    public async Task<string?> RegisterLocalItemAsync(
        string hash,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        var unresolved = _store.Unresolved.Load();
        var target = unresolved.FirstOrDefault(
            file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return null;
        }

        var itemId = LocalItemId.For(target.Hash);

        var record = new LocalFileRecord
        {
            Hash = target.Hash,
            Paths = target.Paths,
            SizeBytes = target.SizeBytes,
            Contents = target.Contents,
        };

        // 同じファイルを2回登録しようとした場合（未確定に二重に載っていた等）。
        // 仮IDはハッシュから決まるので、同じ商品に行き着く
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            await _store.Items.SaveAsync(
                new ItemRecord
                {
                    Id = itemId,
                    Booth = new BoothBlock(),
                    Local = new LocalBlock
                    {
                        DisplayName = displayName.Trim(),
                        LocalFiles = [record],
                    },
                },
                cancellationToken);
        }
        else
        {
            var merged = LocalFileMerger.Merge(existing.Local.LocalFiles, [record]);
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with { DisplayName = displayName.Trim(), LocalFiles = merged },
                [LocalField.DisplayName, LocalField.LocalFiles],
                cancellationToken: cancellationToken);
        }

        unresolved.Remove(target);
        await _store.Unresolved.SaveAsync(unresolved, cancellationToken);

        return itemId;
    }

    /// <summary>
    /// ファイルをこの商品から外し、未確定へ戻す。
    ///
    /// **IDは書き換えない。**商品IDはファイル名にもフォルダ名にもなっていて、
    /// 他の商品からも名前で参照されているので、書き換えると参照が全部迷子になる。
    /// やりたいことは「このファイルの行き先が違う」なので、ファイルの側を動かす。
    ///
    /// 外した記録（<c>detached.json</c>）を残すのは、手掛かりから商品IDが決まる
    /// ファイルだと**次の取り込みで同じ商品へ戻ってしまう**ため。
    /// 外す操作が要るのはまさに手掛かりが間違っている場合なので、記録が無いと直せない。
    /// </summary>
    public async Task<DetachOutcome> DetachFileAsync(
        string itemId,
        string hash,
        bool deleteItemWhenEmpty,
        CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return DetachOutcome.Missing;
        }

        var target = item.Local.LocalFiles.FirstOrDefault(
            file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return DetachOutcome.Missing;
        }

        var remaining = item.Local.LocalFiles.Where(file => file != target).ToList();

        var detached = _store.Detached.Load();
        if (!detached.Any(entry =>
                string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase) && entry.ItemId == itemId))
        {
            detached.Add(new DetachedFile
            {
                Hash = target.Hash,
                ItemId = itemId,
                Paths = target.Paths,
                DetachedAt = DateTimeOffset.Now,
            });
            await _store.Detached.SaveAsync(detached, cancellationToken);
        }

        // 実体が残っているものだけ未確定へ戻す。
        // 既に消えているファイルを並べても、紐付け直す相手がいない
        var alive = target.Paths.Where(File.Exists).ToList();
        if (alive.Count > 0)
        {
            var unresolved = _store.Unresolved.Load();
            if (!unresolved.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
            {
                var modified = DateTimeOffset.Now;
                try
                {
                    modified = new DateTimeOffset(File.GetLastWriteTimeUtc(alive[0]), TimeSpan.Zero);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // 日時が読めなくても未確定には出したいので、今の時刻で通す
                }

                unresolved.Add(new UnresolvedFile
                {
                    Hash = target.Hash,
                    Paths = alive,
                    SizeBytes = target.SizeBytes,
                    ModifiedAtUtc = modified,
                    FirstSeenAt = DateTimeOffset.Now,
                    Contents = target.Contents,
                });
                await _store.Unresolved.SaveAsync(unresolved, cancellationToken);
            }
        }

        // 手元に何も無くなったか。フォルダ登録も所持のうちなので一緒に見る
        var becameEmpty = remaining.Count == 0 && item.Local.LocalFolders.Count == 0;

        if (becameEmpty && deleteItemWhenEmpty)
        {
            _store.Items.Delete(itemId);
            return DetachOutcome.ItemDeleted;
        }

        await _store.Items.SaveLocalAsync(
            itemId,
            item.Local with { LocalFiles = remaining },
            LocalOwners.Import,
            cancellationToken: cancellationToken);

        return becameEmpty ? DetachOutcome.ItemNowEmpty : DetachOutcome.Detached;
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
    /// 次にこの商品を確かめる日。
    ///
    /// 404が続いた商品は間隔を広げる。**ただし広げすぎない。**
    /// 確かめる間隔が「復活している期間」より長いと、原理的に取り逃す。
    /// 季節ものは1ヶ月ほどしか公開されないので、30日で打ち止めにする。
    /// それ以上延ばしても、浮くのは100件あたり年に十数分でしかない。
    ///
    /// ジッタを入れるのは、取得が特定の日に集中しないようにするため。
    /// 商品IDから決めるので、同じ商品は毎回同じ側にずれる。
    /// </summary>
    private DateTimeOffset NextDue(string itemId, int consecutiveNotFound = 0)
    {
        var days = consecutiveNotFound >= _settings.NotFoundThreshold
            ? _settings.DelistedRecheckDays
            : _settings.RefreshIntervalDays;

        var jitterDays = _settings.RefreshJitterDays;
        var offset = jitterDays <= 0
            ? 0
            : Math.Abs(itemId.GetHashCode(StringComparison.Ordinal)) % ((jitterDays * 2) + 1) - jitterDays;

        return DateTimeOffset.Now.AddDays(days + offset);
    }

}

public enum RefreshOutcome
{
    Updated,

    /// <summary>BOOTHに無い商品として登録したもの。問い合わせていない。</summary>
    NotOnBooth,
    NotFound,
    Delisted,
    TemporaryFailure,
    Missing,
}

/// <summary>ファイルを商品から外した結果。</summary>
public enum DetachOutcome
{
    /// <summary>外した。商品にはまだ他のファイルかフォルダが残っている。</summary>
    Detached,

    /// <summary>外した結果、手元に何も無い商品になった。情報だけは残してある。</summary>
    ItemNowEmpty,

    /// <summary>外した結果、手元に何も無くなったので商品ごと消した。</summary>
    ItemDeleted,

    /// <summary>商品かファイルが見つからなかった。</summary>
    Missing,
}
