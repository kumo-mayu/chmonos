using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>今どこまで進んだか。画面の1行に出す。</summary>
public sealed record UnityQueueProgress(int Index, int Total, string Text);

/// <summary>
/// 1件ぶんの結果。開けなかったときは理由を持つ。
/// <paramref name="Cancelled"/> は、取り込み画面で Cancel された（何も入っていない）と分かったとき。
/// </summary>
public sealed record UnityQueueOutcome(UnityPackageEntry Package, bool Opened, string? Problem, bool Cancelled = false);

/// <summary>
/// 開いている Unity へ、unitypackage を1件ずつ積んで取り込ませる（#69）。
///
/// やり方は <c>設計詳細_Unityへの受け渡し.md</c> §9-4b（実機で3件を約10秒）：
/// **エディタの窓を名指しして**メニュー「Assets &gt; Import Package &gt; Custom Package...」を送り、
/// 出てきたファイル選択にパスを入れて「開く」を送る。取り込み画面は利用者に見せ、
/// 閉じられて（Import でも Cancel でも）後処理まで終わったら次の1件を出す。
///
/// **シェルで渡す道は使わない。**エディタが2つ開いていると、どちらに入るかを保証できず、
/// 実際にユーザの別のプロジェクトへ4回入りかけた（§9-1）。窓を名指しすれば狙った方にしか行かない。
///
/// **次を出すのは、前の物の後処理が終わってから（§11-2）。**Packages/ に入る物は取り込み画面を閉じた後も
/// コンパイルと読み込み直しを続け、その間に次の取り込み画面を開いておくと、見た目はそのままで中身が抜ける
/// （実機で 0/35）。終わりは Editor.log で掴む（§11-3・<see cref="UnityImportWatch"/>）。
/// </summary>
public static class UnityImportQueue
{
    private const uint WmCommand = 0x0111;
    private const uint WmSetText = 0x000C;
    private const int IdOk = 1;

    /// <summary>Windows 標準のファイル選択で、ファイル名の欄を包む部品の番号（§9-4b）。</summary>
    private const int FileNameControlId = 1148;

    private const uint MfByPosition = 0x0400;

    /// <summary>取り込み画面の題。Unity のエディタが英語でも日本語でも同じだった（英語で確認）。</summary>
    private const string ImportWindowTitle = "Import Unity Package";

    /// <summary>利用者が取り込み画面を眺めて考える時間は待つ。これを超えたら残りは送らない。</summary>
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Unity 自身の進捗の窓の題（実機で見た物）。これ以外の <c>#32770</c> が出ていたら、
    /// パッケージが利用者に何か尋ねている（VPM の自動インストーラの「Confirm」など §11-3）。
    /// </summary>
    private static readonly string[] ProgressTitles =
    [
        "Importing", "Compiling Scripts", "Reloading Domain", "Completing Domain", "Hold on", "Unity Package Manager",
        "Refreshing", "Compiling",
    ];

    /// <summary>
    /// Unity 2022.3 のログ。開いている全エディタが共有する（Unity 6.5 からはプロジェクトごと §11-3）。
    /// </summary>
    private static readonly string EditorLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor", "Editor.log");

    private static int _running;

