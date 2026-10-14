using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Chmonos.Core.Images;

/// <summary>画像を書き直す先の形式。</summary>
public enum ImageOutputFormat
{
    Png,
    Jpeg,
}

/// <summary>書き直した画像（中身と、その形式の MIME の型）。</summary>
public sealed record ConvertedImage(byte[] Bytes, string ContentType);

/// <summary>
/// 保存した画像を、外の道具が読める形式・大きさに書き直す（外から使う API の画像の道。Unity の API-GAPS 3-1：
/// Unity の <c>Texture2D.LoadImage</c> は PNG と JPEG しか読めず、保存の形式の WebP を読めない）。
///
/// 復号は <see cref="ImageLimits.Safe"/> を通す（形式を絞り・メタデータを読まない。脆弱性の対策で、生の <c>Image.Load</c> は書かない）。
/// 縮め方は保存のとき（<see cref="ImagePipeline"/> の Encode）と同じ：頭だけ読んで寸法の上限で断り、目標より大きいときだけ縮めながら復号し、
/// 残りを <see cref="ResizeMode.Max"/> で合わせる。拡大はしない。
/// </summary>
public static class ImageConversion
{
    /// <summary>
    /// 頼める長辺の下限。一覧の小さな札でも 16px より小さい絵は使い道が無く、0 や負の数の誤りを早く知らせるため
    /// </summary>
    public const int MinEdge = 16;

    /// <summary>
    /// 頼める長辺の上限。保存する画像は長辺 384px（改変 768px・店の帯 1200px）が既定で、拡大はしないので、2048px より大きく頼む理由が無い。
    /// 大きな数で縮めずに復号し直させる無駄も防ぐ
    /// </summary>
    public const int MaxEdge = 2048;

    /// <summary>
    /// JPEG の画質。保存の WebP（既定の画質）から書き直すので、これより上げても元より良くはならない。
    /// 下げると一覧の札で縁のにじみが目立つ
    /// </summary>
    private const int JpegQuality = 85;

    /// <param name="format">書き直す形式。null なら元の形式のまま（大きさだけ変える）。</param>
    /// <param name="maxEdge">長辺の画素。null なら元の大きさ。<see cref="MinEdge"/>〜<see cref="MaxEdge"/> の外は呼ぶ側で断る。</param>
    /// <exception cref="InvalidImageContentException">寸法が大きすぎる画像。</exception>
    /// <exception cref="UnknownImageFormatException">読めない形式。</exception>
    public static ConvertedImage Convert(byte[] bytes, ImageOutputFormat? format, int? maxEdge)
    {
        if (maxEdge is { } edge && edge is < MinEdge or > MaxEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEdge), edge, $"長辺は {MinEdge}〜{MaxEdge} です。");
        }

        var header = Image.Identify(ImageLimits.FirstFrame, bytes);
        if (ImageLimits.IsTooLarge(header))
        {
            throw new InvalidImageContentException($"寸法が大きすぎる画像（{header.Width}×{header.Height}）");
        }

        // 目標より小さい絵に目標を渡すと復号の側が引き伸ばすので、大きいときだけ渡す（ImagePipeline と同じ）
        var shrink = maxEdge is { } target && (header.Width > target || header.Height > target);
        using var image = shrink
            ? Image.Load<Rgba32>(ImageLimits.Safe(new Size(maxEdge!.Value, maxEdge.Value)), bytes)
            : Image.Load<Rgba32>(ImageLimits.FirstFrame, bytes);

        if (maxEdge is { } limit && (image.Width > limit || image.Height > limit))
        {
            image.Mutate(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(limit, limit) }));
        }

        IImageEncoder encoder;
        string contentType;
        switch (format)
        {
            case ImageOutputFormat.Png:
                encoder = new PngEncoder();
                contentType = "image/png";
                break;
            case ImageOutputFormat.Jpeg:
                // JPEG は透過を持てない。保存の WebP は透けた所の色を消してある（黒になる）ので、白で埋めてから書く
                image.Mutate(context => context.BackgroundColor(Color.White));
                encoder = new JpegEncoder { Quality = JpegQuality };
                contentType = "image/jpeg";
                break;
            default:
                var decoded = header.Metadata.DecodedImageFormat
                    ?? throw new UnknownImageFormatException("元の形式が分かりません。");
                encoder = image.Configuration.ImageFormatsManager.GetEncoder(decoded);
                contentType = decoded.DefaultMimeType;
                break;
        }

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return new ConvertedImage(stream.ToArray(), contentType);
    }
}
