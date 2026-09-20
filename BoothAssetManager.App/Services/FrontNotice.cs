using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BoothAssetManager.App.Services;

/// <summary>
/// Unity を操作した後の知らせを、主の窓を手前に戻してから、主の窓を持ち主にして出す（ユーザ指示 2026-09-19）。
///
/// 送る・選ぶの間は Unity が手前に出ている。持ち主の無い知らせは Unity の後ろに出て見えず、
/// 閉じるまで次の操作が止まったように見えた（検索の複数選択の「Unityへ順に送る」の終わりの知らせ。2026-09-19 に確かめた）
/// </summary>
internal static class FrontNotice
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool doAttach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);

    public static MessageBoxResult Show(
        string text,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.Information)
    {
        if (Application.Current?.MainWindow is not { IsLoaded: true } owner)
        {
            return Notice.Show(text, title, buttons, image);
        }

        BringToFront(owner);
        return Notice.Show(owner, text, title, buttons, image);
    }

    /// <summary>
    /// 後ろにいるプロセスは <c>SetForegroundWindow</c> だけでは前に出られない（Windows がタスクバーを点滅させるだけにする）。
    /// 今手前にある窓のスレッドの入力に一時的につなぐと出られる
    /// </summary>
    private static void BringToFront(Window owner)
    {
        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        var handle = new WindowInteropHelper(owner).Handle;
        var foreground = GetForegroundWindow();
        if (foreground == handle)
        {
            return;
        }

        var me = GetCurrentThreadId();
        var them = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        var attached = them != 0 && them != me && AttachThreadInput(me, them, true);
        try
        {
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            owner.Activate();
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(me, them, false);
            }
        }
    }
}