    /// <summary>
    /// 送っている最中か。**送信は1列に限る（ユーザ判断 2026-09-11）。**Editor.log は全エディタが共有するので、
    /// 2つのエディタへ同時に取り込ませると、完了の行がどちらの物か分からなくなる。
    /// VRChat の使い方で、複数のプロジェクトへ同時に取り込むことは考えにくい。
    /// </summary>
    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>送っている最中に別の送信を押されたときの言い方。</summary>
    public const string BusyMessage =
        "Unityへの送信がまだ続いています。\n\nいま出ている取り込み画面を閉じ終えてから、もう一度押してください。";

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] private static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll")] private static extern uint GetMenuItemID(IntPtr menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    /// <summary>この商品の zip に入っている、Unity へ送れるもの（zip に入っている順）。</summary>
    public static IReadOnlyList<UnityPackageEntry> PackagesOf(ItemRecord item)
        => item.Local.OwnedFiles
            .Select(file => file.Paths.FirstOrDefault(File.Exists))
            .Where(path => path is not null && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => UnityHandoff.FindPackages(path!))
            .ToList();

    /// <summary>
    /// 順に送る。途中で続けられなくなったら（エディタが閉じた・メニューが見つからない）、
    /// 残りは理由を付けて返す。別の送信が動いていれば、何も送らずに全件を理由付きで返す。
    /// </summary>
    public static async Task<IReadOnlyList<UnityQueueOutcome>> RunAsync(
        int processId,
        IReadOnlyList<UnityPackageEntry> packages,
        IProgress<UnityQueueProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return packages.Select(package => new UnityQueueOutcome(package, false, "前の送信がまだ続いていました")).ToList();
        }

        try
        {
            return await RunCoreAsync(processId, packages, progress, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private static async Task<IReadOnlyList<UnityQueueOutcome>> RunCoreAsync(
        int processId,
        IReadOnlyList<UnityPackageEntry> packages,
        IProgress<UnityQueueProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<UnityQueueOutcome>();
        var unpacker = new TemporaryUnpacker();
        string? stop = null;

        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            if (stop is not null || cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new UnityQueueOutcome(package, false, stop ?? "途中でやめました"));
                continue;
            }

            void Report(string text) => progress?.Report(new UnityQueueProgress(index + 1, packages.Count, text));

            var main = MainWindowOf(processId);
            if (main == IntPtr.Zero)
            {
                stop = "Unity が閉じられました";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            Report($"{index + 1}/{packages.Count}：「{package.Name}」の取り込み画面を出しています…");

            string path;
            IReadOnlyList<string> expected;
            try
            {
                path = await Task.Run(() => unpacker.ExtractEntry(package.ZipPath, package.EntryPath, cancellationToken), cancellationToken);
                // ログの行が送った物の取り込みかを見分けるため、中身のパスを先に読んでおく
                expected = await Task.Run(() => UnityHandoff.ReadAssetPaths(package), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                outcomes.Add(new UnityQueueOutcome(package, false, $"zip から取り出せませんでした（{exception.Message}）"));
                continue;
            }

            // メニューの番号は毎回探し直す。スクリプトを含むパッケージを取り込むと Unity がメニューを作り直し、
            // 番号が1つずれた（古い番号を送ったら「Export Package」の画面が開いた §9-4b）
            if (FindMenuCommand(main, UnityHandoff.CustomPackageMenuPath) is not { } command)
            {
                stop = "Unity のメニューに「Assets > Import Package > Custom Package...」が見つかりませんでした";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            // 何が新しく出たかを見分けるため、今の窓を控えておく（利用者が開いている別の窓を「取り込み中」と数えない）
            var baseline = VisibleWindows(processId);
            var tail = new LogTail(EditorLogPath);
            PostMessage(main, WmCommand, (IntPtr)command, IntPtr.Zero);

            // ファイル名の欄を持つ窓だけをファイル選択とみなす。Unity の進捗の窓（Importing・Compiling Scripts・
            // Reloading Domain）も同じ #32770 で、種類だけで拾うと進捗の窓を掴んで止まる（§11-1）
            var dialog = await WaitForAsync(
                () => VisibleWindows(processId).FirstOrDefault(window =>
                    !baseline.Contains(window) && ClassOf(window) == "#32770" && FindFileNameBox(window) is not null),
                TimeSpan.FromSeconds(10),
                cancellationToken);

            if (dialog == IntPtr.Zero || FindFileNameBox(dialog) is not { } box)
            {
                stop = "Unity のファイル選択の画面が出ませんでした（Unity が作業中だった可能性があります）";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            SendMessage(box, WmSetText, IntPtr.Zero, path);
            PostMessage(dialog, WmCommand, (IntPtr)IdOk, IntPtr.Zero);

            // 取り込み画面が出るまで（大きいパッケージは中を読むのに時間がかかる）
            var importWindow = await WaitForAsync(
                () => VisibleWindows(processId).FirstOrDefault(window => !baseline.Contains(window) && IsImportWindow(window)),
                TimeSpan.FromSeconds(20),
                cancellationToken);

            Report($"{index + 1}/{packages.Count}：「{package.Name}」— Unity の取り込み画面で「Import」か「Cancel」を押してください");

            var (state, closed) = await WatchUntilDoneAsync(processId, baseline, importWindow, tail, expected, index, packages.Count, package, progress, cancellationToken);
            if (closed)
            {
                stop = "Unity が閉じられました";
                outcomes.Add(new UnityQueueOutcome(package, true, stop));
                continue;
            }

            switch (state)
            {
                case UnityImportState.Imported:
                    outcomes.Add(new UnityQueueOutcome(package, true, null));
                    break;
                case UnityImportState.Cancelled:
                    outcomes.Add(new UnityQueueOutcome(package, true, null, Cancelled: true));
                    break;
                default:
                    outcomes.Add(new UnityQueueOutcome(package, true, "取り込みが終わるのを待ちきれませんでした"));
                    stop = "前の取り込みが終わらないので、残りは送っていません";
                    break;
            }
        }

        return outcomes;
    }

    /// <summary>
    /// 取り込み画面が閉じられ、後処理まで終わるのを待つ。決めるのは <see cref="UnityImportWatch"/>。
    /// パッケージが確認の窓を出したら、1行でそう伝える（止まったように見えないように）。
    /// </summary>
    private static async Task<(UnityImportState State, bool EditorClosed)> WatchUntilDoneAsync(
        int processId,
        HashSet<IntPtr> baseline,
        IntPtr importWindow,
        LogTail tail,
        IReadOnlyList<string> expected,
        int index,
        int total,
        UnityPackageEntry package,
        IProgress<UnityQueueProgress>? progress,
        CancellationToken cancellationToken)
    {
        var watch = new UnityImportWatch(expected);
        var until = DateTime.UtcNow + ImportTimeout;
        string? askedTitle = null;

        while (DateTime.UtcNow < until)
        {
            if (MainWindowOf(processId) == IntPtr.Zero)
            {
                return (UnityImportState.Waiting, true);
            }

            var now = DateTime.UtcNow;
            if (importWindow == IntPtr.Zero || !IsWindow(importWindow) || !IsWindowVisible(importWindow))
            {
                watch.DialogClosed(now);
            }

            foreach (var line in tail.ReadNewLines())
            {
                watch.LogLine(line, now);
            }

            var extra = VisibleWindows(processId)
                .Where(window => !baseline.Contains(window) && window != importWindow)
                .ToList();
            watch.Windows(extra.Count > 0, now);

            var asking = extra.FirstOrDefault(window =>
                ClassOf(window) == "#32770" && FindFileNameBox(window) is null && !IsProgressTitle(TitleOf(window)));
            if (asking != IntPtr.Zero && TitleOf(asking) is var title && title != askedTitle)
            {
                askedTitle = title;
                progress?.Report(new UnityQueueProgress(index + 1, total,
                    $"{index + 1}/{total}：「{package.Name}」— Unity 側で確認（「{title}」）が出ています。Unity で答えると次に進みます"));
            }

            var state = watch.Evaluate(now);
            if (state != UnityImportState.Waiting)
            {
                return (state, false);
            }

            await Task.Delay(200, cancellationToken);
        }

        return (UnityImportState.Waiting, false);
    }

    private static bool IsImportWindow(IntPtr window)
        => ClassOf(window) == "UnityContainerWndClass" && TitleOf(window) == ImportWindowTitle;

    private static bool IsProgressTitle(string title)
        => ProgressTitles.Any(prefix => title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IntPtr MainWindowOf(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? IntPtr.Zero : process.MainWindowHandle;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>メニューを文字でたどって項目の番号を得る。段ごとに候補の名前のどれかに合えばよい。</summary>
    private static uint? FindMenuCommand(IntPtr window, IReadOnlyList<IReadOnlyList<string>> path)
    {
        var menu = GetMenu(window);
        for (var depth = 0; depth < path.Count && menu != IntPtr.Zero; depth++)
        {
            var found = -1;
            var count = GetMenuItemCount(menu);
            for (var position = 0; position < count; position++)
            {
                var text = new StringBuilder(256);
                GetMenuString(menu, (uint)position, text, text.Capacity, MfByPosition);
                var name = UnityHandoff.NormalizeMenuText(text.ToString());
                if (path[depth].Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
                {
                    found = position;
                    break;
                }
            }

            if (found < 0)
            {
                return null;
            }

            if (depth == path.Count - 1)
            {
                var id = GetMenuItemID(menu, found);
                return id == uint.MaxValue ? null : id;
            }

            menu = GetSubMenu(menu, found);
        }

        return null;
    }

    /// <summary>ファイル名の欄。部品番号 1148 の中にある Edit（UI Automation の木には出てこなかった §9-4b）。</summary>
    private static IntPtr? FindFileNameBox(IntPtr dialog)
    {
        var container = GetDlgItem(dialog, FileNameControlId);
        if (container == IntPtr.Zero)
        {
            return null;
        }

        var edit = FindDescendant(container, "Edit");
        return edit == IntPtr.Zero ? null : edit;
    }

    private static IntPtr FindDescendant(IntPtr parent, string className)
    {
        var child = IntPtr.Zero;
        while ((child = FindWindowEx(parent, child, null, null)) != IntPtr.Zero)
        {
            if (ClassOf(child) == className)
            {
                return child;
            }

            var deeper = FindDescendant(child, className);
            if (deeper != IntPtr.Zero)
            {
                return deeper;
            }
        }

        return IntPtr.Zero;
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private static HashSet<IntPtr> VisibleWindows(int processId)
    {
        var windows = new HashSet<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && IsWindowVisible(window))
            {
                windows.Add(window);
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static async Task<IntPtr> WaitForAsync(Func<IntPtr> probe, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = probe();
            if (found != IntPtr.Zero)
            {
                return found;
            }

            await Task.Delay(200, cancellationToken);
        }

        return IntPtr.Zero;
    }
}
