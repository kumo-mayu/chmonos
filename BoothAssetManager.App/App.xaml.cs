using System.IO;
using System.Windows;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App;

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
    static App() => StoreLocation.AllowsUserStore = IsLaunchedAsApp;

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

        if (!EnsureStoreReachable())
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

        _services = new AppServiceContainer();

        if (!_services.IsSingleInstance)
        {
            Services.Notice.Show("既に起動しています。", "Chmonos", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 設定の表示の色。主の窓を作る前に当てる（作るときに色を読む）
        ViewModels.AppTheme.Initialize(_services);

        var main = new MainViewModel(_services);
        var mainWindow = new MainWindow { DataContext = main };
        ViewModels.AppTheme.Watch(mainWindow);
        ViewModels.AppTheme.HideUntilFirstFrame(mainWindow);

        mainWindow.RestorePlacement(_services.UiState.Window);

        // 閉じる直前に採る。Closed だと既に位置を失っている
        mainWindow.Closing += (_, e) =>
        {
            // 保存先を運び終えて開き直すところ。書きかけと待っている保存は運ぶ前に片付けてあり、
            // ここで書くと古い保存先へ行く（MainViewModel.IsRelocatingStore）
            if (main.IsRelocatingStore)
            {
                main.StopBackgroundWork();
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
        };

        mainWindow.Show();

        // 書きかけの片付けは、窓を出してから裏で（保存先の全体を再帰でたどるので、窓が出るまでの待ちに乗せない）
        _services.SweepStaleTemporaryFilesLater();

        // Unity で最後に選んでいたプロジェクトタブを覚え始める（「Unityで選択」の既定・ユーザ判断）。
        // 画面のスレッドで付ける——知らせはこのスレッドのメッセージとして届く
        Services.UnityFocusWatch.Start();
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
    private static bool EnsureStoreReachable()
    {
        var root = StoreLocation.Resolve();

        if (root.Source == StoreRootSource.Default || Directory.Exists(root.Path))
        {
            return true;
        }

        var answer = Services.Notice.Show(
            $"データの保存先が見つかりません。\n\n{root.Path}\n\n"
            + "外付けドライブを外している場合は、つないでからもう一度開いてください。\n\n"
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
        // 一時展開は閉じるときに消す（#56・ユーザ判断）。開いたままの物は次の起動で消える
        if (_services?.IsSingleInstance == true)
        {
            new Core.Services.TemporaryUnpacker().CleanUp();
        }

        Services.UnityFocusWatch.Stop();
        ViewModels.AppTheme.Stop();
        _services?.Dispose();
        base.OnExit(e);
    }
}
