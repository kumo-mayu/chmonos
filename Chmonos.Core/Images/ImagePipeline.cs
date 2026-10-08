using System.Security.Cryptography;
using System.Text;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Chmonos.Core.Images;

public sealed class ImageSyncResult
{
    public int Downloaded { get; init; }

    public int SkippedExisting { get; init; }

    public int Failed { get; init; }

    /// <summary>404で取れなかった枚数。印を置いたので次からは取りに行かない。</summary>
    public int Missing { get; init; }

    /// <summary>BOOTH側の一覧から消えたが、手元には残っている画像のファイル名。</summary>
    public IReadOnlyList<string> OrphanedFiles { get; init; } = [];
}

/// <summary>ショップのアイコンを取りに行った結果。</summary>
public enum ShopIconFetch
{
    /// <summary>手元にある（元から持っていた・今回保存した）。</summary>
    Present,

    /// <summary>問い合わせていない（画像を保存しない設定・URLが無い・打ち切った後）。</summary>
    NotAsked,

    /// <summary>404。BOOTH が「無い」と答えた。</summary>
    NotFound,

    /// <summary>BOOTH は答えたが、絵として読めなかった。</summary>
    Unreadable,

    /// <summary>届かない・5xx・429・手元に書けなかった。次の機会に取り直せば取れ得る。</summary>
    TemporaryFailure,
}

/// <summary>
/// 商品画像をローカルへ取り込む。長辺384pxのWebPに変換して <c>images/{itemId}/{URLハッシュ}.webp</c> に置く。
///
/// ファイル名を取得順の連番ではなく元URLのハッシュにしているのは、
/// BOOTH側で画像が1枚差し込まれた時に以降の番号が全部ずれ、
/// 既に持っている画像まで落とし直すことになるのを避けるため。
/// </summary>
public sealed class ImagePipeline
{
    private readonly IBoothClient _client;
    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _currentSettings;

    public ImagePipeline(IBoothClient client, AppPaths paths, AppSettings? settings = null)
        : this(client, paths, SettingsSource.Fixed(settings))
    {
    }

    /// <param name="currentSettings">
    /// 使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。
    /// 設定画面で長辺や画質を変えたら、次に取る画像から効く。
    /// </param>
    public ImagePipeline(IBoothClient client, AppPaths paths, Func<AppSettings> currentSettings)
    {
        _client = client;
        _paths = paths;
        _currentSettings = currentSettings;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    /// <summary>
    /// 商品の画像を新しく保存した。引数は商品ID。**取得した側のスレッドで呼ばれる。**
    ///
    /// 取り込みの④⑤・裏での取得・期限の取り直しは画面と関係なく画像を置く。
    /// 知らせる道が無いと、開いている検索カードや商品ページは起動し直すまで空のままだった
    /// （友人の報告「画像を取得しても即時反映されていない」）。
    /// </summary>
    public event Action<string>? ItemImagesSaved;

    /// <summary>
    /// 画像を取る設定になっているか。
    ///
    /// 呼び出し側でも梯子の④⑤⑥を飛ばすが、ここでも見る。
    /// 入口が複数あるので、**通信が起きる場所そのもの**で止めておかないと
    /// 足したときに漏れる。
    /// </summary>
    public bool SavesImages => _settings.SaveImages;

    /// <summary>元URLから保存名を導く。同じURLなら常に同じ名前になる。</summary>
    public static string FileNameFor(string originalUrl) => $"{ShortHash(originalUrl)}.webp";

    /// <summary>
    /// 「取りに行ったが404だった」印の名前。
    ///
    /// 中身は要らない。**ファイルがあること自体が「これは取れなかった」を意味する。**
    /// 画像と同じハッシュを使うので、作者が画像を差し替えれば別の名前になり、
    /// 古い印は自然に効かなくなる。
    /// </summary>
    public static string MissingMarkerFor(string originalUrl) => $"{ShortHash(originalUrl)}.missing";

    /// <summary>
    /// 「404ではないが取れなかった」印の名前（ユーザ判断 2026-09-21・G6）。
    ///
    /// 403・接続失敗・画像として読めなかった物には印が無く、**起動のたびに同じ物を取り直していた**
    /// （読めない画像は受信までやり直すので、毎回まるまる通信が無駄になる）。
    /// 404 と違って「もう取りに行く先が無い」とは言えないので、**日時つきで一定期間だけ休む**
    /// （ショップのバナーの「調べた日時」と同じ考え方）。日時はファイルの更新日時をそのまま使う。
    /// </summary>
    public static string RetryMarkerFor(string originalUrl) => $"{ShortHash(originalUrl)}.retry";

    /// <summary>
    /// 取れなかった画像を、次に取りに行くまで休む日数。
    ///
    /// **商品の取り直しの既定（7日）と同じ。**取れなかった理由（相手の不調・権限・壊れた画像）が
    /// 消えたかどうかは、商品ページを取り直すのと同じ頻度で確かめれば足りる。
    /// </summary>
    private const int RetryAfterDays = 7;

    private static string ShortHash(string originalUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originalUrl));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public string FilePathFor(string itemId, string originalUrl)
        => Path.Combine(_paths.ItemImagesDir(itemId), FileNameFor(originalUrl));

