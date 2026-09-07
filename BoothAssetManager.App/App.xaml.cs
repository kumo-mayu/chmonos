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

        var mainWindow = new MainWindow { DataContext = new MainViewModel(_services) };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
