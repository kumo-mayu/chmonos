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
    private readonly AppSettings _settings;

    public ImagePipeline(IBoothClient client, AppPaths paths, AppSettings? settings = null)
    {
        _client = client;
        _paths = paths;
        _settings = settings ?? new AppSettings();
    }

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

    private static string ShortHash(string originalUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originalUrl));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    public string FilePathFor(string itemId, string originalUrl)
        => Path.Combine(_paths.ItemImagesDir(itemId), FileNameFor(originalUrl));

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
        if (!_settings.SaveImages)
        {
            return false;
        }

        var directory = _paths.ItemImagesDir(itemId);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, FileNameFor(image.OriginalUrl));
        if (File.Exists(path))
        {
            return true;
        }

        // 404だったものは取りに行かない。毎回1本ずつ無駄にするのを避ける
        if (File.Exists(Path.Combine(directory, MissingMarkerFor(image.OriginalUrl))))
        {
            return false;
        }

        var result = await _client.GetBinaryAsync(image.OriginalUrl, cancellationToken);

        if (result.Status == BoothFetchStatus.NotFound)
        {
            MarkMissing(directory, image.OriginalUrl);
            return false;
        }

        if (!result.IsSuccess || result.Value is null)
        {
            // 一時エラーでは印を置かない。商品が消えた証拠にならない
            return false;
        }

        try
        {
            await SaveAsWebpAsync(result.Value, path, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
        {
            return false;
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
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.missing").Count()
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

        // BOOTH側の一覧に残っている印。ここに無い印は「もう取りに行く先が無い」ので消す
        var liveMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = FileNameFor(image.OriginalUrl);
            expected.Add(fileName);

            var markerName = MissingMarkerFor(image.OriginalUrl);
            liveMarkers.Add(markerName);

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

            var result = await _client.GetBinaryAsync(image.OriginalUrl, cancellationToken);

            if (result.Status == BoothFetchStatus.NotFound)
            {
                MarkMissing(directory, image.OriginalUrl);
                missing++;
                continue;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                // 一時エラーでは印を置かない。次回もう一度取りに行く
                failed++;
                continue;
            }

            try
            {
                await SaveAsWebpAsync(result.Value, path, cancellationToken);
                downloaded++;
            }
            catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or IOException)
            {
                failed++;
            }
        }

        RemoveStaleMarkers(directory, liveMarkers);

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
            foreach (var marker in Directory.EnumerateFiles(directory, "*.missing"))
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
        => SaveUserImageToAsync(_paths.ItemImagesDir(itemId), bytes, cancellationToken);

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
        => SaveUserImageToAsync(_paths.ModificationImagesDir(modificationId), bytes, cancellationToken);

    /// <summary>
    /// 置き場所を受け取って保存する。
    ///
    /// 商品と改変で置き場所だけが違い、名前の付け方（中身のハッシュ）と
    /// 圧縮は同じ。**同じ絵を2回落としても1枚にまとまる**のも同じ。
    /// </summary>
    private async Task<string?> SaveUserImageToAsync(
        string directory,
        byte[] bytes,
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
            await SaveAsWebpAsync(bytes, path, cancellationToken);
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
