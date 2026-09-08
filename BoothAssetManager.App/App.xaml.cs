using System.Windows;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App;

public partial class App : Application
{
    private AppServiceContainer? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"予期しないエラーが発生しました。\n\n{args.Exception.Message}",
                "BOOTH Asset Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _services = new AppServiceContainer();

        if (!_services.IsSingleInstance)
        {
            MessageBox.Show("既に起動しています。", "BOOTH Asset Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var main = new MainViewModel(_services);
        var mainWindow = new MainWindow { DataContext = main };

        mainWindow.RestorePlacement(_services.Settings.Window);

        // 閉じる直前に採る。Closed だと既に位置を失っている
        mainWindow.Closing += (_, _) => SavePlacement(main, mainWindow);

        mainWindow.Show();
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
        var placement = window.CurrentPlacement();

        try
        {
            Task.Run(() => main.SaveUiStateAsync(settings => settings with { Window = placement }))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception)
        {
            // 位置を覚えられなくても終了は妨げない
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
