using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

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

    /// <summary>1コマ目だけを読む復号の設定。縮めながら読むときは、同じく MaxFrames = 1 を付けた設定を作る。</summary>
    public static DecoderOptions FirstFrame { get; } = new() { MaxFrames = 1 };
}
