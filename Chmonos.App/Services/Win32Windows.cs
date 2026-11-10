using System.Runtime.InteropServices;
using System.Text;

namespace Chmonos.App.Services;

/// <summary>
/// ほかのアプリ（Unity・VCC・ALCOM）とこのアプリの窓を、Windows に直接たずねる・動かす所（R9・2026-11-10）。
///
/// 前は手前に出す・クラス名・題・持ち主のプロセスを読む宣言が6つのファイルに写され、題を読む長さも 64〜512 と揃わず、
/// 「最小化のときだけ戻す」も2か所だけで、残りの2か所は最大化した窓にも戻す合図を送って最大化を解いていた（B10）。
/// ここに1つずつ置き、呼び手は意味の名前（<see cref="BringToFront"/> など）で呼ぶ。
/// 窓の位置とモニターは <see cref="WindowNative"/>（このアプリの窓だけを扱う別の話題）
/// </summary>
internal static class Win32Windows
{
    /// <summary>Unity のエディタの窓（主の窓と浮かせたタブ・取り込みの窓）の種類の名前。</summary>
    public const string UnityContainerClass = "UnityContainerWndClass";

    /// <summary><c>SW_RESTORE</c>。最小化・最大化を解いて元の大きさに戻す。だから最小化のときだけ送る。</summary>
    private const int SwRestore = 9;

    /// <summary>
    /// 題を読む長さ。Unity の主の窓の題は「プロジェクト名 - シーン - 機種 - Unity 版 &lt;DX11&gt;」で、
    /// 深いフォルダのプロジェクトでも 512 字に収まる（前の置き場で一番長かった値）。切れると題での照合を外す
    /// </summary>
    private const int TitleCapacity = 512;

    /// <summary>クラス名を読む長さ。Windows のクラス名は 256 字まで（WNDCLASS の lpszClassName の上限）。</summary>
    private const int ClassCapacity = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public NativeRect CaretRect;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    /// <summary>手前に出す。Windows が断ったら false（後ろのプロセスからはタスクバーが点滅するだけのことがある）。</summary>
    public static bool SetForeground(IntPtr window) => SetForegroundWindow(window);

    /// <summary>
    /// 最小化されていれば元の大きさに戻す。戻したら true。
    /// **最小化でなければ何もしない**：戻す合図は最大化も解くので、最大化した Unity・VCC を手前に出すだけで小さくなっていた（B10）
    /// </summary>
    public static bool RestoreIfMinimized(IntPtr window)
    {
        if (!IsIconic(window))
        {
            return false;
        }

        ShowWindow(window, SwRestore);
        return true;
    }

    /// <summary>最小化のときだけ戻して、手前に出す。出せたかを返す。</summary>
    public static bool BringToFront(IntPtr window)
    {
        RestoreIfMinimized(window);
        return SetForegroundWindow(window);
    }

    /// <summary>窓を持つプロセスの番号。</summary>
    public static uint ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return processId;
    }

    /// <summary>窓を作った画面のスレッドの番号（無い窓なら 0）。</summary>
    public static uint ThreadOf(IntPtr window) => GetWindowThreadProcessId(window, out _);

    /// <summary>その窓の画面のスレッドで、いまフォーカスを持っている窓。別のプロセスでも Windows が答える。無ければ 0。</summary>
    public static IntPtr FocusOf(IntPtr window)
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(ThreadOf(window), ref info) ? info.Focus : IntPtr.Zero;
    }

    public static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(ClassCapacity);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    public static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(TitleCapacity);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }
}
