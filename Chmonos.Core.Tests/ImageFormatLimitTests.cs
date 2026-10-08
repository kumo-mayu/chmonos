using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 画像を読む形式を絞り、メタデータを読まない（2026-10-08・ユーザ判断「Aで良いでしょう」。ImageSharp 3.1.12 の脆弱性の知らせ）。
/// TIFF は読めない画像として断り、ふつうの画像（jpg・png・gif・webp・bmp）は今までどおり保存できる。
/// </summary>
public sealed class ImageFormatLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-imgfmt-" + Guid.NewGuid().ToString("N"));
    private readonly ImagePipeline _images;

    public ImageFormatLimitTests()
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new BoothClient(new HttpClient(new NoNetwork()), settings, (_, _) => Task.CompletedTask);
        _images = new ImagePipeline(client, paths, settings);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static byte[] Encode(Action<Image<Rgba32>, MemoryStream> save, Action<Image<Rgba32>>? change = null)
    {
        using var image = new Image<Rgba32>(8, 8, new Rgba32(200, 120, 40));
        change?.Invoke(image);
        using var stream = new MemoryStream();
        save(image, stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task TIFFの画像は読めない画像として断る()
    {
        var tiff = Encode((image, stream) => image.Save(stream, new TiffEncoder()));

        Assert.Null(await _images.SaveUserImageAsync("1000001", tiff));
    }

    [Fact]
    public async Task ふつうの形式は今までどおり保存できる()
    {
        Assert.NotNull(await _images.SaveUserImageAsync("1000001", Encode((image, stream) => image.SaveAsPng(stream))));
        Assert.NotNull(await _images.SaveUserImageAsync("1000002", Encode((image, stream) => image.SaveAsJpeg(stream))));
        Assert.NotNull(await _images.SaveUserImageAsync("1000003", Encode((image, stream) => image.SaveAsGif(stream))));
        Assert.NotNull(await _images.SaveUserImageAsync("1000004", Encode((image, stream) => image.SaveAsWebp(stream))));
        Assert.NotNull(await _images.SaveUserImageAsync("1000005", Encode((image, stream) => image.SaveAsBmp(stream))));
    }

    /// <summary>メタデータは読まない。壊れた ICC のカラープロファイルが付いていても、画素だけを読んで保存する</summary>
    [Fact]
    public async Task 壊れたICCが付いていても画素だけ読んで保存する()
    {
        var png = Encode(
            (image, stream) => image.SaveAsPng(stream),
            image => image.Metadata.IccProfile = new IccProfile(new byte[200]));

        Assert.NotNull(await _images.SaveUserImageAsync("1000001", png));
    }

    [Fact]
    public void 復号の設定はTIFFを知らずメタデータを読まない()
    {
        var options = ImageLimits.FirstFrame;

        Assert.True(options.SkipMetadata);
        Assert.Equal(1u, options.MaxFrames);
        Assert.DoesNotContain(options.Configuration.ImageFormats, format => format.Name == "TIFF");
        Assert.Contains(options.Configuration.ImageFormats, format => format.Name == "JPEG");
    }
}
