using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chmonos.Core.Storage;

namespace Chmonos.Mcp;

/// <summary>API の答え。<see cref="Problem"/> があれば、AI に渡す文（失敗の理由と次にやること）。</summary>
internal sealed record ApiAnswer(JsonNode? Body, string? Problem, RunningInstanceRecord? Instance)
{
    public bool Failed => Problem is not null;

    public static ApiAnswer Fail(string message, RunningInstanceRecord? instance = null) => new(null, message, instance);
}

/// <summary>
/// 通訳から API への道。探す（<see cref="InstanceFinder"/>）→ トークン（控え・無ければ許しを求める）→ 問い合わせ、を1本にする。
///
/// 通訳は**API に話すだけ**：保存先を直に読まない・書かない・<c>app.lock</c> を取らない・<c>api.json</c> も読まない
/// （トークンは人が Chmonos の窓で許して受け取った物だけ。API の守りと、設定の「つないでいるソフト」での取り消しがそのまま効く）。
/// 落ちない：見つからない・断られた・つながらないは、どれも <see cref="ApiAnswer.Problem"/> の文で返す。
/// </summary>
internal sealed class ChmonosBridge : IDisposable
{
    /// <summary>
    /// 1回の問い合わせの時間切れ。許しの結果を待つ長さ（API の <c>wait</c> は30秒まで）に、答えを作る間の余裕を足した長さ。
    /// 手元の API が答えないまま道具の答えを止め続けないように
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    private const string Disconnected =
        "Chmonos とつながりませんでした。Chmonos が閉じたか、API を止めたようです。Chmonos を起動して、設定の「API」で「APIを使う」がオンかを確かめてから、もう一度試してください。";

    private readonly McpOptions _options;
    private readonly TextWriter _log;
    private readonly HttpClient _http;
    private readonly TokenStore _tokens;
    private readonly SemaphoreSlim _approval = new(1, 1);

