using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Chmonos.App.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// UIスレッドで実行する。バックエンドからの進捗はUIスレッド以外から届くので、ここを通す。
    /// Dispatcherが無い場合（テストなど）はそのまま実行する。
    /// </summary>
    protected static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    /// <summary>
    /// UIスレッドで実行し、終わるまで待てる形で返す（裏から来た知らせの後始末を、画面が済ませてから次へ進めたいとき）。
    /// Dispatcherが無い場合（テストなど）はそのまま実行する。
    /// </summary>
    protected static Task RunOnUiThreadAsync(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess()
            ? action()
            : dispatcher.InvokeAsync(action).Task.Unwrap();
    }
}
