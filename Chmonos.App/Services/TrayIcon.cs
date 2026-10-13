using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Chmonos.App.Services;

/// <summary>
/// 画面を出さずに動いている間の、通知領域のアイコン（ユーザ判断 2026-10-13：右クリックで「Chmonosを開く」「終了」）。
/// WPF には通知領域の部品が無い。WinForms の NotifyIcon を使うと WPF の型と名前がぶつかるので、Windows の機能を直に呼ぶ。
/// メニューも Windows の標準のメニュー（ほかの常駐するアプリと同じ見た目と操作）。
/// 知らせを受けるのは、見えない受け口の窓（メッセージだけを受ける窓）。エクスプローラが落ちて通知領域が作り直されたら、出し直す。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int OpenCommand = 1;
    private const int ExitCommand = 2;

    private readonly HwndSource _source;
    private readonly int _taskbarCreated;
    private readonly IntPtr _icon;
    private readonly string _tip;
    private bool _added;

    public TrayIcon(string tip)
    {
        _tip = tip;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        // 見えない普通の窓（画面にもタスクバーにも出ない）。メッセージ専用の窓（HWND_MESSAGE）にしないのは、
        // エクスプローラが作り直されたときの知らせ（TaskbarCreated）が全部の窓への知らせで、メッセージ専用の窓には届かないため（点検36）
        _source = new HwndSource(new HwndSourceParameters("ChmonosTray") { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = WsExToolWindow });
        _source.AddHook(OnMessage);

        // 実行ファイルに埋めたアイコンの小さい方（通知領域の大きさ）
        ExtractIconEx(Environment.ProcessPath ?? string.Empty, 0, IntPtr.Zero, out _icon, 1);
        Add();
    }

    /// <summary>「Chmonosを開く」（アイコンのダブルクリックも同じ）。</summary>
    public event Action? OpenRequested;

    /// <summary>「終了」。</summary>
    public event Action? ExitRequested;

    private void Add()
    {
        var data = Data();
        _added = Shell_NotifyIcon(NimAdd, ref data);
    }

    private NotifyIconData Data() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _source.Handle,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated)
        {
            Add();
            return IntPtr.Zero;
        }

        if (msg != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        switch ((int)lParam & 0xFFFF)
        {
            case WmLButtonDblClk:
                OpenRequested?.Invoke();
                handled = true;
                break;
            case WmRButtonUp:
                ShowMenu();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, 0, OpenCommand, "Chmonosを開く");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, 0, ExitCommand, "終了");
            SetMenuDefaultItem(menu, OpenCommand, 0);

            // 前に出しておかないと、メニューの外を押しても閉じない（Windows の決まり）
            SetForegroundWindow(_source.Handle);
            GetCursorPos(out var point);
            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton, point.X, point.Y, _source.Handle, IntPtr.Zero);
            switch (chosen)
            {
                case OpenCommand:
                    OpenRequested?.Invoke();
                    break;
                case ExitCommand:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data();
            Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        _source.Dispose();
    }

    /// <summary>WS_EX_TOOLWINDOW。Alt+Tab の一覧に出さない。</summary>
    private const int WsExToolWindow = 0x80;

    private const int NimAdd = 0;
    private const int NimDelete = 2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int WmLButtonDblClk = 0x203;
    private const int WmRButtonUp = 0x205;
    private const int MfSeparator = 0x800;
    private const int TpmReturnCmd = 0x100;
    private const int TpmRightButton = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public int dwState;
        public int dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public int uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, int flags, int id, string? text);

    [DllImport("user32.dll")]
    private static extern bool SetMenuDefaultItem(IntPtr menu, int item, int byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
