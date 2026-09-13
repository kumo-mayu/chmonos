using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>「Unityで選択」の結果。<paramref name="Where"/> はどのタブで開いたか（状態の1行に出す）。</summary>
public sealed record UnityTabOutcome(string? Problem, string Where);

/// <summary>
/// 開いている Unity のプロジェクトタブで、入り先のフォルダを開く（改変の画面の「Unityで選択」）。
///
/// **座標を決め打ちしない**（ユーザ指示：配布するので画面の大きさや窓の並べ方に左右されないように）。
/// **既定は、最後に選んでいたプロジェクトタブで開く**（ユーザ判断 2026-09-13。覚えるのは <see cref="UnityFocusWatch"/>）。
///
/// 1. 最小化されていれば元に戻す（最小化したままでは当てにならなかった・§13-6）
/// 2. 開くタブを決める：最後に選んでいたタブ → 見えているタブが1つならそれ → 複数なら全部 →
///    1つも見えなければメニュー「Window &gt; General &gt; Project」で Unity に出してもらう
/// 3. タブごとに：中ボタンの押下をタブの窓へ送ってフォーカスを渡す（押す所は窓の四角から割合で出す。中ボタンは
///    プロジェクトタブでは何もしないので選択は変わらない）→ メニュー「Edit &gt; Find」→ フォーカスのある窓へ
///    「glob:"入り先のパス" t:Folder」を文字で送る → ↓ で先頭を選び Enter で開く
/// 4. Unity を手前に出す
///
/// 裏付けは <c>設計詳細_Unityへの受け渡し.md</c> §13。
/// </summary>
public static class UnityProjectTab
{
    /// <summary>
    /// 検索の語に足す絞り込み。**フォルダだけを出す。**先頭がファイルだと、Enter がそのファイルを開いてしまう
    /// （シーンやプレハブなら編集の状態が変わる）
    /// </summary>
    private const string FolderFilter = " t:Folder";

    /// <summary>前に入っていた文字を消すために送る BackSpace の数。検索欄に入れる語はこれより短い</summary>
    private const int ClearCount = 120;