    /// <summary>答えを待っている許しの求め（保存先ごと）。次の呼び出しで同じ求めの続きを待つ（求め直すと窓が重なって出る）。</summary>
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);

    public ChmonosBridge(McpOptions options, TextWriter log, Func<int, DateTimeOffset?, bool?>? isAlive = null, Func<DateTimeOffset>? clock = null)
    {
        _options = options;
        _log = log;
        Finder = new InstanceFinder(options.RunningDirectory, options.Store, isAlive);
        _tokens = new TokenStore(options.TokenFile);
        Clock = clock ?? (() => DateTimeOffset.Now);

        // 相手は自分の PC の 127.0.0.1 だけ。プロキシの設定に向かわないよう切る（社内のプロキシが 127.0.0.1 を外へ送ると、トークンが外へ出る）
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public InstanceFinder Finder { get; }

    public Func<DateTimeOffset> Clock { get; }

    /// <summary>許しを求めるときに名乗る名前（MCP の initialize の clientInfo から。<see cref="ClientNameFrom"/>）。</summary>
    public string ClientName { get; set; } = ClientNameFrom(null, null);

    /// <summary>許しで求める書き込みの種類。今は読むだけなので空（書き込みの道具を足したら、その種類をここに足す）。</summary>
    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>
    /// 名乗る名前を作る。API は1〜64字・制御文字なしを求める（400 <c>invalidName</c>）ので、整えて切り詰める。
    /// 末尾の「（MCP）」は、Chmonos の窓と「つないでいるソフト」で、MCP の通訳を通した物と見分けるため
    /// </summary>
    public static string ClientNameFrom(string? title, string? name)
    {
        const string Suffix = "（MCP）";
        var raw = !string.IsNullOrWhiteSpace(title) ? title : name;
        var cleaned = new string((raw ?? string.Empty).Where(c => !char.IsControl(c) && !IsDirectionMark(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "AIの道具";
        }

        var room = 64 - Suffix.Length;
        if (cleaned.Length > room)
        {
            cleaned = cleaned[..room].TrimEnd();
        }

        return cleaned + Suffix;
    }

    private static bool IsDirectionMark(char c)
        => c is (>= (char)0x202A and <= (char)0x202E) or (>= (char)0x2066 and <= (char)0x2069) or (char)0x200E or (char)0x200F;

    public Task<ApiAnswer> GetAsync(string pathAndQuery, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, pathAndQuery, null, cancellationToken);

    public Task<ApiAnswer> PostAsync(string path, JsonNode body, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, path, body, cancellationToken);

    private async Task<ApiAnswer> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var find = Finder.Find();
        if (find is not FindResult.Found found)
        {
            return ApiAnswer.Fail(((FindResult.Problem)find).Message);
        }

        var (instance, port) = (found.Instance, found.Port);
        var baseUri = new Uri($"http://127.0.0.1:{port}");
        try
        {
            var (token, tokenProblem) = await TokenAsync(instance, baseUri, cancellationToken);
            if (token is null)
            {
                return ApiAnswer.Fail(tokenProblem!, instance);
            }

            using var response = await SendRawAsync(method, new Uri(baseUri, path), body, token, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return await AnswerOfAsync(response, instance, cancellationToken);
            }

            // 401：設定で取り消された・保存先が戻された・ほかの通訳が許し直して前のトークンが効かなくなった。
            // まず控えを読み直し、ほかの通訳が後から受け取った別の値があればそれを使う（無ければ捨てて求め直す。
            // 同じ名前の通訳どうしが求め直し合うと、許すたびに相手の行を置き換え合う）
            var stored = _tokens.Get(instance.StoreRoot, ClientName);
            if (stored is null || stored == token)
            {
                _tokens.Forget(instance.StoreRoot, ClientName, token);
                McpLog.Write(_log, "トークンが効かなかったので、許しを求め直す");
                (stored, tokenProblem) = await TokenAsync(instance, baseUri, cancellationToken);
                if (stored is null)
                {
                    return ApiAnswer.Fail(tokenProblem!, instance);
                }
            }

            using var retried = await SendRawAsync(method, new Uri(baseUri, path), body, stored, cancellationToken);
            return await AnswerOfAsync(retried, instance, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            McpLog.Write(_log, "API とつながらない", exception);
            return ApiAnswer.Fail(Disconnected, instance);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiAnswer.Fail("Chmonos の API が時間内に答えませんでした。少し待ってから、もう一度試してください。", instance);
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, Uri uri, JsonNode? body, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        return await _http.SendAsync(request, timeout.Token);
    }

    private static async Task<ApiAnswer> AnswerOfAsync(HttpResponseMessage response, RunningInstanceRecord instance, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new ApiAnswer(string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text), null, instance);
        }

        return ApiAnswer.Fail(ProblemText(response.StatusCode, text), instance);
    }

    /// <summary>API の誤り（problem+json）を AI に渡す文にする。API の文は人向けの日本語で、次にやることを含む。</summary>
    internal static string ProblemText(HttpStatusCode status, string body)
    {
        string? code = null;
        string? detail = null;
        try
        {
            if (JsonNode.Parse(body) is JsonObject problem)
            {
                code = problem["code"]?.GetValue<string>();
                detail = problem["detail"]?.GetValue<string>() ?? problem["title"]?.GetValue<string>();
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
        }

        return $"Chmonos の API が断りました（{(int)status}{(code is null ? string.Empty : " " + code)}）：{detail ?? "理由は書かれていませんでした。"}";
    }

    /// <summary>
    /// 控えのトークンか、無ければ許しを求める（docs/spec/api-clients.md の流れ）。人が［許可］を押すのを <see cref="McpOptions.ApprovalWait"/> だけ待ち、
    /// 待ちきれなければ「押してからもう一度」の文を返す（求めは覚えておき、次の呼び出しで続きを待つ）。
    /// 1本ずつ（使い手は道具を並べて呼べるが、並べて求めると窓が重なる）
    /// </summary>
    private async Task<(string? Token, string? Problem)> TokenAsync(RunningInstanceRecord instance, Uri baseUri, CancellationToken cancellationToken)
    {
        // 先の呼び出しが［許可］を待っている間に並んだ呼び出しは、錠が空いた後は待たずに1回だけ見る
        // （待ちを重ねると、先の待ちと合わせて「一定時間」の倍を待たせる）
        var queued = !_approval.Wait(0, CancellationToken.None);
        if (queued)
        {
            await _approval.WaitAsync(cancellationToken);
        }

        try
        {
            if (_tokens.Get(instance.StoreRoot, ClientName) is { } saved)
            {
                return (saved, null);
            }

            // 覚えていた求めが消えていた（Chmonos が起動し直した・10分取りに来なかった）ら、1回だけ求め直す
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (!_pending.TryGetValue(instance.StoreRoot, out var requestId))
                {
                    var asked = await AskAsync(baseUri, cancellationToken);
                    if (asked.RequestId is null)
                    {
                        return (null, asked.Problem);
                    }

                    requestId = asked.RequestId;
                    _pending[instance.StoreRoot] = requestId;
                    McpLog.Write(_log, "Chmonos に許しを求めた", message: ClientName);
                }

                var wait = queued ? 0 : (int)Math.Clamp(_options.ApprovalWait.TotalSeconds, 0, 30);
                var payload = new JsonObject { ["requestId"] = requestId, ["wait"] = wait };
                using var response = await SendRawAsync(HttpMethod.Post, new Uri(baseUri, "/v1/clients/request/result"), payload, null, cancellationToken);
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _pending.Remove(instance.StoreRoot);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _pending.Remove(instance.StoreRoot);
                    return (null, ProblemText(response.StatusCode, text));
                }

                var result = JsonNode.Parse(text)!;
                switch (result["state"]?.GetValue<string>())
                {
                    case "approved":
                        _pending.Remove(instance.StoreRoot);
                        var token = result["token"]!.GetValue<string>();
                        _tokens.Save(instance.StoreRoot, ClientName, token, Clock());
                        McpLog.Write(_log, "Chmonos で許可された", message: ClientName);
                        return (token, null);

                    case "pending":
                        return (null, "Chmonos に許可の窓を出しました。Chmonos の窓で［許可］を押してから、もう一度試してください。");

                    case "denied":
                        _pending.Remove(instance.StoreRoot);
                        return (null, "Chmonos で許可されませんでした。使うときは、もう一度試して、Chmonos の窓で［許可］を押してください。");

                    default:
                        // delivered：同じ名前のほかの通訳（別のセッション）が先に受け取った。控えに書いていればそれを使う
                        _pending.Remove(instance.StoreRoot);

                        // 先に受け取った通訳が控えに書き終えるのを少し待つ（受け取ってから書くまでの間に、こちらへ delivered が返り得る）。
                        // 2秒は、数行のファイルを書くのに十分な長さ
                        for (var look = 0; look < 20; look++)
                        {
                            if (_tokens.Get(instance.StoreRoot, ClientName) is { } shared)
                            {
                                return (shared, null);
                            }

                            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                        }

                        return (null, "許可は別の所で受け取られました。もう一度試してください。");
                }
            }

            return (null, "Chmonos への許可の求めが続けて消えました。Chmonos を確かめてから、もう一度試してください。");
        }
        finally
        {
            _approval.Release();
        }
    }

    private async Task<(string? RequestId, string? Problem)> AskAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var scopes = new JsonArray(Scopes.Select(scope => (JsonNode?)JsonValue.Create(scope)).ToArray());
        var payload = new JsonObject { ["name"] = ClientName, ["scopes"] = scopes };
        using var response = await SendRawAsync(HttpMethod.Post, new Uri(baseUri, "/v1/clients/request"), payload, null, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return (JsonNode.Parse(text)?["requestId"]?.GetValue<string>(), null);
        }

        var code = TryCode(text);
        return (null, code switch
        {
            "approvalUnavailable" => "Chmonos の窓が開いていないので、許可を聞けませんでした。Chmonos の画面を開いてから、もう一度試してください。",
            "requestBlocked" => "Chmonos が、このソフトの求めを止めています（拒否が続いたため）。Chmonos の設定の「API」の「止めているソフト」で止めを外してから、もう一度試してください。",
            "tooManyRequests" => "Chmonos に答えを待っている許可の求めがたまっています。Chmonos の窓で答えてから、もう一度試してください。",
            _ => ProblemText(response.StatusCode, text),
        });
    }

    private static string? TryCode(string text)
    {
        try
        {
            return JsonNode.Parse(text)?["code"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _approval.Dispose();
    }
}
