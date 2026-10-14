using System.Text.Json;
using System.Threading.Channels;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 変化の記録（<c>/v1/changes</c> と SSE）。通番は API の起動ごとに数え直し、起動ごとに変わる名前（<see cref="Epoch"/>）を添える。
/// 直近の分だけを持つ（外の道具が長く離れていたら、全部を取り直してもらう）。
/// </summary>
internal sealed class ChangeFeed
{
    /// <summary>持っておく変化の数。外の道具が数分離れても追いつける数で、1件は数百バイト。</summary>
    public const int Capacity = 1000;

    private readonly object _gate = new();
    private readonly LinkedList<ChangeEventRef> _recent = new();
    private readonly List<Channel<ChangeEventRef>> _listeners = [];
    private long _sequence;

    public string Epoch { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    public long Sequence => Interlocked.Read(ref _sequence);

    public ChangeEventRef Publish(string kind, IReadOnlyList<string> parts, IReadOnlyList<string> itemIds, string? busy, DateTimeOffset at)
    {
        lock (_gate)
        {
            var change = new ChangeEventRef(++_sequence, kind, parts, itemIds, busy, at);
            _recent.AddLast(change);
            if (_recent.Count > Capacity)
            {
                _recent.RemoveFirst();
            }

            foreach (var listener in _listeners)
            {
                listener.Writer.TryWrite(change);
            }

            return change;
        }
    }

    /// <summary>
    /// <paramref name="since"/> の後の変化と、その時点の通番。持っていない分があれば変化は null（全部を取り直してもらう）。
    /// **一覧と通番は同じ錠の中で取る**（点検30）：別々に読むと、間に起きた変化が一覧に無いのに通番だけ進み、外の道具がそれを取りこぼす。
    /// </summary>
    public (IReadOnlyList<ChangeEventRef>? Changes, long Sequence) Since(long since)
    {
        lock (_gate)
        {
            if (since > _sequence || since < 0)
            {
                return (null, _sequence);
            }

            var oldest = _recent.First?.Value.Seq ?? _sequence + 1;
            return (since + 1 < oldest ? null : _recent.Where(change => change.Seq > since).ToList(), _sequence);
        }
    }

    /// <summary>
    /// SSE の聞き手を足し、足した時点の通番を返す。それより後の変化が届く（それより前は <see cref="Since"/> で取る）。
    /// **足すのと通番を読むのは同じ錠の中**（点検30）：足した後で通番を読むと、間に届いた変化を送り済みと見なして落とす。
    /// 聞き手が遅れて待ち行列があふれると古い物から落ちるので、受け手は通番の飛びで気付く（<see cref="Capacity"/>）。
    /// </summary>
    public (Channel<ChangeEventRef> Channel, long Sequence) Listen(int capacity = Capacity)
    {
        var channel = Channel.CreateBounded<ChangeEventRef>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_gate)
        {
            _listeners.Add(channel);
            return (channel, _sequence);
        }
    }

    public void Stop(Channel<ChangeEventRef> channel)
    {
        lock (_gate)
        {
            _listeners.Remove(channel);
        }

        channel.Writer.TryComplete();
    }

    /// <summary>
    /// 止まるときに、聞き手の待ち行列を全部閉じる。SSE は届いている知らせ（「止まります」）を送り切ってから自分で終わる。
    /// 閉じないと、SSE の要求が切れるまで待ち受けが止まらず、API がアプリと同じプロセスになってからはアプリの終了が待たされた（2026-10-13）
    /// </summary>
    public void CloseAll()
    {
        List<Channel<ChangeEventRef>> listeners;
        lock (_gate)
        {
            listeners = [.. _listeners];
            _listeners.Clear();
        }

        foreach (var listener in listeners)
        {
            listener.Writer.TryComplete();
        }
    }
}

/// <summary>
/// 写しを保存先に合わせる（docs/spec/local-api.md「同期」）。
/// 変わったことはファイルの変化を見張って拾う（アプリの書き込み・削除・画像の保存のどれも同じ道で拾える）。
/// 見張りが知らせを取りこぼしたら全部を見直し、念のため時々、商品の一覧の印も見比べる。
/// アプリがまとめて書いている間（取り込みなど。アプリが <see cref="ApiServer.SetBusy"/> で知らせる）は、古い答えを返し続け、終わってから読み直す。
/// </summary>
internal sealed class StoreSync : IDisposable
{
    /// <summary>知らせを受けてから読み直すまで待つ時間。1回の保存で複数のファイルが続けて変わるので、まとめて1回にする。</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// 時々の見比べの間隔。見張りは取りこぼすことがある（知らせがあふれる・ネットワークのドライブ）。
    /// 見比べるのは商品の一覧の印とマスタの印だけ（2,000 件でも一覧を見るだけ）なので、短めでよい
    /// </summary>
    private static readonly TimeSpan Verify = TimeSpan.FromMinutes(2);

    /// <summary>まとめて書く操作の終わりの知らせが来ないまま（アプリが落ちた）でも、古い答えを返し続けない上限。</summary>
    private static readonly TimeSpan BusyLimit = TimeSpan.FromMinutes(30);

    private readonly ApiState _state;
    private readonly ChangeFeed _feed;
    private readonly FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private StoreChangeSet _pending = new();
    private (string Kind, DateTimeOffset Since)? _busy;
    private Task? _loop;

