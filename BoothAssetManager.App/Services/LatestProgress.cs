using System.Windows.Threading;

namespace BoothAssetManager.App.Services;

/// <summary>
/// 進み具合を**最新の1件だけ**、決まった間隔より細かくならないように画面へ届ける。
///
/// 取り込みは走査の間ファイル1つごと・取得の間1件ごとに進み具合を知らせ、`Progress&lt;T&gt;` はその全部を画面のスレッドへ積んでいた。
/// 1件ごとに表示の欄を10前後知らせ直し、通信の帯も描き直すので、ファイルの多い取り込み元では画面のスレッドがそれで埋まった。
/// 人の目で追えるのは1秒に数回までなので、途中の値は捨てて最新だけを出す。
/// 最初の1件はすぐ出し（押してから反応が遅れて見えないように）、続きは <c>interval</c> ごとにまとめる。
/// 終わったら <see cref="Complete"/> で残りを出し切ってから止める（最後の件数が欠けないように）。
/// </summary>
public sealed class LatestProgress<T> : IProgress<T>
{
    private readonly Action<T> _apply;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();

    private T _latest = default!;
    private bool _hasLatest;

    /// <summary>出す役が決まっているか（最初の1件の呼び出しを積んだか、時計が回っている）。立っている間は積み直さない。</summary>
    private bool _draining;

    private bool _completed;

    public LatestProgress(Action<T> apply, TimeSpan interval, Dispatcher dispatcher)
    {
        _apply = apply;
        _dispatcher = dispatcher;
        _timer = new DispatcherTimer(interval, DispatcherPriority.Normal, (_, _) => Tick(), dispatcher) { IsEnabled = false };
    }

    /// <summary>画面で反映した回数（効き目を数えるため）。</summary>
    public int AppliedCount { get; private set; }

    /// <summary>届いた回数。</summary>
    public int ReportedCount => _reported;

    private int _reported;

    public void Report(T value)
    {
        Interlocked.Increment(ref _reported);
        lock (_gate)
        {
            _latest = value;
            _hasLatest = true;
            if (_draining)
            {
                return;
            }

            _draining = true;
        }

        _dispatcher.BeginInvoke(Start);
    }

    /// <summary>最後の1件を出し切って止める。**画面のスレッドで呼ぶ。**これより後に届いた分は出さない。</summary>
    public void Complete()
    {
        Drain();
        _completed = true;
        _timer.Stop();
    }

    private void Start()
    {
        Drain();
        if (!_completed)
        {
            _timer.Start();
        }
    }

    private void Tick()
    {
        if (!Drain())
        {
            lock (_gate)
            {
                // 止める直前に届いた分は、次の Report が積み直す（ここで印を下ろすので）
                if (_hasLatest)
                {
                    return;
                }

                _draining = false;
            }

            _timer.Stop();
        }
    }

    /// <returns>出す物があったか。</returns>
    private bool Drain()
    {
        T value;
        lock (_gate)
        {
            if (!_hasLatest)
            {
                return false;
            }

            value = _latest;
            _latest = default!;
            _hasLatest = false;
        }

        if (_completed)
        {
            return false;
        }

        AppliedCount++;
        _apply(value);
        return true;
    }
}
