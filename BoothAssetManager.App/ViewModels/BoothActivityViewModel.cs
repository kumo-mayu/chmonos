using System.Windows.Threading;
using BoothAssetManager.Core.Booth;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 今BOOTHに対して何をしているか。アプリに1つだけ。
///
/// 取得は <see cref="BoothClient"/> 1つを通って直列化されているので、
/// どの瞬間も起きていることは1つしかない。だから画面ごとに持たず、これを見る。
/// 常設の1行も、取り込み画面の詳細も、同じここから描く（食い違いようがない）。
/// </summary>
public sealed class BoothActivityViewModel : ViewModelBase
{
    private readonly Dispatcher _dispatcher;
    private BoothActivity _activity = BoothActivity.Idle;

    public BoothActivityViewModel(IBoothClient client, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        client.ActivityChanged += OnActivityChanged;
    }

    public BoothActivity Activity
    {
        get => _activity;
        private set
        {
            if (SetField(ref _activity, value))
            {
                foreach (var name in new[]
                {
                    nameof(Text), nameof(IsActive), nameof(HasProgress),
                    nameof(Progress), nameof(IsThrottled),
                })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    public string Text => _activity.Text;

    public bool IsActive => _activity.IsActive;

    /// <summary>長さの分かっている待ちか。通信そのものは分からないので不定表示になる。</summary>
    public bool HasProgress => _activity.Progress is not null;

    public double Progress => _activity.Progress ?? 0;

    public bool IsThrottled => _activity.IsThrottled;

    /// <summary>
    /// 取得は別のスレッドから知らせてくるので、UIスレッドへ渡し直す。
    /// 0.2秒ごとに来るだけなので、まとめる必要はない。
    /// </summary>
    private void OnActivityChanged(BoothActivity activity)
    {
        if (_dispatcher.CheckAccess())
        {
            Activity = activity;
            return;
        }

        _dispatcher.BeginInvoke(() => Activity = activity);
    }
}
