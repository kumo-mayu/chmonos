using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>
/// 「Unityで選択」の結果。
/// </summary>
/// <param name="Where">どのタブで探したか（状態の1行に出す）。</param>
/// <param name="StopReason">
/// 開かずに止めた理由（「似た名前のフォルダが 2 個あるので」など）。開いたときは null。
/// </param>
/// <param name="Searched">Unity で探したか。探しても出ないと分かっている所（Packages の下）は探さずに場所の名前だけを伝える。</param>
public sealed record UnityTabOutcome(string? Problem, string Where, string? StopReason = null, bool Searched = true)
{
    public bool Stopped => StopReason is not null;
}

/// <summary>
/// 開いている Unity のプロジェクトタブで、入り先のフォルダを開く（改変の画面の「Unityで選択」）。
///
/// **座標を決め打ちしない**（ユーザ指示：配布するので画面の大きさや窓の並べ方に左右されないように）。
/// **既定は、最後に選んでいたプロジェクトタブで開く**（ユーザ判断 2026-09-13。覚えるのは <see cref="UnityFocusWatch"/>）。
/// **検索の結果が1件と言い切れないときは、結果を出したところで止める**（ユーザ指示：一番上を開くと、利用者には何が起きたか
/// 分からないまま違うフォルダが開く）。Unity の画面の中の件数は外から読めないので、プロジェクトのフォルダをディスクで見て決める。
///
/// 1. 最小化されていれば元に戻す（最小化したままでは当てにならなかった・§13-6）
/// 2. 開くタブを決める：最後に選んでいたタブ → 見えているタブが1つならそれ → 複数なら最初に見つけた1つ（2026-09-19 まで全部）→
///    1つも見えなければメニュー「Window &gt; General &gt; Project」で Unity に出してもらう
/// 3. タブごとに：中ボタンの押下をタブの窓へ送ってフォーカスを渡す（押す所は窓の四角から割合で出す。中ボタンは
///    プロジェクトタブでは何もしないので選択は変わらない）→ メニュー「Edit &gt; Find」→ フォーカスのある窓へ
///    「a:assets glob:"入り先のパス" t:Folder」を文字で送る → 1件と言い切れるときだけ ↓ で先頭を選び Enter で開く
/// 4. Unity を手前に出す
///
/// 裏付けは <c>docs/history/unity-handoff.md</c> §13。
/// </summary>
public static class UnityProjectTab
{
    /// <summary>
    /// 検索の語に足す絞り込み。**フォルダだけを出す。**先頭がファイルだと、Enter がそのファイルを開いてしまう
    /// （シーンやプレハブなら編集の状態が変わる）
    /// </summary>
    private const string FolderFilter = " t:Folder";

    /// <summary>
    /// 検索の語の頭に必ず付ける範囲の指定。**検索の範囲はタブが覚えている**ので、前に Packages を探した後や、
    /// 利用者が範囲を切り替えていると、Assets のパスが1件も出なかった（試験用で実機：Packages を探した次の <c>Assets/FUKA</c> が開かなかった）
    /// </summary>
    private const string AssetsScope = "a:assets ";

    /// <summary>glob の記号。パスに入っていると別の意味に読まれる（<c>[作者名]</c> が文字の組に読まれる）</summary>
    private static readonly char[] GlobSymbols = ['*', '?', '[', ']', '{', '}', '"'];

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

    /// <summary>
    /// **Unicode 版で呼ぶ。**文字の指定が無いと ANSI 版（PostMessageA）に結び付き、日本語の文字が化けた
    /// （<c>[試]重複</c> が <c>[f]^</c> になった・実機）。
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
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

    private static string Normalize(string folderPath) => folderPath.Replace('\\', '/').Trim().TrimEnd('/');

    private static bool UsesGlob(string path) => path.IndexOfAny(GlobSymbols) < 0;

