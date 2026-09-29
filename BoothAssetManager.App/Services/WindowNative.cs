using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 窓の位置とモニターの作業領域を、Windows に直接聞く所。
///
/// WPF の Left・Top・Width・Height は DIP で、モニターごとの拡大率（Per-Monitor V2）では
/// **窓のあるモニターの拡大率で割った値**になる。100% のモニターの右に 150% のモニターがあると、
/// 右のモニターの左端（1920px）は DIP で 1280 になり、左のモニターの中の 1280 と区別できない。
/// 窓の位置を覚えて戻すのと、小窓を作業領域に収めるのは、画素（物理ピクセル）で行う。
/// </summary>
internal static class WindowNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public NativePoint MinPosition;
        public NativePoint MaxPosition;
        public NativeRect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    private const int SwHide = 0;
    private const int SwShowMaximized = 3;
    private const int MonitorDefaultToNull = 0;
    private const int MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowPlacement(IntPtr window, ref NativeWindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPlacement(IntPtr window, ref NativeWindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// 今の姿（最大化を解除したときの矩形・画素）と、最大化しているか。
    /// 最大化中・最小化中でも「元に戻したときの矩形」が返る（Windows が持っている）。
    /// </summary>
    public static (NativeRect Normal, bool Maximized)? ReadPlacement(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var placement = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
        if (!GetWindowPlacement(handle, ref placement))
        {
            return null;
        }

        return (placement.NormalPosition, placement.ShowCmd == SwShowMaximized);
    }

    /// <summary>
    /// 覚えた矩形（画素）へ窓を置く。窓を出す前（SourceInitialized）に呼ぶので、隠したまま置く
    /// （出すのと最大化は WPF の Show と WindowState に任せる）。
    ///
    /// **2回置く。**窓は作った時点のモニター（主のモニター）の拡大率で作られていて、拡大率の違うモニターへ
    /// 置くと、WPF が「DIP の大きさを保つ」ように画素の大きさを掛け直す（1回目で大きさがずれる）。
    /// 移った後にもう一度置けば、拡大率はもう変わらないので、覚えた画素のまま収まる。
    /// </summary>
    public static void ApplyPlacement(Window window, NativeRect normal)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var placement = new NativeWindowPlacement
        {
            Length = Marshal.SizeOf<NativeWindowPlacement>(),
            ShowCmd = SwHide,
            NormalPosition = normal,
        };

        SetWindowPlacement(handle, ref placement);
        SetWindowPlacement(handle, ref placement);
    }

    /// <summary>
    /// 矩形（画素）が今つながっているモニターのどれかに掛かっているか。
    /// モニターを外したり並びを変えたりすると、覚えた場所が画面の外になり、タイトルバーを掴めなくなる。
    /// </summary>
    public static bool IsOnAnyMonitor(NativeRect rect)
        => MonitorFromRect(ref rect, MonitorDefaultToNull) != IntPtr.Zero;

    /// <summary>
    /// 窓のあるモニターの作業領域（タスクバーを除いた所）を、そのモニターの DIP で返す。
    /// 窓がまだ無ければ（作る前）null。
    /// </summary>
    public static Size? WorkAreaDip(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var scale = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        return new Size(info.Work.Width / scale, info.Work.Height / scale);
    }
}
