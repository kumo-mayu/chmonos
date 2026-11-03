using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>出すはずだった Windows の通知の中身（<see cref="WindowsToast.Intercept"/> が受ける）。</summary>
internal sealed record ToastRequest(string Title, string Text);

/// <summary>
/// 画面の右下に Windows の通知を出す（設定「Unityへ送るとき、確認を出さずに送り…」・ユーザ判断 2026-11-03）。
///
/// **通知領域のアイコンの風船（Shell_NotifyIcon の NIF_INFO）で出す。**Windows 10・11 では、これが通知として右下に出る
/// （2026-11-03 にこの PC の Windows 11 で確かめた）。WinRT の通知は、出す前にアプリの登録（スタートメニューの
/// ショートカットとレジストリ）が要り、依存も増えるので採らなかった。
///
/// アイコンは出している間だけ足し、しばらくして消す（常駐のアイコンを増やさない）。消すと右下の表示も消える（同日に確かめた）。
/// 続けて出したら同じアイコンの文を差し替え、消すまでの時間を延ばす。
/// Windows の設定で通知を切っている・集中モードのときは出ない（それでも送った結果は窓に戻さない：
/// 窓を出さないことを選んだ人の設定なので、Windows の側の選び方に従う）
/// </summary>
internal static class WindowsToast
{
    /// <summary>
    /// 出してから消すまで。Windows が右下に出しておく時間（既定で約5秒）より長くして、出ている途中で消さない。
    /// 長すぎると、消えるまでの間に通知センターに古い文が残る
    /// </summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(10);

    private static HwndSource? _source;
    private static IntPtr _icon;
    private static DispatcherTimer? _timer;

    /// <summary>
    /// 出す代わりに中身を受ける口。**アプリでは null のまま**（通知を出す）。
    /// 試験と、窓を出さずに画面を描く台（tools/ViewShot）が入れる：作業している人の画面の右下に出てしまう
    /// </summary>
    internal static Action<ToastRequest>? Intercept { get; set; }

    /// <summary>通知を出す。出せなかったら false（通知領域に足せなかった）。画面のスレッドで呼ぶ。</summary>
    public static bool Show(string title, string text)
    {
        if (UiTrace.IsOn)
        {
            UiTrace.Write("通知", $"「{title}」{text}");
        }

        if (Intercept is { } intercept)
        {
            intercept(new ToastRequest(title, text));
            return true;
        }

        try
        {
            var added = _source is not null;
            if (_source is null)
            {
                // 見えない窓（画面にもタスクバーにも出ない・Alt+Tab にも出ない）。アイコンの持ち主が要る
                _source = new HwndSource(new HwndSourceParameters("ChmonosToast") { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = WsExToolWindow });
                ExtractIconEx(Environment.ProcessPath ?? string.Empty, 0, IntPtr.Zero, out _icon, 1);
                var icon = Data(NifIcon | NifTip);
                added = Shell_NotifyIcon(NimAdd, ref icon);
                if (!added)
                {
                    Remove();
                    return false;
                }
            }

            var info = Data(NifInfo);

            // 風船の欄の長さの決まり（題は63字・本文は255字まで。越えると Windows が断る）
            info.szInfoTitle = Clip(title, 63);
            info.szInfo = Clip(text, 255);

            // 「i」の印。アプリのアイコンを大きく出す印（NIIF_USER）は、大きいアイコンを別に渡さないと断られた（2026-11-03）
            info.dwInfoFlags = NiifInfo;
            if (!Shell_NotifyIcon(NimModify, ref info))
            {
                Remove();
                return false;
            }

            _timer ??= new DispatcherTimer { Interval = Hold };
            _timer.Tick -= OnTimer;
            _timer.Tick += OnTimer;
            _timer.Stop();
            _timer.Start();
            return true;
        }
        catch (Exception exception) when (exception is ExternalException or InvalidOperationException)
        {
            Remove();
            return false;
        }
    }

    /// <summary>出している通知を片付ける（アプリを閉じるとき）。</summary>
    public static void Clear()
    {
        _timer?.Stop();
        Remove();
    }

    private static void OnTimer(object? sender, EventArgs e)
    {
        _timer?.Stop();
        Remove();
    }

    private static void Remove()
    {
        if (_source is null)
        {
            return;
        }

        var data = Data(0);
        Shell_NotifyIcon(NimDelete, ref data);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        _source.Dispose();
        _source = null;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static NotifyIconData Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _source?.Handle ?? IntPtr.Zero,
        uID = ToastIconId,
        uFlags = flags,
        hIcon = _icon,
        szTip = "Chmonos",
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    /// <summary>画面無しのときの常駐のアイコン（<see cref="TrayIcon"/> は 1）と取り違えない番号。持ち主の窓も別なので重ならないが、念のため分ける。</summary>
    private const int ToastIconId = 2;

    /// <summary>WS_EX_TOOLWINDOW。Alt+Tab の一覧に出さない。</summary>
    private const int WsExToolWindow = 0x80;

    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int NifInfo = 0x10;
    private const int NiifInfo = 0x1;

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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr large, out IntPtr small, int count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