    /// <summary>
    /// この1枚は決着しているか（ユーザ判断 2026-09-21・G7）。
    ///
    /// 手元にある・404だった・しばらく休んでいる のどれかなら、取りに行く必要が無い。
    /// **枚数では数えない**——自分で足した絵も、BOOTHの一覧から消えたので残している絵も
    /// 同じ場所に同じ拡張子であり、数に混ざると「揃っている」と誤って判定される。
    /// </summary>
    public bool IsSettled(string itemId, string originalUrl)
    {
        var directory = _paths.ItemImagesDir(itemId);

        try
        {
            return File.Exists(Path.Combine(directory, FileNameFor(originalUrl)))
                || File.Exists(Path.Combine(directory, MissingMarkerFor(originalUrl)))
                || IsRestingAfterFailure(directory, originalUrl);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めないなら「決着している」ことにする。取りに行っても同じ場所に置けない
            return true;
        }
    }

    /// <summary>
    /// BOOTHが配っているアイコンの大きさ。
    ///
    /// CDNは決まったサイズしか返さない（実測で 48 / 128 / 150 と原寸のみが200、
    /// 他は403）。原寸はショップごとに240〜600pxとばらつき、150KB級のものもあるので、
    /// 一定の大きさで揃う150を採る。表示は42pxと72pxなので、これで足りる。
    /// </summary>
    private const string IconSizeSegment = "/c/150x150/";

    /// <summary>
    /// ショップのアイコンを、これから問い合わせる必要があるか（手元に無く、取る設定のとき）。
    /// 未確定で登録するときの残りの問い合わせの数（メモ34）を数えるのに使う。
    /// </summary>
    public bool NeedsShopIcon(string subdomain, string? thumbnailUrl)
        => _settings.SaveImages
            && !string.IsNullOrEmpty(thumbnailUrl)
            && !File.Exists(_paths.ShopIconFile(subdomain, LargerIconUrl(thumbnailUrl)));

    /// <summary>
    /// ショップのアイコンを落とす。同じURLのものが既にあれば何もしない。
    ///
    /// 商品JSONに入っているのは48x48のURLだけなので、サイズの部分を差し替えて取る。
    /// 差し替えられない形のURLだったときは、素直に元のURLを使う。
    ///
    /// 保存名に元URLのハッシュが入るので、ショップがアイコンを差し替えたときは
    /// 別ファイルになり、次のitem取得で自動的に落とし直される。
    /// 時間で確かめ直す必要は無い（URLは商品JSONと一緒に毎回届くため）。
    /// </summary>
    /// <returns>手元にアイコンがあるか（元から持っていた場合も true）。</returns>
    public Task<bool> SyncShopIconAsync(
        string subdomain,
        string? thumbnailUrl,
        CancellationToken cancellationToken = default)
        => SyncShopIconAsync(subdomain, thumbnailUrl, outage: null, cancellationToken);

    /// <param name="outage">
    /// 取り込み・裏の作業・ショップ一覧で、届かない失敗を続けて数える物（ユーザ判断 2026-09-29）。
    /// 打ち切った後は問い合わせずに false を返す（まだ取っていない扱いのまま残り、次の機会に取る）。
    /// </param>
    public async Task<bool> SyncShopIconAsync(
        string subdomain,
        string? thumbnailUrl,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken = default)
        => await FetchShopIconAsync(subdomain, thumbnailUrl, outage, cancellationToken) == ShopIconFetch.Present;

