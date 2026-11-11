using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>許しを求めてきたソフト（窓に出す物）。</summary>
/// <param name="Name">ソフトが名乗った名前（本人の確かめではない）。</param>
/// <param name="ExePath">接続を開いた exe（TCP の表から引いた物。引けなければ null）。</param>
/// <param name="Origin">ブラウザのページから求めたときのオリジン（許可したオリジンの決まりを通った物だけ）。</param>
/// <param name="Scopes">欲しい書き込みの種類（<see cref="ApiScopes"/>。知っている物だけ）。</param>
/// <param name="Replaces">前に許した同じソフト（名前・exe・オリジンが同じ）がある。許すと前のトークンは使えなくなる。</param>
/// <param name="RefusalsLeft">あと何回拒否したら止めるか（上限が0で数えないときは null）。窓が「あと1回」を言うため。</param>
public sealed record ApiClientRequestInfo(string Name, string? ExePath, string? Origin, IReadOnlyList<string> Scopes, bool Replaces, int? RefusalsLeft = null);

/// <summary>
/// 人の答え。許したときの <see cref="Scopes"/> は、人が選んだ書き込みの種類（求められた物のうち、窓で外さなかった物）。
/// 許さなかった答えは2つに分ける：人が［拒否］をマウスでクリックした（<see cref="Refused"/>。止めるまでの回数に数える）と、
/// 答えずに閉じた・API が止まって閉じた・聞けなかった（<see cref="Dismissed"/>。人が決めていないので数えない）。ソフトへの答えはどちらも denied
/// </summary>
public sealed record ApiClientDecision(bool Approved, IReadOnlyList<string> Scopes, bool Refused = false)
{
    /// <summary>答えなかった（窓を閉じた・API が止まった・聞けなかった）。拒否に数えない。</summary>
    public static ApiClientDecision Dismissed { get; } = new(false, []);

    /// <summary>人が［拒否］をマウスでクリックした。拒否に数える（キーで押したときは数えない。窓の側が見分ける）。</summary>
    public static ApiClientDecision Refusal { get; } = new(false, [], Refused: true);
}

/// <summary>
/// 許しを人に聞く口（アプリが渡す。API のライブラリはアプリを参照しない）。
/// </summary>
public interface IApiClientApprovals
{
    /// <summary>
    /// 今、窓を出して聞けるか。画面を出さない起動で窓が無いときは false（すぐ断る。待たせない。まとめ役の判断 2026-11-11）。
    /// 起動の引数ではなく今の窓の有無で答えるので、通知領域から窓を開いた後は聞ける
    /// </summary>
    bool CanAsk { get; }

    /// <summary>窓を出して聞く。取り消されたら（API が止まる）窓を閉じて拒否で返す。</summary>
    Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken);
}

/// <summary>今の要求が誰か（<see cref="ApiHost"/> の守りが決めて、要求に添える）。</summary>
/// <param name="Client">ソフトごとのトークンなら、そのソフト。api.json のトークン（古い1本）なら null。</param>
internal sealed record ApiCallerInfo(ApiClientRecord? Client)
{
    public const string ItemKey = "chmonos.caller";

    public static ApiCallerInfo Legacy { get; } = new((ApiClientRecord?)null);

    public bool IsLegacy => Client is null;

    /// <summary>ログに添える名前。</summary>
    public string Label => Client is { } client ? $"ソフト「{client.Name}」" : "api.json のトークン";

    public static ApiCallerInfo Of(HttpContext context)
        => context.Items.TryGetValue(ItemKey, out var value) && value is ApiCallerInfo caller ? caller : Legacy;
}

/// <summary>
/// 許したソフトの控え（<c>api-clients.json</c>）の写しと、トークンから引く索引。
/// 写しは入れ物ごと差し替えるので、照らす途中で揺れない。ファイルの変化（見張り）・自分の書き込み・設定画面の変更で読み直す
/// </summary>
internal sealed class ApiClientBook(Func<DataStore> store, Func<DateTimeOffset> clock)
{
    /// <summary>
    /// 最後に使った日時を書く間隔。要求ごとに書くと、そのたびに保存先へ書き、見張りが拾って読み直す（SSE をつないだ道具は数秒ごとに問い合わせる）。
    /// 設定の一覧で「いつ使ったか」を見る目安には10分の粗さで足りる
    /// </summary>
    internal static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(10);

