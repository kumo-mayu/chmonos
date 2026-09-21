using System.Security.Cryptography;
using System.Text;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace BoothAssetManager.Core.Images;

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
    public async Task<bool> SyncShopIconAsync(
        string subdomain,
        string? thumbnailUrl,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.SaveImages)
        {
            return false;
        }

        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            return false;
        }

        // 取得するURLで名前を決める。商品JSON（48x48）とショップページ（128x128）の
        // どちらから来ても、同じアイコンなら同じ名前になる
        var source = LargerIconUrl(thumbnailUrl);
        var path = _paths.ShopIconFile(subdomain, source);

        if (File.Exists(path))
        {
            return true;
        }

        var result = await _client.GetBinaryAsync(source, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(_paths.ShopIconsDir);
            await SaveAsWebpAsync(result.Value, path, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return false;
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
    public async Task<bool> SyncOneAsync(
        string itemId,
        BoothImage image,
        CancellationToken cancellationToken = default)
    {
        var (present, saved) = await FetchOneAsync(_paths.ItemImagesDir(itemId), image.OriginalUrl, cancellationToken);
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
    public async Task<bool> SyncOneToAsync(
        string directory,
        string originalUrl,
        CancellationToken cancellationToken = default)
        => (await FetchOneAsync(directory, originalUrl, cancellationToken)).Present;

    /// <returns>手元にあるか、この呼び出しで新しく保存したか。</returns>
    private async Task<(bool Present, bool Saved)> FetchOneAsync(
        string directory,
        string originalUrl,
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

        var result = await _client.GetBinaryAsync(originalUrl, cancellationToken);

        if (result.Status == BoothFetchStatus.NotFound)
        {
            MarkMissing(directory, originalUrl);
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
    private static void MarkMissing(string directory, string originalUrl)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(directory, MissingMarkerFor(originalUrl)), []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 印を置けなくても取得は成立している。次回もう一度取りに行くだけ
        }
    }

    /// <summary>404 ではない失敗の印を置き直す（日時はファイルの更新日時。G6）。</summary>
    private static void MarkRetryLater(string directory, string originalUrl)
    {
        try
        {
            var path = Path.Combine(directory, RetryMarkerFor(originalUrl));
            File.WriteAllBytes(path, []);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
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

    private static void ClearRetryMarker(string directory, string originalUrl)
    {
        try
        {
            var path = Path.Combine(directory, RetryMarkerFor(originalUrl));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// この商品の印を全部消す。**商品のJSONを取り直したときに呼ぶ。**
    ///
    /// 取り直した瞬間に新しい画像URLの一覧が手に入るので、そこが唯一の正しいきっかけ。
    /// 作者がたまたま商品ページを非公開にしていただけ、という場合に復活できる。
    /// 日数で外す仕組みを別に持たなくてよいのは、これが成り立つため。
    /// </summary>
    /// <returns>消した印の数。</returns>
    public int ClearMissingMarkers(string itemId)
    {
        var directory = _paths.ItemImagesDir(itemId);
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cleared = 0;

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

    public async Task<ImageSyncResult> SyncAsync(
        string itemId,
        IReadOnlyList<BoothImage> images,
        CancellationToken cancellationToken = default)
    {
        if (!_settings.SaveImages)
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

                var result = await _client.GetBinaryAsync(image.OriginalUrl, cancellationToken);

                if (result.Status == BoothFetchStatus.NotFound)
                {
                    MarkMissing(directory, image.OriginalUrl);
                    missing++;
                    continue;
                }

                if (!result.IsSuccess || result.Value is null)
                {
                    // 404ではないので「もう無い」とは言えない。日時つきの印を置いてしばらく休む（G6）。
                    // 印が無かったので、403・接続失敗・読めない画像を起動のたびに取り直していた
                    MarkRetryLater(directory, image.OriginalUrl);
                    failed++;
                    continue;
                }

                try
                {
                    await SaveAsWebpAsync(result.Value, path, cancellationToken);
                    downloaded++;

                    // 取れたので、前に置いた「しばらく休む」の印は用済み
                    ClearRetryMarker(directory, image.OriginalUrl);
                }
                catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
                {
                    // 落とせたが画像として読めなかった。受信までやり直しても同じなので、ここも休む（G6）
                    MarkRetryLater(directory, image.OriginalUrl);
                    failed++;
                }
            }
        }
        finally
        {
            // 中断したときも片付ける（立てたままの印が「取れなかった画像」として残り続けないように）
            RemoveStaleMarkers(directory, liveMarkers);
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
    public void DeleteUserImage(string itemId, string fileName)
        => DeleteUserImageFrom(_paths.ItemImagesDir(itemId), fileName);

    /// <summary>改変に貼った画像を消す。**ファイルごと消える。**</summary>
    public void DeleteModificationImage(string modificationId, string fileName)
        => DeleteUserImageFrom(_paths.ModificationImagesDir(modificationId), fileName);

    private void DeleteUserImageFrom(string directory, string fileName)
    {
        if (!UserImageName.IsUserAdded(fileName))
        {
            // BOOTHから取った画像はここでは消さない。取り直せば戻るものなので、
            // 「消した」という記録が残らないと次の取得で復活して混乱する
            return;
        }

        var path = Path.Combine(directory, Path.GetFileName(fileName));

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 消せなくても記録からは外す。次の掃除で消える
        }
    }

    private async Task SaveAsWebpAsync(
        byte[] bytes,
        string path,
        CancellationToken cancellationToken,
        int? maxEdgeOverride = null)
    {
        using var image = Image.Load(bytes);

        var maxEdge = maxEdgeOverride ?? _settings.ImageMaxEdgePixels;
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
            Quality = _settings.ImageQuality,
            TransparentColorMode = WebpTransparentColorMode.Clear,
        };

        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await image.SaveAsync(stream, encoder, cancellationToken);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