    /// <summary>
    /// ショップのアイコンを落とし、どうなったかを状態で返す。
    /// 「取れなかった」を1つにまとめると、ショップ一覧が「確かめた」と控えてよい失敗（404・読めない絵）と、
    /// 控えてはいけない失敗（届かない・一時的な不調）を分けられない（点検 2026-09-29・8）。
    /// </summary>
    public async Task<ShopIconFetch> FetchShopIconAsync(
        string subdomain,
        string? thumbnailUrl,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.SaveImages || string.IsNullOrEmpty(thumbnailUrl))
        {
            return ShopIconFetch.NotAsked;
        }

        // 取得するURLで名前を決める。商品JSON（48x48）とショップページ（128x128）の
        // どちらから来ても、同じアイコンなら同じ名前になる
        var source = LargerIconUrl(thumbnailUrl);
        var path = _paths.ShopIconFile(subdomain, source);

        if (File.Exists(path))
        {
            return ShopIconFetch.Present;
        }

        if (outage is { IsStopped: true })
        {
            return ShopIconFetch.NotAsked;
        }

        var result = await _client.GetBinaryAsync(source, cancellationToken);
        outage?.Note(result);

        if (result.Status == BoothFetchStatus.NotFound)
        {
            return ShopIconFetch.NotFound;
        }

        if (!result.IsSuccess || result.Value is null)
        {
            return ShopIconFetch.TemporaryFailure;
        }

        try
        {
            Directory.CreateDirectory(_paths.ShopIconsDir);
            await SaveAsWebpAsync(result.Value, path, cancellationToken);
            return ShopIconFetch.Present;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
        {
            // BOOTH は答えたが絵として読めない。すぐ取り直しても同じ物が返る
            return ShopIconFetch.Unreadable;
        }
        catch (IOException)
        {
            // 書けなかったのは手元の都合（空きが無い・掴まれている）。BOOTH の答えとは関係が無い
            return ShopIconFetch.TemporaryFailure;
        }
    }

    /// <summary>
    /// ショップのバナーを落とす。
    ///
    /// バナーは原寸のPNGで、実測で4KB〜4MBとばらつく。そのまま置くと重いので、
    /// 商品画像より大きめの長辺に落としてWebPにする（BOOTHは960px幅で出している）。
    /// </summary>
    public async Task<bool> SyncShopBannerAsync(
        string subdomain,
        string sourceUrl,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.SaveImages)
        {
            return false;
        }

        var result = await _client.GetBinaryAsync(sourceUrl, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_paths.ShopIconsDir);
            await SaveAsWebpAsync(
                result.Value,
                _paths.ShopBannerFile(subdomain),
                cancellationToken,
                _settings.ShopBannerMaxEdgePixels);

