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

    /// <summary>元URLから保存名を導く。同じURLなら常に同じ名前になる。</summary>
    public static string FileNameFor(string originalUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(originalUrl));
        return $"{Convert.ToHexString(hash)[..8].ToLowerInvariant()}.webp";
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
    /// ショップのアイコンを落とす。既にあれば何もしない。
    ///
    /// 商品JSONに入っているのは48x48のURLだけなので、サイズの部分を差し替えて取る。
    /// 差し替えられない形のURLだったときは、素直に元のURLを使う。
    /// </summary>
    /// <returns>手元にアイコンがあるか（元から持っていた場合も true）。</returns>
    public async Task<bool> SyncShopIconAsync(
        string subdomain,
        string? thumbnailUrl,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.ShopIconFile(subdomain);
        if (File.Exists(path))
        {
            return true;
        }

        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            return false;
        }

        var result = await _client.GetBinaryAsync(LargerIconUrl(thumbnailUrl), cancellationToken);
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

    public async Task<ImageSyncResult> SyncAsync(
        string itemId,
        IReadOnlyList<BoothImage> images,
        CancellationToken cancellationToken = default)
    {
        var directory = _paths.ItemImagesDir(itemId);
        Directory.CreateDirectory(directory);

        var downloaded = 0;
        var skipped = 0;
        var failed = 0;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = FileNameFor(image.OriginalUrl);
            expected.Add(fileName);

            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                skipped++;
                continue;
            }

            var result = await _client.GetBinaryAsync(image.OriginalUrl, cancellationToken);
            if (!result.IsSuccess || result.Value is null)
            {
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

        return new ImageSyncResult
        {
            Downloaded = downloaded,
            SkippedExisting = skipped,
            Failed = failed,
            OrphanedFiles = FindOrphans(directory, expected),
        };
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
