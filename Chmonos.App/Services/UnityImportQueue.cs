using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Services;

/// <summary>今どこまで進んだか。画面の1行に出す。</summary>
public sealed record UnityQueueProgress(int Index, int Total, string Text);

/// <summary>
/// 1件ぶんの結果。開けなかったときは理由を持つ。
/// <paramref name="Cancelled"/> は、取り込み画面で Cancel された（何も入っていない）と分かったとき。
/// <paramref name="AlreadyPresent"/> は、取り込み画面が「Nothing to import!」だった（既に全部入っている）とき。
/// Cancel ではないので、使った足跡などは入ったときと同じに扱う
/// </summary>
public sealed record UnityQueueOutcome(
    UnityPackageEntry Package, bool Opened, string? Problem, bool Cancelled = false, bool AlreadyPresent = false)
{
    /// <summary>
    /// 止めたときの言い方（E7）。**「n 件は送れませんでした（理由）」の括弧に入れない**——
    /// 止めた理由は文が長く、入れ子の括弧になって読めなかった（2026-09-20 に実機で見た）。
    /// </summary>
    public static string Describe(IReadOnlyList<UnityQueueOutcome> outcomes)
    {
        var shown = DescribeShown(outcomes);
        var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
        if (failed.Count == 0)
        {
            return shown;
        }

        // 止めたときは、残りの件数と、Unity 側に残った画面の話だけを言う
        return failed.All(outcome => UnityImportQueue.IsStopped(outcome.Problem))
            ? $"{shown}残り {failed.Count} 件は送っていません。{failed[0].Problem}"
            : $"{shown}{failed.Count} 件は送れませんでした（{failed[0].Problem}）。";
    }

    /// <summary>
    /// 何件の取り込み画面を出したかの1文。複数を順に送った後の知らせ（検索の複数選択・改変の「使ったものを順にUnityへ送る」）で共用する。
    /// </summary>
    public static string DescribeShown(IReadOnlyList<UnityQueueOutcome> outcomes)
    {
        var opened = outcomes.Where(outcome => outcome.Opened).Select(outcome => outcome.Package).Distinct().Count();
        var notes = new List<string>();
        if (outcomes.Count(outcome => outcome.AlreadyPresent) is > 0 and var present)
        {
            notes.Add($"{present} 件は既にすべて入っていました");
        }

        if (outcomes.Count(outcome => outcome.Cancelled) is > 0 and var cancelled)
        {
            notes.Add($"{cancelled} 件はCancelされたので入っていません");
        }

        return notes.Count == 0
            ? $"{opened} 件をUnityへ順に送りました。"
            : $"{opened} 件をUnityへ順に送りました（うち {string.Join("、", notes)}）。";
    }
}