            return true;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return false;
        }
    }

    /// <summary>48x48のURLから150x150のURLを作る。形が違えばそのまま返す。</summary>
    public static string LargerIconUrl(string thumbnailUrl)
    {
        var start = thumbnailUrl.IndexOf("/c/", StringComparison.Ordinal);
        if (start < 0)
        {
            return thumbnailUrl;
        }

        var end = thumbnailUrl.IndexOf('/', start + 3);
        if (end < 0)
        {
            return thumbnailUrl;
        }

        return thumbnailUrl[..start] + IconSizeSegment + thumbnailUrl[(end + 1)..];
    }

    /// <summary>
    /// 1枚だけ落とす。取り込みの④（全商品の1枚目）で使う。
    ///
    /// <see cref="SyncAsync"/> と分けているのは、あちらが
    /// 「渡された一覧に無いファイル＝BOOTHから消えた画像」を数えるため。
    /// 1枚だけ渡すと、まだ落としていない残りが全部「消えた画像」になってしまう。
    /// </summary>
    /// <returns>手元にあるか（元から持っていた場合も true）。</returns>
    public Task<bool> SyncOneAsync(
        string itemId,
        BoothImage image,
        CancellationToken cancellationToken = default)
        => SyncOneAsync(itemId, image, outage: null, cancellationToken);

    /// <param name="outage">画像の段で届かない失敗を続けて数える物（<see cref="SyncShopIconAsync(string, string?, BoothOutageWatch?, CancellationToken)"/>）。</param>
    public async Task<bool> SyncOneAsync(
        string itemId,
        BoothImage image,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken = default)
    {
        // 外した商品の画像フォルダを作り直さない（SyncAsync と同じ理由）
        if (!ItemStillExists(itemId))
        {
            return false;
        }

        var (present, saved) = await FetchOneAsync(_paths.ItemImagesDir(itemId), image.OriginalUrl, outage, cancellationToken);
        if (saved)
        {
            ItemImagesSaved?.Invoke(itemId);
        }

        return present;
    }

    /// <summary>
    /// 1枚だけを指定の場所へ落とす。商品ではないもの（持っていないアバターの1枚目・U18）に使う。
    /// 保存の形（長辺・WebP・URLのハッシュの名前・404の印）は商品の画像と同じ。
    /// </summary>
    /// <returns>手元にあるか（元から持っていた場合も true）。</returns>
    public Task<bool> SyncOneToAsync(
        string directory,
        string originalUrl,
        CancellationToken cancellationToken = default)
        => SyncOneToAsync(directory, originalUrl, outage: null, cancellationToken);

    /// <param name="outage">画像の段で届かない失敗を続けて数える物（<see cref="SyncShopIconAsync(string, string?, BoothOutageWatch?, CancellationToken)"/>）。</param>
    public async Task<bool> SyncOneToAsync(
        string directory,
        string originalUrl,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken = default)
        => (await FetchOneAsync(directory, originalUrl, outage, cancellationToken)).Present;

    /// <returns>手元にあるか、この呼び出しで新しく保存したか。</returns>
    private async Task<(bool Present, bool Saved)> FetchOneAsync(
        string directory,
        string originalUrl,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken)
    {
        if (!_settings.SaveImages)
        {
            return (false, false);
        }

        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileNameFor(originalUrl));
        if (File.Exists(path))
        {
            return (true, false);
        }

        // 404だったものは取りに行かない。毎回1本ずつ無駄にするのを避ける
        if (File.Exists(Path.Combine(directory, MissingMarkerFor(originalUrl))))
        {
            return (false, false);
        }

        // 打ち切った後は問い合わせない。印も置かないので、まだ取っていない扱いのまま次の機会に取る
        if (outage is { IsStopped: true })
        {
            return (false, false);
        }

        var result = await _client.GetBinaryAsync(originalUrl, cancellationToken);
        outage?.Note(result);

        if (result.Status == BoothFetchStatus.NotFound)
        {
            await MarkMissingAsync(directory, originalUrl, cancellationToken);
            return (false, false);
        }

        if (!result.IsSuccess || result.Value is null)
        {
            // 一時エラーでは印を置かない。商品が消えた証拠にならない
            return (false, false);
        }

        try
        {
            await SaveAsWebpAsync(result.Value, path, cancellationToken);
            return (true, true);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return (false, false);
        }
    }

    /// <summary>
    /// 「取りに行ったが404だった」印を置く。
    ///
    /// 記録を <c>local</c> に持たないのは、実態（ディスク）とフラグがずれたときに
    /// どちらが正しいか分からなくなるため。印もファイルなら、実態の側にある。
    /// </summary>
    private static Task MarkMissingAsync(string directory, string originalUrl, CancellationToken cancellationToken)
        // 印を置けなくても取得は成立している。次回もう一度取りに行くだけ
        => TouchStoreAsync(() => File.WriteAllBytes(Path.Combine(directory, MissingMarkerFor(originalUrl)), []), cancellationToken);

    /// <summary>404 ではない失敗の印を置き直す（日時はファイルの更新日時。G6）。</summary>
    private static Task MarkRetryLaterAsync(string directory, string originalUrl, CancellationToken cancellationToken)
        => TouchStoreAsync(
            () =>
            {
                var path = Path.Combine(directory, RetryMarkerFor(originalUrl));
                File.WriteAllBytes(path, []);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            },
            cancellationToken);

    /// <summary>
    /// 印を置く・消す・自分で足した画像を消す。**これも保存先への書き込みなので、運ぶ門を通す**（StoreWriteGate）。
    /// 通さずにいた頃は、運んでいる最中に置いた印が、元を消すときに一緒に消えるか、運ばれずに元の場所に残った。
    /// 失敗しても投げない（どれも無くて困る物ではなく、次の取得で置き直すか、次の掃除で消える）。
    /// </summary>
    private static async Task TouchStoreAsync(Action touch, CancellationToken cancellationToken)
    {
        using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
        try
        {
            touch();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>まだ休んでいる最中か（印が無ければ休んでいない）。</summary>
    private static bool IsRestingAfterFailure(string directory, string originalUrl)
    {
        try
        {
            var path = Path.Combine(directory, RetryMarkerFor(originalUrl));
            return File.Exists(path)
                && File.GetLastWriteTimeUtc(path) > DateTime.UtcNow.AddDays(-RetryAfterDays);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Task ClearRetryMarkerAsync(string directory, string originalUrl, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, RetryMarkerFor(originalUrl));

        // 無ければ門を待たない（取れた画像ごとに通る道なので、空振りを軽くする）
        return File.Exists(path)
            ? TouchStoreAsync(() => File.Delete(path), cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// この商品の印を全部消す。**商品のJSONを取り直したときに呼ぶ。**
    ///
    /// 取り直した瞬間に新しい画像URLの一覧が手に入るので、そこが唯一の正しいきっかけ。
    /// 作者がたまたま商品ページを非公開にしていただけ、という場合に復活できる。
    /// 日数で外す仕組みを別に持たなくてよいのは、これが成り立つため。
    /// </summary>
    /// <returns>消した印の数。</returns>
    public async Task<int> ClearMissingMarkersAsync(string itemId, CancellationToken cancellationToken = default)
    {
        var directory = _paths.ItemImagesDir(itemId);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cleared = 0;
        await TouchStoreAsync(
            () =>
            {
                foreach (var marker in Directory.EnumerateFiles(directory, "*.missing"))
                {
                    try
                    {
                        File.Delete(marker);
                        cleared++;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            },
            cancellationToken);

        return cleared;
    }

    /// <summary>
    /// 取りに行っても仕方がない画像の枚数（404だったもの）。
    ///
    /// 「未取得」と「取れなかった」を分けるために要る。分けないと、
    /// 押しても何も起きないボタンを出し続けることになる。
    /// </summary>
    public int CountMissingMarkers(string itemId)
    {
        var directory = _paths.ItemImagesDir(itemId);

        try
        {
            // 「しばらく休む」印も数える（数えないと、休んでいる間ずっと裏の取得の対象に戻り続ける・G6）
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.missing").Count()
                    + Directory.EnumerateFiles(directory, "*.retry").Count()
                : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public Task<ImageSyncResult> SyncAsync(
        string itemId,
        IReadOnlyList<BoothImage> images,
        CancellationToken cancellationToken = default)
        => SyncAsync(itemId, images, outage: null, cancellationToken);

    /// <param name="outage">
    /// 画像の段で届かない失敗を続けて数える物（ユーザ判断 2026-09-29）。打ち切ったら、この商品の残りも問い合わせない。
    /// 残りには「しばらく休む」の印も置かない——休ませると、つながった後の次の機会にも取りに行かなくなる。
    /// </param>
    /// <param name="requestsLeft">
    /// これから BOOTH へ問い合わせる枚数を始めに、1枚問い合わせるごとに残りを流す。
    /// 未確定で登録した新しい商品は、ここで十数枚を1.5秒ずつ空けて取るので、画面が残りと目安の時間を出すのに使う（メモ34）。
    /// 数えるのは下の「取りに行く」の条件と同じ物だけ（手元にある絵・404の印・休み中は数えない）。
    /// </param>
    public async Task<ImageSyncResult> SyncAsync(
        string itemId,
        IReadOnlyList<BoothImage> images,
        BoothOutageWatch? outage,
        CancellationToken cancellationToken = default,
        IProgress<int>? requestsLeft = null)
    {
        if (!_settings.SaveImages)
        {
            return new ImageSyncResult();
        }

        // **外した商品の画像フォルダを作り直さない。**取り込みや裏の取得は商品の一覧を先に作ってから
        // 1件ずつ回るので、その間に外された商品が回ってくる。ここでフォルダを作ると、
        // JSON の無い画像フォルダが残り、誰も片付けなかった
        if (!ItemStillExists(itemId))
        {
            return new ImageSyncResult();
        }

        var directory = _paths.ItemImagesDir(itemId);
        Directory.CreateDirectory(directory);

        var downloaded = 0;
        var skipped = 0;
        var failed = 0;
        var missing = 0;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pending = requestsLeft is null
            ? 0
            : images.Count(image => !File.Exists(Path.Combine(directory, FileNameFor(image.OriginalUrl)))
                && !File.Exists(Path.Combine(directory, MissingMarkerFor(image.OriginalUrl)))
                && !IsRestingAfterFailure(directory, image.OriginalUrl));
        requestsLeft?.Report(pending);

        // BOOTH側の一覧に残っている印。ここに無い印は「もう取りに行く先が無い」ので消す。
        // **取りに行く前に全部数えておく。**取りながら数えて最後に片付けていたので、
        // 途中で中断すると片付けが走らず、立てたままの印を「取れなかった画像」として数え続けていた
        var liveMarkers = images.SelectMany(image => new[] { MissingMarkerFor(image.OriginalUrl), RetryMarkerFor(image.OriginalUrl) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = FileNameFor(image.OriginalUrl);
                expected.Add(fileName);

                var markerName = MissingMarkerFor(image.OriginalUrl);

                var path = Path.Combine(directory, fileName);
                if (File.Exists(path))
                {
                    skipped++;
                    continue;
                }

                // 404だったものは取りに行かない。印は商品を取り直したときに消える
                if (File.Exists(Path.Combine(directory, markerName)))
                {
                    missing++;
                    continue;
                }

                // 404以外で取れなかった物は、しばらく休んでから取り直す（G6）
                if (IsRestingAfterFailure(directory, image.OriginalUrl))
                {
                    failed++;
                    continue;
                }

                // 打ち切った後は問い合わせない。抜けずに残りを回るのは、手元にある絵を「BOOTHから消えた画像」と
                // 数えないため（expected に入れておく）
                if (outage is { IsStopped: true })
                {
                    continue;
                }

                var result = await _client.GetBinaryAsync(image.OriginalUrl, cancellationToken);
                outage?.Note(result);
                requestsLeft?.Report(Math.Max(0, --pending));

                if (result.Status == BoothFetchStatus.NotFound)
                {
                    await MarkMissingAsync(directory, image.OriginalUrl, cancellationToken);
                    missing++;
                    continue;
                }

                if (!result.IsSuccess || result.Value is null)
                {
                    // 404ではないので「もう無い」とは言えない。日時つきの印を置いてしばらく休む（G6）。
                    // 印が無かったので、403・接続失敗・読めない画像を起動のたびに取り直していた
                    await MarkRetryLaterAsync(directory, image.OriginalUrl, cancellationToken);
                    failed++;
                    continue;
                }

                try
                {
                    await SaveAsWebpAsync(result.Value, path, cancellationToken);
                    downloaded++;

                    // 取れたので、前に置いた「しばらく休む」の印は用済み
                    await ClearRetryMarkerAsync(directory, image.OriginalUrl, cancellationToken);
                }
                catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
                {
                    // 落とせたが画像として読めなかった。受信までやり直しても同じなので、ここも休む（G6）
                    await MarkRetryLaterAsync(directory, image.OriginalUrl, cancellationToken);
                    failed++;
                }
            }
        }
        finally
        {
            // 中断したときも片付ける（立てたままの印が「取れなかった画像」として残り続けないように）
            // 中断したときに門が閉じていれば、ここで待たずに次へ回す（片付けは次に同じ商品を取るときに走る）
            try
            {
                await TouchStoreAsync(() => RemoveStaleMarkers(directory, liveMarkers), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        // 1枚ごとではなく最後に1回。画面は商品単位で組み直すので、枚数分知らせても同じ
        if (downloaded > 0)
        {
            ItemImagesSaved?.Invoke(itemId);
        }

        return new ImageSyncResult
        {
            Downloaded = downloaded,
            SkippedExisting = skipped,
            Failed = failed,
            Missing = missing,
            OrphanedFiles = FindOrphans(directory, expected),
        };
    }

    /// <summary>
    /// BOOTH側の一覧から消えたURLの印を消す。もう取りに行く先が無いので、
    /// 残しておくと「取れなかった画像がある」と数え続けることになる。
    /// </summary>
    private static void RemoveStaleMarkers(string directory, HashSet<string> live)
    {
        try
        {
            // 「しばらく休む」の印も同じ扱い（一覧から消えたURLの分は用済み）
            foreach (var marker in Directory.EnumerateFiles(directory, "*.missing")
                .Concat(Directory.EnumerateFiles(directory, "*.retry")))
            {
                if (!live.Contains(Path.GetFileName(marker)))
                {
                    File.Delete(marker);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// BOOTH側の一覧から消えた画像を探す。ファイルは消さずに残す
    /// （非公開になった商品の画像は二度と取得できないため、アーカイブとして持ち続ける）。
    /// </summary>
    private static IReadOnlyList<string> FindOrphans(string directory, HashSet<string> expected)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.webp")
            .Select(Path.GetFileName)
            .Where(name => name is not null && !expected.Contains(name))
            .Select(name => name!)
            .ToList();
    }


    /// <summary>
    /// ユーザが自分で足した画像を保存する。
    ///
    /// **BOOTHの画像とまったく同じ圧縮を通す。**別の設定にすると、
    /// 同じギャラリーの中で画質と容量の基準が2つになる。
    ///
    /// 保存名は**中身のハッシュ**なので、同じ絵を2回落としても1枚にまとまる。
    /// </summary>
    /// <returns>保存したファイル名。画像として読めなければ null。</returns>
    public Task<string?> SaveUserImageAsync(
        string itemId,
        byte[] bytes,
        CancellationToken cancellationToken = default)
        // 商品画像は今まで通り ImageMaxEdgePixels（既定384）
        => SaveUserImageToAsync(_paths.ItemImagesDir(itemId), bytes, null, cancellationToken);

    /// <summary>
    /// 改変に貼る画像を保存する。置き場所は <c>images/_mods/{改変ID}/</c>。
    ///
    /// 商品の画像と**同じ圧縮を通す**。別の設定にすると、同じアプリの中で
    /// 画質と容量の基準が2つになる。
    /// </summary>
    public Task<string?> SaveModificationImageAsync(
        string modificationId,
        byte[] bytes,
        CancellationToken cancellationToken = default)
        => SaveUserImageToAsync(
            _paths.ModificationImagesDir(modificationId),
            bytes,

            // **商品画像より大きめに保存する。**用途が違う——商品画像は一覧に並ぶ
            // サムネイルだが、改変の写真は見て「何を使ったか」を思い出すもの。
            // 原寸の指定があれば縮小しない
            _settings.SaveModificationImagesAtOriginalSize
                ? int.MaxValue
                : _settings.ModificationImageMaxEdgePixels,
            cancellationToken);

    /// <summary>
    /// 置き場所を受け取って保存する。
    ///
    /// 商品と改変で置き場所だけが違い、名前の付け方（中身のハッシュ）と
    /// 圧縮は同じ。**同じ絵を2回落としても1枚にまとまる**のも同じ。
    /// </summary>
    private async Task<string?> SaveUserImageToAsync(
        string directory,
        byte[] bytes,
        int? maxEdge = null,
        CancellationToken cancellationToken = default)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        Directory.CreateDirectory(directory);

        var fileName = UserImageName.For(bytes);
        var path = Path.Combine(directory, fileName);

        try
        {
            await SaveAsWebpAsync(bytes, path, cancellationToken, maxEdge);
            return fileName;
        }
        catch (Exception exception) when (exception is ImageFormatException or NotSupportedException)
        {
            // 画像として読めないものを落とされた。呼ぶ側が文言を出す
            return null;
        }
    }

    /// <summary>
    /// ユーザが足した画像を消す。**ファイルごと消える。**
    /// BOOTHから取り直しても戻らないので、呼ぶ側で確かめてから呼ぶ。
    /// </summary>
    public Task DeleteUserImageAsync(string itemId, string fileName, CancellationToken cancellationToken = default)
        => DeleteUserImageFromAsync(_paths.ItemImagesDir(itemId), fileName, cancellationToken);

    /// <summary>改変に貼った画像を消す。**ファイルごと消える。**</summary>
    public Task DeleteModificationImageAsync(string modificationId, string fileName, CancellationToken cancellationToken = default)
        => DeleteUserImageFromAsync(_paths.ModificationImagesDir(modificationId), fileName, cancellationToken);

    private static Task DeleteUserImageFromAsync(string directory, string fileName, CancellationToken cancellationToken)
    {
        if (!UserImageName.IsUserAdded(fileName))
        {
            // BOOTHから取った画像はここでは消さない。取り直せば戻るものなので、
            // 「消した」という記録が残らないと次の取得で復活して混乱する
            return Task.CompletedTask;
        }

        var path = Path.Combine(directory, Path.GetFileName(fileName));

        // 消せなくても記録からは外す。次の掃除で消える
        return File.Exists(path)
            ? TouchStoreAsync(() => File.Delete(path), cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// 復号・縮小・圧縮を始めたときに、そのスレッドで呼ぶ（試験だけが使う。画面のスレッドで走っていないかを見る）。
    /// </summary>
    internal Action? EncodingStarted { get; set; }

    private async Task SaveAsWebpAsync(
        byte[] bytes,
        string path,
        CancellationToken cancellationToken,
        int? maxEdgeOverride = null)
    {
        var maxEdge = maxEdgeOverride ?? _settings.ImageMaxEdgePixels;
        var quality = _settings.ImageQuality;

        // **復号・縮小・圧縮は裏のスレッドで回す**（2026-10-06 外部の点検）。Core は続きを元の文脈へ戻すので、
        // 画面の「画像を追加」・ドロップ・貼り付けから来ると、最初の await の前の復号と縮小が画面のスレッドで走り、
        // 大きな写真では1枚ごとに画面が止まっていた。BOOTH から取る画像も、画面から始めた取得は通信の続きが
        // 画面のスレッドへ戻るので同じ道に乗る。ここ1か所で裏へ出せば、どの呼び方でも止めない。
        // 取り消しの印は中で見る（Task.Run に渡すと、始まる前の取り消しで例外の種類が変わる）
        using var encoded = await Task.Run(() => Encode(bytes, maxEdge, quality, cancellationToken));

        // **一時ファイルの名前は毎回変える。失敗したら消す。**
        // 固定の「本体+.tmp」だと、同じ絵を2本が同時に保存したとき（指名された画像と裏の取得が重なる）に
        // 一時ファイルを取り合って落ち、落ちた方の .tmp は誰も片付けずに残っていた
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        using var writing = await StoreWriteGate.EnterAsync(cancellationToken);
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoded.Position = 0;
                await encoded.CopyToAsync(stream, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private MemoryStream Encode(byte[] bytes, int maxEdge, int quality, CancellationToken cancellationToken)
    {
        EncodingStarted?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();

        // **縮めながら復号する**（2026-09-24）。前は元の大きさで復号してから縮めていたので、BOOTH の 3000px 級の JPEG は
        // 1枚ごとに元の大きさの画素（3000×3000 で約36MB）を作っていた。JPEG は復号の段で 1/2・1/4・1/8 に縮められるので、
        // 長辺の設定より大きい範囲で一番小さく復号し、残りを下の縮小で合わせる。縮め方（Bicubic）と、長辺・画質の設定は前と同じ。
        // 復号の側の縮小は、片方の辺が既に目標と同じだと縮めないので、合わせるのは必ず下で行う。
        // 目標より小さい絵に目標を渡すと復号の側が引き伸ばすので、頭だけ読んで大きいときだけ渡す
        var header = Image.Identify(ImageLimits.FirstFrame, bytes);
        if (ImageLimits.IsTooLarge(header))
        {
            throw new InvalidImageContentException($"寸法が大きすぎる画像（{header.Width}×{header.Height}）");
        }

        using var image = header.Width > maxEdge || header.Height > maxEdge
            ? Image.Load(ImageLimits.Safe(new Size(maxEdge, maxEdge), KnownResamplers.Bicubic), bytes)
            : Image.Load(ImageLimits.FirstFrame, bytes);

        if (image.Width > maxEdge || image.Height > maxEdge)
        {
            // 拡大はしない。元が小さい画像はそのままの大きさで保存する。
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxEdge, maxEdge),
            }));
        }

        var encoder = new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossy,
            Quality = quality,
            TransparentColorMode = WebpTransparentColorMode.Clear,
        };

        // 縮めて圧縮するのは手元の計算なので、書く前に済ませる。保存先を運ぶ門（StoreWriteGate）で
        // 「書いている」に数える間を、ファイルに書く一瞬だけにするため
        var encoded = new MemoryStream();
        try
        {
            image.Save(encoded, encoder);
            cancellationToken.ThrowIfCancellationRequested();
            return encoded;
        }
        catch
        {
            encoded.Dispose();
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 残っても起動時に10分より古い .tmp として消える
        }
    }

    /// <summary>商品がまだ在るか（JSON が在るか）。外した商品の画像を保存しないために見る。</summary>
    private bool ItemStillExists(string itemId) => File.Exists(_paths.ItemFile(itemId));
}