    private sealed record Index(
        IReadOnlyDictionary<string, ApiClientRecord> ByHash, IReadOnlyList<ApiClientRecord> All, IReadOnlyList<ApiClientRefusal> Refusals, int RefusalLimit);

    private volatile Index _index = new(new Dictionary<string, ApiClientRecord>(StringComparer.Ordinal), [], [], ApiClientList.DefaultRefusalLimit);

    /// <summary>ソフトごとに、最後に使った日時を最後に書いた時刻（書く間隔を空けるため）。</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _touched = new(StringComparer.Ordinal);

    /// <summary>一覧が変わったかもしれない（読み直した）。設定の画面が一覧を出し直す。裏のスレッドから来る。</summary>
    public event Action? Changed;

    public IReadOnlyList<ApiClientRecord> All => _index.All;

    /// <summary>書き込みを1つでも許したソフトがあるか（ブラウザの事前の確かめで、書く方法を許すか）。</summary>
    public bool AnyWrites => _index.All.Any(client => client.Scopes.Count > 0);

    /// <summary>読み直す。読めなければ（手で壊した JSON）誰のトークンも通さない（空として扱い、ログに残す）。</summary>
    public void Reload()
    {
        ApiClientList list;
        try
        {
            list = store().ApiClients.Load();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API の、許したソフトの一覧を読む", exception);
            list = new ApiClientList();
        }

        var clients = (list.Clients ?? [])
            .Where(client => client is not null && !string.IsNullOrEmpty(client.TokenHash) && !string.IsNullOrEmpty(client.Id))
            // 知らない種類（新しい版が足した物）は、この版では許していない物として扱う
            .Select(client => client with { Scopes = (client.Scopes ?? []).Where(ApiScopes.IsKnown).Distinct(StringComparer.Ordinal).ToList() })
            .ToList();
        var byHash = new Dictionary<string, ApiClientRecord>(StringComparer.Ordinal);
        foreach (var client in clients)
        {
            byHash.TryAdd(client.TokenHash.ToLowerInvariant(), client);
        }

        // 拒否の記録より前の控えには欄が無いので、null は空として受ける
        var refusals = (list.Refusals ?? []).Where(refusal => refusal is not null && !string.IsNullOrEmpty(refusal.Name)).ToList();
        _index = new Index(byHash, clients, refusals, Math.Max(0, list.RefusalLimit));
        Changed?.Invoke();
    }

