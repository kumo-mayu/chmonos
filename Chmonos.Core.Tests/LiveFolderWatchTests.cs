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

    /// <summary>新着を探した時刻（作り物の時計）。探す回りは1本なので、増えるのは1つのスレッドから。</summary>
    private readonly List<long> _findAt = [];

    private LiveFolderWatch Create(Func<string, bool>? folderPresent = null)
    {
        var watch = new LiveFolderWatch(
            _hub.Create,
            (_, _) =>
            {
                _findAt.Add(_delays.GetTimestamp());
                Interlocked.Increment(ref _findCalls);
                return Task.FromResult(new WatchResult { Folders = [Folder], NewFiles = _newFiles });
            },
            _probe,
            _delays.Delay,
            _delays,
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
        _delays.Advance(TimeSpan.FromSeconds(3));
        events.Fire(@"D:\作り物\監視\2.zip");
        events.Fire(@"D:\作り物\監視\2.zip");
        await _delays.ReleaseAsync(Timing.Quiet);

        // 後の出来事（3秒後）から静けさの分までの残り
        await _delays.ReleaseAsync(TimeSpan.FromSeconds(3));
        await _delays.ReleaseAsync(Timing.Settle);

        var found = await NextFoundAsync(1);
        Assert.Equal(2, found.Count);

        // 控え1回と探し直し1回だけ。待つ間に溜まった頼みで、もう1回探し直さない
        Assert.False(await _delays.WaitPendingAsync(Timing.Quiet, TimeSpan.FromMilliseconds(300)));
        Assert.Equal(2, _findCalls);
        Assert.Single(Found());
    }

    /// <summary>
    /// 1つのファイルのコピーでも、作成・書き込み・変更の出来事が同じ瞬間に続けて来る。1つ目で静けさの待ちに入った後に残りが届いても、
    /// 静けさは最後の出来事から5秒の1回分で終わる（plan 50：待ちを丸ごとやり直していて、実機で静けさが10秒になっていた）
    /// </summary>
    [Fact]
    public async Task 同じ瞬間に出来事が3つ来ても_静けさは1回分で終わる()
    {
        using var watch = await StartAsync();
        var events = _hub.Single(Folder);
        var path = @"D:\作り物\監視\衣装.zip";
        _newFiles = [path];
        var start = _delays.GetTimestamp();

        // 実機の順：1つ目で回りが起きて待ちに入った後に、同じ瞬間の残り2つが届く
        events.Fire(path);
        Assert.True(await _delays.WaitPendingAsync(Timing.Quiet));
        events.Fire(path);
        events.Fire(path);
        await _delays.ReleaseAsync(Timing.Quiet);

        await Until(() => _findCalls == 2, "新着を探す");
        Assert.Equal(Timing.Quiet, _delays.GetElapsedTime(start, _findAt[^1]));
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Equal([path], (await NextFoundAsync(1)).Select(file => file.Path));
    }

    /// <summary>静けさの待ちの途中（3秒後）に出来事が来たら、その出来事から静けさの分まで延びる（合計8秒）。</summary>
    [Fact]
    public async Task 待つ間に出来事が来たら_その出来事から静けさの分まで延びる()
    {
        using var watch = await StartAsync();
        var events = _hub.Single(Folder);
        _newFiles = [@"D:\作り物\監視\衣装.zip"];
        var start = _delays.GetTimestamp();

        events.Fire(@"D:\作り物\監視\衣装.zip");
        Assert.True(await _delays.WaitPendingAsync(Timing.Quiet));
        _delays.Advance(TimeSpan.FromSeconds(3));
        events.Fire(@"D:\作り物\監視\衣装.zip");
        await _delays.ReleaseAsync(Timing.Quiet);

        // 5秒たっても、後の出来事からはまだ2秒。探さない
        Assert.Equal(1, _findCalls);
        await _delays.ReleaseAsync(TimeSpan.FromSeconds(3));

        await Until(() => _findCalls == 2, "新着を探す");
        Assert.Equal(TimeSpan.FromSeconds(8), _delays.GetElapsedTime(start, _findAt[^1]));
    }

    /// <summary>出来事が静けさより短い間をあけて続く間は、探さない（コピー・展開の途中で中途半端な一覧を出さない）。</summary>
    [Fact]
    public async Task 出来事が続く間は_静けさを終えない()
    {
        using var watch = await StartAsync();
        var events = _hub.Single(Folder);
        _newFiles = [@"D:\作り物\監視\衣装.zip"];

        events.Fire(@"D:\作り物\監視\衣装.zip");
        Assert.True(await _delays.WaitPendingAsync(Timing.Quiet));
        _delays.Advance(TimeSpan.FromSeconds(4));
        events.Fire(@"D:\作り物\監視\衣装.zip");

        // 5秒の待ちが終わった時点（後の出来事から1秒）：残り4秒
        await _delays.ReleaseAsync(Timing.Quiet);
        Assert.True(await _delays.WaitPendingAsync(TimeSpan.FromSeconds(4)));
        _delays.Advance(TimeSpan.FromSeconds(3));
        events.Fire(@"D:\作り物\監視\衣装.zip");

        // 4秒の待ちが終わった時点（後の出来事から1秒）：また残り4秒
        await _delays.ReleaseAsync(TimeSpan.FromSeconds(4));
        Assert.True(await _delays.WaitPendingAsync(TimeSpan.FromSeconds(4)));
        Assert.Equal(1, _findCalls);

        // 静かになったら1回だけ探す
        await _delays.ReleaseAsync(TimeSpan.FromSeconds(4));
        await _delays.ReleaseAsync(Timing.Settle);
        Assert.Single(await NextFoundAsync(1));
        Assert.Equal(2, _findCalls);
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
        var present = true;
        _newFiles = [];
        var watch = Create(_ => present);
        watch.Start([Folder]);
        await watch.BaselineTaken;
        var first = _hub.Single(Folder);

        present = false;
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

    /// <summary>
    /// 見張りを張るのは裏で行う。届かない共有では見張りを作るだけで長く止まるので、呼び手（画面のスレッド・設定の保存）を待たせない
    /// </summary>
    [Fact]
    public async Task 見張りを作るのが止まっても_始める呼び出しは待たない()
    {
        using var release = new ManualResetEventSlim(false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = new LiveFolderWatch(
            folder =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(10));
                return _hub.Create(folder);
            },
            (_, _) => Task.FromResult(new WatchResult { Folders = [Folder], NewFiles = [] }),
            _probe,
            _delays.Delay,
            _delays,
            _ => true,
            Timing);

        watch.Start([Folder]);

        // 戻った時点で、見張りを作る関数はまだ裏で止まっている
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(_hub.Of(Folder));
        release.Set();
        await watch.BaselineTaken.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(_hub.Of(Folder));
    }

    /// <summary>
    /// 見張っている間に監視フォルダを足すと張り直す。取り込み中で預けていた物と見張り始めの控えは引き継ぐ
    /// （捨てると、預けた物が新しい控えに入り「現れた物」でなくなって、自動の取り込みから黙って落ちる）。控えを取るのは足したフォルダの分だけ
    /// </summary>
    [Fact]
    public async Task 張り直しても_預けた物と控えを引き継ぐ()
    {
        const string Other = @"D:\作り物\監視2";
        var busy = false;
        var asked = new List<IReadOnlyList<string>>();
        IReadOnlyList<string> current = [@"D:\作り物\監視\前から.zip"];
        using var watch = new LiveFolderWatch(
            _hub.Create,
            (folders, _) =>
            {
                lock (asked)
                {
                    asked.Add([.. folders]);
                }

                // 頼まれたフォルダの中の物だけ返す（控えを足したフォルダの分だけ取るかを見るため）
                var inside = current.Where(path => folders.Any(folder => path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
                return Task.FromResult(new WatchResult { Folders = folders, NewFiles = inside });
            },
            _probe,
            _delays.Delay,
            _delays,
            _ => true,
            Timing);
        watch.Found = files =>
        {
            lock (_foundLock)
            {
                _found.Add(files);
            }

            return Task.CompletedTask;
        };
        watch.IsBusy = () => busy;
        watch.Start([Folder]);
        await watch.BaselineTaken;

        // 取り込み中に置かれた物を預ける
        busy = true;
        current = [@"D:\作り物\監視\前から.zip", @"D:\作り物\監視\取り込み中に置いた.zip"];
        _hub.Single(Folder).Fire(@"D:\作り物\監視\取り込み中に置いた.zip");
        await _delays.ReleaseAsync(Timing.Quiet);
        await Until(() => watch.IsDeferred, "預ける");

        // 監視フォルダを足して張り直す
        watch.Start([Folder, Other]);
        await watch.BaselineTaken;
        Assert.True(watch.IsDeferred);
        lock (asked)
        {
            Assert.Equal([Other], asked[^1]);
        }

        busy = false;
        watch.Kick();
        await _delays.ReleaseAsync(Timing.Quiet);
        await _delays.ReleaseAsync(Timing.Settle);

        var found = await NextFoundAsync(1);
        Assert.True(found.Single(file => file.Path.EndsWith("取り込み中に置いた.zip", StringComparison.Ordinal)).Arrived);
        Assert.False(found.Single(file => file.Path.EndsWith("前から.zip", StringComparison.Ordinal)).Arrived);
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
            using var watch = new LiveFolderWatch(hub.Create, folderWatch.FindNewAsync, new FileSettleProbe(), _delays.Delay, _delays, _ => true, Timing);
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

    /// <summary>
    /// 手で進める待ちと時計。待ちが来たかを見て、1つずつ終わらせる。終わらせると、時計をその待ちの期限まで進める
    /// （足し算にしない：待つ間に <see cref="Advance"/> で進めた分を二重に数えない）。
    /// 刻みは1秒あたり <see cref="TimeSpan.TicksPerSecond"/>（残りの待ちが端数にならず、長さで見分けられる）
    /// </summary>
    internal sealed class ManualDelays : TimeProvider
    {
        private readonly object _lock = new();
        private readonly List<(TimeSpan Span, long Due, TaskCompletionSource Done)> _pending = [];
        private long _now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_lock)
            {
                return _now;
            }
        }

        /// <summary>待ちを終わらせずに時計だけ進める（待つ間に、少し後で出来事が来たことにする）。</summary>
        public void Advance(TimeSpan span)
        {
            lock (_lock)
            {
                _now += span.Ticks;
            }
        }

        public Task Delay(TimeSpan span, CancellationToken token)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => done.TrySetCanceled(token));
            lock (_lock)
            {
                _pending.Add((span, _now + span.Ticks, done));
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
                _now = Math.Max(_now, entry.Due);
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
