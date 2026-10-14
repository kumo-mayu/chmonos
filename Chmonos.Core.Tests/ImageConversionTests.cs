using Chmonos.Core.Images;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Chmonos.Core.Tests;

/// <summary>保存した画像を外の道具が読める形式・大きさに書き直す（外から使う API の画像の道。Unity の API-GAPS 3-1）。</summary>
public sealed class ImageConversionTests
{
    /// <summary>保存の形式と同じ WebP の作り物（横長・全面が透けている）。</summary>
    private static byte[] Webp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.Save(stream, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless });
        return stream.ToArray();
    }

    [Fact]
    public void WebPをPNGとJPEGに書き直し_長い辺を縮める()
    {
        var png = ImageConversion.Convert(Webp(64, 48), ImageOutputFormat.Png, 16);
        Assert.Equal("image/png", png.ContentType);
        var info = Image.Identify(png.Bytes);
        Assert.IsType<PngFormat>(info.Metadata.DecodedImageFormat);
        Assert.Equal((16, 12), (info.Width, info.Height));

        var jpeg = ImageConversion.Convert(Webp(48, 64), ImageOutputFormat.Jpeg, 32);
        Assert.Equal("image/jpeg", jpeg.ContentType);
        info = Image.Identify(jpeg.Bytes);
        Assert.IsType<JpegFormat>(info.Metadata.DecodedImageFormat);
        Assert.Equal((24, 32), (info.Width, info.Height));
    }

    [Fact]
    public void 拡大はせず_大きさを省けば元の大きさ()
    {
        Assert.Equal((64, 48), Size(ImageConversion.Convert(Webp(64, 48), ImageOutputFormat.Png, 1024)));
        Assert.Equal((64, 48), Size(ImageConversion.Convert(Webp(64, 48), ImageOutputFormat.Png, null)));

        static (int, int) Size(ConvertedImage image)
        {
            var info = Image.Identify(image.Bytes);
            return (info.Width, info.Height);
        }
    }

    [Fact]
    public void 形式を省けば_元の形式のまま縮める()
    {
        var converted = ImageConversion.Convert(Webp(64, 48), null, 32);
        Assert.Equal("image/webp", converted.ContentType);
        Assert.IsType<WebpFormat>(Image.Identify(converted.Bytes).Metadata.DecodedImageFormat);
    }

    [Fact]
    public void JPEGは透けた所を白で埋める()
    {
        var jpeg = ImageConversion.Convert(Webp(16, 16), ImageOutputFormat.Jpeg, null);
        using var image = Image.Load<Rgb24>(jpeg.Bytes);
        var pixel = image[8, 8];
        Assert.True(pixel.R > 240 && pixel.G > 240 && pixel.B > 240, $"白でない: {pixel}");
    }

    [Fact]
    public void 読めない画像と範囲の外の大きさは断る()
    {
        Assert.ThrowsAny<ImageFormatException>(() => ImageConversion.Convert([1, 2, 3], ImageOutputFormat.Png, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageConversion.Convert(Webp(16, 16), ImageOutputFormat.Png, ImageConversion.MinEdge - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageConversion.Convert(Webp(16, 16), ImageOutputFormat.Png, ImageConversion.MaxEdge + 1));
    }
}
