using System.IO;
using System.Windows;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.App;

public partial class App : Application
{
    private AppServiceContainer? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            MessageBox.Show($"予期しないエラーが発生しました。\n\n{args.Exception.Message}",
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

        if (!EnsureStoreReachable())
        {
            Shutdown();
            return;
        }

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
            MessageBox.Show("既に起動しています。", "Chmonos", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var main = new MainViewModel(_services);
        var mainWindow = new MainWindow { DataContext = main };

        mainWindow.RestorePlacement(_services.UiState.Window);

        // 閉じる直前に採る。Closed だと既に位置を失っている
        mainWindow.Closing += (_, e) =>
        {
            // 編集途中の入力が残っていれば尋ねる（ユーザ判断）。「移動する」「キャンセル」なら閉じない
            if (main.ShouldCancelCloseForDrafts())
            {
                e.Cancel = true;
                return;
            }

            SavePlacement(main, mainWindow);
        };

        mainWindow.Show();

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

        var answer = MessageBox.Show(
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
    private static void SavePlacement(MainViewModel main, MainWindow window)
    {
        // 背景で走っている取得を先に止める。終わるのは待たない
        // （1件ずつ保存しているので、どこで止めても壊れない）
        main.StopBackgroundWork();

        var placement = window.CurrentPlacement();

        try
        {
            Task.Run(() => main.SaveUiStateAsync(state => state with { Window = placement }))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            // 位置を覚えられなくても終了は妨げない
            Core.Diagnostics.AppLog.Error("閉じるときの窓の位置の保存", exception);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 一時展開は閉じるときに消す（#56・ユーザ判断）。開いたままの物は次の起動で消える
        if (_services?.IsSingleInstance == true)
        {
            new Core.Services.TemporaryUnpacker().CleanUp();
        }

        Services.UnityFocusWatch.Stop();
        _services?.Dispose();
        base.OnExit(e);
    }
}
