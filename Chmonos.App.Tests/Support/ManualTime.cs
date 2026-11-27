namespace Chmonos.App.Tests.Support;

/// <summary>
/// 試験が手で進める作り物の時計。<see cref="Advance"/> で進めた時に、時刻の来たタイマーをその場（呼んだスレッド）で鳴らす。
/// 本物の時計を待たずに「299ms ではまだ・300ms で鳴る・途中の変化で数え直す」を確かめるため（時計に左右される試験を書かない決まり）
/// </summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private TimeSpan _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        _timers.Add(timer);
        return timer;
    }

    /// <summary>時計を進め、時刻の来たタイマーを鳴らす（繰り返しは使わないので1回だけ）。</summary>
    public void Advance(TimeSpan span)
    {
        _now += span;
        foreach (var timer in _timers.Where(timer => timer.Due is { } due && due <= _now).ToList())
        {
            timer.Due = null;
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
    {
        public TimeSpan? Due { get; set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Dispose() => owner._timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
