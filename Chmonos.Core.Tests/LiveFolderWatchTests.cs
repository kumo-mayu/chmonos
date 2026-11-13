using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 開いている間の監視フォルダの見張り（plan 49）。出来事・待ち・書き終わったかの確かめは作り物に差し替え、
/// 実際のファイルの出来事と時計に結果を左右させない（CLAUDE.md「確かめ方」）。
/// </summary>
public class LiveFolderWatchTests
{
    private const string Folder = @"D:\作り物\監視";
    private static readonly LiveFolderWatchTiming Timing = new();

    private readonly ManualDelays _delays = new();
    private readonly FakeEventsHub _hub = new();
    private readonly FakeProbe _probe = new();
    private readonly List<IReadOnlyList<LiveWatchFile>> _found = [];
    private readonly object _foundLock = new();
    private IReadOnlyList<string> _newFiles = [];
    private int _findCalls;

    private LiveFolderWatch Create(Func<string, bool>? folderPresent = null)
    {
        var watch = new LiveFolderWatch(
            _hub.Create,
            (_, _) =>
            {
                Interlocked.Increment(ref _findCalls);
                return Task.FromResult(new WatchResult { Folders = [Folder], NewFiles = _newFiles });
            },
            _probe,
            _delays.Delay,
            folderPresent ?? (_ => true),
            Timing);
        watch.Found = files =>
        {
            lock (_foundLock)
            {
                _found.Add(files);
            }

            return Task.CompletedTask;
        };
        return watch;
    }

    private async Task<LiveFolderWatch> StartAsync(IReadOnlyList<string>? before = null)
    {
        _newFiles = before ?? [];
        var watch = Create();
        watch.Start([Folder]);
        await watch.BaselineTaken.WaitAsync(TimeSpan.FromSeconds(10));
        return watch;
    }

    private IReadOnlyList<IReadOnlyList<LiveWatchFile>> Found()
    {
        lock (_foundLock)
        {
            return [.. _found];
        }
    }

    private async Task<IReadOnlyList<LiveWatchFile>> NextFoundAsync(int count)
    {
        await Until(() => Found().Count >= count, "見つけた物が渡る");
        return Found()[count - 1];
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > limit)
            {
                throw new TimeoutException($"{what}まで待ったが来なかった");
            }

            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task 出来事が来たら静けさを待って探し_書き終わった新着を渡す()
    {
        using var watch = await StartAsync();
        _newFiles = [@"D:\作り物\監視\衣装.zip"];

        _hub.Single(Folder).Fire(@"D:\作り物\監視\衣装.zip");
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        var found = await NextFoundAsync(1);
        Assert.Equal([@"D:\作り物\監視\衣装.zip"], found.Select(file => file.Path));
        Assert.True(found[0].Arrived);
    }

