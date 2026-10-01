using System.Collections.Concurrent;
using Chmonos.Core.Commands;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存先を運ぶ命令と書き込みの門（<see cref="StoreWriteGate"/>）の組み合わせ。
/// 門はアプリに1つなので、<see cref="StoreWriteGateTests"/> と同じく他の試験と並べない。
/// </summary>
[Collection(nameof(StoreWriteGateCollection))]
public sealed class StoreGateCommandTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bam-gatecmd-{Guid.NewGuid():N}");

    private string Root => Path.Combine(_directory, "store");

    public StoreGateCommandTests()
    {
        new AppPaths(Root).EnsureCreated();
        File.WriteAllText(Path.Combine(Root, "settings.json"), "{}");
    }

    public void Dispose()
    {
        // 試験が途中で落ちても、閉じたままの門を後の試験へ持ち越さない
        StoreWriteGate.ReopenAfterRestartForTests();
        AppLog.Use(null);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// **画面のスレッドから保存しても、運ぶ命令と待ち合って固まらない**（2026-09-23 に見つけた固まり）。
    ///
    /// 取り込みは画面のスレッドの文脈で進み、その中の unitypackage の控えの保存が同期で門を待っていた。
    /// 一方で運ぶ命令の後始末（門を開ける）は画面のスレッドへ戻ってから走ったので、互いに待ち合っていた。
    /// 画面のスレッドの代わりに、1本のスレッドで回す文脈を使って同じ形を作る。
    /// </summary>
    [Fact]
    public async Task 運んでいる間に画面のスレッドから保存しても固まらない()
    {
        var handler = new CommandHandler(null!, null!);
        var progress = new BlockingProgress();
        var pathStore = new UnityPackagePathStore(new AppPaths(Root));
        var zip = Path.Combine(_directory, "backup.zip");
        using var ui = new SingleThreadContext();

        var done = ui.RunAsync(async () =>
        {
            var exporting = handler.ExecuteAsync(new UiCommand.ExportBackup(Root, zip, IncludeImages: false, progress));

            // 書き出しが門を閉じて中に入ったところで止まっている
            Assert.True(await Task.Run(() => progress.Entered.Wait(TimeSpan.FromSeconds(10))));
            Assert.True(StoreWriteGate.IsHeld);

            // 門の外で少し後に書き出しを進める。画面のスレッドはその間に保存を始める
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                progress.Release.Set();
            });

            var saving = pathStore.SaveAsync(
                "0123abcd",
                new Dictionary<string, IReadOnlyList<UnityPackageAsset>> { ["a.unitypackage"] = [new UnityPackageAsset("00000000000000000000000000000001", "Assets/A/a.prefab")] });

            await saving;
            return await exporting;
        });

        var finished = await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.True(finished == done, "画面のスレッドと、門を開ける側が待ち合って固まった");
        Assert.IsType<CommandResult.BackupExported>(await done);
        Assert.False(StoreWriteGate.IsHeld);
        Assert.True(new UnityPackagePathStore(new AppPaths(Root)).Has("0123abcd"));
    }

    /// <summary>
    /// **運び終えたら門を閉じたまま返す**（済むと必ず開き直す・ユーザ判断 2026-09-23）。
    /// 前は一度開けて返し、画面が閉じ直すまでの隙間に待っていた書き込みが古い保存先へ流れていた。
    /// ログも止める（門を通らずに書き足すので、引越しで消した元の場所に logs/ を作り直していた）。
    /// </summary>
    [Fact]
    public async Task 運び終えたら門を閉じたままにしてログも止める()
    {
        var log = Path.Combine(Root, "logs", "app.log");
        AppLog.Use(new LogFile(log));
        var destination = Path.Combine(_directory, "moved");

        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.MoveStore(Root, destination, Replace: false));

        Assert.True(Assert.IsType<CommandResult.StoreMoved>(result).Result.Succeeded);
        Assert.True(StoreWriteGate.IsHeld);
        Assert.True(StoreWriteGate.IsClosedForRestart);

        var late = StoreWriteGate.EnterAsync();
        await Task.Delay(50);
        Assert.False(late.IsCompleted);

        AppLog.Warn("試験", "開き直しの間に書いたログ");
        Assert.False(File.Exists(log));

        StoreWriteGate.ReopenAfterRestartForTests();
        (await late.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    /// <summary>運べなかったときは開き直さないので、門を開けて返す（待たせた書き込みを止めたままにしない）。</summary>
    [Fact]
    public async Task 運べなかったら門を開けて返す()
    {
        // 今の保存先の内側へは運ばせない（断られる）
        var result = await new CommandHandler(null!, null!)
            .ExecuteAsync(new UiCommand.MoveStore(Root, Path.Combine(Root, "inside"), Replace: false));

        Assert.False(Assert.IsType<CommandResult.StoreMoved>(result).Result.Succeeded);
        Assert.False(StoreWriteGate.IsHeld);
        Assert.False(StoreWriteGate.IsClosedForRestart);
    }

    /// <summary>
    /// 画面を開くついでの小さな書き込み（2026-09-23 に UiCommand へ通した3つ）は、運んでいる間は待たされ、
    /// 運び終えたら通る。直に書いていた頃は、運んでいる最中にも書けた。
    /// </summary>
    [Fact]
    public async Task 開くついでの書き込みは運んでいる間待たされる()
    {
        var store = new DataStore(new AppPaths(Root));
        await store.ImportState.SaveAsync(new ImportState { Done = 3, Total = 10, Targets = [@"D:\BOOTH"] });
        var handler = new CommandHandler(
            null!,
            null!,
            notifications: new NotificationService(store),
            volumes: new VolumeTable(store, new NoVolumes()),
            importState: store.ImportState);

        var hold = await StoreWriteGate.HoldAsync();
        Task<CommandResult> discarding, detecting, observing;
        try
        {
            discarding = handler.ExecuteAsync(new UiCommand.DiscardInterruptedImport());
            detecting = handler.ExecuteAsync(new UiCommand.DetectOrphanReferences());
            observing = handler.ExecuteAsync(new UiCommand.ObserveVolumes([@"E:\BOOTH\a.zip"]));

            await Task.Delay(100);
            Assert.False(discarding.IsCompleted);
            Assert.False(detecting.IsCompleted);
            Assert.False(observing.IsCompleted);
            Assert.True(store.ImportState.Load().HasProgress);
        }
        finally
        {
            hold.Dispose();
        }

        var timeout = TimeSpan.FromSeconds(10);
        Assert.IsType<CommandResult.Done>(await discarding.WaitAsync(timeout));
        Assert.IsType<CommandResult.Counted>(await detecting.WaitAsync(timeout));
        Assert.IsType<CommandResult.VolumesObserved>(await observing.WaitAsync(timeout));
        Assert.False(store.ImportState.Load().HasProgress);
    }

    /// <summary>
    /// 画像の「404だった」印も、運んでいる間は置くのを待つ。
    /// 通さずにいた頃は、運んでいる最中に置いた印が、元を消すときに一緒に消えるか、運ばれずに元の場所に残った。
    /// </summary>
    [Fact]
    public async Task 画像の印も運んでいる間は置かない()
    {
        var settings = new AppSettings { FetchIntervalMs = 0 };
        var client = new Booth.BoothClient(new HttpClient(new NotFoundHandler()), settings, (_, _) => Task.CompletedTask);
        var images = new Images.ImagePipeline(client, new AppPaths(Root), settings);
        var directory = Path.Combine(Root, "images", "_marker");
        const string url = "https://booth.pximg.net/a.png";
        var marker = Path.Combine(directory, Images.ImagePipeline.MissingMarkerFor(url));

        var hold = await StoreWriteGate.HoldAsync();
        Task<bool> syncing;
        try
        {
            syncing = images.SyncOneToAsync(directory, url);
            await Task.Delay(100);
            Assert.False(syncing.IsCompleted);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            hold.Dispose();
        }

        Assert.False(await syncing.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(File.Exists(marker));
    }

    /// <summary>何を聞かれても 404 を返す（通信はしない）。</summary>
    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }

    private sealed class NoVolumes : IVolumeReader
    {
        public IReadOnlyList<MountedVolume> Mounted() => [];
    }

    /// <summary>最初の知らせで止まり、放されるまで書き出しを進めない。</summary>
    private sealed class BlockingProgress : IProgress<BackupProgress>
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public void Report(BackupProgress value)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>
    /// 画面のスレッドの代わり。**1本のスレッドで、送られた続きを順に回す。**
    /// そのスレッドが止まると、そこへ戻る続き（await の後）は全部待たされる——WPF の画面のスレッドと同じ形。
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public SingleThreadContext()
        {
            var thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            {
                IsBackground = true,
                Name = "画面のスレッドの代わり",
            };
            thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.Add((d, state));
            }
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public Task<T> RunAsync<T>(Func<Task<T>> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ => _ = RunCoreAsync(), null);
            return done.Task;

            async Task RunCoreAsync()
            {
                try
                {
                    done.SetResult(await work());
                }
                catch (Exception exception)
                {
                    done.SetException(exception);
                }
            }
        }

        public void Dispose() => _queue.CompleteAdding();
    }
}
