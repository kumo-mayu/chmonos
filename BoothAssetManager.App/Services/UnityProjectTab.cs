using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 開いている Unity のプロジェクトタブの検索欄に文字を入れる（改変の画面の「Unityで選択」）。
///
/// **どのフォルダがそのアセットかを示せれば十分**（ユーザ判断 2026-09-13）なので、入り先のルートフォルダの名前を
/// 検索欄に入れて止める。ダブルクリックで開くところまではしない。
///
/// 道は <c>設計詳細_Unityへの受け渡し.md</c> §8-2：タブの窓（窓の名前が <c>UnityEditor.ProjectBrowser</c>）へ、
/// マウスと文字のメッセージを直接送る。**前面は奪わない。**Unity の画面は自前で描いていて中の部品は見えないので、
/// 検索欄の位置は決め打ち（タブの右上）。版・レイアウト・表示倍率で外れうるが、外れても違う所を押すだけで壊しはしない。
/// </summary>
public static class UnityProjectTab
{
    private const string ProjectBrowserName = "UnityEditor.ProjectBrowser";

    /// <summary>
    /// 検索欄を押す位置（100% 表示のときの px）。§8-2 の実測で、検索欄はタブの右上から約 320px の幅。
    /// 右端には絞り込みのアイコンが並ぶので、欄の中ほどを押す
    /// </summary>
    private const int SearchFromRight = 200;

    private const int SearchFromTop = 10;

    /// <summary>前に入っていた文字を消すために送る BackSpace の数。検索欄に入れる語はこれより短い</summary>
    private const int ClearCount = 80;

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const int MkLButton = 0x0001;
    private const uint VkBack = 0x08;
    private const uint VkEnd = 0x23;

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>
    /// 検索欄に <paramref name="text"/> を入れる。入れられなければ理由を返す（入れたら null）。
    /// </summary>
    public static async Task<string?> SearchAsync(int processId, string text)
    {
        var tab = FindProjectBrowser(processId);
        if (tab == IntPtr.Zero)
        {
            return "Unity のプロジェクトタブが見つかりませんでした。タブが隠れているか閉じていたら、"
                + "Unity で Project タブを表に出してから、もう一度押してください。";
        }

        GetClientRect(tab, out var rect);
        var dpi = GetDpiForWindow(tab);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        var x = (int)(rect.Right - SearchFromRight * scale);
        var y = (int)(SearchFromTop * scale);
        if (x <= 0)
        {
            return "Unity のプロジェクトタブが狭すぎて、検索欄を押せませんでした。タブを広げてから、もう一度押してください。";
        }

        var point = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(tab, WmLButtonDown, MkLButton, point);
        PostMessage(tab, WmLButtonUp, IntPtr.Zero, point);

        // 押した結果（検索欄に入る）を Unity が処理してから文字を送る
        await Task.Delay(150);

        // 前の語が残っていると混ざるので、末尾へ行ってから消す
        Key(tab, VkEnd);
        for (var index = 0; index < ClearCount; index++)
        {
            Key(tab, VkBack);
        }

        foreach (var character in text)
        {
            PostMessage(tab, WmChar, character, 1);
        }

        return null;
    }

    private static void Key(IntPtr window, uint virtualKey)
    {
        var scan = (long)MapVirtualKey(virtualKey, 0) << 16;
        PostMessage(window, WmKeyDown, (IntPtr)virtualKey, (IntPtr)(1 | scan));
        PostMessage(window, WmKeyUp, (IntPtr)virtualKey, (IntPtr)(1 | scan | 0xC0000000L));
    }

    /// <summary>
    /// そのプロセスのプロジェクトタブの窓。**切り離した窓にある場合もある**ので、本体の子だけでなく
    /// そのプロセスの窓を全部見る。複数あれば見えていて一番大きいもの。
    /// </summary>
    private static IntPtr FindProjectBrowser(int processId)
    {
        var found = new List<IntPtr>();
        var name = new StringBuilder(128);

        bool Check(IntPtr window, IntPtr _)
        {
            name.Clear();
            GetWindowText(window, name, name.Capacity);
            if (name.ToString() == ProjectBrowserName && IsWindowVisible(window))
            {
                found.Add(window);
            }

            return true;
        }

        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId)
            {
                Check(window, IntPtr.Zero);
                EnumChildWindows(window, Check, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);

        return found
            .Select(window => (Window: window, Area: GetClientRect(window, out var rect) ? (long)rect.Right * rect.Bottom : 0))
            .OrderByDescending(entry => entry.Area)
            .Select(entry => entry.Window)
            .FirstOrDefault();
    }
}
