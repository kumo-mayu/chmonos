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

    private async Task SaveAsWebpAsync(byte[] bytes, string path, CancellationToken cancellationToken)
    {
        using var image = Image.Load(bytes);

        var maxEdge = _settings.ImageMaxEdgePixels;
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
