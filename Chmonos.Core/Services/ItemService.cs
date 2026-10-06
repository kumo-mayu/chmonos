using Chmonos.Core.Booth;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

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

    /// <summary>
    /// 登録したときに BOOTH へ問い合わせる数の見込み（商品JSON・商品ページ・1枚目の画像・手元に無ければショップのアイコン。残りの画像は登録の後に⑤の段で取るので数えない）。
    /// 未確定の画面が「この商品は約 m 分」と、後に並んだ登録の「開始まで約 n 分」を出すのに使う（メモ60）。持っている商品は問い合わせないので null
    /// </summary>
    public int? RequestsToRegister { get; init; }
}

public interface IItemService
{
    Task<RefreshOutcome> RefreshAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>登録の列が動いている間、登録した商品の残りの画像を待たせる札（<see cref="ItemService.HoldRemainingImages"/>）。既定は待たせない。</summary>
    IDisposable HoldRemainingImages() => NoHold.Instance;

    private sealed class NoHold : IDisposable
    {
        public static readonly NoHold Instance = new();

        public void Dispose()
        {
        }
    }

    Task<ItemPreview?> PreviewAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得できなかった理由まで返す版。画面はこちらを使う。
    /// <c>NotOnBooth</c> は BOOTH が「無い」と答えたとき（一時的に届かないのとは分ける。見つからないIDのまま登録できるのはこのときだけ）。
    /// </summary>
    Task<(ItemPreview? Preview, string? Error, bool NotOnBooth)> PreviewWithReasonAsync(
        string itemId,
        CancellationToken cancellationToken = default);