    /// <summary>エクスプローラのコピーや zip の展開は出来事が数百続く。静けさを待つ間に来た物は、待ち直して1回にまとめる。</summary>
    [Fact]
    public async Task 続けて置かれた物は_1回の探し直しにまとめる()
    {
        using var watch = await StartAsync();
        var events = _hub.Single(Folder);
        _newFiles = [@"D:\作り物\監視\1.zip", @"D:\作り物\監視\2.zip"];

        events.Fire(@"D:\作り物\監視\1.zip");
        Assert.True(await _delays.WaitPendingAsync(Timing.Quiet));
        events.Fire(@"D:\作り物\監視\2.zip");
        events.Fire(@"D:\作り物\監視\2.zip");
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        var found = await NextFoundAsync(1);
        Assert.Equal(2, found.Count);

        // 控え1回と探し直し1回だけ。待つ間に溜まった頼みで、もう1回探し直さない
        Assert.False(await _delays.WaitPendingAsync(Timing.Quiet, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(2, _findCalls);
        Assert.Single(Found());
    }

    [Fact]
    public async Task 大きさが動いている物は渡さず_落ち着いてから渡す()
    {
        using var watch = await StartAsync();
        var path = @"D:\作り物\監視\落としている途中.zip";
        _newFiles = [path];
        _probe.Sequence(path, new FileSettle(10, Day, true), new FileSettle(20, Day, true), new FileSettle(20, Day, true), new FileSettle(20, Day, true));

        _hub.Single(Folder).Fire(path);
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Empty(await NextFoundAsync(1));

        // 出来事が来なくても確かめ直す（一覧の大きさは閉じるまで変わらない書き方がある）
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Equal([path], (await NextFoundAsync(2)).Select(file => file.Path));
    }

    /// <summary>掴まれている・隣にダウンロードの途中のファイルがある物（Ready が偽）は、大きさが止まっていても渡さない。</summary>
    [Fact]
    public async Task 書いている途中の印がある物は渡さない()
    {
        using var watch = await StartAsync();
        var path = @"D:\作り物\監視\掴まれている.zip";
        _newFiles = [path];
        _probe.Sequence(path, new FileSettle(10, Day, false), new FileSettle(10, Day, false), new FileSettle(10, Day, true), new FileSettle(10, Day, true));

        _hub.Single(Folder).Fire(path);
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Empty(await NextFoundAsync(1));

        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Single(await NextFoundAsync(2));
    }

    [Fact]
    public async Task 取り込み中は渡さず_終わってから探し直して渡す()
    {
        var busy = true;
        _newFiles = [];
        var watch = Create();
        watch.IsBusy = () => busy;
        watch.Start([Folder]);
        await watch.BaselineTaken;
        _newFiles = [@"D:\作り物\監視\衣装.zip"];

        _hub.Single(Folder).Fire(@"D:\作り物\監視\衣装.zip");
        await _delays.ReleaseAsync(Timing.Quiet);
        Assert.False(await _delays.WaitPendingAsync(Timing.Settle, TimeSpan.FromMilliseconds(300)));
        Assert.Empty(Found());

        busy = false;
        watch.Kick();
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Single(await NextFoundAsync(1));
        watch.Dispose();
    }

    /// <summary>取り込みが終わっても、取り込み中に何も見つけていなければ探し直さない（終わるたびに監視フォルダを舐めない）。</summary>
    [Fact]
    public async Task 取り込み中に何も無ければ_終わっても探し直さない()
    {
        using var watch = await StartAsync();

        watch.Kick();

        Assert.False(await _delays.WaitPendingAsync(Timing.Quiet, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(1, _findCalls);
    }

    [Fact]
    public async Task あふれたら探し直す()
    {
        using var watch = await StartAsync();
        _newFiles = [@"D:\作り物\監視\衣装.zip"];

        _hub.Single(Folder).Fail(FolderEventsFailure.Overflow);
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        Assert.Single(await NextFoundAsync(1));
        Assert.False(_hub.Single(Folder).Disposed);
    }

    /// <summary>フォルダが消えた・ドライブが外れたら、見張りを外して後で張り直し、張り直せたら探し直す（その間に置かれた物を拾う）。</summary>
    [Fact]
    public async Task 壊れたら張り直し_張り直せたら探し直す()
    {
        var present = false;
        _newFiles = [];
        var watch = Create(_ => present);
        watch.Start([Folder]);
        await watch.BaselineTaken;
        var first = _hub.Single(Folder);

        first.Fail(FolderEventsFailure.Broken);
        Assert.True(first.Disposed);
        await _delays.ReleaseAsync(Timing.Quiet);

        // まだ戻っていない：張らない
        await _delays.ReleaseAsync(Timing.Rearm);
        Assert.True(await _delays.WaitPendingAsync(Timing.Rearm));
        Assert.Single(_hub.Of(Folder));

        present = true;
        _newFiles = [@"D:\作り物\監視\外れている間に置いた.zip"];
        await _delays.ReleaseAsync(Timing.Rearm);
        await Until(() => _hub.Of(Folder).Count == 2, "張り直す");
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        Assert.Equal([@"D:\作り物\監視\外れている間に置いた.zip"], (await NextFoundAsync(2)).Select(file => file.Path));

        // 壊れたときの探し直し（まだ何も無い）が先に渡っている
        Assert.Empty(Found()[0]);
        watch.Dispose();
    }

    [Fact]
    public async Task 止めたら見張りを外し_後から来た出来事を捨てる()
    {
        var watch = await StartAsync();
        var events = _hub.Single(Folder);

        watch.Stop();
        events.Fire(@"D:\作り物\監視\衣装.zip");

        Assert.True(events.Disposed);
        Assert.False(watch.IsRunning);
        Assert.False(await _delays.WaitPendingAsync(Timing.Quiet, TimeSpan.FromMilliseconds(300)));
    }

    /// <summary>自動で取り込むのは見張り始めた後に現れた物だけ。前から置いてあって取り込まずにいた物は見分けて渡す。</summary>
    [Fact]
    public async Task 見張り始める前からの新着は_現れた物と見分ける()
    {
        using var watch = await StartAsync([@"D:\作り物\監視\前から.zip"]);
        _newFiles = [@"D:\作り物\監視\前から.zip", @"D:\作り物\監視\今置いた.zip"];

        _hub.Single(Folder).Fire(@"D:\作り物\監視\今置いた.zip");
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        var found = await NextFoundAsync(1);
        Assert.False(found.Single(file => file.Path.EndsWith("前から.zip", StringComparison.Ordinal)).Arrived);
        Assert.True(found.Single(file => file.Path.EndsWith("今置いた.zip", StringComparison.Ordinal)).Arrived);
    }

    [Fact]
    public async Task 保存先の中とダウンロードの途中の名前の出来事では探さない()
    {
        _newFiles = [];
        var watch = Create();
        watch.IgnoredRoot = @"D:\作り物\監視\保存先";
        watch.Start([Folder]);
        await watch.BaselineTaken;
        var events = _hub.Single(Folder);

        events.Fire(@"D:\作り物\監視\保存先\items\1.json");
        events.Fire(@"D:\作り物\監視\衣装.zip.crdownload");
        events.Fire(@"D:\作り物\監視\衣装.zip.part");

        Assert.False(await _delays.WaitPendingAsync(Timing.Quiet, TimeSpan.FromMilliseconds(300)));
        watch.Dispose();
    }

    /// <summary>
    /// 新しいと数えるのは起動時と同じ判定（走査の控え・外した物）。出来事の中身は信じない：取り込み済みと外した物の出来事が来ても渡さない。
    /// </summary>
    [Fact]
    public async Task 既に取り込んだ物と外した物は数えない()
    {
        var root = Path.Combine(Path.GetTempPath(), "chmonos-live-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "download");
        Directory.CreateDirectory(folder);
        try
        {
            var paths = new AppPaths(Path.Combine(root, "library"));
            paths.EnsureCreated();
            var store = new DataStore(paths);
            var folderWatch = new FolderWatch(store);

            var imported = Write(folder, "取り込み済み.zip");
            var excluded = Write(folder, "外した.zip");
            await RememberAsync(store, imported, "AAAA");
            await RememberAsync(store, excluded, "BBBB");
            var excludedList = store.Excluded.Load();
            excludedList.Add(new ExcludedEntry { Hash = "BBBB", Paths = [excluded], ExcludedAt = DateTimeOffset.Now });
            await store.Excluded.SaveAsync(excludedList);

            var hub = new FakeEventsHub();
            using var watch = new LiveFolderWatch(hub.Create, folderWatch.FindNewAsync, new FileSettleProbe(), _delays.Delay, _ => true, Timing);
            var found = new List<IReadOnlyList<LiveWatchFile>>();
            watch.Found = files =>
            {
                lock (found)
                {
                    found.Add(files);
                }

                return Task.CompletedTask;
            };
            watch.Start([folder]);
            await watch.BaselineTaken;

            var added = Write(folder, "新しい.zip");
            hub.Single(folder).Fire(imported);
            hub.Single(folder).Fire(excluded);
            hub.Single(folder).Fire(added);
            await _delays.ReleaseAsync(Timing.Quiet);
            await _delays.ReleaseAsync(Timing.Settle);

            await Until(() => { lock (found) { return found.Count > 0; } }, "見つけた物が渡る");
            Assert.Equal([added], found[0].Select(file => file.Path));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>本物の確かめ：書き込みのために掴まれている物、隣にダウンロードの途中のファイルがある物は、書いている途中と見る。</summary>
    [Fact]
    public void 掴まれている物とダウンロードの途中の隣がある物は_書いている途中と見る()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chmonos-settle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var probe = new FileSettleProbe();
            var done = Write(folder, "書き終わった.zip");
            Assert.True(probe.Probe(done)!.Value.Ready);

            // Firefox は出来上がる名前の空のファイルを先に作り、.part に書く
            var placeholder = Write(folder, "落としている.zip");
            File.WriteAllText(placeholder + ".part", "途中");
            Assert.False(probe.Probe(placeholder)!.Value.Ready);

            var held = Write(folder, "書いている.zip");
            using (new FileStream(held, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                Assert.False(probe.Probe(held)!.Value.Ready);
            }

            Assert.True(probe.Probe(held)!.Value.Ready);
            Assert.Null(probe.Probe(Path.Combine(folder, "無い.zip")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static readonly DateTime Day = new(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);

    private static string Write(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        return path;
    }

    private static async Task RememberAsync(DataStore store, string path, string hash)
    {
        var info = new FileInfo(path);
        var cache = store.ScanCache.Load();
        cache.Add(new ScanCacheEntry
        {
            Path = path,
            SizeBytes = info.Length,
            ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            Hash = hash,
        });
        await store.ScanCache.SaveAsync(cache);
    }

    /// <summary>手で進める待ち。待ちが来たかを見て、1つずつ終わらせる。</summary>
    internal sealed class ManualDelays
    {
        private readonly object _lock = new();
        private readonly List<(TimeSpan Span, TaskCompletionSource Done)> _pending = [];

        public Task Delay(TimeSpan span, CancellationToken token)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => done.TrySetCanceled(token));
            lock (_lock)
            {
                _pending.Add((span, done));
            }

            return done.Task;
        }

        /// <summary>その長さの待ちが来るまで待つ。来なければ false（来ないことを確かめるときは短く待つ）。</summary>
        public async Task<bool> WaitPendingAsync(TimeSpan span, TimeSpan? within = null)
        {
            var limit = DateTime.UtcNow + (within ?? TimeSpan.FromSeconds(10));
            while (DateTime.UtcNow < limit)
            {
                lock (_lock)
                {
                    _pending.RemoveAll(entry => entry.Done.Task.IsCompleted);
                    if (_pending.Any(entry => entry.Span == span))
                    {
                        return true;
                    }
                }

                await Task.Delay(5);
            }

            return false;
        }

        public async Task ReleaseAsync(TimeSpan span)
        {
            Assert.True(await WaitPendingAsync(span), $"{span} の待ちが来ない");
            lock (_lock)
            {
                var index = _pending.FindIndex(entry => entry.Span == span && !entry.Done.Task.IsCompleted);
                var entry = _pending[index];
                _pending.RemoveAt(index);
                entry.Done.TrySetResult();
            }
        }
    }

    internal sealed class FakeEvents : IFolderEvents
    {
        public event Action<string>? Changed;

        public event Action<FolderEventsFailure>? Failed;

        public bool Disposed { get; private set; }

        public void Fire(string path) => Changed?.Invoke(path);

        public void Fail(FolderEventsFailure failure) => Failed?.Invoke(failure);

        public void Dispose() => Disposed = true;
    }

    internal sealed class FakeEventsHub
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, List<FakeEvents>> _made = new(StringComparer.OrdinalIgnoreCase);

        public IFolderEvents? Create(string folder)
        {
            var events = new FakeEvents();
            lock (_lock)
            {
                if (!_made.TryGetValue(folder, out var list))
                {
                    _made[folder] = list = [];
                }

                list.Add(events);
            }

            return events;
        }

        public IReadOnlyList<FakeEvents> Of(string folder)
        {
            lock (_lock)
            {
                return _made.TryGetValue(folder, out var list) ? [.. list] : [];
            }
        }

        public FakeEvents Single(string folder) => Of(folder).Last();
    }

    /// <summary>書き終わったかの作り物。決めていない場所は「書き終わっている」と答える。</summary>
    internal sealed class FakeProbe : IFileSettleProbe
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Queue<FileSettle>> _sequences = new(StringComparer.OrdinalIgnoreCase);

        public void Sequence(string path, params FileSettle[] answers)
        {
            lock (_lock)
            {
                _sequences[path] = new Queue<FileSettle>(answers);
            }
        }

        public FileSettle? Probe(string path)
        {
            lock (_lock)
            {
                if (_sequences.TryGetValue(path, out var queue) && queue.Count > 0)
                {
                    return queue.Count == 1 ? queue.Peek() : queue.Dequeue();
                }
            }

            return new FileSettle(1, Day, true);
        }
    }
}