    /// <summary>
    /// <c>Authorization</c> の値からソフトを引く。トークンの SHA-256 で索引を引く（ハッシュは送られた値から予め作れないので、
    /// 引く時間から控えのトークンを推し量れない）
    /// </summary>
    public ApiClientRecord? Find(string authorization)
    {
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal) || authorization.Length == prefix.Length)
        {
            return null;
        }

        return _index.ByHash.GetValueOrDefault(HashOf(authorization[prefix.Length..]));
    }

    public static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>前に許した同じソフト（名前・exe・オリジンが同じ）があるか。</summary>
    public bool HasSame(string name, string? exePath, string? origin) => _index.All.Any(client => client.IsSame(name, exePath, origin));

    /// <summary>
    /// 拒否が続いて止めているソフトか（窓を出さずに断る）。上限が0のときは止めない
    /// （0 にすると設定の画面が止めの記録も消すが、手で書いた控えでも、0 は「今までと同じに毎回聞く」にそろえる）
    /// </summary>
    public bool IsBlocked(string name, string? exePath, string? origin)
    {
        var index = _index;
        return index.RefusalLimit > 0 && index.Refusals.Any(refusal => refusal.BlockedAt is not null && refusal.IsSame(name, exePath, origin));
    }

    /// <summary>あと何回拒否したら止めるか（上限が0なら null）。</summary>
    public int? RefusalsLeft(string name, string? exePath, string? origin)
    {
        var index = _index;
        if (index.RefusalLimit <= 0)
        {
            return null;
        }

        var count = index.Refusals.FirstOrDefault(refusal => refusal.IsSame(name, exePath, origin))?.Count ?? 0;

        // 上限を下げた後は、数がもう上限を超えていることがある。次の拒否で止まるので「あと1回」にそろえる
        return Math.Max(1, index.RefusalLimit - count);
    }

    /// <summary>
    /// 人が［拒否］を押した：回数を1つ足し、上限に届いたら止める。上限が0なら数えない（覚えない）。
    /// 控えの今の値に当てる（設定の画面が同時に止めを外しても、外した後の値に足す）
    /// </summary>
    public async Task RefuseAsync(string name, string? exePath, string? origin, DateTimeOffset now)
    {
        await store().ApiClients.UpdateAsync(list =>
        {
            var limit = list.RefusalLimit;
            if (limit <= 0)
            {
                return list;
            }

            var refusals = (list.Refusals ?? []).Where(refusal => refusal is not null).ToList();
            var before = refusals.FirstOrDefault(refusal => refusal.IsSame(name, exePath, origin));
            var count = (before?.Count ?? 0) + 1;
            var after = new ApiClientRefusal
            {
                Name = name,
                ExePath = exePath,
                Origin = origin,
                Count = count,
                LastRefusedAt = now,
                BlockedAt = before?.BlockedAt ?? (count >= limit ? now : null),
            };
            return list with { Refusals = [.. refusals.Where(refusal => !ReferenceEquals(refusal, before)), after] };
        });
        Reload();
    }

    /// <summary>許可したら、そのソフトの拒否の記録を消す（回数を0に戻す。ユーザの言葉 2026-11-11「一度許可したらカウントリセット」）。</summary>
    public async Task ForgetRefusalsAsync(string name, string? exePath, string? origin)
    {
        // 記録が無ければ書かない（許すたびに控えを書いて、見張りに読み直させない）
        if (!_index.Refusals.Any(refusal => refusal.IsSame(name, exePath, origin)))
        {
            return;
        }

        await store().ApiClients.UpdateAsync(list => list with
        {
            Refusals = (list.Refusals ?? []).Where(refusal => refusal is not null && !refusal.IsSame(name, exePath, origin)).ToList(),
        });
        Reload();
    }

    /// <summary>
    /// 許したソフトを控えに足す（前に許した同じソフトの行は置き換える：トークンを失くして求め直したときに、古い行が溜まらないように）。
    /// API が自分の控えとして書く（api.json と同じ。人の操作は窓で済んでいる）。同じ入れ物の錠の中で今の値に当てる
    /// </summary>
    public async Task AddAsync(ApiClientRecord record, CancellationToken cancellationToken)
    {
        await store().ApiClients.UpdateAsync(
            list => list with
            {
                Clients = [.. (list.Clients ?? []).Where(client => !client.IsSame(record.Name, record.ExePath, record.Origin)), record],
            },
            cancellationToken);
        Reload();
    }

    /// <summary>
    /// 使った印。前に書いてから <see cref="TouchInterval"/> 経っていれば、最後に使った日時を書く。書けなくても答えは止めない（ログに残す）
    /// </summary>
    public void Touch(ApiClientRecord client)
    {
        var now = clock();
        var last = _touched.GetOrAdd(client.Id, client.LastUsedAt ?? DateTimeOffset.MinValue);
        if (now - last < TouchInterval || !_touched.TryUpdate(client.Id, now, last))
        {
            return;
        }

        _ = TouchCoreAsync(client.Id, now);
    }

    private async Task TouchCoreAsync(string clientId, DateTimeOffset now)
    {
        try
        {
            await store().ApiClients.UpdateAsync(list => list with
            {
                Clients = (list.Clients ?? []).Select(client => client.Id == clientId ? client with { LastUsedAt = now } : client).ToList(),
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Core.Diagnostics.AppLog.Error("外から使う API の、ソフトの最後に使った日時を書く", exception);
        }
    }
}

/// <summary>
/// 許しを求める要求の列（plan 31）。窓は1つずつ出し（列を1本の流れで回す）、同じソフト（名前・exe・オリジンが同じ）から重ねて来た要求は1つにまとめる。
/// トークンは結果を取りに来たときに作って1回だけ渡し、控えにはハッシュだけを書く。平文はどこにも残さない
/// </summary>
internal sealed class ApiClientRequests
{
    /// <summary>まだ答えていない要求の上限。人が1つずつ答える窓なので、これを超えて溜まるのは道具の誤りか嫌がらせ（429 で断る）。</summary>
    internal const int MaxPending = 8;

    /// <summary>
    /// 決まった結果を取りに来るのを待つ間。道具が問い合わせ直す間隔は数秒〜数十秒の見込みで、人が窓に答えた後に道具を閉じていたら取りに来ない。
    /// 過ぎたら捨てる（許しても受け取られなかった物は控えに書かない）
    /// </summary>
    internal static readonly TimeSpan KeepDecided = TimeSpan.FromMinutes(10);

    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Delivered = "delivered";

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private readonly Channel<Entry> _queue = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly IApiClientApprovals? _approvals;
    private readonly ApiClientBook _book;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Task _loop;

    public ApiClientRequests(IApiClientApprovals? approvals, ApiClientBook book, Func<DateTimeOffset> clock)
    {
        _approvals = approvals;
        _book = book;
        _clock = clock;
        _loop = Task.Run(AskLoopAsync);
    }

    private sealed class Entry(string id, ApiClientRequestInfo info)
    {
        public string Id { get; } = id;

        public ApiClientRequestInfo Info { get; } = info;

        public string State { get; set; } = Pending;

        public IReadOnlyList<string> Scopes { get; set; } = [];

        public DateTimeOffset? DecidedAt { get; set; }

        public string? ClientId { get; set; }

        /// <summary>決まったら終わる（結果を待っている問い合わせを起こす）。</summary>
        public TaskCompletionSource Decided { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>今、人に聞けるか（聞けなければ入口で断る）。</summary>
    public bool CanAsk => _approvals?.CanAsk == true;

    /// <summary>
    /// 要求を列に入れ、結果を取る合言葉を返す。同じソフトの答えていない要求があれば、それを返す（窓を重ねない）。
    /// 列が溢れていれば null
    /// </summary>
    public string? Submit(ApiClientRequestInfo info)
    {
        lock (_lock)
        {
            Forget();
            var waiting = _byId.Values.Where(entry => entry.State == Pending).ToList();
            if (waiting.FirstOrDefault(entry => SameSoftware(entry.Info, info)) is { } same)
            {
                return same.Id;
            }

            if (waiting.Count >= MaxPending)
            {
                return null;
            }

            var entry = new Entry(ApiFiles.NewToken(), info);
            _byId[entry.Id] = entry;
            _queue.Writer.TryWrite(entry);
            return entry.Id;
        }
    }

    private static bool SameSoftware(ApiClientRequestInfo a, ApiClientRequestInfo b)
        => string.Equals(a.Name, b.Name, StringComparison.Ordinal)
            && string.Equals(a.ExePath, b.ExePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Origin, b.Origin, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 結果を取る。決まっていなければ <paramref name="wait"/> まで待つ。許されていれば、ここでトークンを作って控えに書き、1回だけ返す。
    /// 知らない合言葉（期限切れも）は null
    /// </summary>
    public async Task<ClientResultAnswer?> CollectAsync(string requestId, TimeSpan wait, CancellationToken cancellationToken)
    {
        Entry? entry;
        lock (_lock)
        {
            Forget();
            entry = _byId.GetValueOrDefault(requestId);
        }

        if (entry is null)
        {
            return null;
        }

        if (wait > TimeSpan.Zero && !entry.Decided.Task.IsCompleted)
        {
            // API が止まるときは待ちを解く（解かないと、待ち受けを止めるのがこの待ちの終わりまで止まる。SSE を閉じるのと同じ）
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            try
            {
                await entry.Decided.Task.WaitAsync(wait, linked.Token);
            }
            catch (TimeoutException)
            {
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
            }
        }

        string? token = null;
        lock (_lock)
        {
            if (entry.State != Approved)
            {
                return new ClientResultAnswer(entry.State, null, entry.State == Delivered ? entry.ClientId : null, entry.State == Delivered ? entry.Scopes : null);
            }

            // 渡すのは1回だけ。錠の中で状態を進めてから作る（同時に2つ取りに来ても、2本目は delivered）
            entry.State = Delivered;
            token = ApiFiles.NewToken();
            entry.ClientId = Guid.NewGuid().ToString("N");
        }

        var record = new ApiClientRecord
        {
            Id = entry.ClientId,
            Name = entry.Info.Name,
            ExePath = entry.Info.ExePath,
            Origin = entry.Info.Origin,
            Scopes = entry.Scopes,
            TokenHash = ApiClientBook.HashOf(token),
            GrantedAt = _clock(),
        };
        try
        {
            await _book.AddAsync(record, CancellationToken.None);
        }
        catch
        {
            // 控えに書けなければ渡さない（渡したトークンが使えないまま残る）。もう一度取りに来れば作り直す
            lock (_lock)
            {
                entry.State = Approved;
                entry.ClientId = null;
            }

            throw;
        }

        Core.Diagnostics.AppLog.Note("外から使う API", $"ソフト「{record.Name}」を許した（書き込み：{(record.Scopes.Count == 0 ? "なし" : string.Join("・", record.Scopes))}）");
        return new ClientResultAnswer(Approved, token, record.Id, record.Scopes);
    }

    /// <summary>決まってから長く取りに来ない物を捨てる（錠の中で呼ぶ）。</summary>
    private void Forget()
    {
        var now = _clock();
        foreach (var id in _byId.Values.Where(entry => entry.DecidedAt is { } decided && now - decided > KeepDecided).Select(entry => entry.Id).ToList())
        {
            _byId.Remove(id);
        }
    }

    /// <summary>窓を1つずつ出す流れ。止まるときは今の窓を閉じ、残りは答えずに捨てる（結果を取りに来ても見つからない）。</summary>
    private async Task AskLoopAsync()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(_stop.Token))
            {
                var info = entry.Info;

                // 前の窓の拒否で止まったソフトの求めが列に残っていたら、窓を出さずに断る（拒否の書き込みは前の回で済んでいる）。
                // 窓を出す直前の回数で「あと1回」を言い直す（列に入れた時の数は、前の窓の拒否より古いことがある）
                var blocked = _book.IsBlocked(info.Name, info.ExePath, info.Origin);
                info = info with { RefusalsLeft = _book.RefusalsLeft(info.Name, info.ExePath, info.Origin) };
                ApiClientDecision decision;
                try
                {
                    decision = !blocked && _approvals is { CanAsk: true } approvals
                        ? await approvals.AskAsync(info, _stop.Token)
                        : ApiClientDecision.Dismissed;
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    Core.Diagnostics.AppLog.Error("外から使う API の許しを聞く", exception);
                    decision = ApiClientDecision.Dismissed;
                }

                // 回数を書き終えてから結果を知らせる（先に知らせると、ソフトがすぐ求め直したときに数える前の回数で窓が出る）
                await RememberAsync(info, decision);

                lock (_lock)
                {
                    entry.State = decision.Approved ? Approved : Denied;

                    // 人が選べるのは求められた物の中だけ（窓が求められていない物を足して返しても、許さない）
                    entry.Scopes = decision.Approved
                        ? ApiScopes.All.Where(scope => decision.Scopes.Contains(scope) && entry.Info.Scopes.Contains(scope)).ToList()
                        : [];
                    entry.DecidedAt = _clock();
                }

                if (!decision.Approved)
                {
                    Core.Diagnostics.AppLog.Note(
                        "外から使う API",
                        blocked ? $"ソフト「{info.Name}」の求めを、止めているので断った" : $"ソフト「{info.Name}」の求めを断った");
                }

                entry.Decided.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 拒否を数える・許可で0に戻す。数えるのは人が［拒否］をマウスでクリックしたときだけ（キーで押した・閉じた・API が止まった・聞けなかったは数えない）。
    /// 書けなくても流れは止めない（止めると、後の求めが全部 pending のまま残る）。ログに残す
    /// </summary>
    private async Task RememberAsync(ApiClientRequestInfo info, ApiClientDecision decision)
    {
        try
        {
            if (decision.Approved)
            {
                // 0に戻すのは人が［許可］を押した時（トークンを受け取りに来なくても、人は許している）
                await _book.ForgetRefusalsAsync(info.Name, info.ExePath, info.Origin);
            }
            else if (decision.Refused)
            {
                await _book.RefuseAsync(info.Name, info.ExePath, info.Origin, _clock());
                if (_book.IsBlocked(info.Name, info.ExePath, info.Origin))
                {
                    Core.Diagnostics.AppLog.Note("外から使う API", $"ソフト「{info.Name}」は拒否が続いたので、次から窓を出さずに断る");
                }
            }
        }
        catch (Exception exception)
        {
            // 書けない以外（読み直しの知らせを受けた側の例外など）も受ける。1つでも抜けると窓の流れが終わり、後の求めが全部 pending のまま残る
            Core.Diagnostics.AppLog.Error("外から使う API の、拒否の回数を書く", exception);
        }
    }

    /// <summary>
    /// 止める（API が止まるとき）。開いている窓は取り消しで閉じる。流れの終わりは待たない：
    /// 止めるのが画面のスレッドから来ると、窓を閉じるのにも画面のスレッドが要り、待つと固まる
    /// </summary>
    public void Stop()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();
        _queue.Writer.TryComplete();
    }

    /// <summary>流れが終わるまで待つ（試験）。</summary>
    internal Task Completion => _loop;
}
