using System.Collections.Concurrent;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 検出と取り込みは、画面のスレッドから呼ばれても**中身を画面のスレッドの外で回す**（2026-09-24）。
/// Core は続きを元の文脈へ戻すので、前は全商品の走査・全件の読み込み・フォルダの測り直しが画面のスレッドで走り、
/// 2000件の検出で約1.6秒、300本の取り込み直しで約2秒、画面が止まっていた。
/// 進み具合を知らせた所のスレッドで確かめる（画面の Progress は、どこから知らせても画面のスレッドへ運ぶ）。
/// </summary>
public sealed class OffUiThreadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-off-ui-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public OffUiThreadTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task DetectionDoesNotRunOnTheCallingUiThread()
    {
        for (var index = 0; index < 5; index++)
        {
            await _store.Items.SaveAsync(new ItemRecord
            {
                Id = (100 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "商品", Tags = ["タグ"] },
            });
        }

        using var ui = new UiThreadStandIn();
        var progress = new ThreadRecorder<AvatarDetectProgress>();

        await ui.RunAsync(() => new AvatarService(_store, new AppSettings()).DetectAsync(progress));

        Assert.NotEmpty(progress.Threads);
        Assert.DoesNotContain(ui.ThreadId, progress.Threads);
    }

    [Fact]
    public async Task ImportDoesNotRunOnTheCallingUiThread()
    {
        var folder = Path.Combine(_root, "取り込み元");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "絵.psd"), [1, 2, 3]);

        var client = new OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);

        using var ui = new UiThreadStandIn();
        var progress = new ThreadRecorder<ImportProgress>();

        var summary = await ui.RunAsync(() => pipeline.RunAsync([folder], progress));

        Assert.Equal(1, summary.UnresolvedFiles);
        Assert.Contains(progress.Phases, phase => phase == ImportPhase.Resolving);
        Assert.DoesNotContain(ui.ThreadId, progress.Threads);
        Assert.Equal(0, client.Calls);
    }

    /// <summary>知らせを受けたスレッドを書き留める（Progress と違い、知らせた所でそのまま受ける）。</summary>
    private sealed class ThreadRecorder<T> : IProgress<T>
    {
        private readonly ConcurrentQueue<int> _threads = new();
        private readonly ConcurrentQueue<ImportPhase> _phases = new();

        public IReadOnlyCollection<int> Threads => _threads;

        public IReadOnlyCollection<ImportPhase> Phases => _phases;

        public void Report(T value)
        {
            _threads.Enqueue(Environment.CurrentManagedThreadId);
            if (value is ImportProgress import)
            {
                _phases.Enqueue(import.Phase);
            }
        }
    }

    /// <summary>通信しない作り物。呼ばれたら数えて「一時的な失敗」を返す（取り込み直しの試験でも使う）。</summary>
    internal sealed class OfflineClient : IBoothClient
    {
        public int Calls;

        private Task<BoothFetchResult<T>> Fail<T>()
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(BoothFetchResult<T>.Temporary("試験"));
        }

        public Task<BoothFetchResult<string>> GetItemJsonAsync(string itemId, CancellationToken cancellationToken = default) => Fail<string>();

        public Task<BoothFetchResult<string>> GetItemHtmlAsync(string itemId, CancellationToken cancellationToken = default) => Fail<string>();

        public Task<BoothFetchResult<byte[]>> GetBinaryAsync(string url, CancellationToken cancellationToken = default) => Fail<byte[]>();

        public Task<BoothFetchResult<string>> SearchAsync(string query, CancellationToken cancellationToken = default) => Fail<string>();

        public Task<BoothFetchResult<string>> GetTextUntilAsync(
            string url, Func<string, bool> found, int maxBytes = 262144, CancellationToken cancellationToken = default) => Fail<string>();

        public int CurrentIntervalMs => 1500;

        public bool IsThrottled => false;

        public event Action<BoothActivity>? ActivityChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>画面のスレッドの代わり。1本のスレッドで、送られた続きを順に回す（StoreGateCommandTests と同じ形）。</summary>
    private sealed class UiThreadStandIn : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public UiThreadStandIn()
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
            };
            thread.Start();
            ThreadId = thread.ManagedThreadId;
        }

        public int ThreadId { get; }

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
