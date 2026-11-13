namespace Chmonos.App.Tests.Support;

/// <summary>
/// 待つとすぐ終わり、待った分だけ進む作り物の時計（開いている間の監視の静けさ・書き終わりの確かめを、試験で待たないため）。
/// 監視は「最後の出来事からの残り」を時計で測って待つので、待ちだけすぐ終わらせて時計を本物にすると、残りが減らずに空回りする。
/// 刻みは1秒あたり <see cref="TimeSpan.TicksPerSecond"/>（待った長さがそのまま時刻の差になる）
/// </summary>
internal sealed class InstantTime : TimeProvider
{
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _now);

    public Task Delay(TimeSpan span, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled(token);
        }

        Interlocked.Add(ref _now, Math.Max(span.Ticks, 0));
        return Task.CompletedTask;
    }
}