/// <summary>
/// 開いている Unity へ、unitypackage を1件ずつ積んで取り込ませる（#69）。
///
/// やり方は <c>docs/history/unity-handoff.md</c> §9-4b（実機で3件を約10秒）：
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

    /// <summary><c>SW_RESTORE</c>。最小化を解いて元の大きさに戻す。</summary>
    private const int SwRestore = 9;

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

    /// <summary>取り出しの進み具合を帯へ出す間隔。一時展開の帯と同じ（1秒に10回まで。人の目で追えるのはそのくらいまで）。</summary>
    private static readonly TimeSpan ExtractProgressInterval = TimeSpan.FromMilliseconds(100);

    private static int _running;

    /// <summary>
    /// 送っている最中か。**送信は1列に限る（ユーザ判断 2026-09-11）。**Editor.log は全エディタが共有するので、
    /// 2つのエディタへ同時に取り込ませると、完了の行がどちらの物か分からなくなる。
    /// VRChat の使い方で、複数のプロジェクトへ同時に取り込むことは考えにくい。
    /// </summary>
    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>送っている最中に別の送信を押されたときの言い方。</summary>
    public const string BusyMessage =
        "Unityへの送信がまだ続いています。\n\nいま出ている取り込み画面を閉じ終えてから、もう一度押してください。\n"
        + "やめるなら、画面の下の帯の「中止」を押してください。";

    /// <summary>
    /// 走っているかが変わった（ユーザ判断 2026-09-20）。**どの画面からでも止められるように**、常設の帯を出すのに使う。
    /// 前は「中止」が送信を始めた画面にしか無く、別の画面へ移ると止める手立てが無くなっていた（実機で踏んだ）。
    /// </summary>
    public static event Action<bool>? RunningChanged;

    /// <summary>進み具合の1行。常設の帯にも同じ文を出す。</summary>
    public static event Action<string>? ProgressChanged;

    /// <summary>今走っている送信を止める合図。送信は1本ずつなので1つでよい（<see cref="IsRunning"/>）。</summary>
    private static CancellationTokenSource? _stop;

    /// <summary>
    /// 送るのをやめる（ユーザ判断 2026-09-20・E7）。**止められるのは待っている間**——
    /// いちばん長いのは、人が Unity の取り込み画面を見ている時間（上限30分）で、そこが止まる。
    /// zip からの取り出しも書き出しの途中で止まり、書きかけを消す（ユーザ判断 2026-09-30。前は1件を書き切るまで止まらなかった）。
    /// 中身のパス読みとプロジェクトの走査は、始まってしまえば最後まで走る（割り込む手段が無い）。
    ///
    /// **止めても Unity の取り込み画面は残る。**閉じる手段をこちらは持たないので、
    /// Unity 側で Import を押せば実際に入る（こちらは見ていないので記録には残らない）。言い方もそう書く。
    /// </summary>
    public static void Stop() => _stop?.Cancel();

    /// <summary>止めたときの言い方（画面と記録で同じ文を使う）。</summary>
    public const string StoppedMessage = "送るのを中止しました。残った取り込み画面は、Unityで「Cancel」を押して閉じてください。"
        + "「Import」を押すと、このアプリの記録には残りません。";

    /// <summary>
    /// Unity に窓を出す前に止めたときの言い方（ユーザ判断 2026-09-30）。zip から取り出している途中で止められるようになり、
    /// その時点では閉じてもらう取り込み画面がまだ無い。無い画面を「閉じてください」と言わない
    /// </summary>
    public const string StoppedBeforeWindowMessage = "送るのを中止しました。";

    /// <summary>人が止めた結果か（失敗と分けて言うため）。</summary>
    public static bool IsStopped(string? problem) => problem is StoppedMessage or StoppedBeforeWindowMessage;

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
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    /// <summary>この商品の zip に入っている、Unity へ送れるもの（zip に入っている順）。</summary>
    /// <remarks>
    /// 中の一覧は item に書いてあればそれを使い、zip を開かない（<see cref="UnityHandoff.PlacesOf(LocalFileRecord)"/>）。
    /// zip のハッシュを持たせるので、中身のパスは取り込みの裏で読んだ控えから引ける（zip を解き直さない）
    /// </remarks>
    public static IReadOnlyList<UnityPackageEntry> PackagesOf(ItemRecord item)
        => PlacesOf(item).Select(place => place.Entry).ToList();

    /// <summary><see cref="PackagesOf"/> に、item に書いてある入る先を添えた物。</summary>
    public static IReadOnlyList<UnityPackagePlace> PlacesOf(ItemRecord item)
        => item.Local.OwnedFiles.SelectMany(UnityHandoff.PlacesOf).ToList();

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

        // 画面の「中止」から止められるように、この送信のトークンを預かる（E7）
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _stop = stop;
        RunningChanged?.Invoke(true);
        try
        {
            return await RunCoreAsync(processId, packages, progress, stop.Token);
        }
        finally
        {
            _stop = null;
            Volatile.Write(ref _running, 0);
            ProgressChanged?.Invoke(string.Empty);
            RunningChanged?.Invoke(false);
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
        // 同じ zip の包みを続けて送るとき、中身の控えを zip ごとに1回だけ読む
        var reads = new UnityPackageReads();
        string? stop = null;
        var windowPending = false;

        // 送り先のプロジェクトの場所。「既に全部入っているか」を調べるのに使う（引けなければ調べない）
        var project = await Task.Run(() => UnityEditors.PathOf(processId), cancellationToken);

        try
        {
            await RunLoopAsync();
        }
        catch (OperationCanceledException)
        {
            // 止めたときは待ちの中（`Task.Delay`）から例外で出てくる。**そのまま上へ投げない**——
            // 「残りは理由を付けて返す」という約束が果たせず、呼んだ側は結果を受け取れない（E7）
            stop = windowPending ? StoppedMessage : StoppedBeforeWindowMessage;
        }

        // まだ結果を積んでいない分（止めた分）を理由付きで埋める
        for (var rest = outcomes.Count; rest < packages.Count; rest++)
        {
            outcomes.Add(new UnityQueueOutcome(packages[rest], false, stop ?? StoppedBeforeWindowMessage));
        }

        return outcomes;

        async Task RunLoopAsync()
        {
        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            if (stop is not null || cancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new UnityQueueOutcome(package, false, stop ?? "途中でやめました"));
                continue;
            }

            void Report(string text)
            {
                progress?.Report(new UnityQueueProgress(index + 1, packages.Count, text));

                // 常設の帯にも同じ文を出す（どの画面へ移っても、何をしているかと「中止」が見える）
                ProgressChanged?.Invoke(text);
            }

            var main = MainWindowOf(processId);
            if (main == IntPtr.Zero)
            {
                stop = "Unityが閉じられました";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            // 最小化されていると取り込み画面を見逃し、押されないまま Cancel と数えてしまう（ユーザ指摘 2026-09-20）。
            // 人に見せて押してもらう画面なので、送る前に開いておく
            Restore(main);

            var sendingLine = $"{index + 1}/{packages.Count}：「{package.Name}」を送っています…";
            Report(sendingLine);

            // **取り出しが長いときだけ、文を大きさの進み具合に替える**（ユーザ判断 2026-09-30）。数GBの unitypackage を
            // 遅いディスクで送ると、「送っています…」のまま長く動かなかった。すぐ終わる取り出しでは替えない
            // （替えるかの決まりは DisplayText.ShowsUnityExtracting）。
            // 進み具合は 81,920 バイトごとに届くので、最新だけを1秒に10回まで出す（LatestProgress に理由）
            var number = index + 1;
            var extracting = true;
            var showsExtracting = false;
            var clock = Stopwatch.StartNew();
            var extractProgress = new LatestProgress<TemporaryUnpackProgress>(
                report =>
                {
                    // 取り出しが終わった後に届いた分で、次の段の文を上書きしない
                    if (!extracting)
                    {
                        return;
                    }

                    showsExtracting = showsExtracting
                        || DisplayText.ShowsUnityExtracting(clock.Elapsed, report.DoneBytes, report.TotalBytes);
                    if (showsExtracting)
                    {
                        Report(DisplayText.UnityExtractingLine(
                            number, packages.Count, package.Name, report.DoneBytes, report.TotalBytes));
                    }
                },
                ExtractProgressInterval,
                System.Windows.Threading.Dispatcher.CurrentDispatcher);

            string path;
            IReadOnlyList<UnityPackageAsset> assets;
            IReadOnlyList<string> expected;
            try
            {
                try
                {
                    // 中止は書き出しの途中でも効き、取り出しの側が書きかけを消してから戻る。
                    // ここへ戻るのは片付けが済んだ後（止めた・失敗した後に空きを食う書きかけを残さない）
                    path = await Task.Run(
                        () => unpacker.ExtractEntry(package.ZipPath, package.EntryPath, extractProgress, cancellationToken),
                        cancellationToken);
                }
                finally
                {
                    extracting = false;
                    extractProgress.Complete();
                }

                // 取り出しの文に替えていたら戻す。この後も中身の読み取りとファイル選択の窓を待つ間があり、
                // 「取り出しています… 2.3 GB / 2.3 GB」のまま止まって見える
                if (showsExtracting)
                {
                    Report(sendingLine);
                }

                // ログの行が送った物の取り込みかを見分けるため、中身のパスを先に読んでおく
                assets = await Task.Run(() => reads.ReadAssets(package), cancellationToken);
                expected = assets.Select(asset => asset.Path).ToList();
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Core.Diagnostics.AppLog.Error("Unityへ送る：zip から取り出す", exception);
                outcomes.Add(new UnityQueueOutcome(package, false,
                    $"zipから取り出せませんでした。{Core.Services.FailureText.Cause(exception)}"));
                continue;
            }

            // 中身がプロジェクトに全部あれば、Unity は取り込み画面に「Nothing to import!」しか出さない。
            // その窓を OK で閉じても Unity は1行も書かないので、ログだけでは Cancel と見分けられない（2026-09-19 に実機で確かめた）。
            // 入っているかは「Unityで選択」と同じ調べ方（<see cref="UnityProjectMatcher"/>。パスの一覧は控えがある）。
            // 利用者が移した物も GUID で見つけて「入っている」と数える（Unity も GUID で同じ物と見て取り込まない）
            var alreadyThere = project is not null && assets.Count > 0
                && await Task.Run(
                    () => UnityProjectMatcher.Match(
                        project,
                        new Dictionary<string, IReadOnlyList<UnityPackageAsset>>(StringComparer.Ordinal) { [package.Name] = assets })
                        .FirstOrDefault() is { Total: > 0 } match && match.Present == match.Total,
                    cancellationToken);

            // メニューの番号は毎回探し直す。スクリプトを含むパッケージを取り込むと Unity がメニューを作り直し、
            // 番号が1つずれた（古い番号を送ったら「Export Package」の画面が開いた §9-4b）
            if (FindMenuCommand(main, UnityHandoff.CustomPackageMenuPath) is not { } command)
            {
                stop = "Unityのメニューに「Assets > Import Package > Custom Package...」が見つかりませんでした";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            // 何が新しく出たかを見分けるため、今の窓を控えておく（利用者が開いている別の窓を「取り込み中」と数えない）
            var baseline = VisibleWindows(processId);
            var tail = new LogTail(EditorLogPath);
            PostMessage(main, WmCommand, (IntPtr)command, IntPtr.Zero);

            // ここから先で止めると、Unity にファイル選択か取り込み画面が残り得る（止めたときの文を分ける）
            windowPending = true;

            // ファイル名の欄を持つ窓だけをファイル選択とみなす。Unity の進捗の窓（Importing・Compiling Scripts・
            // Reloading Domain）も同じ #32770 で、種類だけで拾うと進捗の窓を掴んで止まる（§11-1）
            var dialog = await WaitForAsync(
                () => VisibleWindows(processId).FirstOrDefault(window =>
                    !baseline.Contains(window) && ClassOf(window) == "#32770" && FindFileNameBox(window) is not null),
                TimeSpan.FromSeconds(10),
                cancellationToken,
                busyProcessId: processId);

            if (dialog == IntPtr.Zero || FindFileNameBox(dialog) is not { } box)
            {
                stop = "Unityのファイル選択の画面が表示されませんでした。Unityが作業中だった可能性があります";
                outcomes.Add(new UnityQueueOutcome(package, false, stop));
                continue;
            }

            SendMessage(box, WmSetText, IntPtr.Zero, path);
            PostMessage(dialog, WmCommand, (IntPtr)IdOk, IntPtr.Zero);

            // 取り込み画面が出るまで（大きいパッケージは中を読むのに時間がかかる）
            var importWindow = await WaitForAsync(
                () => VisibleWindows(processId).FirstOrDefault(window => !baseline.Contains(window) && IsImportWindow(window)),
                TimeSpan.FromSeconds(20),
                cancellationToken,
                busyProcessId: processId);

            Report($"{index + 1}/{packages.Count}：「{package.Name}」— Unityの取り込み画面で「Import」か「Cancel」を押してください");

            if (alreadyThere)
            {
                Report($"{index + 1}/{packages.Count}：「{package.Name}」は既にすべて入っています。Unityで「OK」を押すと次に進みます");
            }

            UiTrace.Write("Unity", $"{index + 1}/{packages.Count} 「{package.Name}」の取り込み画面を出した"
                + (alreadyThere ? "（既に全部入っている）" : string.Empty));

            var (state, closed) = await WatchUntilDoneAsync(
                processId, baseline, importWindow, tail, expected, alreadyThere, index, packages.Count, package, progress, cancellationToken);
            UiTrace.Write("Unity", $"{index + 1}/{packages.Count} 「{package.Name}」→ {state}{(closed ? "（Unity が閉じた）" : string.Empty)}");
            windowPending = false;
            if (closed)
            {
                stop = "Unityが閉じられました";
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
                case UnityImportState.AlreadyPresent:
                    outcomes.Add(new UnityQueueOutcome(package, true, null, AlreadyPresent: true));
                    break;
                default:
                    outcomes.Add(new UnityQueueOutcome(package, true, "取り込みが終わるのを待ちきれませんでした"));
                    stop = "前の取り込みが終わらないので、残りは送っていません";
                    break;
            }
        }
        }
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
        bool alreadyThere,
        int index,
        int total,
        UnityPackageEntry package,
        IProgress<UnityQueueProgress>? progress,
        CancellationToken cancellationToken)
    {
        var watch = new UnityImportWatch(expected);
        if (alreadyThere)
        {
            watch.AlreadyInProject();
        }

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
                    $"{index + 1}/{total}：「{package.Name}」— Unity側で確認（「{title}」）が表示されています。Unityで答えると次に進みます"));
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

    /// <summary>最小化されていたら開く。送る前に呼ぶ（取り込み画面は人に押してもらう物なので、隠れていては困る）。</summary>
    private static void Restore(IntPtr window)
    {
        if (!IsIconic(window))
        {
            return;
        }

        ShowWindow(window, SwRestore);
        SetForegroundWindow(window);
    }

    private static bool IsImportWindow(IntPtr window)
        => ClassOf(window) == "UnityContainerWndClass" && TitleOf(window) == ImportWindowTitle;

    private static bool IsProgressTitle(string title)
        => ProgressTitles.Any(prefix => title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    // 浮いた窓ではなく、メニューを持つ主の窓を選ぶ（MainWindowHandle は浮いた窓に当たり、メニューが見つからなかった）
    private static IntPtr MainWindowOf(int processId) => UnityEditors.MainWindowOf(processId);

    /// <summary>
    /// メニューを文字でたどって項目の番号を得る。段ごとに候補の名前のどれかに合えばよい。
    /// 改変の画面の「Unityで選択」（<see cref="UnityProjectTab"/>）でも使う。
    /// </summary>
    internal static uint? FindMenuCommand(IntPtr window, IReadOnlyList<IReadOnlyList<string>> path)
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

    /// <param name="busyProcessId">
    /// **Unity が作業中の間は待ち時間を数え直す**（ユーザ判断 2026-09-21・C18）。
    ///
    /// 固定の待ちだけで見ていたので、Unity がコンパイル中・ドメイン再読込中だと
    /// 普通に超えてしまい、「ファイル選択の画面が出ませんでした」で1件落ちていた。
    /// 進捗の窓（Importing・Compiling Scripts・Reloading Domain…）が出ている間は、
    /// **こちらの都合ではなく Unity の都合**なので、時間切れにしない。
    /// 渡さなければ、これまで通り単純な時間切れ。
    /// </param>
    private static async Task<IntPtr> WaitForAsync(
        Func<IntPtr> probe,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int? busyProcessId = null)
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

            if (busyProcessId is { } processId && IsUnityBusy(processId))
            {
                until = DateTime.UtcNow + timeout;
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>Unity が自分の作業（取り込み・コンパイル・ドメイン再読込）で塞がっているか。</summary>
    private static bool IsUnityBusy(int processId)
        => VisibleWindows(processId).Any(window => IsProgressTitle(TitleOf(window)));
}
