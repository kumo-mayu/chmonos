using System.IO;
using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.Core.Storage;

namespace Chmonos.App;

public partial class App : Application
{
    /// <summary>
    /// このプロセスがアプリ本体として起動されたか（入口がこのアセンブリ＝自分の実行ファイル）。
    ///
    /// 試験や確かめの道具は、色の表や部品の見た目を読むために <c>new App()</c> する。
    /// WPF の Application は**コンストラクタの中で**「OnStartup を呼ぶ仕事」を積むので、Run を呼ばなくても、
    /// 道具がメッセージを回した時点で起動の処理が丸ごと走る（保存先を決める → サービス一式 → 主の窓 → 起動時の裏の作業）。
    /// 2026-09-30 に、保存先を指定していない道具がこの道で本番の指す先（友人のデータの写し）を開き、
    /// 取り込みと BOOTH からの取り直しを走らせた（17:40 と 19:31 の2回）。道具の側の注意に頼らず、ここで分ける
    /// </summary>
    public static bool IsLaunchedAsApp { get; } =
        System.Reflection.Assembly.GetEntryAssembly() == typeof(App).Assembly;

    // 利用者の本当の保存先を使ってよいのは、アプリ本体として起動されたときだけ。どの型よりも先に立てる
    // （AppPaths.Default は最初に触れたときに1回だけ決まる）。試験や道具が new App() しても立たず、環境変数で保存先を決める
    static App()
    {
        StoreLocation.AllowsUserStore = IsLaunchedAsApp;

        // 部品の型に掛ける決まりは、部品を1つも作らないうちに入れる。道具がこの型を資源の入れ物として作ったときも同じ名前で読めるよう、本体かどうかによらず入れる
        Services.AutomationNames.Register();

        // Shift＋ホイールの横送りも型に掛ける決まり。道具で描く画面も同じ動きになる
        Controls.HorizontalWheel.Register();

        // フォーカスを受けた部品を流すとき、外側に出す印が流れの縁で欠けないよう少し余分に流す
        Controls.FocusScrollMargin.Register();
    }

    private AppServiceContainer? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 道具や試験の中では、資源を読むための入れ物としてだけ使われる。起動の処理は進めない
        if (!IsLaunchedAsApp)
        {
            return;
        }

        // ImageSharp は復号・縮小に使った作業領域を後で使い回すために溜めておく。
        // 既定の上限は搭載メモリから決まり、数百MBまで握り得る。
        // このアプリが一度に扱うのは長辺384pxのサムネイルか取り込み中の1枚なので、
        // 溜めるのは小さくてよい（#71・メモリは多いときでも200〜300MBに抑える）
        SixLabors.ImageSharp.Configuration.Default.MemoryAllocator =
            SixLabors.ImageSharp.Memory.MemoryAllocator.Create(new SixLabors.ImageSharp.Memory.MemoryAllocatorOptions
            {
                MaximumPoolSizeMegabytes = 16,
            });

        DispatcherUnhandledException += (_, args) =>
        {
            Core.Diagnostics.AppLog.Error("画面の処理", args.Exception);
            // 例外の文（英語や内部の型の名前）は見せず、見当と次の一手だけ出す。詳しくはログ（E5）
            Services.Notice.Show(
                "予期しないエラーが発生しました。今の操作は途中で止まっているかもしれません。\n\n"
                + Core.Services.FailureText.Cause(args.Exception) + "\n\n"
                + "同じ操作で繰り返すときは、アプリを開き直してください。詳しい記録は保存先のlogs\\app.logに残しました。",
                "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 投げっぱなしの作業（_ = 〜Async()）の中の失敗は、上では拾えず、どこにも出ずに消えていた（技術的負債 2-2）。
        // 拾えるのは片付けられるときなので遅れて書かれるが、残らないよりよい。画面には出さない（頼まれた操作の失敗は各画面が出す）
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Core.Diagnostics.AppLog.Error("投げっぱなしの作業", args.Exception);
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Core.Diagnostics.AppLog.Error("アプリが止まった", exception);
            }
        };

        // 表示の色は窓を1つでも出す前に当てる。保存先に設定があればその色、無い・読めない（初回・保存先が見つからない）なら Windows に合わせる。
        // サービス一式を作る前なので、設定は色の1欄だけを読む（書かない）。前はここで Windows に合わせるだけで、
        // 「既に起動しています」の窓が設定の色と違う色で出ていた（ユーザ判断 2026-09-30）
        ViewModels.AppTheme.Start();

