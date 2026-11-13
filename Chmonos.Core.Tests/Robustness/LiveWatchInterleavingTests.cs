using System.Collections.Concurrent;
using Chmonos.Core.Scanning;
using static Chmonos.Core.Tests.LiveFolderWatchTests;

namespace Chmonos.Core.Tests.Robustness;

/// <summary>
/// 開いている間の監視で、止める・張り直す・書き終わりの確かめが重なる道（2026-11-13 の Codex の attack から移した物）。
/// 1件目は W5（止めた古い見張りの控えが、張り直した見張りの控えに混ざる）。
/// </summary>
public sealed class LiveWatchInterleavingTests : IDisposable
{
    private readonly RobustnessDirectory _directory = new();
    private readonly string _root;
    private readonly ManualDelays _time = new();
    private readonly FakeEventsHub _hub = new();
    private readonly ConcurrentQueue<IReadOnlyList<LiveWatchFile>> _found = new();
    private static readonly LiveFolderWatchTiming Timing = new();
    private string FilePath => Path.Combine(_root, "sample.zip");
    private readonly DateTime _day = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);

    public LiveWatchInterleavingTests() => _root = _directory.Root;

    private LiveFolderWatch Create(Func<IReadOnlyList<string>, CancellationToken, Task<WatchResult>> find, IFileSettleProbe? probe = null)
    {
        var watch = new LiveFolderWatch(_hub.Create, find, probe ?? new FakeProbe(), _time.Delay, _time, Directory.Exists);
        watch.Found = files => { _found.Enqueue(files); return Task.CompletedTask; };
        return watch;
    }

    private WatchResult Result(params string[] files) => new() { Folders = [_root], NewFiles = files };

    [Fact]
    public async Task 停止した旧監視の遅れた控えは_再開した監視を汚さない()
    {
        var oldScan = new TaskCompletionSource<WatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var watch = Create((_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) { entered.SetResult(); return oldScan.Task; }
            return Task.FromResult(call == 2 ? Result() : Result(FilePath));
        });
        watch.Start([_root]);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var oldLoop = watch.Running;
        watch.Stop();
        watch.Start([_root]);
        await watch.BaselineTaken.WaitAsync(TimeSpan.FromSeconds(10));
        // 停止要求の直前に読み終わった結果が、継続の実行待ちだった状況を作る。
        oldScan.SetResult(Result(FilePath));
        await oldLoop.WaitAsync(TimeSpan.FromSeconds(10));
        _hub.Single(_root).Fire(FilePath);
        await _time.ReleaseAsync(Timing.Quiet);
        await _time.ReleaseAsync(Timing.Settle);
        await Until(() => watch.Passes == 1);
        Assert.True(Assert.Single(_found.Last()).Arrived, "旧監視の控えが混ざり、新着が Arrived=false になった");
    }

    [Fact]
    public async Task 安定確認の途中で停止すると_知らせずに終了する()
    {
        var calls = 0;
        using var watch = Create((_, _) => Task.FromResult(++calls == 1 ? Result() : Result(FilePath)));
        watch.Start([_root]);
        await watch.BaselineTaken;
        _hub.Single(_root).Fire(FilePath);
        await _time.ReleaseAsync(Timing.Quiet);
        Assert.True(await _time.WaitPendingAsync(Timing.Settle));
        var loop = watch.Running;
        var events = _hub.Single(_root);
        watch.Stop();
        events.Fire(FilePath);
        events.Fail(FolderEventsFailure.Broken);
        await loop.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(_found);
        Assert.True(events.Disposed);
    }

    [Fact]
    public async Task 同名の付け替えと成長が続く間は渡さず_最後の安定した物だけ渡す()
    {
        var probe = new FakeProbe();
        probe.Sequence(FilePath,
            new(1, _day, true), new(1, _day.AddSeconds(1), true),
            new(1, _day.AddSeconds(1), true), new(2, _day.AddSeconds(2), true),
            new(2, _day.AddSeconds(2), true), new(2, _day.AddSeconds(2), true));
        var calls = 0;
        using var watch = Create((_, _) => Task.FromResult(++calls == 1 ? Result() : Result(FilePath)), probe);
        watch.Start([_root]);
        await watch.BaselineTaken;
        _hub.Single(_root).Fire(FilePath);
        for (var pass = 1; pass <= 3; pass++)
        {
            await _time.ReleaseAsync(Timing.Quiet);
            await _time.ReleaseAsync(Timing.Settle);
            await Until(() => watch.Passes == pass);
            if (pass < 3) Assert.All(_found, files => Assert.Empty(files));
        }
        Assert.Single(_found.SelectMany(files => files));
    }

    [Fact]
    public async Task 安定確認の間に消えた物は渡さない()
    {
        File.WriteAllText(FilePath, "synthetic");
        var calls = 0;
        using var watch = Create((_, _) => Task.FromResult(++calls == 1 ? Result() : Result(FilePath)), new FileSettleProbe());
        watch.Start([_root]);
        await watch.BaselineTaken;
        _hub.Single(_root).Fire(FilePath);
        await _time.ReleaseAsync(Timing.Quiet);
        Assert.True(await _time.WaitPendingAsync(Timing.Settle));
        File.Delete(FilePath);
        await _time.ReleaseAsync(Timing.Settle);
        await Until(() => watch.Passes == 1);
        Assert.All(_found, files => Assert.Empty(files));
    }

    [Fact]
    public async Task 書き込みで掴んだ物は渡さず_解放後に出来事なしでも拾う()
    {
        File.WriteAllText(FilePath, "synthetic");
        var calls = 0;
        using var watch = Create((_, _) => Task.FromResult(++calls == 1 ? Result() : Result(FilePath)), new FileSettleProbe());
        watch.Start([_root]);
        await watch.BaselineTaken;
        using (var writer = new FileStream(FilePath, FileMode.Open, FileAccess.Write, FileShare.Read))
        {
            _hub.Single(_root).Fire(FilePath);
            await _time.ReleaseAsync(Timing.Quiet);
            await _time.ReleaseAsync(Timing.Settle);
            await Until(() => watch.Passes == 1);
            Assert.All(_found, files => Assert.Empty(files));
        }
        await _time.ReleaseAsync(Timing.Quiet);
        await _time.ReleaseAsync(Timing.Settle);
        await Until(() => watch.Passes == 2);
        Assert.Single(_found.SelectMany(files => files));
    }

    [Fact]
    public async Task フォルダが消えて戻れば_停止した見張りを張り直して拾う()
    {
        var calls = 0;
        using var watch = Create((_, _) => Task.FromResult(++calls == 1 || !Directory.Exists(_root) ? Result() : Result(FilePath)));
        watch.Start([_root]);
        await watch.BaselineTaken;
        var old = _hub.Single(_root);
        Directory.Delete(_root);
        old.Fail(FolderEventsFailure.Broken);
        await _time.ReleaseAsync(Timing.Quiet);
        await Until(() => watch.Passes == 1);
        Directory.CreateDirectory(_root);
        await _time.ReleaseAsync(Timing.Rearm);
        await Until(() => _hub.Of(_root).Count == 2);
        await _time.ReleaseAsync(Timing.Quiet);
        await _time.ReleaseAsync(Timing.Settle);
        await Until(() => watch.Passes == 2);
        Assert.True(old.Disposed);
        Assert.Single(_found.SelectMany(files => files));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    public void Dispose() => _directory.Dispose();
}