    /// <summary>この商品の未取得の画像を、行列の先頭で取る。</summary>
    Task<int> FetchImagesAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>ファイルを持たない商品として登録する。既にあれば何もしない。</summary>
    /// <summary>BOOTH から取って商品を作る。**失敗の種類をそのまま返す**（E3：見つからないのと一時的に届かないは次の一手が違う）。</summary>
    Task<Booth.BoothFetchStatus> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default);

    Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default);

    Task<bool> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null);

    Task<bool> UnregisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 見つからない登録フォルダの場所を、人が選んだ場所に差し替える（見つからない・移動の点検 10-A）。
    /// </summary>
    Task<FolderRelocation> RelocateFolderAsync(string itemId, string fromPath, string toPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 展開フォルダで登録していた商品を、隣に現れたzipの方で登録し直す。
    /// <paramref name="liftExclusion"/>：除外した zip なら除外を解いて付ける（人が窓で頼んだときだけ）。
    /// <paramref name="takeFromOtherItems"/>：ほかの商品が持つ zip なら、そちらから外してこちらに付ける（人が窓で頼んだときだけ）。
    /// </summary>
    Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(
        string itemId,
        string folderPath,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 商品ページから、選んだ・落としたファイルをこの商品に結ぶ（ユーザ指示 2026-10-06）。事情（除外・ほかの持ち主）は
    /// 「zipで登録し直す」と同じに、何も書かずに返す。<paramref name="liftExclusion"/>・<paramref name="takeFromOtherItems"/> は人が窓で頼んだときだけ。
    /// </summary>
    Task<FileAttachOutcome> AttachFileAsync(
        string itemId,
        string path,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default);

    Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null);

    /// <summary>
    /// 未確定のファイルを、BOOTHで見つからなかった商品IDのまま登録する（ユーザ判断 2026-09-29）。
    /// **BOOTHへは問い合わせない**（直前の確かめで見つからなかったID）。⑦で確かめ直し、公開されたら情報を取って知らせる。
    /// </summary>
    /// <returns>登録できたか。対象のファイルが未確定に無い・仮IDを渡されたら false。</returns>
    Task<bool> AssignUnpublishedItemIdAsync(
        string hash,
        string itemId,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>自分で足す画像を1枚入れる。BOOTHと同じ圧縮を通す。</summary>
    /// <returns>保存したファイル名。画像として読めなければ null。</returns>
    Task<string?> AddUserImageAsync(
        string itemId,
        byte[] bytes,
        string? caption = null,
        CancellationToken cancellationToken = default);

    /// <summary>自分で足した画像を消す。ファイルごと消える。</summary>
    Task<bool> RemoveUserImageAsync(
        string itemId,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>自分で足した画像の並びを1つ動かす（-1 で前へ、+1 で後ろへ）。</summary>
    Task<bool> MoveUserImageAsync(
        string itemId,
        string fileName,
        int delta,
        CancellationToken cancellationToken = default);

    /// <summary>サムネイルに使う1枚を指名する。null で指名を外す。</summary>
    Task<bool> PinThumbnailAsync(
        string itemId,
        string? fileName,
        CancellationToken cancellationToken = default);

    /// <summary>画像に役割を付ける。出どころから決まる値と同じなら記録しない。</summary>
    Task<bool> SetImageRoleAsync(
        string itemId,
        string fileName,
        Models.ImageRole role,
        bool isUserAdded,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// IDを変更したら何が起きるかの下見。**書き込まない。**
    /// 移した先が手元に無ければBOOTHへ1度だけ聞きに行く。
    /// </summary>
    Task<ItemIdChangePlan?> PlanItemIdChangeAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 商品まるごとを別のIDへ移す。移し終えたら元の商品は消える。
    /// </summary>
    /// <param name="skippedPurchases">移さない購入記録の番号（二重計上と判断したもの）。</param>
    Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 未確定のファイルを「BOOTHに無い商品」として登録する。
    /// 仮ID（<see cref="LocalItemId"/>）を与えるので、BOOTHへは一切問い合わせない。
    /// </summary>
    /// <param name="hashes">1つの商品にまとめるファイル。仮IDは未確定に見つかった最初のファイルから決まる。</param>
    /// <returns>作った商品のID。対象のファイルが1つも無ければ null。</returns>
    Task<string?> RegisterLocalItemAsync(
        IReadOnlyList<string> hashes,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>ファイルに種類を付け直す（null で外す）。商品が無ければ false。</summary>
    Task<bool> SetFileVariationsAsync(
        string itemId,
        IReadOnlyDictionary<string, long?> variationByHash,
        CancellationToken cancellationToken = default);

    /// <summary>使おうとして見た在る・無いを、ファイルの「見つからなくなった日時」に当てる。書いたら true。</summary>
    Task<bool> NoteFilePresenceAsync(
        string itemId,
        IReadOnlyCollection<FileSighting> sightings,
        CancellationToken cancellationToken = default);

    Task<DetachOutcome> DetachFileAsync(
        string itemId,
        string hash,
        bool deleteItemWhenEmpty,
        CancellationToken cancellationToken = default);

    Task<ReattachOutcome> ReattachFileAsync(string itemId, string hash, CancellationToken cancellationToken = default);

    /// <summary>古い版の記録（<see cref="LocalFileRecord.IsOldVersion"/>）を消す。消したら true。</summary>
    Task<bool> ForgetOldVersionAsync(string itemId, string hash, CancellationToken cancellationToken = default);

    /// <returns>今回除外の記録に足したハッシュ（前から除外していた物は入らない）。戻すときに <see cref="UndoExcludeAsync"/> へ渡す。</returns>
    Task<IReadOnlyList<string>> ExcludeAsync(IReadOnlyList<UnresolvedFile> files, string? reason, CancellationToken cancellationToken = default);

    /// <param name="excludedHashes">除外から消すハッシュ。除外したときに <see cref="ExcludeAsync"/> が返した物（今回足した物だけ）。</param>
    Task UndoExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        IReadOnlyCollection<string> excludedHashes,
        CancellationToken cancellationToken = default);
}

/// <summary>1件のitemに対する操作。UIに依存しないので、そのまま単体テストできる。</summary>
public sealed class ItemService : IItemService
{
    private readonly DataStore _store;
    private readonly IBoothClient _client;
    private readonly ImagePipeline _images;
    private readonly Func<AppSettings> _currentSettings;

    public ItemService(DataStore store, IBoothClient client, ImagePipeline images, AppSettings? settings = null)
        : this(store, client, images, SettingsSource.Fixed(settings))
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public ItemService(DataStore store, IBoothClient client, ImagePipeline images, Func<AppSettings> currentSettings)
    {
        _store = store;
        _client = client;
        _images = images;
        _currentSettings = currentSettings;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

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
            // BOOTH側の一時的な不調か、つながっていない。カウントも更新予定も動かさず、そのまま次回へ回す。
            // 押した人への次の一手が違う（待つ／つなぐ）ので、結果は分けて返す。
            // 5xx も分けて返す——⑦が続いたら打ち切りに数える（429 は数えない）
            return jsonResult.IsUnreachable ? RefreshOutcome.Unreachable
                : jsonResult.IsServerError ? RefreshOutcome.ServerError
                : RefreshOutcome.TemporaryFailure;
        }

        var htmlResult = await _client.GetItemHtmlAsync(itemId, cancellationToken);
        var extraction = htmlResult.IsSuccess && htmlResult.Value is not null
            ? H2SectionExtractor.Extract(htmlResult.Value)
            : new H2ExtractionResult();

        // 読めない応答（200 なのに JSON でない・形が変わった）。投げると⑦の残りが止まる。
        //
        // **予定日は普段の間隔で進める。**通信の失敗（上）と違って BOOTH は応答を返しているので、
        // こちらが圏外だったという話ではなく、次の起動で取り直しても同じ物が返りやすい。
        // 動かさないでいた頃は、読めない商品が毎回の⑦の先頭に来て、そのたびに1本を使っていた。
        // 404 の回数・取得日時・booth には触れない（読めていないので、何も分かっていない）
        if (BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, extraction.Sections, itemId) is not { } booth)
        {
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with { NextFetchDueAt = NextDue(itemId) },
                LocalOwners.Fetch,
                cancellationToken: cancellationToken);
            return RefreshOutcome.Unreadable;
        }

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
        await _images.ClearMissingMarkersAsync(itemId, cancellationToken);

        await NoteBackOnBoothAsync(existing, booth, cancellationToken);
        await NoteVariationLinksAsync(existing, booth, cancellationToken);
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

        // 同じ商品の未読が既にあれば、そこへ重ねる（ユーザ判断 2026-10-02「重ねましょう」）。
        // 前は差し替えていて、既読にする前に2回変わると1回目の差が消えていた。
        // 別の知らせとして溜めず1件にするのは、要確認の行・商品ページの印・ナビの数・「既読にする」が
        // どれも「商品1件に未読1件」で数えているから。読む方も、最初の前と最後の後が分かれば足りる。
        // 錠の中で今の一覧に当てる（読んでから書くまでに人が既読にしていたら、その知らせには重ねない）
        var id = $"item-updated:{existing.Id}";
        var now = DateTimeOffset.Now;
        await _store.Notifications.UpdateAsync(
            notifications =>
            {
                var unread = notifications
                    .Where(entry => entry.Id == id && !entry.IsRead && !entry.IsResolved)
                    .OrderBy(entry => entry.CreatedAt)
                    .ToList();

                if (unread.Count == 0)
                {
                    notifications.Add(new NotificationRecord
                    {
                        Id = id,
                        Kind = NotificationKind.ItemUpdated,
                        ItemId = existing.Id,
                        Title = booth.Name ?? existing.Id,
                        Detail = BoothChanges.Summarize(diffs),
                        Diffs = diffs,
                        CreatedAt = now,
                        IsStrong = BoothChanges.HasStrongChange(diffs),
                    });

                    return notifications;
                }

                // 手で直した JSON などで未読が2件以上あっても、古い順に重ねて1件にまとめる
                var stacked = unread.Skip(1).Aggregate(
                    unread[0].Diffs ?? [],
                    (accumulated, entry) => ChangeStack.Stack(accumulated, entry.Diffs ?? []));
                stacked = ChangeStack.Stack(stacked, diffs);

                // 記録は値で比べると同じ中身の別の行も拾うので、置き場所は参照で探す
                var at = notifications.FindIndex(entry => ReferenceEquals(entry, unread[0]));
                notifications.RemoveAll(entry => unread.Any(target => ReferenceEquals(target, entry)));

                // 戻って元と同じになった（価格が上がって戻った、など）なら、知らせることが無いので消す
                if (stacked.Count > 0)
                {
                    notifications.Insert(Math.Min(at, notifications.Count), unread[0] with
                    {
                        Title = booth.Name ?? existing.Id,
                        Detail = BoothChanges.Summarize(stacked),
                        Diffs = stacked,
                        UpdatedAt = now,
                        IsStrong = BoothChanges.HasStrongChange(stacked),
                    });
                }

                return notifications;
            },
            cancellationToken);
    }

    /// <summary>
    /// 手元のファイル・購入の記録が指す種類が、BOOTH側から消えた／戻ったことを要確認に出す
    /// （ユーザ判断 2026-09-18：どちらも一度きりの出来事で、商品ごとに結び直しの手当てができる）。
    ///
    /// 種類ごとの販売終了は普通の商品でも起こるので、消えたままだと
    /// 「買ったのに記録を入れる行が無い」状態に気付けない。
    /// </summary>
    private async Task NoteVariationLinksAsync(ItemRecord existing, BoothBlock booth, CancellationToken cancellationToken)
    {
        var linked = existing.Local.LocalFiles.Select(file => file.VariationId)
            .Concat(existing.Local.Purchases.Select(purchase => purchase.VariationId))
            .OfType<long>()
            .Distinct()
            .ToList();

        if (linked.Count == 0)
        {
            return;
        }

        var present = booth.Variations.Select(variation => variation.Id).ToHashSet();
        var missing = linked.Where(id => !present.Contains(id)).ToList();

        var goneId = $"variation-gone:{existing.Id}";
        var name = booth.Name ?? existing.Id;

        await _store.Notifications.TryUpdateAsync(
            notifications =>
            {
                var wasGone = notifications.FindIndex(entry => entry.Id == goneId && !entry.IsResolved);
                if (missing.Count > 0)
                {
                    if (wasGone >= 0)
                    {
                        return null;
                    }

                    notifications.Add(new NotificationRecord
                    {
                        Id = goneId,
                        Kind = NotificationKind.OrphanVariationLink,
                        ItemId = existing.Id,
                        // 説明は束の見出しに出るので、行にはこの行だけの事実を書く（ユーザ指示 2026-09-18）
                        Title = name,
                        Detail = $"消えたバリエーション：{NameVariations(missing, existing)}",
                        CreatedAt = DateTimeOffset.Now,
                    });

                    return notifications;
                }

                if (wasGone < 0)
                {
                    return null;
                }

                // 消えていた種類が戻った。前の知らせは用が済んだので解消済みにし、戻ったことを1件出す
                notifications[wasGone] = notifications[wasGone] with { IsResolved = true };
                notifications.Add(new NotificationRecord
                {
                    Id = $"variation-back:{existing.Id}:{DateTimeOffset.Now:yyyyMMddHHmmss}",
                    Kind = NotificationKind.VariationBackOnBooth,
                    ItemId = existing.Id,
                    Title = name,
                    Detail = $"戻ったバリエーション：{NameVariations(linked.Where(present.Contains).ToList(), existing, booth)}",
                    CreatedAt = DateTimeOffset.Now,
                });

                return notifications;
            },
            cancellationToken);
    }

    /// <summary>行に出すバリエーションの名前を並べる（ユーザ要望 2026-09-18：件数だけでは何が消えたか分からない）。</summary>
    /// <remarks>
    /// 消えたバリエーションの名前は**BOOTHにはもう無い**。
    /// 取り直す前の <c>booth</c> ブロックと、購入時に写し取った名前（<see cref="Purchase.NameSnapshot"/>）から引く。
    /// BOOTH が名前を持たせていなかったもの（種類が1つだけの商品に多い）は、ほかの画面と同じく <see cref="DisplayText.NoVariationName"/> と呼ぶ
    /// （ユーザ判断 2026-09-29。「ID 12345」では何のことか分からない）。
    /// どちらでもない（在ったかも分からない）ものだけIDで言う（黙って落とすと、どれのことか辿れなくなる）。
    /// </remarks>
    private static string NameVariations(IReadOnlyList<long> ids, ItemRecord existing, BoothBlock? booth = null)
    {
        const int shown = 3;

        var names = new Dictionary<long, string>();
        var unnamed = new HashSet<long>();
        foreach (var variation in (booth ?? existing.Booth).Variations.Concat(existing.Booth.Variations))
        {
            if (variation.Name is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
            {
                names.TryAdd(variation.Id, text);
            }
            else
            {
                unnamed.Add(variation.Id);
            }
        }

        foreach (var purchase in existing.Local.Purchases)
        {
            if (purchase.VariationId is { } id && purchase.NameSnapshot is { Length: > 0 } text)
            {
                names.TryAdd(id, text);
            }
        }

        var labels = ids
            .Select(id => names.TryGetValue(id, out var text) ? text
                : unnamed.Contains(id) ? DisplayText.NoVariationName
                : $"ID {id}")
            .ToList();

        return labels.Count <= shown
            ? string.Join("・", labels)
            : string.Join("・", labels.Take(shown)) + $"　ほか {labels.Count - shown} 件";
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

        var id = $"item-back:{existing.Id}";
        var name = existing.Local.DisplayName;
        var detail = name is { Length: > 0 }
            ? $"「販売終了」の印を外しました。名前は自分で付けた「{name}」のままです。編集画面で変えられます。"
            : "「販売終了」の印を外しました。";

        await _store.Notifications.UpdateAsync(
            notifications =>
            {
                notifications.RemoveAll(entry => entry.Id == id && !entry.IsRead);
                notifications.Add(new NotificationRecord
                {
                    Id = id,
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = existing.Id,
                    Title = name ?? booth.Name ?? existing.Id,
                    Detail = detail,
                    CreatedAt = DateTimeOffset.Now,
                });

                return notifications;
            },
            cancellationToken);
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
    public async Task<Booth.BoothFetchStatus> RegisterItemAsync(string itemId, CancellationToken cancellationToken = default)
    {
        if (await _store.Items.LoadAsync(itemId, cancellationToken) is not null)
        {
            return Booth.BoothFetchStatus.Success;
        }

        return (await FetchNewItemAsync(itemId, cancellationToken)).Status;
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
        CancellationToken cancellationToken = default,
        IProgress<int>? requestsLeft = null)
    {
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        if (!_store.Items.Exists(itemId) && (await FetchNewItemAsync(itemId, cancellationToken, requestsLeft)).Item is null)
        {
            return false;
        }

        // 中の unitypackage も同じ1回の列挙で拾う（Unity へ送る候補。メモ65-③）
        var survey = RegisteredFolderSet.Survey(folderPath);
        var normalized = Path.TrimEndingDirectorySeparator(folderPath);
        var record = new LocalFolderRecord
        {
            Path = normalized,
            FileCount = survey.FileCount,
            TotalBytes = survey.TotalBytes,
            UnityPackages = survey.UnityPackages,
            RegisteredAt = DateTimeOffset.Now,
            LastSeenAt = DateTimeOffset.Now,
        };

        // BOOTH から取る数秒と、フォルダを測る時間（大きなフォルダでは数十秒）を挟むので、
        // 一覧は書く直前の今の値に当てる。始めに読んだ写しで書くと、その間に取り込みが足したフォルダが消えていた
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current => current with
            {
                LocalFolders =
                [
                    .. current.LocalFolders.Where(folder => !string.Equals(
                        Path.TrimEndingDirectorySeparator(folder.Path), normalized, StringComparison.OrdinalIgnoreCase)),
                    record,
                ],
            },
            LocalOwners.Import,
            cancellationToken);

        // 測っている間に商品が消されていたら、未確定からも外さない（行き先が無くなったので）
        if (!written)
        {
            return false;
        }

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
        // 商品の錠の中で今の一覧から外す（2026-10-05・file-lifecycle.md「気になった所」3）。前は錠の外で読んだ写しを
        // 取り込みの持ち物（localFiles も入る）として書いていたので、読んでから書くまでに取り込みが足したファイルが消え得た
        var normalized = Path.TrimEndingDirectorySeparator(folderPath);
        return await _store.Items.ChangeLocalAsync(
            itemId,
            current => WithoutFolder(current.LocalFolders, normalized) is { } remaining
                ? current with { LocalFolders = remaining }
                : null,
            [LocalField.LocalFolders],
            cancellationToken);
    }

    /// <summary>
    /// 見つからない登録フォルダの場所を、人が「この場所にする」で選んだ場所に差し替える（見つからない・移動の点検 10-A・ユーザ判断 2026-10-05）。
    /// </summary>
    /// <remarks>
    /// フォルダは場所が同一性なので、前は移すと「見つかりません」のままで、登録を外して登録し直すしかなかった（登録した日時も失う）。
    /// 候補は「見つからないファイルを探す」が名前・ファイル数・大きさで見せ、ここは人が選んだ場所を書くだけ。
    /// 測る（大きなフォルダでは数十秒）のは錠の外で、書くのは錠の中の今の値に当てる。その間に登録が外されていれば何も書かない。
    /// 登録したときと同じく、新しい場所の下の未確定は片付ける（取り込みはフォルダを移すと中身を未確定に出す）。
    /// </remarks>
    public async Task<FolderRelocation> RelocateFolderAsync(
        string itemId,
        string fromPath,
        string toPath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(toPath))
        {
            return FolderRelocation.TargetMissing;
        }

        var from = Path.TrimEndingDirectorySeparator(fromPath);
        var to = Path.TrimEndingDirectorySeparator(toPath);

        // 2つの登録が同じ場所（とその中）を指すと、走査が飛ばす範囲と容量が二重になる。候補から外してあるが、選ぶまでの間に登録され得る
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var others = new RegisteredFolderSet(loaded.Items
            .Where(item => item.Id != itemId)
            .SelectMany(item => item.Local.LocalFolders)
            .Select(folder => folder.Path));
        if (others.Contains(to))
        {
            return FolderRelocation.RegisteredElsewhere;
        }

        var (count, bytes) = RegisteredFolderSet.Measure(to);
        var now = DateTimeOffset.Now;
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var record = current.LocalFolders.FirstOrDefault(folder => string.Equals(
                    Path.TrimEndingDirectorySeparator(folder.Path), from, StringComparison.OrdinalIgnoreCase));
                if (record is null)
                {
                    return null;
                }

                // 同じ商品が選んだ場所を既に登録していれば、1つにまとめる（同じ場所の登録が2つ並ばないように）
                return current with
                {
                    LocalFolders =
                    [
                        .. current.LocalFolders.Where(folder =>
                        {
                            var path = Path.TrimEndingDirectorySeparator(folder.Path);
                            return !string.Equals(path, from, StringComparison.OrdinalIgnoreCase)
                                && !string.Equals(path, to, StringComparison.OrdinalIgnoreCase);
                        }),
                        record with
                        {
                            Path = to,
                            FileCount = count,
                            TotalBytes = bytes,
                            LastSeenAt = now,
                            MissingSince = null,
                        },
                    ],
                };
            },
            [LocalField.LocalFolders],
            cancellationToken);

        if (!written)
        {
            return FolderRelocation.RecordGone;
        }

        await RemoveUnresolvedUnderAsync(to, cancellationToken);
        return FolderRelocation.Moved;
    }

    /// <summary>その場所の登録を除いた一覧。除く物が無ければ null。</summary>
    private static List<LocalFolderRecord>? WithoutFolder(IReadOnlyList<LocalFolderRecord> folders, string normalized)
    {
        var remaining = folders
            .Where(folder => !string.Equals(
                Path.TrimEndingDirectorySeparator(folder.Path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return remaining.Count == folders.Count ? null : remaining;
    }

    /// <summary>
    /// まだ手元に無い商品をBOOTHから取ってきて保存する。説明HTMLと画像もここで揃える。
    /// ファイル確定とフォルダ登録の両方から使う（どちらも「新しい商品が増える」点は同じ）。
    /// </summary>
    /// <summary>
    /// BOOTHから取って新しいitemを作る。**仮IDでは何もしない**——
    /// 存在しないIDなので、通信するだけ無駄になる。
    /// </summary>
    /// <param name="requestsLeft">
    /// BOOTH への問い合わせの残りの数を流す（メモ34）。商品JSON・商品ページ・画像・ショップのアイコンで、
    /// 画像の枚数はJSONを読むまで分からないので、始めは JSON と商品ページの2つだけ数え、分かった所で数え直す。
    /// 数え方は問い合わせる物だけ（画像の側は <see cref="ImagePipeline.SyncAsync(string, IReadOnlyList{BoothImage}, BoothOutageWatch?, CancellationToken, IProgress{int}?)"/>）。
    /// </param>
    /// <param name="galleryLater">
    /// 画像は1枚目だけを取り、残りを⑤の段で裏に頼む（<see cref="RequestRemainingImagesLater"/>）。未確定の「このIDで登録」だけが使う
    /// （登録の列で後ろの登録を待たせるのはこの道だけ。ID の付け替え・ファイルを持たない登録・フォルダの登録は今までどおり全部取る）。
    /// </param>
    private async Task<(ItemRecord? Item, Booth.BoothFetchStatus Status)> FetchNewItemAsync(
        string itemId, CancellationToken cancellationToken, IProgress<int>? requestsLeft = null, bool galleryLater = false)
    {
        if (LocalItemId.IsLocal(itemId))
        {
            return (null, Booth.BoothFetchStatus.NotFound);
        }

        requestsLeft?.Report(2);
        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);
        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            // **失敗の種類をそのまま返す**（E3）。見つからないのと、一時的に届かないのは次の一手が違う
            return (null, jsonResult.Status);
        }

        requestsLeft?.Report(1);
        var htmlResult = await _client.GetItemHtmlAsync(itemId, cancellationToken);
        var extraction = htmlResult.IsSuccess && htmlResult.Value is not null
            ? H2SectionExtractor.Extract(htmlResult.Value)
            : new H2ExtractionResult();

        // 読めない応答は「一時的に届かない」と同じ扱い。投げると画面に理由の分からない失敗が出る
        if (BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, extraction.Sections, itemId) is not { } fetched)
        {
            return (null, Booth.BoothFetchStatus.TemporaryFailure);
        }

        var item = new ItemRecord
        {
            Id = itemId,
            Booth = fetched,
            Local = new LocalBlock
            {
                NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                LastFetchedAt = DateTimeOffset.Now,
                NextFetchDueAt = NextDue(itemId),
            },
        };

        // **「無い」と判断した時点と書く時点がずれている**（取り込みの①と同じ・L13）。
        // BOOTH へ2回問い合わせる3秒以上の間に、取り込みや別の「このIDで登録」が同じ商品を作ることがあり、
        // 丸ごと書くとそちらが入れたファイル・名前・購入記録が消えていた。
        // 書く直前に読み直し、あれば取ってきた booth と取得の記録だけを重ねる（手元のファイルは呼ぶ側が足す）。
        // **在るかを見てから書くまでを商品の錠の中で行う**（外で見ていた頃は、見てから書くまでの数 ms に
        // 作られた商品を、こちらの新しい商品で丸ごと上書きし得た）
        await _store.Items.CreateOrChangeLocalAsync(
            itemId,
            () => item,
            local => local with
            {
                ConsecutiveNotFoundCount = 0,
                IsDelisted = false,
                LastFetchedAt = item.Local.LastFetchedAt,
                NextFetchDueAt = item.Local.NextFetchDueAt,
            },
            LocalOwners.Fetch,
            cancellationToken,
            booth: item.Booth);

        item = await _store.Items.LoadAsync(itemId, cancellationToken) ?? item;

        if (extraction.DescriptionHtml is not null)
        {
            await _store.Items.SaveDescriptionHtmlAsync(itemId, extraction.DescriptionHtml, cancellationToken);
        }

        // ショップのアイコンは、画像の後に1枚問い合わせる（まだ手元に無いときだけ）
        var iconLeft = item.Booth.Shop is { } iconShop && _images.NeedsShopIcon(iconShop.Subdomain, iconShop.ThumbnailUrl) ? 1 : 0;
        if (galleryLater)
        {
            // 1枚目はカードと商品ページの顔なので、登録の中で取る（梯子の④と同じ）
            var first = item.Booth.Images.Count > 0 ? item.Booth.Images[0] : null;
            var firstLeft = first is not null && _images.SavesImages && !_images.IsSettled(itemId, first.OriginalUrl) ? 1 : 0;
            requestsLeft?.Report(firstLeft + iconLeft);
            if (first is not null)
            {
                await _images.SyncOneAsync(itemId, first, cancellationToken);
                if (firstLeft > 0)
                {
                    requestsLeft?.Report(iconLeft);
                }
            }
        }
        else
        {
            await _images.SyncAsync(
                itemId, item.Booth.Images, null, cancellationToken,
                requestsLeft is null ? null : new ShiftedProgress(requestsLeft, iconLeft));
        }

        if (item.Booth.Shop is { } shop)
        {
            await _images.SyncShopIconAsync(shop.Subdomain, shop.ThumbnailUrl, cancellationToken);
        }

        requestsLeft?.Report(0);

        if (galleryLater)
        {
            RequestRemainingImagesLater(itemId, item.Booth.Images);
        }

        return (item, Booth.BoothFetchStatus.Success);
    }

    /// <summary>
    /// 登録した商品の残りの画像（2枚目から）を、梯子の⑤（<see cref="BoothPriority.Gallery"/>）で裏に頼む（メモ60 案B・ユーザ判断 2026-10-06）。
    ///
    /// 登録の中で全部を取ると、1件が「2＋画像の枚数＋アイコン」になる。友人の写し206件で画像は平均7.4枚・90%で15枚・最大43枚あり、
    /// 1件の登録が中央 約13秒・90% 約30秒・最大 約1.1分かかって、列の後ろの登録を待たせていた。残りを⑤へ回すと1件 約6秒になる。
    /// **人が押した優先度は掛けない**——起動時の⑤と同じ段で走らせ、次に並んだ登録（人が押した操作）や取り込みの①②に先を譲る。
    /// 閉じて途中で止まっても印は置かないので、手元の JSON とディスクの差で次の起動の⑤（<see cref="ImageBacklog"/>）が拾う。
    /// </summary>
    private void RequestRemainingImagesLater(string itemId, IReadOnlyList<BoothImage> images)
    {
        if (!_images.SavesImages || images.Count <= 1)
        {
            return;
        }

        lock (_galleryHoldGate)
        {
            if (_galleryHolds > 0)
            {
                // 登録の列が動いている間は始めない（下の HoldRemainingImages）。始めると、門が空いた瞬間に待っている
                // 残りの画像が、次の登録の問い合わせの合間に1本ずつ入り、2件目からの登録が見込みの倍ほどかかっていた
                _heldGalleries.Add((itemId, images));
                return;
            }
        }

        StartRemainingImages(itemId, images);
    }

    private readonly object _galleryHoldGate = new();
    private int _galleryHolds;
    private readonly List<(string ItemId, IReadOnlyList<BoothImage> Images)> _heldGalleries = [];

    /// <summary>
    /// 登録の列が動いている間、登録した商品の残りの画像を頼むのを待たせる（ユーザ判断 2026-10-06・メモ60 案B の続き）。
    /// 返した物を Dispose すると（列が空になったら）、待たせた分をまとめて⑤の段で頼む。
    /// 門の決まり（空いた時点で待っている物から選ぶ）には触れず、列を短くする狙いがそのまま出る
    /// </summary>
    public IDisposable HoldRemainingImages()
    {
        lock (_galleryHoldGate)
        {
            _galleryHolds++;
        }

        return new GalleryHold(this);
    }

    private void ReleaseGalleryHold()
    {
        List<(string ItemId, IReadOnlyList<BoothImage> Images)> released;
        lock (_galleryHoldGate)
        {
            if (--_galleryHolds > 0)
            {
                return;
            }

            released = [.. _heldGalleries];
            _heldGalleries.Clear();
        }

        foreach (var (itemId, images) in released)
        {
            StartRemainingImages(itemId, images);
        }
    }

    private sealed class GalleryHold(ItemService owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.ReleaseGalleryHold();
            }
        }
    }

    private void StartRemainingImages(string itemId, IReadOnlyList<BoothImage> images)
    {
        BackgroundWork.Run("登録した商品の残りの画像", async () =>
        {
            using var priority = BoothClient.Prioritize(BoothPriority.Gallery);

            // 起動時の⑤と同じく、届かない失敗が3件続いたら残りは問い合わせない（取らなかった絵は次の起動の⑤で取る）
            var outage = new BoothOutageWatch();
            await _images.SyncAsync(itemId, images, outage);
            outage.LogIfStopped("登録した商品の残りの画像");
        });
    }

    /// <summary>
    /// 登録したフォルダの配下にあった未確定を取り除く。行き先が決まったため。
    ///
    /// 配下に1件も無ければ書かない。前は「在るかを見るために読む → 在れば錠の中でもう一度読んで書く」の2回読みで、
    /// 1回目は命令の頭で画面のスレッドを止めていた。錠の中で今の一覧を見て、外す物が無ければ書かずに抜ける（読むのは1回）
    /// </summary>
    private Task RemoveUnresolvedUnderAsync(string folderPath, CancellationToken cancellationToken)
    {
        var registered = new RegisteredFolderSet([folderPath]);
        return _store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => file.Paths.Any(registered.Contains)) > 0 ? current : null,
            cancellationToken);
    }

    /// <summary>
    /// 未確定の一覧から1件外す。
    ///
    /// **錠の中で今の一覧から外す**（技術的負債 1-2）。一覧は取り込みも書くので、始めに読んだ写しを書き戻すと、
    /// その間に取り込みが足した物が消える（逆に取り込みがこちらの変更を消すのは <see cref="UnresolvedMerge"/> で防ぐ）。
    /// 一覧に無ければ書かない（商品に戻す・zipで登録し直すでは、未確定に居ないのが普通。同じ中身を書き直すだけで、
    /// 未確定が数万件あると数十MBの書き出しになる）。
    /// </summary>
    private Task RemoveUnresolvedAsync(string hash, CancellationToken cancellationToken)
        => _store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0
                ? current
                : null,
            cancellationToken);

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
        // 「未確定」を開くたびに画面のスレッドから呼ばれる。記録は裏で読む（8万件で、開くたびに 150〜290ms 止まっていた）
        var unresolved = await _store.Unresolved.LoadAsync(cancellationToken);
        if (unresolved.Count == 0)
        {
            return 0;
        }

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var owned = loaded.Items
            .SelectMany(item => item.Local.AttachedFiles)
            .Select(file => file.Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!unresolved.Any(file => owned.Contains(file.Hash)))
        {
            return 0;
        }

        // 商品を全部読む間に取り込みが一覧を書くことがあるので、錠の中で今の一覧から外す（技術的負債 1-2）
        var removed = 0;
        await _store.Unresolved.UpdateAsync(
            current =>
            {
                removed = current.RemoveAll(file => owned.Contains(file.Hash));
                return current;
            },
            cancellationToken);
        return removed;
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
    public async Task<(ItemPreview? Preview, string? Error, bool NotOnBooth)> PreviewWithReasonAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is not null)
        {
            // 名前は画面のほかの所と同じ決め方にする（付けた名前 → BOOTH の名前 → ID）。BOOTH に無い商品として登録した物は
            // BOOTH の名前を持たないので、BOOTH の側だけを見ると、確認の題が仮の ID（local-…）で出ていた（2026-09-30）
            return (ToPreview(itemId, existing.Booth, isAlreadyOwned: true, existing.DisplayName), null, false);
        }

        var jsonResult = await _client.GetItemJsonAsync(itemId, cancellationToken);

        if (jsonResult.Status == BoothFetchStatus.NotFound)
        {
            await NoteBoothAnswerAsync(itemId, stillMissing: true, cancellationToken);
            return (null, $"商品ID {itemId} はBOOTHに見つかりませんでした。IDが違うか、販売が終わって非公開になっています。", true);
        }

        if (!jsonResult.IsSuccess || jsonResult.Value is null)
        {
            // 一時的に届かないときは、残してある答え（無い）を変えない。確かめられていないだけ
            var detail = string.IsNullOrWhiteSpace(jsonResult.Error) ? string.Empty : $"（{jsonResult.Error}）";
            return (null, $"BOOTHに問い合わせできませんでした{detail}。通信を確かめて、もう一度お試しください。", false);
        }

        if (BoothItemMapper.TryMap(jsonResult.Value, DateTimeOffset.Now, [], itemId) is not { } booth)
        {
            return (null, "BOOTHから届いた商品情報を読み取れませんでした。少し待ってから、もう一度お試しください。", false);
        }

        await NoteBoothAnswerAsync(itemId, stillMissing: false, cancellationToken);
        return (ToPreview(itemId, booth, isAlreadyOwned: false, requestsToRegister: RequestsToRegister(booth)), null, false);
    }

    /// <summary>
    /// 人が聞き直した BOOTH の答えを、未確定に残した「無い」の記録（<see cref="UnresolvedFile.NotOnBooth"/>）に合わせる（ユーザ判断 2026-10-06）。
    /// 今も無ければ日時を新しくし、公開されていれば外す（外さないと、一覧の札が「BOOTHで非公開」のまま残る）。
    ///
    /// 合わせるのは、もうそのIDの記録を持つ行だけ。人が打ったIDで「無い」と出ても、その行に付けない——打ち間違いかもしれず、
    /// どの行の物かも決まらない。**錠の中で今の一覧に当てる**（取り込みが同じ一覧を書く。記録の無い一覧は書かない）
    /// </summary>
    private Task NoteBoothAnswerAsync(string itemId, bool stillMissing, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        return _store.Unresolved.TryUpdateAsync(
            current =>
            {
                var changed = false;
                for (var index = 0; index < current.Count; index++)
                {
                    if (current[index].NotOnBooth is { } note && string.Equals(note.ItemId, itemId, StringComparison.Ordinal))
                    {
                        current[index] = current[index].WithNotOnBooth(stillMissing ? note with { CheckedAt = now } : null);
                        changed = true;
                    }
                }

                return changed ? current : null;
            },
            cancellationToken);
    }

    /// <summary>
    /// 登録の見込みの数。数え方は「このIDで登録」の <see cref="FetchNewItemAsync"/> の問い合わせと同じ（JSON・ページ・1枚目・無ければアイコン）。
    /// 残りの画像は登録の後に⑤の段で裏に取るので数えない（メモ60 案B）
    /// </summary>
    private int RequestsToRegister(BoothBlock booth)
        => 2 + Math.Min(booth.Images.Count, 1)
            + (booth.Shop is { } shop && _images.NeedsShopIcon(shop.Subdomain, shop.ThumbnailUrl) ? 1 : 0);

    private static ItemPreview ToPreview(
        string itemId, BoothBlock booth, bool isAlreadyOwned, string? name = null, int? requestsToRegister = null) => new()
    {
        RequestsToRegister = requestsToRegister,
        Id = itemId,
        Name = name ?? booth.Name ?? itemId,
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

    /// <summary>
    /// 展開フォルダで登録していた商品を、隣に現れたzipの方で登録し直す（ユーザ指示 2026-09-18）。
    ///
    /// フォルダ登録はzipが手元に無いときの受け皿で、zipが手に入ったら役目を終える。
    /// 以前は「フォルダの登録を外す」だけで、zipは人が取り込み直すしかなかった——
    /// **押した後に商品のファイルが1つも無くなる**ので、何が起きたのか分からなくなっていた。
    ///
    /// zipを1本だけハッシュして商品に付け、同時にフォルダの登録を外す。
    /// 取り込み全体を回さないのは、親フォルダに何百件あっても数えるだけで時間がかかるため。
    /// **ディスクのファイルには触らない。**
    /// </summary>
    public async Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(
        string itemId,
        string folderPath,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ItemMissing, null);
        }

        var archivePath = Scanning.RegisteredFolderSet.FindArchiveFor(folderPath);
        if (archivePath is null)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ArchiveMissing, null);
        }

        var name = Path.GetFileName(archivePath);
        // 外した行は「付いている」に数えない（2026-10-05・file-lifecycle.md「気になった所」7）。前は外した zip を「登録済み」と読んで
        // フォルダの登録だけ外し、商品の手元の物が無くなっていた。外していた zip なら下へ進み、突き合わせで印を下ろして付け直す
        // （押した方が新しい判断。ユーザ判断 2026-10-05）
        var already = item.Local.OwnedFiles.Any(file => file.Paths.Any(
            path => string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase)));

        if (already)
        {
            // zipは既に付いている。あとはフォルダの登録を外すだけ
            await UnregisterFolderAsync(itemId, folderPath, cancellationToken);
            return new ArchiveSwapOutcome(ArchiveSwapResult.AlreadyRegistered, name);
        }

        if (await ReadHandFileAsync(archivePath, cancellationToken) is not { } record)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ArchiveUnreadable, name);
        }

        var hash = record.Hash;
        switch (await HandAttachBlockAsync(itemId, hash, liftExclusion, takeFromOtherItems, cancellationToken))
        {
            case { Excluded: true }:
                return new ArchiveSwapOutcome(ArchiveSwapResult.Excluded, name);
            case { Holders: { Count: > 0 } holders }:
                return new ArchiveSwapOutcome(ArchiveSwapResult.OwnedElsewhere, name) { Holders = holders };
        }

        var normalized = Path.TrimEndingDirectorySeparator(folderPath);

        // ファイルとフォルダを1回で、商品の錠の中で今の値に当てて書く。2回に分けると、間に人が触った入力が消える。
        // 上で読んだ写しは使わない（2026-10-05・file-lifecycle.md「気になった所」3）：zip のハッシュに数秒〜かかる間に
        // 取り込みが足したファイル・付けた種類・外す／戻す・見つからなくなった日時が、写しで書くと古い値に戻っていた。
        // 重いハッシュは錠の外で済ませ、錠の中では足し合わせるだけ
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current => current with
            {
                LocalFiles = Scanning.LocalFileMerger.MergeByHand(current.LocalFiles, [record]),
                LocalFolders = WithoutFolder(current.LocalFolders, normalized) ?? current.LocalFolders,
            },
            [LocalField.LocalFiles, LocalField.LocalFolders],
            cancellationToken);

        // ハッシュの間に商品が消されていたら、未確定からも外さない（行き先が無くなったので）
        if (!written)
        {
            return new ArchiveSwapOutcome(ArchiveSwapResult.ItemMissing, name);
        }

        // 未確定に同じzipが居たなら、行き先が決まったので外す（除外を解く・ほかの商品から外すも、こちらに付けた後で）
        await AfterHandAttachAsync(itemId, hash, liftExclusion, takeFromOtherItems, cancellationToken);
        await RemoveUnresolvedUnderAsync(normalized, cancellationToken);

        return new ArchiveSwapOutcome(ArchiveSwapResult.Registered, name);
    }

    /// <summary>
    /// 商品ページから、選んだ・落としたファイルをこの商品に結ぶ（ユーザ指示 2026-10-06）。
    ///
    /// 作者が前の商品を消して同じ物を新しいIDで出し直すと、ファイルの手掛かり（Zone.Identifier・ファイル名・zip の中の URL）は
    /// 古いIDを指したままなので、取り込みでは古い商品の方へ行くか未確定に出る。人が「これはこの商品の物」と言える道が要る。
    ///
    /// 決まりは「zipで登録し直す」（<see cref="SwapFolderForArchiveAsync"/>）と同じ：重いハッシュは錠の外、書くのは商品の錠の中で今の一覧へ
    /// <see cref="Scanning.LocalFileMerger.MergeByHand"/>（人の登録と同じ足し方。外していた物は印が下り、無い場所は外さない）。
    /// 除外・ほかの持ち主は人が決めたことなので、何も書かずに返して画面に聞かせ、頼まれたときだけ立てて呼び直させる。
    /// **ディスクのファイルには触らない。BOOTH へも行かない。**
    /// </summary>
    public async Task<FileAttachOutcome> AttachFileAsync(
        string itemId,
        string path,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(path);

        // 単体の unitypackage・7z などは取り込みも扱わない（Unity へ送る道も zip の中しか見ない）。持ち物にすると、所持には数えるのに
        // 送れも開けもしない行になる
        if (!Scanning.FolderScanner.TargetExtensions.Contains(Path.GetExtension(path)))
        {
            return new FileAttachOutcome(FileAttachResult.NotTarget, name);
        }

        if (!_store.Items.Exists(itemId))
        {
            return new FileAttachOutcome(FileAttachResult.ItemMissing, name);
        }

        if (!File.Exists(path))
        {
            return new FileAttachOutcome(FileAttachResult.FileMissing, name);
        }

        if (await ReadHandFileAsync(path, cancellationToken) is not { } record)
        {
            return new FileAttachOutcome(FileAttachResult.FileUnreadable, name);
        }

        switch (await HandAttachBlockAsync(itemId, record.Hash, liftExclusion, takeFromOtherItems, cancellationToken))
        {
            case { Excluded: true }:
                return new FileAttachOutcome(FileAttachResult.Excluded, name);
            case { Holders: { Count: > 0 } holders }:
                return new FileAttachOutcome(FileAttachResult.OwnedElsewhere, name) { Holders = holders };
        }

        // 在るかを見るのも書くのも錠の中の今の一覧で。この場所のまま外さずに持っていれば書かない（同じ物を2回選んだ・落とした）
        var already = false;
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                already = current.LocalFiles.Any(file => !file.Detached
                    && string.Equals(file.Hash, record.Hash, StringComparison.OrdinalIgnoreCase)
                    && file.Paths.Any(known => string.Equals(known, path, StringComparison.OrdinalIgnoreCase)));
                return already
                    ? null
                    : current with { LocalFiles = Scanning.LocalFileMerger.MergeByHand(current.LocalFiles, [record]) };
            },
            [LocalField.LocalFiles],
            cancellationToken);

        if (already)
        {
            return new FileAttachOutcome(FileAttachResult.AlreadyAttached, name);
        }

        // ハッシュの間に商品が消されていたら、未確定からも外さない（行き先が無くなったので）
        if (!written)
        {
            return new FileAttachOutcome(FileAttachResult.ItemMissing, name);
        }

        await AfterHandAttachAsync(itemId, record.Hash, liftExclusion, takeFromOtherItems, cancellationToken);
        return new FileAttachOutcome(FileAttachResult.Attached, name);
    }

    /// <summary>
    /// 人が選んだファイル1本を読み、商品のファイルの記録にする（錠の外で呼ぶ。大きいファイルはハッシュに数秒かかる）。
    /// 読めなければ null。中身の一覧は zip だけ読む（取り込みの <c>InspectFile</c> と同じ）。
    /// </summary>
    private static Task<LocalFileRecord?> ReadHandFileAsync(string path, CancellationToken cancellationToken)

        // 画面のスレッドから来る命令なので、大きさ・zip の目録の読み取りもスレッドの外で行う（外付け・ネットワークで待たされないように）
        => Task.Run<LocalFileRecord?>(async () =>
        {
            try
            {
                var size = new FileInfo(path).Length;
                var hash = await Scanning.FileHasher.ComputeSha256Async(path, cancellationToken);
                IReadOnlyList<string> contents = [];
                var broken = false;

                // 中のファイル名は、欠落復旧の照合と動作環境の推測に使う。読めなければ空で進む
                if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        contents = BoothZipInspector.ZipInspector.Inspect(path).Summary.Files
                            .Select(entry => entry.RelativePath)
                            .ToList();
                    }
                    catch (InvalidDataException)
                    {
                        // 形式が合わない＝壊れている。取り込みと同じ印を付ける（ほかのアプリが開いていた・権限が無いは、壊れているとは言えない）
                        broken = true;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                return new LocalFileRecord
                {
                    Hash = hash,
                    Paths = [path],
                    SizeBytes = size,
                    Contents = contents,
                    ArchiveBroken = broken,
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }, cancellationToken);

    /// <summary>人の登録を止める事情（除外・ほかの持ち主）。頼まれた方は見ない。</summary>
    private readonly record struct HandAttachBlock(bool Excluded, IReadOnlyList<ArchiveHolder> Holders);

    /// <summary>
    /// 除外とほかの持ち主は、人が決めたこと。黙って上書きせず、何も書かずに返して画面に聞かせる（ユーザ判断 2026-10-05・
    /// file-lifecycle.md「気になった所」7）。前は見ずに付けていたので、除外した zip が除外のまま持ち物になり、
    /// ほかの商品が持つ zip は2つの商品の持ち物になって容量も二重に数えていた
    /// </summary>
    private async Task<HandAttachBlock> HandAttachBlockAsync(
        string itemId, string hash, bool liftExclusion, bool takeFromOtherItems, CancellationToken cancellationToken)
    {
        var excluded = await _store.Excluded.LoadAsync(cancellationToken);
        if (!liftExclusion && excluded.Any(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)))
        {
            return new HandAttachBlock(true, []);
        }

        if (takeFromOtherItems)
        {
            return new HandAttachBlock(false, []);
        }

        return new HandAttachBlock(false, await OtherHoldersAsync(itemId, hash, cancellationToken));
    }

    /// <summary>
    /// こちらに付けた後の片付け：頼まれていれば除外を解き、ほかの商品から外し、未確定から消す。
    /// 除外を解くのも、ほかの商品から外すのも、こちらに付けられた後。逆にすると、付ける前に商品が消されていたとき
    /// 除外も持ち主も失ったファイルが残る。この順なら、途中で落ちても二重に持つだけで、どこからも消えない
    /// </summary>
    private async Task AfterHandAttachAsync(
        string itemId, string hash, bool liftExclusion, bool takeFromOtherItems, CancellationToken cancellationToken)
    {
        if (liftExclusion)
        {
            await _store.Excluded.TryUpdateAsync(
                current => current.RemoveAll(entry => string.Equals(entry.Hash, hash, StringComparison.OrdinalIgnoreCase)) > 0
                    ? current
                    : null,
                cancellationToken);
        }

        if (takeFromOtherItems)
        {
            // 見てから書くまでに持ち主が変わっていることがあるので、それぞれの錠の中で今の一覧に印を付ける（DetachFileAsync と同じ形）。
            // 行は消さずに外した印にする：手掛かりが指せば次の取り込みでそちらへ戻ってしまうのを止め、商品ページで「この商品に戻す」もできる。
            // 手元の物が無くなっても商品は消さない（消すかは人が商品ページで決める）
            foreach (var holder in await OtherHoldersAsync(itemId, hash, cancellationToken))
            {
                await _store.Items.ChangeLocalAsync(
                    holder.ItemId,
                    current => current.LocalFiles.Any(file =>
                            !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                        ? current with
                        {
                            LocalFiles = [.. current.LocalFiles
                                .Select(file => !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                                    ? file with { Detached = true }
                                    : file)],
                        }
                        : null,
                    LocalOwners.Import,
                    cancellationToken);
            }
        }

        await RemoveUnresolvedAsync(hash, cancellationToken);
    }

    /// <summary>
    /// この中身を持っている（外していない）ほかの商品。外した行は持ち物ではないので数えない（ReattachFileAsync と同じ見方）。
    /// 全件を読むのは人が押した1回だけで、取り込みの道では呼ばない。
    /// </summary>
    private async Task<IReadOnlyList<ArchiveHolder>> OtherHoldersAsync(
        string itemId, string hash, CancellationToken cancellationToken)
    {
        bool Holds(LocalFileRecord file) => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase);

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return loaded.Items
            .Where(other => other.Id != itemId && other.Local.AttachedFiles.Any(Holds))
            .Select(other => new ArchiveHolder(
                other.Id,
                other.DisplayName,
                !HoldsAnything(other.Local with
                {
                    LocalFiles = [.. other.Local.LocalFiles.Where(file => !Holds(file))],
                })))
            .ToList();
    }

    /// <summary>
    /// 未確定の記録から、商品のファイルの記録を作る。3つの登録の道（このIDで登録・見つからないIDのまま登録・BOOTHに無い商品として登録）で同じ。
    /// **開けなかった印も引き継ぐ**（ユーザ判断 2026-09-30）：壊れた zip でも登録は止めないので、
    /// 引き継がないと、未確定では出ていた「壊れたzip」が商品ページで消える
    /// </summary>
    private static LocalFileRecord FromUnresolved(UnresolvedFile target) => new()
    {
        Hash = target.Hash,
        Paths = target.Paths,
        SizeBytes = target.SizeBytes,
        Contents = target.Contents,
        ArchiveBroken = target.ArchiveBroken,
    };

    public async Task<bool> AssignItemIdAsync(string hash, string itemId, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null)
    {
        // 登録の命令は画面のスレッドから来る。記録は裏で読む（FromUnresolved の3つの道とも同じ）
        var unresolved = await _store.Unresolved.LoadAsync(cancellationToken);
        var target = unresolved.FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return false;
        }

        var record = FromUnresolved(target);

        if (!_store.Items.Exists(itemId) && (await FetchNewItemAsync(itemId, cancellationToken, requestsLeft, galleryLater: true)).Item is null)
        {
            return false;
        }

        // 取得に数秒かかるので、その間に取り込みが同じ商品へファイルを足していることがある。
        // 前は作った時点の写しに [このファイル] を置き換えで書いていたので、足された物が消えていた。
        // 書く直前の一覧に足す
        if (!await _store.Items.ChangeLocalAsync(
                itemId,
                current => current with { LocalFiles = LocalFileMerger.MergeByHand(current.LocalFiles, [record]) },
                LocalOwners.Import,
                cancellationToken))
        {
            return false;
        }

        // 前に「この商品のものではない」と外していたなら、上の突き合わせ（LocalFileMerger）で印が下りている。
        // ユーザが改めて選び直したのだから、こちらが覚えていて弾き続ける方がおかしい
        await RemoveUnresolvedAsync(target.Hash, cancellationToken);

        return true;
    }

    /// <summary>
    /// 未確定のファイルを、BOOTHで見つからなかった商品IDのまま登録する（ユーザ判断 2026-09-29）。
    ///
    /// 商品IDが分かっているのに仮ID（<see cref="LocalItemId"/>）で登録すると、⑦の対象から外れるので、
    /// 後で再び公開されても情報を取れない（季節ものは1ヶ月ほどだけ公開されることがある）。本物のIDで持っておけば、
    /// 今ある「販売終了」の商品と同じ道に乗る：
    /// <list type="bullet">
    /// <item><c>Booth</c> は空（<c>FetchedAt</c> が null＝一度も取れていない。観測していないので、それが正しい）。名前は <c>Local.DisplayName</c></item>
    /// <item><c>IsDelisted</c> を立て、見つからない回数は非公開と確定する回数にしておく。⑦で見つからなければ回数が増えるだけで
    ///   印は外れない（回数を1で始めると、次に見つからなかったとき「3回未満」で印が外れてしまう）。確かめ直しの間隔も販売終了と同じ</item>
    /// <item>公開されたら <see cref="RefreshAsync"/> が booth を埋め、印を外し、要確認に「BOOTHに現れました」を出す（<c>NoteBackOnBoothAsync</c>）</item>
    /// </list>
    /// **BOOTHへは問い合わせない。**直前の確かめ（<see cref="PreviewWithReasonAsync"/>）で見つからなかったIDで、もう一度聞いても同じ答えになる。
    /// </summary>
    public async Task<bool> AssignUnpublishedItemIdAsync(
        string hash,
        string itemId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        // 仮IDは BOOTH に存在しないので、⑦に乗せても意味が無い（そちらは RegisterLocalItemAsync）
        if (LocalItemId.IsLocal(itemId))
        {
            return false;
        }

        // BOOTH へ行かない道なので、未確定の錠を持ったまま最後まで進める（RegisterLocalItemAsync と同じ形・読むのは1回）。
        // 順番は前と同じ：商品を保存してから、一覧から外す
        return await _store.Unresolved.TryUpdateAwaitingAsync(
            async current =>
            {
                var target = current.FirstOrDefault(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    return null;
                }

                var record = FromUnresolved(target);

                // 在るかを見てから作るまでを商品の錠の中で行う（取り込みや別の道が同じIDを作っていることがある・L13 と同じ形）。
                // 在れば、ファイルを足すだけにする。取得の記録（見つからない回数・予定日）は持ち主の⑦に任せ、名前も上書きしない
                var written = await _store.Items.CreateOrChangeLocalAsync(
                    itemId,
                    () => UnpublishedItem(itemId, displayName),
                    local => local with { LocalFiles = LocalFileMerger.MergeByHand(local.LocalFiles, [record]) },
                    LocalOwners.Import,
                    cancellationToken);
                if (!written)
                {
                    return null;
                }

                current.RemoveAll(file => string.Equals(file.Hash, target.Hash, StringComparison.OrdinalIgnoreCase));
                return current;
            },
            cancellationToken);
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

    /// <summary>
    /// IDを変更したら何が起きるかの下見。**書き込まない。**
    ///
    /// 移した先が手元に無ければBOOTHへ聞きに行く（①②の2本）。
    /// **取れなくても止めない**——非公開の商品へ寄せることもあるので、
    /// 「見つかりませんが、このIDで登録しますか」と聞ける形にする。
    /// </summary>
    public async Task<ItemIdChangePlan?> PlanItemIdChangeAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default)
    {
        var source = await _store.Items.LoadAsync(fromId, cancellationToken);
        if (source is null || string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return null;
        }

        var target = await _store.Items.LoadAsync(toId, cancellationToken);
        if (target is not null)
        {
            // 既に手元にあるなら聞きに行かない。通信を増やさない
            return ItemIdChange.Plan(source, target, toId, ItemIdTargetStatus.Found);
        }

        // 仮IDへ移すことはない（BOOTHに無いIDへ寄せる意味がない）ので、そこは聞きに行かない
        if (LocalItemId.IsLocal(toId))
        {
            return ItemIdChange.Plan(source, target: null, toId, ItemIdTargetStatus.NotFound);
        }

        // 一時的に届かないのを「BOOTHにある」と読まない。窓が「移すときに取得します」と言い切ってしまう（点検 2026-09-29・19）
        var onBooth = (await _client.GetItemJsonAsync(toId, cancellationToken)).Status switch
        {
            BoothFetchStatus.Success => ItemIdTargetStatus.Found,
            BoothFetchStatus.NotFound => ItemIdTargetStatus.NotFound,
            _ => ItemIdTargetStatus.Unknown,
        };

        return ItemIdChange.Plan(source, target: null, toId, onBooth);
    }

    /// <summary>
    /// 商品まるごとを別のIDへ移す。
    ///
    /// **IDは書き換えない。**新しいIDの商品へ中身を移し、元の商品を消す。
    /// 商品IDはファイル名にもフォルダ名にもなっていて、他の商品からも名前で
    /// 参照されているので、IDだけ書き換えると参照が全部迷子になる。
    ///
    /// 移した先が手元に無ければ、BOOTHから取って作る。取れなければ
    /// **中身が空の商品として作る**——買って手元にあるものを、
    /// 移し先が非公開だという理由で消してはいけない。
    /// </summary>
    public async Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return ItemIdChangeOutcome.SameId;
        }

        if (await _store.Items.LoadAsync(fromId, cancellationToken) is not { } before)
        {
            return ItemIdChangeOutcome.SourceMissing;
        }

        // 自分で足した画像のファイルは、何かを書く前に移した先のフォルダへ写す。
        // 元の商品を消すと画像のフォルダごと消えるので、写せなかったら何も書かずに元を残す。
        // BOOTH から取るより前に行うのは、写せなかったときに取ってきた空の商品を残さないため
        if (CopyUserImages(fromId, toId, before.Local.UserImages) is not { } copied)
        {
            return ItemIdChangeOutcome.ImagesNotMoved;
        }

        var prepared = await _store.Items.LoadAsync(toId, cancellationToken);
        var fetchStatus = Booth.BoothFetchStatus.Success;
        if (prepared is null)
        {
            (prepared, fetchStatus) = await FetchNewItemAsync(toId, cancellationToken);
        }

        // BOOTHが「無い」と答えたIDは、未確定の「見つからないIDのまま登録」と同じ状態で作る（⑦で確かめ直し、公開されたら情報を取る）。
        // 一時的に届かなかっただけのIDに販売終了の印を付けると、統計や検索で販売終了として数えてしまうので空の商品のまま。
        // 仮IDは BOOTH に存在しないので⑦に乗せない
        // 一時的に届かなかったIDは、販売終了の印を付けず、予定日を今にして次の⑦で取りに行く（ユーザ判断 2026-09-29）。
        // 予定日を持たない空の商品は⑦に乗らず、後から情報を取りに行かなかった
        ItemRecord NewItem() => LocalItemId.IsLocal(toId)
            ? EmptyItem(toId)
            : fetchStatus switch
            {
                Booth.BoothFetchStatus.NotFound => UnpublishedItem(toId, null),
                Booth.BoothFetchStatus.TemporaryFailure => EmptyItem(toId) is var empty
                    ? empty with { Local = empty.Local with { NextFetchDueAt = DateTimeOffset.Now } }
                    : empty,
                _ => EmptyItem(toId),
            };

        var skipped = skippedPurchases ?? new HashSet<int>();
        var refused = ItemIdChangeOutcome.TargetUnavailable;

        // **移す元の錠を持ったまま、今の値を読み、移し、消す**（ItemRepository.MoveAwayAsync）。
        // BOOTH から取って作ると数秒かかり、その間に取り込みが元の商品へファイルを足したり、人がメモを書いたり画像を足したりする。
        // 前は錠の外で読み直してから消したので、読み直してから消すまでに書かれた分が、元の商品と一緒に消えていた
        var moved = await _store.Items.MoveAwayAsync(
            fromId,
            async source =>
            {
                // 取っている間に足された画像の分（写し済みの物は飛ばされる）
                if (CopyUserImages(fromId, toId, source.Local.UserImages) is not { } late)
                {
                    refused = ItemIdChangeOutcome.ImagesNotMoved;
                    return false;
                }

                copied.AddRange(late);

                // **合わせるのは錠の中で読み直した今の値**（技術的負債 1-5）。
                // 移すのは手元の記録の全部なので、全項目の持ち主として書く。
                // 購入記録は移した先のvariation一覧で照合し直される（保存側）。指していない記録は支出にそのまま数える
                LocalBlock? Merge(LocalBlock current) => ItemIdChange.Merge(source.Local, current, skipped);

                // 手元にも BOOTH にも無ければ新しく作る。在るかは移す先の錠の中で見る——取れなかった間に、
                // 取り込みが同じIDの商品を作っていることがある（L13 と同じ形）。在ればそちらへ重ねる。
                // 取って作った物が取った後で消されていれば、作り直さずに断る（元は消さずに残す）
                return prepared is null
                    ? await _store.Items.CreateOrChangeLocalAsync(
                        toId, NewItem, Merge, Enum.GetValues<LocalField>(), cancellationToken)
                    : await _store.Items.ChangeLocalAsync(toId, Merge, Enum.GetValues<LocalField>(), cancellationToken);
            },
            cancellationToken);

        if (moved is not true)
        {
            // 元は残っている（または初めから無い）。写した画像は片付ける
            DeleteQuietly(copied);
            return moved is null ? ItemIdChangeOutcome.SourceMissing : refused;
        }

        // 元の商品は移し終えた後で消えている。ここまでで落ちても、中身は移した先に残っている
        // （両方に出るのは二重に見えるが、消えてしまうよりはるかによい）

        // 外した印はファイルの行と一緒に移した先へ移っている（ItemIdChange.Merge）。
        // 以前は別の detached.json をここで読み替えていた

        await MoveModificationsAsync(fromId, toId, cancellationToken);
        await MoveReferencesAsync(fromId, toId, cancellationToken);

        return ItemIdChangeOutcome.Moved;
    }

    /// <summary>
    /// 自分で足した画像のファイルを、移した先の画像のフォルダへ写す（元は元の商品と一緒に消える）。
    ///
    /// **名前がぶつかったら写さない。**保存名は中身のハッシュなので、同じ名前なら同じ絵で、
    /// 移した先の記録と1枚にまとまる（<see cref="ItemIdChange.Merge"/>）。
    /// 元のファイルが既に無い記録はそのまま移す（元の商品でも絵は出ていなかったので、失う物は無い）。
    /// </summary>
    /// <returns>新しく写したファイル。写せない物があれば、写した分を片付けて null。</returns>
    private List<string>? CopyUserImages(string fromId, string toId, IReadOnlyList<UserImage> images)
    {
        var fromDir = _store.Paths.ItemImagesDir(fromId);
        var toDir = _store.Paths.ItemImagesDir(toId);
        var copied = new List<string>();

        try
        {
            foreach (var image in images)
            {
                var name = Path.GetFileName(image.FileName);
                var from = Path.Combine(fromDir, name);
                var to = Path.Combine(toDir, name);
                if (!File.Exists(from) || File.Exists(to))
                {
                    continue;
                }

                Directory.CreateDirectory(toDir);
                File.Copy(from, to);
                copied.Add(to);
            }

            return copied;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("IDの変更で自分で足した画像を写す", exception);
            DeleteQuietly(copied);
            return null;
        }
    }

    private static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 片付けられなかった写しは、どの記録からも指されない1枚が残るだけ。移し替えの結果は変わらない
                AppLog.Error("IDの変更で写した画像を片付ける", exception);
            }
        }
    }

    /// <summary>
    /// 商品IDを指している他の記録を、移した先へ付け替える（ユーザ判断 2026-09-21・L16）。
    ///
    /// 改変だけを読み替えていたので、**登録簿・他の商品の対応アバター・足跡・要確認が
    /// 消えたIDを指したまま**になっていた（アバターの一覧から消える、持っていない扱いになる、
    /// 他の商品の対応アバターが迷子になる）。
    /// </summary>
    private async Task MoveReferencesAsync(string fromId, string toId, CancellationToken cancellationToken)
    {
        // 登録簿（アバターそのもの・素体グループが指す商品）
        await _store.Avatars.TryUpdateAsync(
            registry =>
            {
                var entries = registry.Entries
                    .Select(entry => entry.ItemId == fromId ? entry with { ItemId = toId } : entry)
                    .ToList();

                var groups = registry.BaseGroups
                    .Select(group => group.ItemId == fromId ? group with { ItemId = toId } : group)
                    .ToList();

                return registry.Entries.Any(entry => entry.ItemId == fromId)
                    || registry.BaseGroups.Any(group => group.ItemId == fromId)
                        ? new AvatarRegistry
                        {
                            DetectedAt = registry.DetectedAt,
                            Entries = entries,
                            BaseGroups = groups,
                        }
                        : null;
            },
            cancellationToken);

        // 足跡と要確認
        await _store.Recent.TryUpdateAsync(
            log => log.Entries.Any(entry => entry.ItemId == fromId)
                ? new Services.RecentLog
                {
                    Entries = [.. log.Entries.Select(entry => entry.ItemId == fromId ? entry with { ItemId = toId } : entry)],
                }
                : null,
            cancellationToken);

        await _store.Notifications.TryUpdateAsync(
            records =>
            {
                var touched = false;
                for (var index = 0; index < records.Count; index++)
                {
                    if (records[index].ItemId == fromId)
                    {
                        records[index] = records[index] with { ItemId = toId };
                        touched = true;
                    }
                }

                return touched ? records : null;
            },
            cancellationToken);

        // 他の商品が対応アバターとして指している分
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        foreach (var item in loaded.Items.Where(item =>
            item.Local.Avatars.Any(link => link.AvatarItemId == fromId)))
        {
            await _store.Items.ChangeLocalAsync(
                item.Id,
                current => current.Avatars.Any(link => link.AvatarItemId == fromId)
                    ? current with
                    {
                        Avatars = [.. current.Avatars
                            .Select(link => link.AvatarItemId == fromId ? link with { AvatarItemId = toId } : link)],
                    }
                    : null,
                LocalOwners.SupportedAvatars,
                cancellationToken);
        }
    }

    /// <summary>
    /// 改変の記録を移した先のIDへ読み替える。
    ///
    /// **改変は「そのとき何を使ったか」という過去の事実。**IDを移したからといって
    /// 使った事実は変わらないので、指す先だけを付け替える。
    /// 読み替えないと、消えたIDを指したまま「手元に無い」と出続ける。
    ///
    /// アバターとして指されている場合も同じ（改変はアバター1体に属する）。
    /// </summary>
    private async Task MoveModificationsAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken)
    {
        var loaded = await _store.Modifications.LoadAllAsync(cancellationToken);

        foreach (var found in loaded.Modifications)
        {
            if (!UsesItem(found, fromId))
            {
                continue;
            }

            // 全件を読んでから1件ずつ書くまでの間に、改変の画面が名前やメモを書き、Unity から構成物が届く。
            // 読んだ写しで丸ごと書くとそれが消えるので、錠の中で読み直した今の値に当てる
            await _store.Modifications.UpdateAsync(
                found.Id,
                record =>
                {
                    if (!UsesItem(record, fromId))
                    {
                        return record;
                    }

                    return record with
                    {
                        AvatarItemId = string.Equals(record.AvatarItemId, fromId, StringComparison.Ordinal)
                            ? toId
                            : record.AvatarItemId,
                        Members = [.. record.Members
                            .Select(member => string.Equals(member.ItemId, fromId, StringComparison.Ordinal)
                                ? member with { ItemId = toId }
                                : member)],

                        // 触った跡は残す。あとで「なぜ変わったか」を辿れるようにする
                        UpdatedAt = DateTimeOffset.Now,
                    };
                },
                cancellationToken);
        }
    }

    private static bool UsesItem(ModificationRecord record, string itemId)
        => string.Equals(record.AvatarItemId, itemId, StringComparison.Ordinal)
            || record.Members.Any(member => string.Equals(member.ItemId, itemId, StringComparison.Ordinal));

    /// <summary>
    /// 自分で足す画像を1枚入れる。
    ///
    /// BOOTHの画像と同じ圧縮を通してライブラリへ保存する。
    /// **同じ絵を2回入れても1枚**にまとまる（保存名が中身のハッシュなので）。
    /// </summary>
    /// <returns>保存したファイル名。画像として読めなければ null。</returns>
    public async Task<string?> AddUserImageAsync(
        string itemId,
        byte[] bytes,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var fileName = await _images.SaveUserImageAsync(itemId, bytes, cancellationToken);
        if (fileName is null)
        {
            return null;
        }

        // 既に同じ絵が入っていれば、記録は増やさずファイルだけ入れ替わる。
        // 一覧は書く直前の値に足す（絵を保存する間に別の操作が並べ替えていることがある）
        await _store.Items.ChangeLocalAsync(
            itemId,
            current => current.UserImages.Any(image => string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                ? null
                : current with
                {
                    UserImages = [.. current.UserImages, new UserImage
                    {
                        FileName = fileName,
                        AddedAt = DateTimeOffset.Now,
                        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
                    }],
                },
            LocalOwners.UserImages,
            cancellationToken);

        return fileName;
    }

    /// <summary>
    /// 自分で足した画像を消す。**ファイルごと消える。**
    ///
    /// サムネイルに指名していたなら、指名も外す。
    /// BOOTHの画像が消えたときは指名を残す（取り直せば戻る）が、
    /// **自分で消したものは戻らない**ので、指名を残すと永久に空振りする。
    /// </summary>
    public async Task<bool> RemoveUserImageAsync(
        string itemId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.Items.LoadAsync(itemId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        await _images.DeleteUserImageAsync(itemId, fileName, cancellationToken);

        await _store.Items.ChangeLocalAsync(
            itemId,
            current => current with
            {
                UserImages = [.. current.UserImages
                    .Where(image => !string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase))],
                ThumbnailImage = string.Equals(current.ThumbnailImage, fileName, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : current.ThumbnailImage,

                // 役割の行も落とす。残すと、同じ絵を入れ直したときに外したはずの役割が復活する
                ImageRoles = current.ImageRoles
                    .Where(pair => !string.Equals(pair.Key, fileName, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
            },
            LocalOwners.RemoveUserImage,
            cancellationToken);

        return true;
    }

    /// <summary>
    /// 自分で足した画像の並びを1つ動かす。
    ///
    /// **動かせるのは自分の画像の中だけ。**BOOTHの並びは観測した事実なので触らない
    /// （ギャラリーは「観測 → 自分の分 → 消えたもの」の順で出る）。
    /// </summary>
    /// <param name="delta">-1 で前へ、+1 で後ろへ。</param>
    public async Task<bool> MoveUserImageAsync(
        string itemId,
        string fileName,
        int delta,
        CancellationToken cancellationToken = default)
    {
        // 並べ替えは書く直前の一覧に当てる（続けて押したとき、前の結果を古い写しで消さない）
        return await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var images = current.UserImages.ToList();
                var from = images.FindIndex(image =>
                    string.Equals(image.FileName, fileName, StringComparison.OrdinalIgnoreCase));

                var to = from + delta;
                if (from < 0 || to < 0 || to >= images.Count)
                {
                    return null;
                }

                (images[from], images[to]) = (images[to], images[from]);
                return current with { UserImages = images };
            },
            LocalOwners.UserImages,
            cancellationToken);
    }

    /// <summary>
    /// 画像に役割を付ける。
    ///
    /// **出どころから決まる値と同じなら記録しない。**BOOTHの画像に「BOOTH」を、
    /// 自分で足した画像に「その他」を付けても、それは既定と同じなので書かない。
    /// 全画像分を書き出すと、観測しただけのものまで人が決めたように見える。
    /// </summary>
    public async Task<bool> SetImageRoleAsync(
        string itemId,
        string fileName,
        ImageRole role,
        bool isUserAdded,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var name = Path.GetFileName(fileName);
        var natural = isUserAdded ? ImageRole.Other : ImageRole.Booth;

        return await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var roles = current.ImageRoles
                    .Where(pair => !string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);

                if (role != natural)
                {
                    roles[name] = role;
                }

                return current with { ImageRoles = roles };
            },
            LocalOwners.ImageRoles,
            cancellationToken);
    }

    /// <summary>
    /// サムネイルに使う1枚を指名する。**BOOTHの画像も指名できる。**
    /// null を渡すと指名を外し、並びの1枚目に戻る。
    /// </summary>
    public async Task<bool> PinThumbnailAsync(
        string itemId,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        var pinned = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);

        return await _store.Items.ChangeLocalAsync(
            itemId,
            current => current with { ThumbnailImage = pinned },
            LocalOwners.ThumbnailImage,
            cancellationToken);
    }

    /// <summary>
    /// BOOTHから取れなかったIDのために、中身が空の商品を作る。
    /// <c>Booth.FetchedAt</c> は null のまま——観測していないので、それが正しい。
    /// </summary>
    private static ItemRecord EmptyItem(string itemId) => new()
    {
        Id = itemId,
        Booth = new BoothBlock(),
        Local = new LocalBlock(),
    };

    /// <summary>
    /// BOOTHで見つからなかった本物のIDのために作る商品。**販売終了の商品と同じ状態**にして⑦に乗せる：
    /// <c>Booth</c> は空（観測していない）、<c>IsDelisted</c>、見つからない回数は非公開と確定する回数、予定日は確かめ直しの間隔。
    /// 回数を1で始めると、次に見つからなかったとき「回数が足りない」として印が外れる。
    /// 未確定の「見つからないIDのまま登録」と、商品ページの「IDを変更」で見つからないIDへ移したときの両方がここを通る
    /// （IDの変更の側は空の商品を作るだけで予定日を持たず、公開されても情報を取れなかった）。
    /// </summary>
    private ItemRecord UnpublishedItem(string itemId, string? displayName)
    {
        var threshold = Math.Max(1, _settings.NotFoundThreshold);
        var name = displayName?.Trim() ?? "";
        return new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock(),
            Local = new LocalBlock
            {
                DisplayName = name.Length > 0 ? name : null,
                NotifyOnUpdate = _settings.NotifyOnUpdateByDefault,
                ConsecutiveNotFoundCount = threshold,
                IsDelisted = true,
                NextFetchDueAt = NextDue(itemId, threshold),
            },
        };
    }

    /// <summary>1件だけを登録する（試験と道具の書きやすさのため）。</summary>
    public Task<string?> RegisterLocalItemAsync(
        string hash,
        string displayName,
        CancellationToken cancellationToken = default)
        => RegisterLocalItemAsync([hash], displayName, cancellationToken);

    public async Task<string?> RegisterLocalItemAsync(
        IReadOnlyList<string> hashes,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        // 記録の読み書きは画面のスレッドの外で走る（窓口の印）。前は最初の await より前に同期で読んでいて、
        // 未確定が8万件（37.7MB）あると、1件登録するたびに 330〜540ms 画面が止まっていた（2026-09-30 に測った）。
        //
        // **未確定の錠を持ったまま、探す → 商品を保存 → 一覧から外す、まで進める**（読むのは1回）。
        // 前は探すための読みと、外すための錠の中の読み直しで2回読んでいた。順番は前と同じで、商品を保存してから一覧を書く
        // （先に外して商品の保存に失敗すると、ファイルの記録ごと失う）。BOOTH へ行かない道なので、錠を持つのは保存1回ぶんの間だけ
        string? itemId = null;
        await _store.Unresolved.TryUpdateAwaitingAsync(
            async current =>
            {
                // 渡された順に並べる（仮IDは先頭のファイルから決まり、画面が押す前に見せたIDと合わせる）。
                // 未確定から既に消えたファイルは飛ばす（同じ中身が先に片付いた・別の画面で登録した）
                var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var hash in hashes)
                {
                    order.TryAdd(hash, order.Count);
                }

                var targets = current
                    .Where(file => order.ContainsKey(file.Hash))
                    .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(file => order[file.Hash])
                    .ToList();
                if (targets.Count == 0)
                {
                    return null;
                }

                var id = LocalItemId.For(targets[0].Hash);
                var records = targets.Select(FromUnresolved).ToList();

                // 同じファイルを2回登録しようとした場合（未確定に二重に載っていた等）。
                // 仮IDはハッシュから決まるので、同じ商品に行き着く。
                // **在るかは商品の錠の中で見る。**前は錠の外で見て、無ければ丸ごと保存していたので、見てから書くまでの間に
                // 人の保存が同じ商品を作ると、人が入れたメモ・購入記録を消していた（2026-10-02。CreateWhileSomeoneSavesTests）
                var created = false;
                await _store.Items.CreateOrChangeLocalAsync(
                    id,
                    () =>
                    {
                        created = true;
                        return new ItemRecord
                        {
                            Id = id,
                            Booth = new BoothBlock(),
                            Local = new LocalBlock
                            {
                                DisplayName = displayName.Trim(),
                                LocalFiles = records,
                            },
                        };
                    },
                    current => created
                        ? current
                        : current with
                        {
                            // 既にある商品の名前は残す（2026-10-05・file-lifecycle.md「気になった所」11）。仮IDはハッシュから決まるので、
                            // 外した後に同じファイルを登録し直すと既にある商品へ行き着く。欄の下書きはファイル名なので、
                            // 前は人が付けて直してきた名前を黙ってファイル名で上書きしていた。名前が無い（手で消した）時だけ入れる
                            DisplayName = string.IsNullOrWhiteSpace(current.DisplayName) ? displayName.Trim() : current.DisplayName,
                            LocalFiles = LocalFileMerger.MergeByHand(current.LocalFiles, records),
                        },
                    [LocalField.DisplayName, LocalField.LocalFiles],
                    cancellationToken);

                itemId = id;
                var registered = targets.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                current.RemoveAll(file => registered.Contains(file.Hash));
                return current;
            },
            cancellationToken);

        return itemId;
    }

    /// <summary>
    /// ファイルがどの種類のものかを付け直す（#40）。
    ///
    /// **商品の錠の中で、今の一覧の種類だけを書き換える。**LocalFiles は取り込みも書くので、
    /// 画面が開いた時点の写しを渡すと、その間に足されたファイルやパスが消える。
    /// 前は錠の外で読み直して一覧ごと書いていて、読んでから書くまでの間に取り込みが足したファイルが消えていた
    /// （2026-10-04 に試験で再現）。変え方を渡して錠の中で当てれば、その一瞬も無くなる。
    /// </summary>
    public async Task<bool> SetFileVariationsAsync(
        string itemId,
        IReadOnlyDictionary<string, long?> variationByHash,
        CancellationToken cancellationToken = default)
    {
        // ハッシュは大文字で持っているが、呼び出し側の表記に左右されないようにする
        var wanted = new Dictionary<string, long?>(variationByHash, StringComparer.OrdinalIgnoreCase);

        // 書かなかったのが「商品が無い」か「変える物が無い」かを分ける（後者は成功として返す）
        var found = false;
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            local =>
            {
                found = true;
                var changed = false;
                var files = local.LocalFiles
                    .Select(file =>
                    {
                        if (!wanted.TryGetValue(file.Hash, out var variationId) || file.VariationId == variationId)
                        {
                            return file;
                        }

                        changed = true;
                        return file with { VariationId = variationId };
                    })
                    .ToList();

                return changed ? local with { LocalFiles = files } : null;
            },
            LocalOwners.FileVariations,
            cancellationToken);

        return written || found;
    }

    /// <summary>
    /// 使おうとして見た在る・無い（商品ページ・開く・Unityへ送る）を、ファイルの「見つからなくなった日時」に当てる（ユーザ判断 2026-10-04）。
    ///
    /// 前は商品ページだけがその場でディスクを見て「見つかりません」を出し、記録は書かなかったので、
    /// 同じ商品がカードの印・検索の条件・統計には出ず、画面どうしで食い違っていた。
    /// **書くのは日時だけ**で、場所・種類・メモなど人が入れた値には触れない。商品の錠の中で今の一覧に当て、
    /// 見たときと場所が変わったファイルには当てない（<see cref="FileMissingMarks.Apply"/>）。変わる物が無ければ書かない。
    /// </summary>
    public async Task<bool> NoteFilePresenceAsync(
        string itemId,
        IReadOnlyCollection<FileSighting> sightings,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        return await _store.Items.ChangeLocalAsync(
            itemId,
            local => FileMissingMarks.Apply(local.LocalFiles, sightings, now) is { } files
                ? local with { LocalFiles = files }
                : null,
            LocalOwners.FilePresence,
            cancellationToken);
    }

    /// <summary>
    /// 古い版の記録を片付ける（商品ページの「古い版の記録を片付ける」・2026-10-05・点検の8）。
    ///
    /// **外した印（<see cref="LocalFileRecord.Detached"/>）ではなく、行ごと消す。**外した印は「手掛かりが同じ商品へ戻すのを止める」ための物で、
    /// 古い版はどこにも無いので止める相手がいない。印にすると灰色の行が残り、片付けたことにならない。
    /// 古い版をまたどこかに置けば、次の取り込みで手掛かりから戻り得る（設定の「外した記録を消す」と同じ）。
    /// 錠の中の今の値で、まだ古い版のときだけ消す（見てから押すまでに、取り込みが古い版を別の所で見つけて場所を足していれば消さない）。
    /// </summary>
    public async Task<bool> ForgetOldVersionAsync(string itemId, string hash, CancellationToken cancellationToken = default)
        => await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                var files = current.LocalFiles
                    .Where(file => !(file.IsOldVersion && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return files.Count == current.LocalFiles.Count ? null : current with { LocalFiles = files };
            },
            [LocalField.LocalFiles],
            cancellationToken);

    /// <summary>
    /// ファイルをこの商品から外し、未確定へ戻す。
    ///
    /// **IDは書き換えない。**商品IDはファイル名にもフォルダ名にもなっていて、
    /// 他の商品からも名前で参照されているので、書き換えると参照が全部迷子になる。
    /// やりたいことは「このファイルの行き先が違う」なので、ファイルの側を動かす。
    ///
    /// **行は消さずに外した印（<see cref="LocalFileRecord.Detached"/>）を付ける**（ユーザ判断 2026-09-12）。
    /// 手掛かりでこの商品に紐付いていたこと自体は確かなので、消すと何を外したのかが見えなくなる。
    /// 印は、手掛かりから商品IDが決まるファイルが**次の取り込みで同じ商品へ戻ってしまう**のも止める
    /// （外す操作が要るのはまさに手掛かりが間違っている場合）。以前は別の detached.json に持っていた。
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

        var target = item.Local.LocalFiles.FirstOrDefault(file =>
            !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return DetachOutcome.Missing;
        }

        // 実体が残っているものだけ未確定へ戻す。
        // 既に消えているファイルを並べても、紐付け直す相手がいない
        var alive = target.Paths.Where(File.Exists).ToList();
        if (alive.Count > 0)
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

            // ダウンロード元の記録（Zone.Identifier）は、未確定の記録を作るここで読む（ユーザ判断 2026-09-30）。
            // 商品の記録は持っておらず、未確定の画面は開くたびには読み直さない（UnresolvedOrigin）。
            // 入れないと、展開した中身は次に取り込み直すまで元zipの束に入らず、フォルダで束ねられる。
            // 人の操作1回につき1ファイルで、読めなければ無いものとして返る
            var zone = BoothZipInspector.ZoneIdentifierReader.Read(alive[0]);

            var entry = new UnresolvedFile
            {
                Hash = target.Hash,
                Paths = alive,
                SizeBytes = target.SizeBytes,
                ModifiedAtUtc = modified,
                FirstSeenAt = DateTimeOffset.Now,
                Contents = target.Contents,
                ZoneHostUrl = zone.HostUrl,
                ZoneReferrerUrl = zone.ReferrerUrl,

                // 開けなかった印は商品の記録から引き継ぐ。未確定の画面は開き直して確かめないので、
                // ここで落とすと次の取り込みまで普通の未確定に見える
                ArchiveBroken = target.ArchiveBroken,
            };

            // 錠の中で今の一覧に足す（技術的負債 1-2）
            await _store.Unresolved.UpdateAsync(
                current =>
                {
                    if (!current.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        current.Add(entry);
                    }

                    return current;
                },
                cancellationToken);
        }

        // 在るかを見るのは落ちたネットワークドライブなら数秒かかり、未確定の錠も待つ。
        // その間に取り込みが同じ商品へファイルやフォルダを足すことがあるので、
        // 外す印は書く直前の今の一覧に付け、空になったかも今の値で見る
        var becameEmpty = false;
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current =>
            {
                if (!current.LocalFiles.Any(file =>
                        !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                var files = current.LocalFiles
                    .Select(file => !file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                        ? file with { Detached = true }
                        : file)
                    .ToList();

                becameEmpty = !HoldsAnything(current with { LocalFiles = files });
                return current with { LocalFiles = files };
            },
            LocalOwners.Import,
            cancellationToken);

        if (!written)
        {
            return DetachOutcome.Missing;
        }

        // 商品ごと消すと、外した印も一緒に消える（次の取り込みで手掛かりが指せば、また作られる）。
        // **空かは消す錠の中でもう一度見る。**印を書いてから消すまでの間に取り込みがファイルを足すと、
        // 前は足された分ごと消していた。足されていれば消さずに、外しただけとして返す
        if (becameEmpty && deleteItemWhenEmpty)
        {
            return await _store.Items.DeleteIfAsync(itemId, item => !HoldsAnything(item.Local), cancellationToken)
                ? DetachOutcome.ItemDeleted
                : DetachOutcome.Detached;
        }

        return becameEmpty ? DetachOutcome.ItemNowEmpty : DetachOutcome.Detached;
    }

    /// <summary>
    /// 手元に何か持っているか（所持の答え <see cref="LocalBlock.IsOwned"/>。外したファイル・上書きで残った古い版は数えない。フォルダ登録は数える）。
    /// 古い版だけが残る外し方も「空になった」として、画面が最後のファイルのときと同じく残し方を聞けるようにする（⑤-B）。
    /// </summary>
    private static bool HoldsAnything(LocalBlock local) => local.IsOwned;

    /// <summary>
    /// 外したファイルをこの商品に戻す（商品ページの灰色の行の「この商品に戻す」・ユーザ判断 2026-09-12）。
    /// 未確定からは取り除く。**外した後で別の商品へ紐付け直していたら戻さない**——同じファイルが
    /// 2つの商品の持ち物になり、容量も二重に数える。
    /// </summary>
    public async Task<ReattachOutcome> ReattachFileAsync(
        string itemId,
        string hash,
        CancellationToken cancellationToken = default)
    {
        var item = await _store.Items.LoadAsync(itemId, cancellationToken);
        var target = item?.Local.LocalFiles.FirstOrDefault(file =>
            file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (item is null || target is null)
        {
            return ReattachOutcome.Missing;
        }

        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        if (loaded.Items.Any(other => other.Id != itemId
                && other.Local.AttachedFiles.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))))
        {
            return ReattachOutcome.OwnedElsewhere;
        }

        // 全件を読んで確かめる間に取り込みが一覧を書き換えることがあるので、書く直前の今の一覧で戻す
        var written = await _store.Items.ChangeLocalAsync(
            itemId,
            current => current.LocalFiles.Any(file =>
                    file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase))
                ? current with
                {
                    LocalFiles = [.. current.LocalFiles
                        .Select(file => file.Detached && string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)
                            ? file with { Detached = false }
                            : file)],
                }
                : null,
            LocalOwners.Import,
            cancellationToken);

        if (!written)
        {
            return ReattachOutcome.Missing;
        }

        await RemoveUnresolvedAsync(hash, cancellationToken);

        return ReattachOutcome.Reattached;
    }

    /// <summary>
    /// ファイルを管理対象から外す。未確定一覧からも取り除く。1個でも数千個でも1回で書く。
    ///
    /// 前は1個ごとに2つの記録を丸ごと読み書きしていて、フォルダごと外すと 500 個で約 16 秒・5,000 個で約 2分34秒かかった
    /// （件数の2乗で伸び、1個ごとにディスクへ書き切る分も重なる。`docs/research/large-files-2026-09-30.md`「除外の記録を測った」）。
    /// どちらの記録も錠の中で今の値に当てるので、読んでから書くまでの間に取り込みが書いた分は消えない。
    ///
    /// **除外の記録に足してから、未確定から外す。**逆にすると、未確定から消えた後で除外に書けなかったとき、
    /// ファイルがどちらの記録にも無くなる（次の取り込みまで見えない）。この順なら、途中で落ちても未確定に残るだけで、もう一度押せば済む。
    /// </summary>
    public async Task<IReadOnlyList<string>> ExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return [];
        }

        // 足した物を覚えて返す。戻すときに前からの除外まで消さないため（2026-10-05・file-lifecycle.md「気になった所」18）
        List<string> added = [];
        await _store.Excluded.TryUpdateAsync(
            excluded =>
            {
                // 既に在る中身は足さない（外したときの日時と理由は、先に外したときの物を残す）。同じ一覧の中の重なりも1件にする
                var known = excluded.Select(entry => entry.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var now = DateTimeOffset.Now;

                // 錠の中の変え方は書き込みに失敗すると呼び直されることがあるので、毎回空から数える
                added = [];
                foreach (var file in files)
                {
                    if (known.Add(file.Hash))
                    {
                        excluded.Add(new ExcludedEntry
                        {
                            Hash = file.Hash,
                            Paths = file.Paths,
                            ExcludedAt = now,
                            Reason = reason,
                        });
                        added.Add(file.Hash);
                    }
                }

                return added.Count > 0 ? excluded : null;
            },
            cancellationToken);

        var hashes = files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _store.Unresolved.TryUpdateAsync(
            current => current.RemoveAll(file => hashes.Contains(file.Hash)) > 0 ? current : null,
            cancellationToken);

        return added;
    }

    /// <summary>
    /// 未確定の画面で外した直後に戻す（ユーザ判断 2026-09-17：戻す場所が設定の「隠したもの」だけだった）。
    /// 設定の「解除」はファイルから未確定の記録を作り直す（候補は控えの手掛かりだけ）。ここでは外す前の未確定の記録（候補・元zipの記録を含む）をそのまま戻す。
    /// </summary>
    public async Task UndoExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        IReadOnlyCollection<string> excludedHashes,
        CancellationToken cancellationToken = default)
    {
        // 消すのは今回足した除外だけ（2026-10-05・file-lifecycle.md「気になった所」18）。除外は既にあるハッシュを足さないので、
        // 一覧のハッシュで全部消すと、前から除外していた物の記録（日時・理由）まで消えていた
        var hashes = excludedHashes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await _store.Excluded.TryUpdateAsync(
            excluded => excluded.RemoveAll(entry => hashes.Contains(entry.Hash)) > 0 ? excluded : null,
            cancellationToken);

        await _store.Unresolved.UpdateAsync(
            current =>
            {
                foreach (var file in files)
                {
                    if (!current.Any(entry => string.Equals(entry.Hash, file.Hash, StringComparison.OrdinalIgnoreCase)))
                    {
                        current.Add(file);
                    }
                }

                return current;
            },
            cancellationToken);
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

        // 取り込みの側と同じ、起動をまたいで変わらないずらし方（RefreshJitter）。前は string.GetHashCode で、
        // 起動し直すたびに同じ商品でも違う日になっていた（file-lifecycle.md 気になった所19の残り）
        return DateTimeOffset.Now.AddDays(days + RefreshJitter.Days(itemId, _settings.RefreshJitterDays));
    }

}

