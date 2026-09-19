using System.Runtime.InteropServices;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 別のプロセスの窓の絵を画素で取る。Unity の取り込み画面が「Nothing to import!」かを見るため（<see cref="Core.Services.UnityImportWindowLook"/>）。
///
/// 画面から写すのではなく、窓に描かせる（<c>PrintWindow</c> の <c>PW_RENDERFULLCONTENT</c>）。取り込み画面は Chmonos の後ろに出ることが多く、
/// 画面から写すと手前の窓が写った（2026-09-19 に確かめた。この指定なら後ろにあっても Unity の中身が取れた）
/// </summary>
internal static class WindowPicture
{
    private const uint PwRenderFullContent = 2;
    private const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, int[] bits, ref BitmapInfoHeader info, uint usage);

    /// <summary>窓の絵（上の行から順・0xAARRGGBB）。取れなければ null（最小化・閉じた・大きすぎる）。</summary>
    public static (int Width, int Height, int[] Pixels)? Capture(IntPtr window)
    {
        if (IsIconic(window) || !GetWindowRect(window, out var rect))
        {
            return null;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        // 取り込み画面は 366×589 ほど。画面いっぱいより大きい物は窓の取り違えなので取らない
        if (width <= 0 || height <= 0 || width > 8000 || height > 8000)
        {
            return null;
        }

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(window, memory, PwRenderFullContent))
            {
                return null;
            }

            SelectObject(memory, old);
            var info = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height, // 負にすると上の行から並ぶ
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new int[width * height];
            return GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, DibRgbColors) == height
                ? (width, height, pixels)
                : null;
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
