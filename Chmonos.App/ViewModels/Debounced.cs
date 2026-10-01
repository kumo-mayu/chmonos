using System.Windows.Threading;

namespace Chmonos.App.ViewModels;

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

    private readonly Func<Task> _run;

    public Debounced(TimeSpan wait, Action run)
        : this(wait, () =>
        {
            run();
            return Task.CompletedTask;
        })
    {
    }

    /// <summary>
    /// 保存のように待てる物は、こちらで作る。<see cref="RunNowAsync"/> が
    /// **書き終わりまで待てる**ようになる（閉じる前に待つため）。
    /// </summary>
    public Debounced(TimeSpan wait, Func<Task> run)
    {
        _run = run;
        _timer = new DispatcherTimer { Interval = wait };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _run().Forget();
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
    /// 画面を離れる前・閉じる前に呼ぶ——打ち終えてすぐ閉じると、待っている 0.8 秒のうちに捨てられていた。
    /// </summary>
    /// <returns>書き終わりまでの待ち。待っている物が無ければ済んだ状態で返る。</returns>
    public Task RunNowAsync()
    {
        if (!_timer.IsEnabled)
        {
            return Task.CompletedTask;
        }

        _timer.Stop();
        return _run();
    }
}
