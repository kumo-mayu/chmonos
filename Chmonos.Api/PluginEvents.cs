using Chmonos.Core.Models;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// ソフトへの出来事の列（plan 44 の①・docs/spec/plugins.md）。ソフトごとに列を持ち、ソフトが <c>GET /v1/plugin/events</c> で取りに来る（Chmonos から外へつながない）。
/// アプリ（<c>ApiRunner</c>）が1つ持ち、API を止めて始め直しても（ポートの変更・トークンの作り直し）購読と通番を保つ。
/// 通番と起動の名前（<see cref="Epoch"/>）はアプリの起動ごとに新しくなる（<c>/v1/changes</c> と同じ考え）。
/// 誰も購読していなければ <see cref="Watches"/> が false で、画面は出来事を作らない（ふだんの重さを増やさない）
/// </summary>
public sealed class PluginEventHub(Func<DateTimeOffset>? clock = null)
{
    /// <summary>
    /// ソフトごとの列の上限。1件は数百バイト（選択は ID が最大 <see cref="MaxSelectionIds"/> 個で約20KB）なので、選択で埋まっても1ソフト約5MB に収まる。
    /// ソフトは返ってきたらすぐ次を聞くので、ふつうは数件しか溜まらない。256 は、ソフトが数分止まっている間の選択の変化も取りこぼさない程度
    /// </summary>
    internal const int Capacity = 256;

    /// <summary>
    /// 選択の出来事に載せる ID の上限。2万件を全部選ぶと ID だけで約40万字になり、列に256件溜まると百MB を超える。
    /// ソフトが選んだ物に何かをする使い方なら数百件で足りる見込みで、超えたら数（<c>count</c>）と切った印（<c>truncated</c>）で伝える
    /// </summary>
    public const int MaxSelectionIds = 1000;

    /// <summary>待つ上限（秒）。許しの結果を待つのと同じ：道具の HTTP の時間切れ（HttpClient の既定は100秒）より十分短く。</summary>
    internal const int MaxWaitSeconds = 30;

    public const string ItemOpened = "itemOpened";
    public const string SelectionChanged = "selectionChanged";
    public const string ImportFinished = "importFinished";

    /// <summary>購読できる種類（並べる順）。許しの名前は <c>watch.</c> を前に付けた物（<see cref="ApiScopes.Watches"/>）。</summary>
    public static IReadOnlyList<string> Kinds { get; } = [ItemOpened, SelectionChanged, ImportFinished];

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly object _lock = new();
    private readonly Dictionary<string, Subscriber> _subscribers = new(StringComparer.Ordinal);

    /// <summary>誰かが購読している種類（入れ物ごと差し替える。画面のスレッドから錠を取らずに見るため）。</summary>
    private volatile HashSet<string> _watched = new(StringComparer.Ordinal);

    /// <summary>今つないでいる API の許しの控え（止まっている間は null で、出来事を作らせない）。</summary>
    private ApiClientBook? _book;