    public StoreSync(ApiState state, ChangeFeed feed, bool watch = true)
    {
        _state = state;
        _feed = feed;
        if (watch)
        {
            _watcher = new FileSystemWatcher(state.Paths.Root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += (_, e) => Note(e.FullPath);
            _watcher.Created += (_, e) => Note(e.FullPath);
            _watcher.Deleted += (_, e) => Note(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Note(e.OldFullPath);
                Note(e.FullPath);
            };

            // 知らせがあふれた。何が変わったか分からないので全部を見直す
            _watcher.Error += (_, _) => Enqueue(StoreChangeSet.Everything());
        }
    }

    public string? Busy => _busy?.Kind;

    public void Start()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = true;
        }

        _loop = Task.Run(() => LoopAsync(_stop.Token));
    }

    /// <summary>ファイルの場所から変わった部分を見分けて貯める。写しに関係の無いファイル（ログ・書きかけ）は捨てる。</summary>
    public void Note(string fullPath)
    {
        if (StoreChanges.Classify(_state.Paths, fullPath) is { } change)
        {
            var changes = new StoreChangeSet();
            changes.Add(change.Part, change.ItemId);
            Enqueue(changes);
        }
    }

    public void Enqueue(StoreChangeSet changes)
    {
        lock (_gate)
        {
            _pending.Add(changes);
        }

        _signal.Release();
    }

    public void SetBusy(string? kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (kind is null && _busy is null)
            {
                return;
            }

            _busy = kind is null ? null : (kind, now);
        }

        _feed.Publish("busy", [], [], kind, now);

        // 終わったら、待たせていた分を読み直す
        _signal.Release();
    }

    /// <summary>
    /// 読み直しを1つずつにする。同期の流れのほか、書き込みの API が書いた直後にも読み直す（書いた後の答えを新しい写しで返すため。2026-10-13）。
    /// 2か所から同時に読み直すと、古い写しから作った新しい写しが、もう一方の結果を上書きし得る
    /// </summary>
    private readonly SemaphoreSlim _applying = new(1, 1);

    /// <summary>
    /// 貯まった変化を今すぐ読み直す（同期の流れ・書き込みの直後・試験）。まとめて書いている途中なら写しは差し替えない。
    /// 差し替えたか（読む物が無かったときも真）を返す。書き込みの API は、偽なら「受け付けた」（202）で答える
    /// （前は錠を待つ間に取り込みが始まると、差し替えていないのに書けたと答えた。点検37）
    /// </summary>
    public async Task<bool> ApplyPendingAsync(CancellationToken cancellationToken = default)
    {
        await _applying.WaitAsync(cancellationToken);
        try
        {
            return await ApplyPendingCoreAsync(cancellationToken);
        }
        finally
        {
            _applying.Release();
        }
    }

    private async Task<bool> ApplyPendingCoreAsync(CancellationToken cancellationToken)
    {
        StoreChangeSet changes;
        bool settingsWhileBusy;
        lock (_gate)
        {
            settingsWhileBusy = _busy is { } busy && _state.Clock() - busy.Since < BusyLimit;
            if (!settingsWhileBusy)
            {
                _busy = null;
                changes = _pending;
                _pending = new StoreChangeSet();
            }
            else
            {
                changes = new StoreChangeSet();
            }
        }

        // まとめて書いている途中は商品の写しを差し替えない。ただし守りの設定（書き込みの許し・許可するオリジン）は、その場で読み直す
        // （前は取り込みが終わるまで、切った許しで書き込みを受け付けた。点検37）。変化は貯めたまま残すので、終わったら全部を読み直す
        if (settingsWhileBusy)
        {
            _state.ApplySettings();
            return false;
        }

        if (changes.IsEmpty)
        {
            return true;
        }

        try
        {
            await _state.ApplyAsync(changes, _feed, cancellationToken);
        }
        catch
        {
            // 読み直しに失敗した（ファイルがほかのアプリに開かれていた・書き換えの途中）。取り出した変化を戻し、次の知らせか見比べで読み直す。
            // 戻さないと、その部分は次にファイルが変わるまで古いまま残る（点検30）
            lock (_gate)
            {
                changes.Add(_pending);
                _pending = changes;
            }

            throw;
        }

        return true;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        var lastVerify = _state.Clock();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(Verify, cancellationToken);
                await Task.Delay(Settle, cancellationToken);

                // 待っている間に重なった知らせはまとめて1回で読み直す
                while (_signal.CurrentCount > 0)
                {
                    await _signal.WaitAsync(cancellationToken);
                }

                if (_state.Clock() - lastVerify >= Verify)
                {
                    lastVerify = _state.Clock();
                    var everything = new StoreChangeSet();
                    foreach (var part in Enum.GetValues<StorePart>().Where(part => part != StorePart.Images))
                    {
                        everything.Add(part);
                    }

                    Enqueue(everything);
                    while (_signal.CurrentCount > 0)
                    {
                        await _signal.WaitAsync(cancellationToken);
                    }
                }

                await ApplyPendingAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // 読み直しの途中でファイルが動いた。次の知らせか見比べで読み直す
                Console.Error.WriteLine($"読み直しを後に回しました: {exception.GetType().Name}");
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _watcher?.Dispose();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        _stop.Dispose();
    }
}
