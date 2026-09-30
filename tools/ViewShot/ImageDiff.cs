using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ViewShot;

/// <summary>2枚を比べた結果。</summary>
/// <param name="DifferentPixels">違う画素の数。大きさが違うときは、重ならない所も違う画素に数える。</param>
/// <param name="Bounds">違う画素を全部含む四角（画素）。同じなら null。</param>
/// <param name="MaxDelta">色の差のいちばん大きい値（0〜255。RGBA のどれか）。1〜2 なら丸めの差、大きければ形か色が違う。</param>
internal sealed record DiffResult(
    int BeforeWidth, int BeforeHeight, int AfterWidth, int AfterHeight, int DifferentPixels, Int32Rect? Bounds, int MaxDelta)
{
    public bool SameSize => BeforeWidth == AfterWidth && BeforeHeight == AfterHeight;

    public bool IsSame => SameSize && DifferentPixels == 0;

    public long TotalPixels => (long)Math.Max(BeforeWidth, AfterWidth) * Math.Max(BeforeHeight, AfterHeight);

    public string Describe()
    {
        if (IsSame)
        {
            return $"同じ（{BeforeWidth}x{BeforeHeight}）";
        }

        var size = SameSize ? $"{BeforeWidth}x{BeforeHeight}" : $"大きさが違う {BeforeWidth}x{BeforeHeight} → {AfterWidth}x{AfterHeight}";
        var where = Bounds is { } box ? $"範囲 x={box.X} y={box.Y} 幅={box.Width} 高さ={box.Height}" : "範囲なし";
        return $"違う：{DifferentPixels:N0} 画素（{100.0 * DifferentPixels / TotalPixels:0.###}%）・{where}・色の差の最大 {MaxDelta}・{size}";
    }
}

/// <summary>
/// 前の版と後の版の画像を比べる。**見た目を変えない直し（名前を足す・作りを替える）で、本当に変わっていないことを確かめる**ための物。
/// 目で2枚を見比べても、1px のずれや薄い色の違いは見落とす。
/// </summary>
internal static class ImageDiff
{
    /// <summary>
    /// 比べる。<paramref name="tolerance"/> は色の差をここまでは同じとみなす幅（既定 0＝1でも違えば違う）。
    /// 台の描画は同じ入力なら画素まで同じになるので、既定は 0 でよい。実際の窓を撮った画像（文字の縁の出方が毎回少し違う）を比べるときだけ上げる
    /// </summary>
    public static DiffResult Compare(BitmapSource before, BitmapSource after, int tolerance = 0)
    {
        var first = Read(before);
        var second = Read(after);
        var width = Math.Max(before.PixelWidth, after.PixelWidth);
        var height = Math.Max(before.PixelHeight, after.PixelHeight);

        var count = 0;
        var maxDelta = 0;
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var delta = Delta(first, before, second, after, x, y);
                if (delta <= tolerance)
                {
                    continue;
                }

                count++;
                maxDelta = Math.Max(maxDelta, delta);
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Int32Rect? bounds = count > 0 ? new Int32Rect(left, top, right - left + 1, bottom - top + 1) : null;
        return new DiffResult(before.PixelWidth, before.PixelHeight, after.PixelWidth, after.PixelHeight, count, bounds, maxDelta);
    }

    public static DiffResult Compare(string beforePath, string afterPath, int tolerance = 0)
        => Compare(Load(beforePath), Load(afterPath), tolerance);

    /// <summary>
    /// 前・後・違う所（赤）を横に並べた1枚を書く。どこが違うかを目で見るため。
    /// 3枚目は後の版を薄くした上に、違う画素を赤で塗る（場所が分かるように、元の絵を薄く残す）
    /// </summary>
    public static void SaveSideBySide(string beforePath, string afterPath, string outPath, int tolerance = 0)
    {
        var before = Load(beforePath);
        var after = Load(afterPath);
        var first = Read(before);
        var second = Read(after);
        var width = Math.Max(before.PixelWidth, after.PixelWidth);
        var height = Math.Max(before.PixelHeight, after.PixelHeight);

        const int Gap = 8;
        var total = width * 3 + Gap * 2;
        var stride = total * 4;
        var canvas = new byte[stride * height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Put(canvas, stride, x, y, Pixel(first, before, x, y));
                var late = Pixel(second, after, x, y);
                Put(canvas, stride, width + Gap + x, y, late);

                var differs = Delta(first, before, second, after, x, y) > tolerance;
                var mark = differs
                    ? (B: (byte)40, G: (byte)40, R: (byte)230, A: (byte)255)
                    : (B: Fade(late.B), G: Fade(late.G), R: Fade(late.R), A: (byte)255);
                Put(canvas, stride, (width + Gap) * 2 + x, y, mark);
            }
        }

        var image = BitmapSource.Create(total, height, 96, 96, PixelFormats.Bgra32, null, canvas, stride);
        image.Freeze();
        Stage.Save(image, outPath);
    }

    // 元の絵を白へ 3/4 寄せる。赤の印が埋もれない薄さで、どの部品の上かは読める
    private static byte Fade(byte channel) => (byte)(255 - (255 - channel) / 4);

    private static void Put(byte[] canvas, int stride, int x, int y, (byte B, byte G, byte R, byte A) color)
    {
        var offset = y * stride + x * 4;
        canvas[offset] = color.B;
        canvas[offset + 1] = color.G;
        canvas[offset + 2] = color.R;
        canvas[offset + 3] = color.A;
    }

    private static int Delta(byte[] first, BitmapSource before, byte[] second, BitmapSource after, int x, int y)
    {
        var inFirst = x < before.PixelWidth && y < before.PixelHeight;
        var inSecond = x < after.PixelWidth && y < after.PixelHeight;
        if (!inFirst || !inSecond)
        {
            // 片方にしか無い所は、いちばん大きい差として数える
            return 255;
        }

        var a = y * before.PixelWidth * 4 + x * 4;
        var b = y * after.PixelWidth * 4 + x * 4;
        var delta = 0;
        for (var channel = 0; channel < 4; channel++)
        {
            delta = Math.Max(delta, Math.Abs(first[a + channel] - second[b + channel]));
        }

        return delta;
    }

    private static (byte B, byte G, byte R, byte A) Pixel(byte[] pixels, BitmapSource image, int x, int y)
    {
        if (x >= image.PixelWidth || y >= image.PixelHeight)
        {
            return (255, 255, 255, 255);
        }

        var offset = y * image.PixelWidth * 4 + x * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static byte[] Read(BitmapSource image)
    {
        // 形式を揃える（PNG は保存した形式で読まれる）。透けの掛け算をしない形で比べる
        var converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        return Stage.Pixels(converted);
    }

    public static BitmapSource Load(string path)
    {
        // ファイルを掴んだままにしない（比べた後で上書きできるように、全部読んでから閉じる）
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
