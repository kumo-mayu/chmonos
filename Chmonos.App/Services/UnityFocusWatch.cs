using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Chmonos.App.Services;

/// <summary>
/// 利用者が Unity の中で最後に選んでいたプロジェクトタブを覚える。
///
/// 改変の画面の「Unityで選択」は、**最後に選んでいたプロジェクトタブで開く**（ユーザ判断 2026-09-13）。
/// Unity 自身もこれを覚えているが（<c>s_LastInteractedProjectBrowser</c>）、外からは読めず、メニューで開くときにも使われない
/// （<c>docs/history/unity-handoff.md</c> §13-5）。そこで OS の知らせ（アクセシビリティ用の WinEvent）を見張って、こちらで覚える。
///
/// **何を「選んだ」と数えるか**（2026-09-13 実機で確かめた・§13-7）：
/// - **マウスをつかんだ知らせ**（<c>EVENT_SYSTEM_CAPTURESTART</c>）がプロジェクトタブで出たら覚える。Unity の画面の部品は
///   押下のたびにマウスをつかむので、**フォーカスが変わらないクリックでも出る**。フォーカスの知らせだけだと、
///   こちらの操作で最後にフォーカスが残ったタブを利用者がクリックしても気付けなかった
/// - **フォーカスの知らせ**（キーボードで移ったとき）も覚える。ただし Unity が前面に来た直後の分は数えない——
///   前にフォーカスがあった部品に戻っただけで、利用者が選んだとは限らない（こちらの操作が残したタブに戻ることがある）
///
/// そのほか：
/// - **プロセスを決めずに見張る。**アプリより後に起動した Unity や、開き直した Unity にもそのまま効く
/// - 覚えるのは Unity ごと（プロセス番号ごと）。Unity を2つ開いていても混ざらない
/// - 押した瞬間に Unity に聞く道は使えない。Unity が裏にあると「フォーカス無し」が返る（§13-6）
/// - アプリ自身の操作（フォーカスを渡すための押下）で書き換わらないよう、操作の間は止める（<see cref="Suppress"/>）
/// - キーボードやマウスを丸ごと見張る仕組み（低レベルのフック）は使わない。セキュリティソフトに怪しまれやすい
///
/// 知らせは付けたスレッド（画面のスレッド）で届くので、覚えた値は画面のスレッドからだけ触る。
/// </summary>
public static class UnityFocusWatch
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemCaptureStart = 0x0008;
    private const uint EventObjectFocus = 0x8005;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

    /// <summary>
    /// 前面に来た直後のフォーカスの知らせを「戻っただけ」とみなす長さ（ms）。手元では前面の知らせと同じ時刻に来た
    /// </summary>
    private const uint RestoreWindowMs = 150;

    private const string ProjectBrowserName = "UnityEditor.ProjectBrowser";
    private const string GuiViewClass = "UnityGUIViewWndClass";
    private const string ContainerClass = "UnityContainerWndClass";

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
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
        public Rect CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    /// <summary>知らせの受け口。捨てられると知らせの途中で落ちるので、見張っている間は持っておく。</summary>
    private static WinEventProc? s_callback;

    private static readonly List<IntPtr> Hooks = [];

    private static readonly Dictionary<int, IntPtr> LastByProcess = [];

    /// <summary>Unity ごとの、前面に来た時刻（知らせの時刻・ms）。</summary>
    private static readonly Dictionary<int, uint> ActivatedAt = [];

    private static int s_suppressed;

    /// <summary>見張りを始める。画面のスレッドから呼ぶ（知らせはそのスレッドのメッセージとして届く）。</summary>
    public static void Start()
    {
        if (Hooks.Count > 0)
        {
            return;
        }

        s_callback = OnEvent;
        const uint flags = WinEventOutOfContext | WinEventSkipOwnProcess;

        // 前面（0x0003）からマウスのつかみ始め（0x0008）までをまとめて受け、要らない種類は捨てる
        foreach (var (min, max) in new[] { (EventSystemForeground, EventSystemCaptureStart), (EventObjectFocus, EventObjectFocus) })
        {
            var hook = SetWinEventHook(min, max, IntPtr.Zero, s_callback, 0, 0, flags);
            if (hook != IntPtr.Zero)
            {
                Hooks.Add(hook);
            }
        }

        SeedFromCurrentFocus();
    }

    public static void Stop()
    {
        foreach (var hook in Hooks)
        {
            UnhookWinEvent(hook);
        }

        Hooks.Clear();
    }

    /// <summary>その Unity で最後に選ばれていたプロジェクトタブの窓。知らなければ <see cref="IntPtr.Zero"/>。</summary>
    public static IntPtr LastProjectBrowser(int processId)
        => LastByProcess.TryGetValue(processId, out var window) ? window : IntPtr.Zero;

    /// <summary>こちらで開いたタブを、最後に選ばれていたものとして覚える（開いた後は利用者がそのタブを見ている）。</summary>
    public static void Remember(int processId, IntPtr window) => LastByProcess[processId] = window;

    /// <summary>アプリ自身の操作の間、覚えるのを止める。</summary>
    public static IDisposable Suppress()
    {
        s_suppressed++;
        return new Resume();
    }

    private sealed class Resume : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                s_suppressed--;
            }
        }
    }

    private static void OnEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time)
    {
        if (window == IntPtr.Zero)
        {
            return;
        }

        switch (eventType)
        {
            case EventSystemForeground:
                if (ClassOf(window) == ContainerClass)
                {
                    ActivatedAt[ProcessOf(window)] = time;
                }

                break;

            case EventSystemCaptureStart:
                if (s_suppressed == 0 && IsProjectBrowser(window))
                {
                    LastByProcess[ProcessOf(window)] = window;
                }

                break;

            case EventObjectFocus:
                if (s_suppressed == 0 && IsProjectBrowser(window))
                {
                    var process = ProcessOf(window);

                    // 前面に来た直後は、前にフォーカスがあった部品へ戻っただけ（知らせの時刻は起動からの ms で、一周しても差は正しく出る）
                    if (ActivatedAt.TryGetValue(process, out var activated) && unchecked(time - activated) < RestoreWindowMs)
                    {
                        break;
                    }

                    LastByProcess[process] = window;
                }

                break;
        }
    }

    /// <summary>
    /// 見張りを始める前に選ばれていたタブ。Unity が前面にあるときだけ分かる（裏にあるとフォーカス無しが返る）。
    /// 分からなければ覚えないまま——そのときは開く側が「1つならそれ・複数なら全部」に落とす
    /// </summary>
    private static void SeedFromCurrentFocus()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("Unity"))
            {
                using (process)
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    var thread = GetWindowThreadProcessId(process.MainWindowHandle, out _);
                    var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
                    if (GetGUIThreadInfo(thread, ref info) && info.Focus != IntPtr.Zero && IsProjectBrowser(info.Focus))
                    {
                        LastByProcess[process.Id] = info.Focus;
                    }
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>
    /// Unity のプロジェクトタブの窓か。窓の名前は表に出ているタブの型（同じ場所の Console を表にすると
    /// <c>UnityEditor.ConsoleWindow</c> に変わる・§13-6）
    /// </summary>
    internal static bool IsProjectBrowser(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetWindowText(window, name, name.Capacity);
        return name.ToString() == ProjectBrowserName && ClassOf(window) == GuiViewClass;
    }

    private static string ClassOf(IntPtr window)
    {
        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        return className.ToString();
    }

    private static int ProcessOf(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        return (int)processId;
    }
}
