using Chmonos.Core.Booth;
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

    Task<FolderRegistration> RegisterFolderAsync(string itemId, string folderPath, CancellationToken cancellationToken = default, IProgress<int>? requestsLeft = null);

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
    /// <param name="record">
    /// 指紋をやりかけの記録へ書く（元を読んだ直後と、移す先へ書く直前の2回）。偽なら何も書かずに
    /// <see cref="ItemIdChangeOutcome.NotRecorded"/>。null なら記録しない。
    /// </param>
    Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        CancellationToken cancellationToken = default,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null);

    /// <summary>
    /// 途中で止まった IDの変更の続きを済ませる（次の起動で、やりかけの記録が残っていたとき）。BOOTHへは問い合わせない。
    /// 記録した指紋（<paramref name="recorded"/>）で、どこまで済んだか・記録した時と同じ物かを見分ける。何度当てても同じ結果になる。
    /// </summary>
    /// <param name="recorded">記録した指紋。null ならまだ何も書いていない。</param>
    /// <param name="record">合わせ直すときに指紋を書き直す（<see cref="ChangeItemIdAsync"/> と同じ）。</param>
    Task<ItemIdChangeOutcome> ResumeItemIdChangeAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        ItemIdChangeFingerprints? recorded = null,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null,
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
        _userImages = new UserImageEditor(store, images);
        _changeNotes = new BoothChangeNotes(store);
        _remainingImages = new RemainingImageRequests(images);
        _localFiles = new LocalFileEditor(store);
        _idChanger = new ItemIdChanger(store, client, this);
    }

    private readonly RemainingImageRequests _remainingImages;

    private readonly LocalFileEditor _localFiles;

    private readonly ItemIdChanger _idChanger;

    private readonly BoothChangeNotes _changeNotes;

    private readonly UserImageEditor _userImages;

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

        // **商品ページが一時的な不調で取れなかったら、JSON が取れなかったときと同じく何も書かずに不調として返す**
        // （外部の点検 2026-10-07）。前は見出しを空にしたまま「取れた」として保存し、予定も普段どおり進めていた。
        // ⑦はそれを成功と数えるので、商品ページだけの不調が続いても打ち切らず、見出しも消していた
        if (htmlResult.Status == BoothFetchStatus.TemporaryFailure)
        {
            return htmlResult.IsUnreachable ? RefreshOutcome.Unreachable
                : htmlResult.IsServerError ? RefreshOutcome.ServerError
                : RefreshOutcome.TemporaryFailure;
        }

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
            // 名乗るのは予定日だけ（点検22）。取得の記録4欄を名乗ると、問い合わせを待つ間にほかの取得が書いた
            // 取得日時・404 の回数・販売終了の印を、問い合わせの前に読んだ古い値へ戻した
            await _store.Items.SaveLocalAsync(
                itemId,
                existing.Local with { NextFetchDueAt = NextDue(itemId) },
                [LocalField.NextFetchDueAt],
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

        await _changeNotes.NoteBackOnBoothAsync(existing, booth, cancellationToken);
        await _changeNotes.NoteVariationLinksAsync(existing, booth, cancellationToken);
        await _changeNotes.NoteChangesAsync(existing, booth, cancellationToken);

        // **画像はここで落とさない。**梯子の規則をここだけ破らないため。
        // 落とすと「①②が画像より先」の外側に画像の取得が生まれる。
        // 増えた画像は ImageBacklog が拾い、人が押した取り直しでは
        // 呼び出し側が優先ボタンと同じ経路で取りに行く。
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
    public async Task<FolderRegistration> RegisterFolderAsync(
        string itemId,
        string folderPath,
        CancellationToken cancellationToken = default,
        IProgress<int>? requestsLeft = null)
    {
        if (!Directory.Exists(folderPath))
        {
            return FolderRegistration.FolderMissing;
        }

        if (!_store.Items.Exists(itemId) && (await FetchNewItemAsync(itemId, cancellationToken, requestsLeft)).Item is null)
        {
            return FolderRegistration.ItemUnavailable;
        }

        // 中の unitypackage も同じ1回の列挙で拾う（Unity へ送る候補。メモ65-③）
        // 中を読めなければ登録しない（0件・0バイトで残すと、移したときの候補が数で合わせられなくなる。外部の点検 2026-10-06）
        // **数えるのは裏のスレッドで**（外部の点検 2026-10-06）。画面から来ると、Core は続きを元の文脈へ戻すので、
        // 確かめの窓の後の登録で、同じフォルダを画面のスレッドでもう一度数え、その間ずっと画面が止まっていた
        if (await Task.Run(() => RegisteredFolderSet.Survey(folderPath, cancellationToken), cancellationToken) is not { } survey)
        {
            return FolderRegistration.Unreadable;
        }

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
            return FolderRegistration.ItemRemoved;
        }

        await _localFiles.RemoveUnresolvedUnderAsync(normalized, cancellationToken);
        return FolderRegistration.Registered;
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
    /// 画像は1枚目だけを取り、残りを⑤の段で裏に頼む（<see cref="RemainingImageRequests.Request"/>）。未確定の「このIDで登録」だけが使う
    /// （登録の列で後ろの登録を待たせるのはこの道だけ。ID の付け替え・ファイルを持たない登録・フォルダの登録は今までどおり全部取る）。
    /// </param>
    internal async Task<(ItemRecord? Item, Booth.BoothFetchStatus Status)> FetchNewItemAsync(
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
            _remainingImages.Request(itemId, item.Booth.Images);
        }

        return (item, Booth.BoothFetchStatus.Success);
    }

    /// <inheritdoc cref="RemainingImageRequests.Hold"/>
    public IDisposable HoldRemainingImages() => _remainingImages.Hold();

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
        await _localFiles.RemoveUnresolvedAsync(target.Hash, cancellationToken);

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
    /// <item>公開されたら <see cref="RefreshAsync"/> が booth を埋め、印を外し、要確認に「BOOTHに現れました」を出す（<c>BoothChangeNotes.NoteBackOnBoothAsync</c>）</item>
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


    // 商品IDの付け替え（と、途中で止まった付け替えの再開）は ItemIdChanger が受け持つ（分けた理由はそちら）

    /// <inheritdoc cref="ItemIdChanger.PlanItemIdChangeAsync"/>
    public Task<ItemIdChangePlan?> PlanItemIdChangeAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default)
        => _idChanger.PlanItemIdChangeAsync(fromId, toId, cancellationToken);

    /// <inheritdoc cref="ItemIdChanger.ChangeItemIdAsync"/>
    public Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        CancellationToken cancellationToken = default,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null)
        => _idChanger.ChangeItemIdAsync(fromId, toId, skippedPurchases, cancellationToken, record);

    /// <inheritdoc cref="ItemIdChanger.ResumeItemIdChangeAsync"/>
    public Task<ItemIdChangeOutcome> ResumeItemIdChangeAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        ItemIdChangeFingerprints? recorded = null,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null,
        CancellationToken cancellationToken = default)
        => _idChanger.ResumeItemIdChangeAsync(fromId, toId, skippedPurchases, recorded, record, cancellationToken);

    // 手元のファイル・フォルダの付け外し・除外・見つからない印・未確定の片付けは LocalFileEditor が受け持つ（分けた理由はそちら）

    /// <inheritdoc cref="LocalFileEditor.UnregisterFolderAsync"/>
    public Task<bool> UnregisterFolderAsync(
        string itemId,
        string folderPath,
        CancellationToken cancellationToken = default)
        => _localFiles.UnregisterFolderAsync(itemId, folderPath, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.RelocateFolderAsync"/>
    public Task<FolderRelocation> RelocateFolderAsync(
        string itemId,
        string fromPath,
        string toPath,
        CancellationToken cancellationToken = default)
        => _localFiles.RelocateFolderAsync(itemId, fromPath, toPath, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.ReconcileUnresolvedAsync"/>
    public Task<int> ReconcileUnresolvedAsync(CancellationToken cancellationToken = default)
        => _localFiles.ReconcileUnresolvedAsync(cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.SwapFolderForArchiveAsync"/>
    public Task<ArchiveSwapOutcome> SwapFolderForArchiveAsync(
        string itemId,
        string folderPath,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
        => _localFiles.SwapFolderForArchiveAsync(itemId, folderPath, liftExclusion, takeFromOtherItems, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.AttachFileAsync"/>
    public Task<FileAttachOutcome> AttachFileAsync(
        string itemId,
        string path,
        bool liftExclusion = false,
        bool takeFromOtherItems = false,
        CancellationToken cancellationToken = default)
        => _localFiles.AttachFileAsync(itemId, path, liftExclusion, takeFromOtherItems, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.SetFileVariationsAsync"/>
    public Task<bool> SetFileVariationsAsync(
        string itemId,
        IReadOnlyDictionary<string, long?> variationByHash,
        CancellationToken cancellationToken = default)
        => _localFiles.SetFileVariationsAsync(itemId, variationByHash, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.NoteFilePresenceAsync"/>
    public Task<bool> NoteFilePresenceAsync(
        string itemId,
        IReadOnlyCollection<FileSighting> sightings,
        CancellationToken cancellationToken = default)
        => _localFiles.NoteFilePresenceAsync(itemId, sightings, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.ForgetOldVersionAsync"/>
    public Task<bool> ForgetOldVersionAsync(string itemId, string hash, CancellationToken cancellationToken = default)
        => _localFiles.ForgetOldVersionAsync(itemId, hash, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.DetachFileAsync"/>
    public Task<DetachOutcome> DetachFileAsync(
        string itemId,
        string hash,
        bool deleteItemWhenEmpty,
        CancellationToken cancellationToken = default)
        => _localFiles.DetachFileAsync(itemId, hash, deleteItemWhenEmpty, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.ReattachFileAsync"/>
    public Task<ReattachOutcome> ReattachFileAsync(
        string itemId,
        string hash,
        CancellationToken cancellationToken = default)
        => _localFiles.ReattachFileAsync(itemId, hash, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.ExcludeAsync"/>
    public Task<IReadOnlyList<string>> ExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        string? reason,
        CancellationToken cancellationToken = default)
        => _localFiles.ExcludeAsync(files, reason, cancellationToken);

    /// <inheritdoc cref="LocalFileEditor.UndoExcludeAsync"/>
    public Task UndoExcludeAsync(
        IReadOnlyList<UnresolvedFile> files,
        IReadOnlyCollection<string> excludedHashes,
        CancellationToken cancellationToken = default)
        => _localFiles.UndoExcludeAsync(files, excludedHashes, cancellationToken);

    // 自分で足す画像・役割・サムネイルの指名は UserImageEditor が受け持つ（分けた理由はそちら）

    /// <inheritdoc cref="UserImageEditor.AddUserImageAsync"/>
    public Task<string?> AddUserImageAsync(string itemId, byte[] bytes, string? caption = null, CancellationToken cancellationToken = default)
        => _userImages.AddUserImageAsync(itemId, bytes, caption, cancellationToken);

    /// <inheritdoc cref="UserImageEditor.RemoveUserImageAsync"/>
    public Task<bool> RemoveUserImageAsync(string itemId, string fileName, CancellationToken cancellationToken = default)
        => _userImages.RemoveUserImageAsync(itemId, fileName, cancellationToken);

    /// <inheritdoc cref="UserImageEditor.MoveUserImageAsync"/>
    public Task<bool> MoveUserImageAsync(string itemId, string fileName, int delta, CancellationToken cancellationToken = default)
        => _userImages.MoveUserImageAsync(itemId, fileName, delta, cancellationToken);

    /// <inheritdoc cref="UserImageEditor.SetImageRoleAsync"/>
    public Task<bool> SetImageRoleAsync(string itemId, string fileName, ImageRole role, bool isUserAdded, CancellationToken cancellationToken = default)
        => _userImages.SetImageRoleAsync(itemId, fileName, role, isUserAdded, cancellationToken);

    /// <inheritdoc cref="UserImageEditor.PinThumbnailAsync"/>
    public Task<bool> PinThumbnailAsync(string itemId, string? fileName, CancellationToken cancellationToken = default)
        => _userImages.PinThumbnailAsync(itemId, fileName, cancellationToken);

    /// <summary>
    /// BOOTHから取れなかったIDのために、中身が空の商品を作る。
    /// <c>Booth.FetchedAt</c> は null のまま——観測していないので、それが正しい。
    /// </summary>
    internal static ItemRecord EmptyItem(string itemId) => new()
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
    internal ItemRecord UnpublishedItem(string itemId, string? displayName)
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
