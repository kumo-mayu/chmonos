using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace Chmonos.Core.Images;

/// <summary>
/// 画像を復号する前の上限（外部の点検 2026-10-06）。
/// 圧縮すると小さいのに寸法がとても大きい画像（20,000×20,000 なら画素だけで約1.6GB）を、
/// 頭だけ読んで断る。動く画像は1コマ目だけ読む（サムネイルにも保存にも1コマ目しか使わない）。
/// </summary>
public static class ImageLimits
{
    /// <summary>
    /// 画素の数の上限。BOOTH の商品画像は長辺3,000px級（約900万画素）で、カメラの写真でも約5,000万画素。
    /// その上の 8,192×8,192 にした。Bgra32 で復号すると一時に約256MB で、それ以上は断る
    /// </summary>
    public const long MaxPixels = 8192L * 8192L;

    public static bool IsTooLarge(ImageInfo info) => (long)info.Width * info.Height > MaxPixels;

    /// <summary>
    /// 読む形式を絞った設定（2026-10-08・ユーザ判断「Aで良いでしょう」）。ImageSharp 3.1.12 に、BigTIFF で復号が止まり続ける脆弱性の知らせが出た
    /// （GHSA-wmxv-xphr-5c9g。直ったのは 4.1.2 だけで、4.x は配る物を組むのにライセンスキーが要る）。BOOTH の画像は jpg・png・gif・webp で、
    /// 自分で足す画像もこの範囲で足りるので、TIFF などは受けない（読めない画像として扱う）。
    /// 画像を縮める作業領域は、アプリが起動のときに既定の設定へ入れた物を使う（App.OnStartup。初めて使うときに写す）
    /// </summary>
    private static readonly Lazy<Configuration> Formats = new(() => new Configuration(
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new GifConfigurationModule(),
        new WebpConfigurationModule(),
        new BmpConfigurationModule())
    {
        MemoryAllocator = Configuration.Default.MemoryAllocator,
    });

    /// <summary>
    /// 復号の設定の元。形式を絞り（<see cref="Formats"/>）、**メタデータを読まない**——ICC のカラープロファイルの解析で大きなメモリを取る
    /// 脆弱性の知らせ（GHSA-gwg2-r3hj-4w44）が出た。保存も表示も画素しか使わないので、EXIF・ICC・XMP は要らない
    /// </summary>
    public static DecoderOptions Safe(Size? targetSize = null, IResampler? sampler = null) => new()
    {
        Configuration = Formats.Value,
        SkipMetadata = true,
        MaxFrames = 1,
        TargetSize = targetSize,
        Sampler = sampler ?? KnownResamplers.Bicubic,
    };

    /// <summary>1コマ目だけを、絞った形式とメタデータなしで読む復号の設定。縮めながら読むときは <see cref="Safe"/> に大きさを渡す。</summary>
    public static DecoderOptions FirstFrame => Safe();
}