/// <summary>画像の残りに、後に続く問い合わせ（ショップのアイコン）の数を足して流す。</summary>
internal sealed class ShiftedProgress(IProgress<int> inner, int offset) : IProgress<int>
{
    public void Report(int value) => inner.Report(value + offset);
}

public enum RefreshOutcome
{
    Updated,

    /// <summary>BOOTHに無い商品として登録したもの。問い合わせていない。</summary>
    NotOnBooth,
    NotFound,
    Delisted,

    /// <summary>BOOTH が応答したが一時的に取れなかった（429 など、5xx 以外）。待てば取れる。</summary>
    TemporaryFailure,

    /// <summary>
    /// BOOTH が 5xx（サーバの不調）を返した。待てば取れる。
    /// <see cref="TemporaryFailure"/> と分けるのは、⑦が続いたら打ち切りに数えるため（<see cref="Booth.BoothOutageWatch"/>。
    /// 429 はこちらの出し過ぎなので数えない。ユーザ判断 2026-09-29）
    /// </summary>
    ServerError,

    /// <summary>BOOTH から応答が来なかった（接続できない・タイムアウト）。ネットにつながっていないことがある。</summary>
    Unreachable,

    /// <summary>
    /// BOOTH は応答したが読めなかった（JSON でない・形が変わった）。待てば直る一時失敗とは言い分ける
    /// （「次回に再試行します」と言うと、待っても直らない失敗を待てば直るように読ませる）。予定日は普段の間隔で進める。
    /// </summary>
    Unreadable,
    Missing,
}

/// <summary>外したファイルを商品に戻した結果。</summary>
public enum ReattachOutcome
{
    Reattached,

    /// <summary>外した後で別の商品へ紐付けてあった。戻していない。</summary>
    OwnedElsewhere,

    /// <summary>商品か、外したファイルが見つからなかった。</summary>
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