    /// <summary>起動の名前（通番を数え直したことを見分ける）。</summary>
    public string Epoch { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    private sealed class Subscriber
    {
        public HashSet<string> Kinds { get; } = new(StringComparer.Ordinal);

        public Queue<PluginEvent> Events { get; } = new();

        public long LastSeq { get; set; }

        /// <summary>積んだら終わる（待っている問い合わせを起こす）。画面のスレッドで積むので、続きは別のスレッドで走らせる。</summary>
        public TaskCompletionSource Signal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset? LastPolledAt { get; set; }

        public int Waiting { get; set; }

        /// <summary>取り消された（待っている問い合わせは、出来事が無くてもすぐ返る）。</summary>
        public bool Removed { get; set; }
    }

    /// <summary>今つないでいる API があるか（試験）。</summary>
    internal bool IsAttached => _book is not null;

    /// <summary>この種類を誰かが購読しているか（API が動いていなければ false）。画面はこれを見てから出来事を作る。</summary>
    public bool Watches(string kind) => _book is not null && _watched.Contains(kind);

    /// <summary>商品ページを開いた。</summary>
    public void PublishItemOpened(string itemId)
        => Publish(ItemOpened, (seq, at) => new PluginEvent(seq, Epoch, ItemOpened, at, new PluginItemOpened(itemId), null, null));

    /// <summary>複数選択が変わった（まとめるのは画面の側）。ID は先頭から <see cref="MaxSelectionIds"/> まで載せる。</summary>
    public void PublishSelection(string screen, IReadOnlyList<string> itemIds)
    {
        if (!Watches(SelectionChanged))
        {
            return;
        }

        var truncated = itemIds.Count > MaxSelectionIds;
        var selection = new PluginSelection(screen, truncated ? itemIds.Take(MaxSelectionIds).ToList() : itemIds.ToList(), itemIds.Count, truncated);
        Publish(SelectionChanged, (seq, at) => new PluginEvent(seq, Epoch, SelectionChanged, at, null, selection, null));
    }

    /// <summary>取り込みが終わった。</summary>
    public void PublishImport(PluginImport import)
        => Publish(ImportFinished, (seq, at) => new PluginEvent(seq, Epoch, ImportFinished, at, null, null, import));

    private void Publish(string kind, Func<long, DateTimeOffset, PluginEvent> create)
    {
        if (!Watches(kind))
        {
            return;
        }

        var at = _clock();
        lock (_lock)
        {
            foreach (var subscriber in _subscribers.Values.Where(subscriber => subscriber.Kinds.Contains(kind)))
            {
                subscriber.LastSeq++;
                subscriber.Events.Enqueue(create(subscriber.LastSeq, at));

                // あふれたら古い方から捨てる。捨てた数は、取りに来たときに通番の飛びから数える（dropped）
                while (subscriber.Events.Count > Capacity)
                {
                    subscriber.Events.Dequeue();
                }

                Wake(subscriber);
            }
        }
    }

    /// <summary>待っている問い合わせを起こす（錠の中で呼ぶ）。</summary>
    private static void Wake(Subscriber subscriber)
    {
        var signal = subscriber.Signal;
        subscriber.Signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.TrySetResult();
    }

    /// <summary>購読を丸ごと置き換え、今の最後の通番を返す。許しの確かめは呼ぶ側（入口）で済ませる。</summary>
    internal long Subscribe(string clientId, IReadOnlyCollection<string> kinds)
    {
        lock (_lock)
        {
            var subscriber = SubscriberOf(clientId);
            subscriber.Kinds.Clear();
            subscriber.Kinds.UnionWith(kinds);
            RecomputeWatched();
            return subscriber.LastSeq;
        }
    }

    /// <summary>今購読している種類（並べる順で）。</summary>
    internal IReadOnlyList<string> KindsOf(string clientId)
    {
        lock (_lock)
        {
            return _subscribers.TryGetValue(clientId, out var subscriber) ? Kinds.Where(subscriber.Kinds.Contains).ToList() : [];
        }
    }

    private Subscriber SubscriberOf(string clientId)
    {
        if (!_subscribers.TryGetValue(clientId, out var subscriber))
        {
            subscriber = new Subscriber();
            _subscribers[clientId] = subscriber;
        }

        return subscriber;
    }

    private void RecomputeWatched()
        => _watched = _subscribers.Values.SelectMany(subscriber => subscriber.Kinds).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="after"/> より後の出来事を返す。無ければ <paramref name="wait"/> まで待ち、空で返す。
    /// 起動の名前が違う・<paramref name="after"/> がまだ無い通番なら、頭から返して <c>reset</c> を立てる（起動し直した後の古い after を見分ける）。
    /// 止める（<paramref name="cancellationToken"/>）と、その時点の答えを返す
    /// </summary>
    internal async Task<PluginEventsAnswer> WaitAsync(string clientId, long after, string? epoch, TimeSpan wait, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + wait;
        Subscriber subscriber;
        lock (_lock)
        {
            subscriber = SubscriberOf(clientId);
            subscriber.LastPolledAt = _clock();
            subscriber.Waiting++;
        }

        try
        {
            while (true)
            {
                Task signal;
                TimeSpan remaining;
                lock (_lock)
                {
                    var answer = AnswerOf(subscriber, after, epoch);
                    remaining = until - DateTime.UtcNow;
                    if (answer.Events.Count > 0 || answer.Reset || answer.Dropped > 0 || subscriber.Removed || remaining <= TimeSpan.Zero || cancellationToken.IsCancellationRequested)
                    {
                        return answer;
                    }

                    signal = subscriber.Signal.Task;
                }

                try
                {
                    // 錠の中で出した正の残りを渡す（錠の外で計り直すと、負の小さな値が「いつまでも待つ」に読まれ得る）
                    await signal.WaitAsync(remaining, cancellationToken);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                subscriber.Waiting--;
                subscriber.LastPolledAt = _clock();
            }
        }
    }

    private PluginEventsAnswer AnswerOf(Subscriber subscriber, long after, string? epoch)
    {
        var reset = (epoch is not null && epoch != Epoch) || after > subscriber.LastSeq || after < 0;
        var from = reset ? 0 : after;
        var events = subscriber.Events.Where(item => item.Seq > from).ToList();

        // 残っているいちばん古い通番より前を求められたら、その間は捨てた物（あふれ）
        var oldest = subscriber.Events.Count > 0 ? subscriber.Events.Peek().Seq : subscriber.LastSeq + 1;
        var dropped = Math.Max(0, oldest - from - 1);
        return new PluginEventsAnswer(Epoch, events.Count > 0 ? events[^1].Seq : from, reset, dropped, events);
    }

    /// <summary>最後に取りに来た時刻（メモリの正確な値。②で「応答していない」を決める）と、今待っているか。</summary>
    internal (DateTimeOffset? LastPolledAt, bool Waiting) PollStateOf(string clientId)
    {
        lock (_lock)
        {
            return _subscribers.TryGetValue(clientId, out var subscriber) ? (subscriber.LastPolledAt, subscriber.Waiting > 0) : (null, false);
        }
    }

    /// <summary>API が始まった：許しの控えを見て、取り消し・許しの変更を購読へ映す。</summary>
    internal void Attach(ApiClientBook book)
    {
        lock (_lock)
        {
            if (_book is { } before)
            {
                before.Changed -= OnClientsChanged;
            }

            _book = book;
            book.Changed += OnClientsChanged;
        }

        OnClientsChanged();
    }

    /// <summary>API が止まる：出来事を作らせない（購読と列は残し、始め直したら続ける）。</summary>
    internal void Detach(ApiClientBook book)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_book, book))
            {
                book.Changed -= OnClientsChanged;
                _book = null;
            }
        }
    }

    /// <summary>
    /// 許しの控えを読み直した：取り消した・求め直して ID が変わったソフトの列を捨て、許しを外した種類の購読を外す
    /// （外した後に流れ続けないように。人が設定でチェックを外したら、その場で止まる）
    /// </summary>
    private void OnClientsChanged()
    {
        lock (_lock)
        {
            if (_book is not { } book)
            {
                return;
            }

            var clients = book.All.ToDictionary(client => client.Id, StringComparer.Ordinal);
            foreach (var (clientId, subscriber) in _subscribers.ToList())
            {
                if (!clients.TryGetValue(clientId, out var client))
                {
                    _subscribers.Remove(clientId);
                    subscriber.Removed = true;

                    // 待っている問い合わせは返す（次の問い合わせはトークンが通らない）
                    Wake(subscriber);
                    continue;
                }

                subscriber.Kinds.RemoveWhere(kind => !client.Scopes.Contains(ApiScopes.WatchOf(kind)));
            }

            RecomputeWatched();
        }
    }
}
