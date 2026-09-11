using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.Services;

/// <summary>今どこまで進んだか。画面の1行に出す。</summary>
public sealed record UnityQueueProgress(int Index, int Total, string Text);

/// <summary>1件ぶんの結果。開けなかったときは理由を持つ。</summary>
public sealed record UnityQueueOutcome(UnityPackageEntry Package, bool Opened, string? Problem);

/// <summary>
/// 開いている Unity へ、unitypackage を1件ずつ積んで取り込ませる（#69）。
///
/// やり方は <c>設計詳細_Unityへの受け渡し.md</c> §9-4b（実機で3件を約10秒）：
/// **エディタの窓を名指しして**メニュー「Assets &gt; Import Package &gt; Custom Package...」を送り、
/// 出てきたファイル選択にパスを入れて「開く」を送る。取り込み画面は利用者に見せ、
/// 閉じられて（Import でも Cancel でも）エディタが落ち着いたら次の1件を出す。
///
/// **シェルで渡す道は使わない。**エディタが2つ開いていると、どちらに入るかを保証できず、
/// 実際にユーザの別のプロジェクトへ4回入りかけた（§9-1）。窓を名指しすれば狙った方にしか行かない。
/// </summary>
public static class UnityImportQueue
{
    private const uint WmCommand = 0x0111;
    private const uint WmSetText = 0x000C;
    private const int IdOk = 1;

    /// <summary>Windows 標準のファイル選択で、ファイル名の欄を包む部品の番号（§9-4b）。</summary>
    private const int FileNameControlId = 1148;

    private const uint MfByPosition = 0x0400;

    /// <summary>エディタの窓が本体だけに戻ってから、次を出すまで待つ時間。取り込み後の再コンパイルの窓が出入りするため。</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(2);

    /// <summary>利用者が取り込み画面を眺めて考える時間は待つ。</summary>
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromMinutes(30);

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

    /// <summary>この商品の zip に入っている、Unity へ送れるもの（zip に入っている順）。</summary>
    public static IReadOnlyList<UnityPackageEntry> PackagesOf(ItemRecord item)
        => item.Local.LocalFiles
            .Select(file => file.Paths.FirstOrDefault(File.Exists))
            .Where(path => path is not null && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => UnityHandoff.FindPackages(path!))
            .ToList();

    /// <summary>
    /// 順に送る。途中で続けられなくなったら（エディタが閉じた・メニューが見つからない）、
    /// 残りは理由を付けて返す。取り込み画面で Cancel されたかどうかは、こちらからは分からない
    /// （画面が消えたことしか見えない）ので「開いた」とだけ言う。
    /// </summary>
    public static async Task<IReadOnlyList<UnityQueueOutcome>> RunAsync(
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
            try
            {
                path = await Task.Run(() => unpacker.ExtractEntry(package.ZipPath, package.EntryPath, cancellationToken), cancellationToken);
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
            PostMessage(main, WmCommand, (IntPtr)command, IntPtr.Zero);

            // ファイル名の欄を持つ窓だけをファイル選択とみなす。Unity の進捗の窓（Importing・Compiling Scripts・
            // Reloading Domain）も同じ #32770 で、Packages/ に入るパッケージは取り込み画面が閉じた後も
            // 数秒それを出し続ける（実機で9秒 §11）。種類だけで拾うと進捗の窓を掴んで止まる
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

            await WaitForAsync(() => IsWindow(dialog) && IsWindowVisible(dialog) ? IntPtr.Zero : (IntPtr)1,
                TimeSpan.FromSeconds(10), cancellationToken);

            Report($"{index + 1}/{packages.Count}：「{package.Name}」— Unity の取り込み画面で「Import」か「Cancel」を押してください");

            // 取り込み画面が出るまで（大きいパッケージは中を読むのに時間がかかる）
            await WaitForAsync(
                () => VisibleWindows(processId).FirstOrDefault(window => !baseline.Contains(window)),
                TimeSpan.FromSeconds(20),
                cancellationToken);

            // 取り込み画面が閉じ、再コンパイルの窓も出入りし終えて、元の窓だけに戻ったら次へ
            var settled = await WaitUntilSettledAsync(processId, baseline, cancellationToken);
            outcomes.Add(new UnityQueueOutcome(package, true, settled ? null : "取り込みが終わるのを待ちきれませんでした"));
            if (!settled)
            {
                stop = "前の取り込みが終わらないので、残りは送っていません";
            }
        }

        return outcomes;
    }

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

    /// <summary>送る前にあった窓だけに戻り、それが続いたら落ち着いたと見る。エディタが閉じても終わる。</summary>
    private static async Task<bool> WaitUntilSettledAsync(int processId, HashSet<IntPtr> baseline, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + ImportTimeout;
        DateTime? quietSince = null;
        while (DateTime.UtcNow < until)
        {
            if (MainWindowOf(processId) == IntPtr.Zero)
            {
                return true;
            }

            var extra = VisibleWindows(processId).Any(window => !baseline.Contains(window));
            if (extra)
            {
                quietSince = null;
            }
            else
            {
                quietSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - quietSince >= SettleTime)
                {
                    return true;
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }
}
