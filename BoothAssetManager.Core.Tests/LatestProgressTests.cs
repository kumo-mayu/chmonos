using BoothAssetManager.Core.Scanning;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 取り込みの1ファイルごとの知らせを、1秒に10回ほどに間引いて最新だけを渡す（<see cref="LatestProgress{T}"/>）。
/// 時刻は差し替えて決める（時計に結果が左右される試験にしない）。
/// </summary>
public sealed class LatestProgressTests
{
    private sealed class Recorder : IProgress<ImportProgress>
    {
        public List<ImportProgress> Received { get; } = [];

        public void Report(ImportProgress value) => Received.Add(value);
    }

    private static ImportProgress At(ImportPhase phase, int current) => new() { Phase = phase, Current = current };

    [Fact]
    public void PassesOnlyTheLatestWithinTheInterval()
    {
        var now = 0L;
        var inner = new Recorder();
        var progress = new LatestProgress<ImportProgress>(inner, report => report.Phase, () => now);

        progress.Report(At(ImportPhase.Scanning, 1));
        now = 10;
        progress.Report(At(ImportPhase.Scanning, 2));
        now = 50;
        progress.Report(At(ImportPhase.Scanning, 3));

        Assert.Equal([1], inner.Received.Select(report => report.Current));

        now = 100;
        progress.Report(At(ImportPhase.Scanning, 4));
        Assert.Equal([1, 4], inner.Received.Select(report => report.Current));
    }

    /// <summary>段の終わりに、持っていた最新を渡す（最後のファイルの件数を落とさない）。</summary>
    [Fact]
    public void FlushHandsOverTheLastHeldReport()
    {
        var now = 0L;
        var inner = new Recorder();
        var progress = new LatestProgress<ImportProgress>(inner, report => report.Phase, () => now);

        progress.Report(At(ImportPhase.Resolving, 1));
        now = 5;
        progress.Report(At(ImportPhase.Resolving, 2));
        progress.Report(At(ImportPhase.Resolving, 3));
        progress.Flush();
        progress.Flush();

        Assert.Equal([1, 3], inner.Received.Select(report => report.Current));
    }

    /// <summary>段が変わった知らせは間引かない。</summary>
    [Fact]
    public void AStageChangeIsPassedAtOnce()
    {
        var now = 0L;
        var inner = new Recorder();
        var progress = new LatestProgress<ImportProgress>(inner, report => report.Phase, () => now);

        progress.Report(At(ImportPhase.Scanning, 9));
        now = 1;
        progress.Report(At(ImportPhase.Resolving, 1));

        Assert.Equal([ImportPhase.Scanning, ImportPhase.Resolving], inner.Received.Select(report => report.Phase));
    }

    [Fact]
    public void DoesNothingWithoutAReceiver()
    {
        var progress = new LatestProgress<ImportProgress>(null, report => report.Phase, () => 0);

        progress.Report(At(ImportPhase.Scanning, 1));
        progress.Flush();
    }
}
