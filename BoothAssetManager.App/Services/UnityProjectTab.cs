using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 開いている Unity のプロジェクトタブで、入り先のフォルダを選ぶ（改変の画面の「Unityで選択」）。
///
/// **座標を使わない**（ユーザ指示 2026-09-13：配布するので、画面の大きさや窓の並べ方に左右されないように）。
/// 以前は検索欄の位置を決め打ちで押していて、プロジェクトタブが2つ開いていると、見ていない方のタブに入っていた（実機）。
/// 今の道は、メニューとフォーカスだけでたどる（2026-09-13 Unity 2022.3.22f1 で確かめた）：
///
/// 1. メニュー「Window &gt; General &gt; Project」を送る → プロジェクトタブにフォーカスが移る（複数あれば Unity が選んだ1つ）
/// 2. メニュー「Edit &gt; Find」（Ctrl+F と同じ）を送る → そのタブの検索欄にフォーカスが移る
/// 3. フォーカスのある窓を OS に聞き（<c>GetGUIThreadInfo</c>）、そこへ「名前 t:Folder」を文字で送る → フォルダだけが出る
/// 4. ↓ で先頭を選び、Enter で開く → そのフォルダが開き、左の木でも選ばれ、検索欄は空に戻る
///
/// メニューの番号は版やスクリプトの取り込みで変わる（§9-4b）ので、毎回文字でたどる。
/// Unity Search（Edit &gt; Search All）も試したが、<c>dir:</c> で出るのはフォルダの中身で、フォルダそのものは出なかった。
/// </summary>
public static class UnityProjectTab
{
    private const string ProjectBrowserName = "UnityEditor.ProjectBrowser";

    /// <summary>
    /// 検索の語に足す絞り込み。**フォルダだけを出す。**先頭がファイルだと、Enter がそのファイルを開いてしまう
    /// （シーンやプレハブなら編集の状態が変わる）
    /// </summary>
    private const string FolderFilter = " t:Folder";

    /// <summary>前に入っていた文字を消すために送る BackSpace の数。検索欄に入れる語はこれより短い</summary>
    private const int ClearCount = 80;

    private const uint WmCommand = 0x0111;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint VkBack = 0x08;
    private const uint VkReturn = 0x0D;
    private const uint VkEnd = 0x23;
    private const uint VkDown = 0x28;
    private const int SwRestore = 9;

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
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    /// <summary>
    /// プロジェクトタブで <paramref name="folderName"/> のフォルダを選ぶ。選べなければ理由を返す（選んだら null）。
    /// 最後に Unity を手前に出す（押した人は Unity で見たい）。
    /// </summary>
    public static async Task<string?> SelectFolderAsync(int processId, string folderName)
    {
        IntPtr main;
        try
        {
            using var process = Process.GetProcessById(processId);
            main = process.MainWindowHandle;
        }
        catch (ArgumentException)
        {
            return "Unity が閉じられたようです。";
        }

        if (main == IntPtr.Zero)
        {
            return "Unity の窓が見つかりませんでした。";
        }

        if (UnityImportQueue.FindMenuCommand(main, UnityHandoff.ProjectWindowMenuPath) is not { } projectCommand
            || UnityImportQueue.FindMenuCommand(main, UnityHandoff.FindMenuPath) is not { } findCommand)
        {
            return "Unity のメニューに「Window > General > Project」か「Edit > Find」が見つかりませんでした。";
        }

        // メニューは Unity が順に処理する。前の操作が済んでから次を送る（待たないと、検索欄に移る前に文字が届く）
        PostMessage(main, WmCommand, (IntPtr)projectCommand, IntPtr.Zero);
        await Task.Delay(600);
        PostMessage(main, WmCommand, (IntPtr)findCommand, IntPtr.Zero);
        await Task.Delay(600);

        var box = FocusOf(main);
        if (box == IntPtr.Zero || NameOf(box) != ProjectBrowserName)
        {
            return "Unity のプロジェクトタブにフォーカスを移せませんでした。Unity で Project タブを開いてから、もう一度押してください。";
        }

        // 前の語が残っていると混ざるので、末尾へ行ってから消す
        Key(box, VkEnd);
        for (var index = 0; index < ClearCount; index++)
        {
            Key(box, VkBack);
        }

        foreach (var character in folderName + FolderFilter)
        {
            PostMessage(box, WmChar, character, 1);
        }

        // 検索は打つたびに走る。結果が出てから先頭を選ぶ
        await Task.Delay(1200);
        Key(box, VkDown);
        await Task.Delay(400);
        Key(box, VkReturn);

        if (IsIconic(main))
        {
            ShowWindow(main, SwRestore);
        }

        SetForegroundWindow(main);
        return null;
    }

    /// <summary>Unity の画面のスレッドで、いまフォーカスを持っている窓。別のプロセスでも OS が答える。</summary>
    private static IntPtr FocusOf(IntPtr window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
    }

    private static string NameOf(IntPtr window)
    {
        var text = new StringBuilder(128);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static void Key(IntPtr window, uint virtualKey)
    {
        var scan = (long)MapVirtualKey(virtualKey, 0) << 16;
        PostMessage(window, WmKeyDown, (IntPtr)virtualKey, (IntPtr)(1 | scan));
        PostMessage(window, WmKeyUp, (IntPtr)virtualKey, (IntPtr)(1 | scan | 0xC0000000L));
    }
}
