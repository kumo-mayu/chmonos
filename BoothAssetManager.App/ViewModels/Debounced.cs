using System.Windows.Threading;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 続けて来る変更を1回にまとめる。
///
/// スライダをドラッグすると、動かすたびに値が変わる（1秒に数十回）。
/// **そのたびに絞り直すと、件数に比例した走査が追いつかない。**
/// 表示（数・要約）はすぐ更新し、絞り直しだけを止まってから1回行うために使う。
///
/// 画面のスレッドで動かす（<see cref="DispatcherTimer"/>）。作るのも画面のスレッドで。
/// </summary>
public sealed class Debounced
{
    private readonly DispatcherTimer _timer;

    private readonly Action _run;

    public Debounced(TimeSpan wait, Action run)
    {
        _run = run;
        _timer = new DispatcherTimer { Interval = wait };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            run();
        };
    }

    /// <summary>やり直しの合図。前の待ちは捨てて、待ち時間を数え直す。</summary>
    public void Request()
    {
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>待っているものを捨てる。</summary>
    public void Cancel() => _timer.Stop();

    /// <summary>
    /// 待っているものがあれば、待たずに今やる（ユーザ判断 2026-09-20・I10）。
    /// 画面を離れる前に呼ぶ——打ち終えてすぐ閉じると、待っている 0.8 秒のうちに捨てられていた。
    /// </summary>
    public bool RunNow()
    {
        if (!_timer.IsEnabled)
        {
            return false;
        }

        _timer.Stop();
        _run();
        return true;
    }
}