    private static bool IsInPackages(string path) => path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 検索欄に入れる語と、開いてよいか（結果が1件と言い切れるか）。言い切れないときは止める理由を返す。
    /// 語が null のときは、Unity の検索に出ないので探さない。
    ///
    /// - <c>Assets/</c> の下で glob の記号が無い：<c>a:assets glob:"Assets/FUKA" t:Folder</c>。そのフォルダが実在すれば1件
    ///   （名前だけで探すと、同じ名前の <c>Assets/Addon/FUKA</c> を開いた・§13-7）
    /// - glob の記号が入る（<c>[作者名]</c> が文字の組に読まれる）：名前で探し、名前の語をすべて含むフォルダを数える。1つなら開く
    ///   （プロジェクトタブの名前の検索は語の一部で当たる・§13-3）
    /// - <c>Packages/</c> の下：**Unity の検索に出なかった**（<c>a:packages</c>・<c>a:all</c>、表示名・フォルダの名前・glob の
    ///   どれでも0件・kip01 と試験用で実機）。探しても空の結果を見せるだけなので、左の木で見つける名前を伝える
    /// </summary>
    internal static (string? Query, string? StopReason) Plan(string projectPath, string folderPath)
    {
        var path = Normalize(folderPath);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            if (IsInPackages(path))
            {
                var name = segments.Length >= 2 ? PackageDisplayName(projectPath, segments[1]) ?? segments[1] : "Packages";
                return (null, $"Packagesの中（プロジェクトタブの左の木ではPackagesの下の「{name}」）はUnityの検索に表示されないので");
            }

            if (UsesGlob(path))
            {
                var byPath = $"{AssetsScope}glob:\"{path}\"{FolderFilter}";
                return Directory.Exists(Path.Combine(projectPath, path.Replace('/', Path.DirectorySeparatorChar)))
                    ? (byPath, null)
                    : (byPath, "プロジェクトの中にそのフォルダが見つからないので");
            }

            var words = segments[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = Directory.EnumerateDirectories(Path.Combine(projectPath, "Assets"), "*", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Count(folder => folder is not null
                    && words.All(word => folder.Contains(word, StringComparison.OrdinalIgnoreCase)));
            var byName = AssetsScope + string.Join(' ', words) + FolderFilter;
            return count == 1 ? (byName, null) : (byName, $"似た名前のフォルダが {Math.Max(count, 2)} 個あるので");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (AssetsScope + (segments.Length > 0 ? segments[^1] : string.Empty) + FolderFilter,
                "プロジェクトのフォルダを数えられなかったので");
        }
    }

    /// <summary>
    /// パッケージの表示名（<c>package.json</c> の <c>displayName</c>）。プロジェクトタブの Packages の下には
    /// フォルダの名前（<c>com.triturbo.blendshare</c>）ではなく表示名（BlendShare）で並ぶ。読めなければ null。
    /// </summary>
    private static string? PackageDisplayName(string projectPath, string packageId)
    {
        var file = Path.Combine(projectPath, "Packages", packageId, "package.json");
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            return document.RootElement.TryGetProperty("displayName", out var display) && display.ValueKind == JsonValueKind.String
                ? display.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// プロジェクトタブで <paramref name="folderPath"/>（<c>Assets/FUKA</c> のような Unity の中のパス）を開く。
    /// <paramref name="projectPath"/> はそのプロジェクトのフォルダ（結果が1件になるかをディスクで見るのに使う）。
    /// </summary>
    public static async Task<UnityTabOutcome> SelectFolderAsync(int processId, string projectPath, string folderPath)
    {
        IntPtr main;
        try
        {
            using var process = Process.GetProcessById(processId);

            // メニューを持つ主の窓で探す（MainWindowHandle は浮いた窓に当たることがあり、メニューが無い）
            main = UnityEditors.MainWindowOf(processId);
        }
        catch (ArgumentException)
        {
            return new("Unityが閉じられたようです。", string.Empty);
        }

        if (main == IntPtr.Zero)
        {
            return new("Unityの窓が見つかりませんでした。", string.Empty);
        }

        if (UnityImportQueue.FindMenuCommand(main, UnityHandoff.FindMenuPath) is not { } findCommand)
        {
            return new("Unityのメニューに「Edit > Find」が見つかりませんでした。", string.Empty);
        }

        // 最小化したままでは、タブへ送った押下が効かなかった（§13-6）
        if (IsIconic(main))
        {
            ShowWindow(main, SwRestore);
            await Task.Delay(800);
        }

        var (query, stopReason) = await Task.Run(() => Plan(projectPath, folderPath));
        if (query is null)
        {
            // 探しても出ないので、タブには触らない。Unity だけ手前に出して、左の木で見つけてもらう
            SetForegroundWindow(main);
            return new(null, string.Empty, stopReason, Searched: false);
        }

        var choose = stopReason is null;
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
                // どれを使っていたか分からない（アプリを開く前に触ったタブ・覚えたタブが隠れた）。
                // 前は全部で開いていたが、見ていないタブの今のフォルダまで勝手に変わった（ユーザ指摘 2026-09-19）。
                // あまり起きないので、最初に見つけたタブ（窓の並びで手前の物）1つで開く（ユーザ判断 同日）
                targets = [tabs[0]];
                where = "最初に見つけたプロジェクトタブ";
            }
            else
            {
                // 見えているタブが無い（閉じた・別のタブの裏に隠れた）。メニューで Unity に出してもらう
                if (UnityImportQueue.FindMenuCommand(main, UnityHandoff.ProjectWindowMenuPath) is not { } projectCommand)
                {
                    return new("Unityのメニューに「Window > General > Project」が見つかりませんでした。", string.Empty);
                }

                PostMessage(main, WmCommand, (IntPtr)projectCommand, IntPtr.Zero);
                await Task.Delay(700);
                var shown = FocusOf(main);
                if (!UnityFocusWatch.IsProjectBrowser(shown))
                {
                    return new("Unityのプロジェクトタブを表示できませんでした。UnityでProjectタブを開いてから、もう一度押してください。", string.Empty);
                }

                targets = [shown];
                where = "プロジェクトタブ";
            }

            var opened = new List<IntPtr>();
            foreach (var tab in targets)
            {
                if (await OpenInAsync(main, tab, query, findCommand, choose))
                {
                    opened.Add(tab);
                }
            }

            // 自分の押下のフォーカスの知らせは遅れて届く。流し切ってから覚えるのを戻す
            await Task.Delay(300);

            if (opened.Count == 0)
            {
                return new("Unityのプロジェクトタブにフォーカスを移せませんでした。Unityを一度前に出してから、もう一度押してください。", where);
            }

            // 開いたタブを、利用者はこれから見る。次もこのタブで開く（別のタブを使うなら、そのタブを触れば覚え直す）
            if (opened.Count == 1 && targets.Count == 1)
            {
                UnityFocusWatch.Remember(processId, opened[0]);
            }
        }

        SetForegroundWindow(main);
        return new(null, where, stopReason);
    }

    /// <summary>
    /// 1つのタブで検索する。<paramref name="choose"/> なら先頭を選んで開き、そうでなければ結果を出したところで止める。
    /// フォーカスを渡せなければ false。
    /// </summary>
    private static async Task<bool> OpenInAsync(IntPtr main, IntPtr tab, string query, uint findCommand, bool choose)
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

        if (!choose)
        {
            return true;
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