        // 横ホイール（チルト）はスレッドのメッセージを見る。道具や試験のメッセージには掛けない
        Controls.HorizontalWheel.ListenForTilt();

        // 画面を出さない起動（docs/history/headless-core-2026-10-13.md）。保存先の確かめ → 初回 → 形式 → 錠 → 組み立ては、画面ありと同じ道を通す
        _headless = e.Args.Any(arg => string.Equals(arg, HeadlessArgument, StringComparison.OrdinalIgnoreCase));

        if (!EnsureStoreReachable(_headless))
        {
            Shutdown();
            return;
        }

        // 保存先の確かめで既定の場所へ切り替えたなら、そこの設定の色に合わせ直す（変わっていなければ何もしない）
        ViewModels.AppTheme.UseStoredMode();

        // 初回だけ、後から変えると高くつく2つを聞く。
        // サービス一式より先に出すのは、ここで保存先が変わり得るため
        if (Views.FirstRunWindow.IsNeeded())
        {
            // 画面を出さない起動では保存先を決められない。勝手に作らず、先に画面ありで起動してもらう（ユーザ判断 2026-10-13）
            if (_headless)
            {
                Services.Notice.Show(
                    "ウィンドウを表示せずに起動する前に、一度ふつうに起動して、データの保存先を決めてください。",
                    "Chmonos", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // この窓が閉じた時点では本体がまだ無い。
            // 既定（最後の窓が閉じたら終了）のままだと、そこでアプリごと終わってしまう
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var started = Views.FirstRunWindow.Run();

            ShutdownMode = ShutdownMode.OnLastWindowClose;

            if (!started)
            {
                Shutdown();
                return;
            }
        }

        // 保存データの形式の版を、サービス一式を作る前に確かめる（docs/spec/data-format.md）。ここでは読むだけ——
        // 新しい版の保存先なら、組み立てが設定を読む所で「読めない」と止まる前に、何が起きたかを言って終える
        if (!CheckStoreFormat(StoreLocation.Resolve().Path))
        {
            Shutdown();
            return;
        }

        // **組み立ての途中で止まったら、知らせて終える**（外部の点検 2026-10-06）。設定の JSON が壊れているなどで
        // ここが投げると、共通の受け口が知らせて「済んだ」にするだけで、窓が1つも無いままアプリが残っていた
        MainViewModel? main = null;
        MainWindow? mainWindow = null;
        try
        {
            _services = new AppServiceContainer();

            if (!_services.IsSingleInstance)
            {
                // 画面を出さない起動は、既に動いていれば黙って終える（ログオン時などに二重に起動されても窓を出さない）
                if (!_headless)
                {
                    Services.Notice.Show("既に起動しています。", "Chmonos", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                Shutdown();
                return;
            }

            // 控えと印の書き込みは、多重起動の錠を取った後（＝ほかの起動が書いていない）、主の画面が裏の作業を始める前。
            // 錠より先に書くと、開いている古い版の横で印だけ上がり、古い版が前の形式で書き続けてしまう
            if (!SettleStoreFormat())
            {
                _services.Dispose();
                _services = null;
                Shutdown();
                return;
            }

            // 設定の表示の色。主の窓を作る前に当てる（作るときに色を読む）
            ViewModels.AppTheme.Initialize(_services);

            if (!_headless)
            {
                main = new MainViewModel(_services);
                mainWindow = new MainWindow { DataContext = main };
            }
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("起動", exception);
            Services.Notice.Show(
                "起動できませんでした。\n\n"
                + Core.Services.FailureText.Cause(exception) + "\n\n"
                + "詳しい記録は保存先のlogs\\app.logに残しました。",
                "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
            _services?.Dispose();
            _services = null;
            Shutdown();
            return;
        }

        if (_headless || main is null || mainWindow is null)
        {
            StartHeadless();
            return;
        }

        _main = main;
        _mainWindow = mainWindow;
        ShowMainWindow(main, mainWindow);

        // 書きかけの片付けは、窓を出してから裏で（保存先の全体を再帰でたどるので、窓が出るまでの待ちに乗せない）
        _services.SweepStaleTemporaryFilesLater();

        // 見つからなくなった日時の見回りも、窓を出してから裏で（全件を読み、記録の場所を全部見るので。ユーザ判断 2026-10-05）
        main.StartMissingMarksSweep();

        // 前の起動で終わらなかった「このIDで登録」を同じ順で続ける（ユーザ判断 2026-10-05 メモ60「4」）。
        // 人が押した登録なので、起動時の裏の作業（設定で切れる）とは別に、押したときと同じ優先度で流す
        main.ResumeRegistrationsAsync().Forget();

        // 新しい版の確認（1日1回まで・設定で切れる）。窓を出してから裏で
        main.CheckForUpdatesAsync().Forget();

        // 外から使う API（設定でオンのときだけ。読み込みに数秒かかるので、窓を出してから裏で）
        _services.Api.ApplyAsync().Forget();

        // Unity で最後に選んでいたプロジェクトタブを覚え始める（「Unityで選択」の既定・ユーザ判断）。
        // 画面のスレッドで付ける——知らせはこのスレッドのメッセージとして届く
        Services.UnityFocusWatch.Start();
    }

    /// <summary>画面を出さずに起動する引数。</summary>
    public const string HeadlessArgument = "--headless";

    private bool _headless;
    private MainViewModel? _main;
    private MainWindow? _mainWindow;
    private Services.TrayIcon? _tray;
    private Services.IdleCompactor? _compactor;

    /// <summary>
    /// 画面を出さずに動かす（ユーザ判断 2026-10-13）。起動したときの作業は画面ありと同じ設定に従い、前の起動の登録の続きも流す。
    /// 新しい版の確認は、知らせを出す場所が無いので窓を開いたときに回す。動いていることは通知領域のアイコンで見せ、そこから開く・終える
    /// </summary>
    private void StartHeadless()
    {
        var services = _services!;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _tray = new Services.TrayIcon("Chmonosはウィンドウを表示せずに動いています");
        _tray.OpenRequested += OpenFromTray;
        _tray.ExitRequested += ExitFromTray;

        // 作業が静かになったら1回片付ける（画面の無い起動では、起動したときの作業のごみが片付かずに残る。IdleCompactor）
        _compactor = new Services.IdleCompactor();
        services.Startup.WorkEnded += _compactor.Note;
        services.Imports.Finished += _compactor.Note;
        services.Api.StateChanged += _compactor.Note;
        _compactor.NoteAfter(TimeSpan.FromSeconds(20));

        services.SweepStaleTemporaryFilesLater();
        services.Startup.Start();
        services.Startup.StartMissingMarksSweep();
        services.ResumeRegistrationsAsync().Forget();
        ImportOnHeadlessLaunchAsync(services).Forget();

        // 外から使う API も、画面ありと同じく設定でオンのときだけ（オフならポートを開けない。ユーザ判断 2026-10-13）
        services.Api.ApplyAsync().Forget();
    }

    /// <summary>
    /// 画面を出さない起動の自動の取り込み（API の設定 <see cref="Core.Models.ApiSettings.ImportOnHeadlessLaunch"/>。既定はオフ）。
    /// 対象の決め方と取り込みの道は、画面ありの「起動したとき…取り込む」と同じ（ユーザ判断 2026-10-13：経路は1本、設定は別）。
    /// 押してもいないので、展開先のファイルについて尋ねない（G2）
    /// </summary>
    private static async Task ImportOnHeadlessLaunchAsync(AppServiceContainer services)
    {
        try
        {
            if (!services.Store.ApiSettings.Load().ImportOnHeadlessLaunch)
            {
                return;
            }

            var found = await services.Watch.FindNewAsync(services.Settings.WatchedFolders, CancellationToken.None);
            var targets = await services.Imports.LaunchTargetsAsync(found.NewFiles);
            if (targets.Count == 0)
            {
                return;
            }

            await services.Imports.StartOrStackAsync(new Services.ImportRequest(
                () => services.Imports.TargetsWithoutAsking(targets.ToList()),
                targets));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Core.Diagnostics.AppLog.Error("画面を出さない起動の自動の取り込み", exception);
        }
    }

    /// <summary>
    /// 通知領域から窓を開く。主画面は1回だけ作り、閉じたら隠して取っておく（主画面はアプリの間1つの前提で作られていて、
    /// 作り直すと知らせを二重に受ける）。起動したときの作業は済んでいるので、主画面は走らせ直さない
    /// </summary>
    private void OpenFromTray()
    {
        // 窓を開いている間は、強いメモリの片付けを走らせない（画面が止まる。点検36）
        _compactor?.SetWindowShown(true);

        if (_mainWindow is { } window)
        {
            window.Show();
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
            }

            window.Activate();
            return;
        }

        try
        {
            _main = new MainViewModel(_services!);
            _mainWindow = new MainWindow { DataContext = _main };
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("通知領域から窓を開く", exception);
            Services.Notice.Show(
                "窓を開けませんでした。\n\n" + Core.Services.FailureText.Cause(exception) + "\n\n"
                + "詳しい記録は保存先のlogs\\app.logに残しました。",
                "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
            _main = null;
            _mainWindow = null;
            return;
        }

        ShowMainWindow(_main, _mainWindow);
        _main.CheckForUpdatesAsync().Forget();
        Services.UnityFocusWatch.Start();
    }

    /// <summary>通知領域の「終了」。窓を開いていれば、窓を閉じるときと同じことを確かめてから終える。</summary>
    private void ExitFromTray()
    {
        var services = _services!;
        if (_main is { } main && _mainWindow is { } window)
        {
            if (main.ShouldCancelCloseForRegistrations())
            {
                return;
            }

            WaitForPendingWrites(main.FlushPendingWritesAsync());

            // 書きかけを尋ねる窓の「移動する」は編集画面を開くので、窓を出してから尋ねる
            if (main.HasDrafts())
            {
                OpenFromTray();
                if (main.ShouldCancelCloseForDrafts())
                {
                    return;
                }
            }

            if (window.IsVisible)
            {
                SaveWindowPlacement(main, window);
            }

            main.StopBackgroundWork();
        }
        else
        {
            // 窓を開いていない間も、登録の列は流れている。終わっていなければ聞く（閉じても次の起動で続く）
            if (ViewModels.RegistrationQueue.CloseConfirm(services.Registrations.Jobs.Count, services.Registrations.SecondsLeftAll()) is { } confirm
                && Views.ChoiceDialog.AskTwo(confirm.Title, confirm.Question, confirm.Detail, "閉じる", "閉じない") != Views.ChoiceDialogResult.First)
            {
                return;
            }

            WaitForPendingWrites(Task.WhenAll(services.Registrations.WhenWrittenAsync(), services.BackgroundWrites.WhenIdleAsync()));
            services.Startup.Stop();
        }

        Shutdown();
    }

    /// <summary>主の窓を出す。閉じるときの確かめは <see cref="OnMainWindowClosing"/>。</summary>
    private void ShowMainWindow(MainViewModel main, MainWindow mainWindow)
    {
        ViewModels.AppTheme.Watch(mainWindow);
        ViewModels.AppTheme.HideUntilFirstFrame(mainWindow);

        mainWindow.RestorePlacement(_services!.UiState.Window);

        // 閉じる直前に採る。Closed だと既に位置を失っている
        mainWindow.Closing += (_, e) => OnMainWindowClosing(main, mainWindow, e);

        mainWindow.Show();
    }

    private void OnMainWindowClosing(MainViewModel main, MainWindow mainWindow, System.ComponentModel.CancelEventArgs e)
    {
        // 保存先を運び終えて開き直すところ。書きかけと待っている保存は運ぶ前に片付けてあり、
        // ここで書くと古い保存先へ行く（MainViewModel.IsRelocatingStore）
        if (main.IsRelocatingStore)
        {
            main.StopBackgroundWork();
            return;
        }

        // 画面を出さない起動から開いた窓は、閉じたら隠すだけ（プロセスは通知領域で動き続ける）。
        // 主画面はそのまま取っておくので、書きかけも登録の列も残る。待っている保存だけ書き、位置を覚える
        if (_tray is not null)
        {
            WaitForPendingWrites(main.FlushPendingWritesAsync());
            SaveWindowPlacement(main, mainWindow);
            e.Cancel = true;
            mainWindow.Hide();
            _compactor?.SetWindowShown(false);
            return;
        }

        // 終わっていない登録があれば聞く（メモ60）。閉じても列は記録にあり、次の起動で続く
        if (main.ShouldCancelCloseForRegistrations())
        {
            e.Cancel = true;
            return;
        }

        // 待っている自動保存（メモ）を今書く（I10：打ち終えてすぐ閉じると 0.8 秒の待ちごと捨てられていた）。
        // **書き終わるまで待つ。**投げっぱなしにすると、窓が閉じて主のスレッドが終わった時点で
        // 書いている途中の作業が切られ、結局その回の入力だけが消えていた（2026-09-20）
        WaitForPendingWrites(main.FlushPendingWritesAsync());

        // 編集途中の入力が残っていれば尋ねる（ユーザ判断）。「移動する」「キャンセル」なら閉じない
        if (main.ShouldCancelCloseForDrafts())
        {
            e.Cancel = true;
            return;
        }

        SavePlacement(main, mainWindow);
    }

    /// <summary>
    /// 保存先の形式の版を読み、このアプリで開けるかを決める（ユーザ判断 2026-10-08）。**読むだけで書かない。**
    /// 新しいバージョンの Chmonos が使った保存先は開かない——起動時の裏の作業が知らない欄を消しながら書いてしまうため、ファイルごとに止めるのでは間に合わない。
    /// 印が在るのに読めないときも開かない（点検26：読めない印を今の版とみなすと、新しいバージョンの保存先を止められない）
    /// </summary>
    private static bool CheckStoreFormat(string root)
    {
        int version;
        try
        {
            version = StoreFormat.StoreVersion(root);
        }
        catch (StoreFormatUnreadableException exception)
        {
            // 中身が壊れている印は、待っても直らない（点検27：一時的に読めないときと同じ「待って開き直す」しか言わず、抜け出せなかった）。
            // 開かないのはそのまま。版を確かめずに印を消して作り直す救済はしない——新しいバージョンの保存先だったら、知らない欄を消してしまう
            Core.Diagnostics.AppLog.Error("保存データの形式の版を読む", exception);
            var answer = Services.Notice.Show(
                $"保存先の {StoreFormat.MarkerFileName} が壊れていて、データの形式を確かめられないため、開けませんでした。\n\n"
                + $"新しいバージョンの Chmonos でこのデータを使ったことがなければ、{StoreFormat.MarkerFileName} を削除すると開けます。"
                + "使ったことがあれば、新しいバージョンで開いてください。\n\n"
                + "［はい］保存先をエクスプローラで開きます。\n"
                + "［いいえ］何もせずに終了します。",
                "Chmonos", MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Yes)
            {
                // この後すぐ終えるので、開くまで待つ（裏の仕事のまま終えると、開く前にプロセスが閉じる）。開く処理は画面のスレッドの外で動くので待ち合わない
                Services.Shell.TrySelectAsync(Path.Combine(root, StoreFormat.MarkerFileName)).GetAwaiter().GetResult();
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Error("保存データの形式の版を読む", exception);
            Services.Notice.Show(
                "データの形式を確かめられなかったため、開けませんでした。\n\n"
                + $"保存先の {StoreFormat.MarkerFileName} が読めません。同期のアプリなどが開いている場合は、少し待ってから開き直してください。",
                "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        switch (StoreFormat.Check(version))
        {
            case StoreFormat.Verdict.TooNew:
                var answer = Services.Notice.Show(
                    "このデータは、新しいバージョンの Chmonos で使われています。このバージョンでは開けません。\n\n"
                    + "［はい］ダウンロードページを開きます。新しいバージョンを入れてから開いてください。\n"
                    + "［いいえ］何もせずに終了します。",
                    "新しいバージョンのデータです",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Yes)
                {
                    Services.Shell.OpenUrl(Core.Services.UpdateCheck.DownloadPage);
                }

                return false;

            case StoreFormat.Verdict.TooOld:
                Services.Notice.Show(
                    "このデータは古い形式のため、このバージョンでは開けません。",
                    "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;

            default:
                return true;
        }
    }

    /// <summary>
    /// 多重起動の錠を取った後に、版を**読み直して**から控えと印を書く（点検26：錠を取るまでの間に、別のバージョンが形式を上げて終わることがある）。
    /// 印の無い保存先（v1.0.0 が使った・初めて開く）は版 1 として扱い、今の版より古ければ控えを取ってから上げる。
    /// 控えか印が書けなければ開かない（控えが無いまま形式を上げると、古いバージョンへ戻せなくなる）
    /// </summary>
    private bool SettleStoreFormat()
    {
        var root = _services!.Paths.Root;
        if (!CheckStoreFormat(root))
        {
            return false;
        }

        int? marked;
        try
        {
            marked = StoreFormat.ReadMarker(root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 直前の確かめでは読めた。読めなくなったなら、確かめと同じく開かない
            Core.Diagnostics.AppLog.Error("保存データの形式の版を読む", exception);
            return false;
        }

        var version = marked ?? StoreFormat.Unversioned;
        if (StoreFormat.Check(version) == StoreFormat.Verdict.Same)
        {
            // 印が無ければ今の版で書き、後のバージョンが見分けられるようにする。書けなくても形式は変えていないので開く
            if (marked is null)
            {
                TryMarkCurrent(root);
            }

            return true;
        }

        try
        {
            StoreFormat.Backup(root, version, DateTimeOffset.Now);
            StoreFormat.MarkCurrent(root, Services.AppVersion.Text);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Error("形式を上げる前の控え", exception);
            Services.Notice.Show(
                "データの控えを作れなかったため、開けませんでした。ディスクの空きを確かめてから、もう一度開いてください。",
                "Chmonos", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void TryMarkCurrent(string root)
    {
        try
        {
            StoreFormat.MarkCurrent(root, Services.AppVersion.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Error("保存データの形式の版を書く", exception);
        }
    }

    /// <summary>
    /// 保存先に手が届くかを、何かを作る前に確かめる。
    ///
    /// <see cref="AppPaths.EnsureCreated"/> は無ければ作ってしまうので、その前に見る。
    /// 外付けを外したまま起動して黙って既定の場所で始まると、
    /// ライブラリが2つに分かれ、後からどちらが本物か分からなくなる。
    ///
    /// 既定の場所しか指していないなら、無くて当たり前なので何も聞かない（初回起動がこれ）。
    /// </summary>
    /// <param name="headless">
    /// 画面を出さない起動か。そのときは「既定の場所で開きますか」を聞かずに終える（保存先を勝手に替えない。ユーザ判断 2026-10-13）
    /// </param>
    private static bool EnsureStoreReachable(bool headless)
    {
        var root = StoreLocation.Resolve();

        // 保存先が引越し・戻すの途中で止まった写しかけなら開かない。今の作りでは location.json が写しかけを指すことは無い
        // （印を外してから場所を書き換える）が、手で直したときなどに起こり得る。開くと商品の大半が欠けたライブラリとして動き、
        // 起動時の作業がそこへ書き足すので、写しかけと元を見比べられなくなる。既定の場所へ切り替える問いは出さない
        // （見つからないときと違い、つなぎ直せば済む話ではなく、どこへ直すかは使う人が知っている）
        if (UnfinishedCopy.IsAt(root.Path))
        {
            Services.Notice.Show(
                ViewModels.UnfinishedCopyPrompt.StartupText(root.Path, UnfinishedCopy.Read(root.Path)),
                "保存先を開けません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        if (!AsksWhenStoreMissing(root, Directory.Exists(root.Path)))
        {
            return true;
        }

        if (headless)
        {
            Services.Notice.Show(
                $"データの保存先が見つからないため、起動しませんでした。\n\n{root.Path}\n\n"
                + "外付けのドライブや同期のアプリをつないでから、もう一度起動してください。",
                "保存先が見つかりません",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var answer = Services.Notice.Show(
            $"データの保存先が見つかりません。\n\n{root.Path}\n\n"
            + "外付けのドライブを外しているか、Googleドライブなどの同期のアプリが止まっている場合は、つなぐか起動してから、もう一度開いてください。\n\n"
            + "［はい］既定の場所（%LOCALAPPDATA%）で開きます。保存先の設定はそちらに変わります。\n"
            + "［いいえ］何もせずに終了します。つなぎ直してから開き直せます。",
            "保存先が見つかりません",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        // 既定へ戻したことを覚える。黙って既定で開くと、次もまた同じ問いが出る
        StoreLocation.Clear();
        return true;
    }

    /// <summary>
    /// 保存先が見つからないと聞くのは、設定した場所（<c>location.json</c>）を指しているときだけ。
    ///
    /// 既定の場所は、無くて当たり前（初回起動）。環境変数で指定した場所は、指定した側が作る前提で、聞かずにそこで開く
    /// （無ければ作られる）。前は環境変数のときも聞いていて、「はい」で <see cref="StoreLocation.Clear"/> が走り、
    /// 環境変数とは関係の無い本番の <c>location.json</c> を消していた。確かめの道具が無い写しを開くと、この窓が出る
    /// （2026-10-01 の点検で見つけた。押されてはいない）
    /// </summary>
    internal static bool AsksWhenStoreMissing(StoreRoot root, bool exists)
        => root.Source == StoreRootSource.Configured && !exists;

    /// <summary>
    /// 終了時の姿を覚える。
    ///
    /// 投げっぱなしにするとプロセスが先に終わって毎回書けないので、ここは待つ。
    /// UIスレッドから直接待つと保存側の継続がUIスレッドを待って詰まるため、
    /// Task.Run で切り離してから待つ。小さなJSON1枚なので数msで終わる。
    /// </summary>
    /// <summary>
    /// 閉じる前の書き出しを待つ。
    ///
    /// **画面のスレッドを塞がずに待つ**（<see cref="System.Windows.Threading.DispatcherFrame"/>）。
    /// 保存の続きは画面のスレッドへ戻ってくるので、ここで塞ぐと噛み合わずに止まる。
    /// 待っている間は画面のメッセージが回る——閉じている最中なので、数 ms のあいだだけ。
    ///
    /// **5秒で諦める。**書くのは小さなJSON1枚なので普通は数 ms で終わる。それでも終わらないのは
    /// 保存先の引越し・バックアップが書き込みの門を持っているときで、その門は分単位で開かない。
    /// 閉じる操作を分単位で止めるより、諦めて記録に残す。
    /// </summary>
    private static void WaitForPendingWrites(Task pending)
    {
        if (pending.IsCompleted)
        {
            return;
        }

        var frame = new System.Windows.Threading.DispatcherFrame();
        var gaveUp = true;

        pending.ContinueWith(
            _ => Current?.Dispatcher.BeginInvoke(() =>
            {
                gaveUp = false;
                frame.Continue = false;
            }),
            TaskScheduler.Default);

        var limit = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        limit.Tick += (_, _) => frame.Continue = false;
        limit.Start();

        System.Windows.Threading.Dispatcher.PushFrame(frame);
        limit.Stop();

        if (gaveUp)
        {
            Core.Diagnostics.AppLog.Warn("閉じるときの書き出し", "5秒待っても書き終わらなかったので諦めた");
        }
    }

    private static void SavePlacement(MainViewModel main, MainWindow window)
    {
        // 背景で走っている取得を先に止める。終わるのは待たない
        // （1件ずつ保存しているので、どこで止めても壊れない）
        main.StopBackgroundWork();
        SaveWindowPlacement(main, window);
    }

    /// <summary>窓の位置と大きさを覚える（画面を出さない起動で窓を隠すときは、裏の作業は止めずにこれだけ）。</summary>
    private static void SaveWindowPlacement(MainViewModel main, MainWindow window)
    {
        // 窓が無ければ（閉じる途中で既に壊れているなど）覚え直さず、前回の値を残す
        if (window.CurrentPlacement() is not { } placement)
        {
            return;
        }

        // **画面のスレッドで同期に待たない。**保存は書き込みの門を待つので、保存先を運んでいる間
        // （門は分単位で閉じ、運び終えると開き直すまで開かない）に閉じると、終わりのない「応答なし」になった（点検 2026-09-28）。
        // メモの自動保存と同じく、画面を止めずに待ち、5秒で諦める。位置を覚えられなくても終了は妨げない
        WaitForPendingWrites(Task.Run(async () =>
        {
            try
            {
                await main.SaveUiStateAsync(state => state with { Window = placement });
            }
            catch (Exception exception)
            {
                Core.Diagnostics.AppLog.Error("閉じるときの窓の位置の保存", exception);
            }
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 一時展開は閉じるときに消す（#56・ユーザ判断）。開いたままの物は次の起動で消える。
        // 消すのはこの保存先の置き場所だけ（保存先の違う別のアプリが展開した物は、そのアプリが消す）
        if (_services?.IsSingleInstance == true)
        {
            new Core.Services.TemporaryUnpacker().CleanUp();
        }

        Services.UnityFocusWatch.Stop();
        ViewModels.AppTheme.Stop();
        _tray?.Dispose();
        _compactor?.Dispose();

        // API を止めて api.json を「止まった」にする。止まるのを待つのは5秒まで（閉じる操作を API の都合で長く止めない）
        if (_services is { } services && !Task.Run(services.Api.StopAsync).Wait(TimeSpan.FromSeconds(5)))
        {
            Core.Diagnostics.AppLog.Warn("閉じるときの API の停止", "5秒待っても止まらなかったので諦めた");
        }

        _services?.Dispose();
        base.OnExit(e);
    }
}