    private const uint WmCommand = 0x0111;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const int MkMButton = 0x0010;
    private const uint VkBack = 0x08;
    private const uint VkReturn = 0x0D;
    private const uint VkEnd = 0x23;
    private const uint VkDown = 0x28;
    private const int SwRestore = 9;

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

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

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);

    /// <summary>
    /// 検索欄に入れる語。**パスで絞る**（<c>glob:"Assets/FUKA" t:Folder</c>）。
    ///
    /// 名前だけで探すと、同じ名前のフォルダ（<c>Assets/FUKA</c> と <c>Assets/Addon/FUKA</c>）のうち先に出た方を開いた
    /// （試験用プロジェクトで実機・§13-7）。プロジェクトタブの検索は <c>glob:</c> でパスを受け付け、そのフォルダ1件だけを出した。
    /// パスに glob の記号が入っていると別の意味に読まれる（<c>[作者名]</c> が文字の組に読まれる）ので、そのときだけ名前で探す
    /// </summary>
    internal static string SearchQuery(string folderPath)
    {
        var path = folderPath.Replace('\\', '/').Trim().TrimEnd('/');
        return path.IndexOfAny(['*', '?', '[', ']', '{', '}', '"']) >= 0
            ? path.Split('/').Last() + FolderFilter
            : $"glob:\"{path}\"{FolderFilter}";
    }

    /// <summary>プロジェクトタブで <paramref name="folderPath"/>（<c>Assets/FUKA</c> のような Unity の中のパス）を開く。</summary>
    public static async Task<UnityTabOutcome> SelectFolderAsync(int processId, string folderPath)
    {
        IntPtr main;
        try
        {
            using var process = Process.GetProcessById(processId);
            main = process.MainWindowHandle;
        }
        catch (ArgumentException)
        {
            return new("Unity が閉じられたようです。", string.Empty);
        }

        if (main == IntPtr.Zero)
        {
            return new("Unity の窓が見つかりませんでした。", string.Empty);
        }

        if (UnityImportQueue.FindMenuCommand(main, UnityHandoff.FindMenuPath) is not { } findCommand)
        {
            return new("Unity のメニューに「Edit > Find」が見つかりませんでした。", string.Empty);
        }

        // 最小化したままでは、タブへ送った押下が効かなかった（§13-6）
        if (IsIconic(main))
        {
            ShowWindow(main, SwRestore);
            await Task.Delay(800);
        }

        var query = SearchQuery(folderPath);
        string where;

        // こちらの押下で「最後に選んでいたタブ」が書き換わらないように止めておく
        using (UnityFocusWatch.Suppress())
        {
            var tabs = VisibleProjectBrowsers(processId);
            var last = UnityFocusWatch.LastProjectBrowser(processId);

            List<IntPtr> targets;
            if (last != IntPtr.Zero && tabs.Contains(last))
            {
                targets = [last];
                where = tabs.Count > 1 ? "最後に使っていたプロジェクトタブ" : "プロジェクトタブ";
            }
            else if (tabs.Count == 1)
            {
                targets = [tabs[0]];
                where = "プロジェクトタブ";
            }
            else if (tabs.Count > 1)
            {
                // どれを使っていたか分からない（アプリを開く前に触ったタブ・覚えたタブが隠れた）。全部で開けば必ず見える
                targets = tabs;
                where = $"プロジェクトタブ {tabs.Count} つ（どれを使っていたか分からなかったので全部）";
            }
            else
            {
                // 見えているタブが無い（閉じた・別のタブの裏に隠れた）。メニューで Unity に出してもらう
                if (UnityImportQueue.FindMenuCommand(main, UnityHandoff.ProjectWindowMenuPath) is not { } projectCommand)
                {
                    return new("Unity のメニューに「Window > General > Project」が見つかりませんでした。", string.Empty);
                }

                PostMessage(main, WmCommand, (IntPtr)projectCommand, IntPtr.Zero);
                await Task.Delay(700);
                var shown = FocusOf(main);
                if (!UnityFocusWatch.IsProjectBrowser(shown))
                {
                    return new("Unity のプロジェクトタブを出せませんでした。Unity で Project タブを開いてから、もう一度押してください。", string.Empty);
                }

                targets = [shown];
                where = "プロジェクトタブ";
            }

            var opened = new List<IntPtr>();
            foreach (var tab in targets)
            {
                if (await OpenInAsync(main, tab, query, findCommand))
                {
                    opened.Add(tab);
                }
            }

            // 自分の押下のフォーカスの知らせは遅れて届く。流し切ってから覚えるのを戻す
            await Task.Delay(300);

            if (opened.Count == 0)
            {
                return new("Unity のプロジェクトタブにフォーカスを移せませんでした。Unity を一度前に出してから、もう一度押してください。", where);
            }

            // 1つで開いたなら、利用者はこれからそのタブを見る。全部で開いたときは、どれを見るか分からないので覚えない
            if (opened.Count == 1 && targets.Count == 1)
            {
                UnityFocusWatch.Remember(processId, opened[0]);
            }
        }

        SetForegroundWindow(main);
        return new(null, where);
    }

    /// <summary>1つのタブで開く。フォーカスを渡せなければ false。</summary>
    private static async Task<bool> OpenInAsync(IntPtr main, IntPtr tab, string query, uint findCommand)
    {
        if (!GetClientRect(tab, out var rect) || rect.Right <= 0 || rect.Bottom <= 0)
        {
            return false;
        }

        // 押す所は窓の四角から割合で出す。上端の見出し（タブの名前）や下端の欄を避け、一覧の空いていそうな所
        var x = (int)(rect.Right * 0.7);
        var y = (int)(rect.Bottom * 0.8);
        var point = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(tab, WmMButtonDown, MkMButton, point);
        PostMessage(tab, WmMButtonUp, IntPtr.Zero, point);
        await Task.Delay(400);

        PostMessage(main, WmCommand, (IntPtr)findCommand, IntPtr.Zero);
        await Task.Delay(600);

        var box = FocusOf(main);
        if (box != tab)
        {
            return false;
        }

        // 前の語が残っていると混ざるので、末尾へ行ってから消す
        Key(box, VkEnd);
        for (var index = 0; index < ClearCount; index++)
        {
            Key(box, VkBack);
        }

        foreach (var character in query)
        {
            PostMessage(box, WmChar, character, 1);
        }

        // 検索は打つたびに走る。結果が出てから先頭を選ぶ
        await Task.Delay(1200);
        Key(box, VkDown);
        await Task.Delay(400);
        Key(box, VkReturn);
        await Task.Delay(500);
        return true;
    }

    /// <summary>
    /// そのプロセスの、見えているプロジェクトタブ。**切り離した窓にある場合もある**ので、本体の子だけでなく
    /// そのプロセスの窓を全部見る。別のタブの裏に隠れたものは名前が変わるので入らない。
    /// </summary>
    private static List<IntPtr> VisibleProjectBrowsers(int processId)
    {
        var found = new List<IntPtr>();

        bool Check(IntPtr window, IntPtr _)
        {
            if (IsWindowVisible(window) && UnityFocusWatch.IsProjectBrowser(window) && !found.Contains(window))
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

        return found;
    }

    /// <summary>Unity の画面のスレッドで、いまフォーカスを持っている窓。別のプロセスでも OS が答える。</summary>
    private static IntPtr FocusOf(IntPtr window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
    }

    private static void Key(IntPtr window, uint virtualKey)
    {
        var scan = (long)MapVirtualKey(virtualKey, 0) << 16;
        PostMessage(window, WmKeyDown, (IntPtr)virtualKey, (IntPtr)(1 | scan));
        PostMessage(window, WmKeyUp, (IntPtr)virtualKey, (IntPtr)(1 | scan | 0xC0000000L));
    }
}
