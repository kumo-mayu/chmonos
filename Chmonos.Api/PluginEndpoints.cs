using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// ソフトへの出来事の流れと購読（plan 44 の①・docs/spec/plugins.md）。どちらもソフトごとのトークンで使う：
/// 列はソフトごとに持つので、誰か分からない api.json のトークン（古い1本）では使えない
/// </summary>
internal static class PluginEndpoints
{
    public const string EventsPath = "/v1/plugin/events";

    public const string SubscriptionsPath = "/v1/plugin/subscriptions";

    public static void Map(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapGet(EventsPath, async (PluginEventHub hub, ApiState state, IHostApplicationLifetime lifetime, HttpContext context, long? after, int? wait, string? epoch) =>
        {
            var client = RequireClient(context);
            state.Clients.NotePolled(client);

            // API が止まるときは待ちを解く（解かないと、待ち受けを止めるのが待ちの終わりまで約30秒止まる。許しの結果を待つのと同じ）
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
            var answer = await hub.WaitAsync(
                // 空の epoch（`epoch=`）は送っていないのと同じ：初めて聞くソフトが前の値を持たないまま送っても、起動し直しと取り違えない
                client.Id, after ?? 0, string.IsNullOrEmpty(epoch) ? null : epoch, TimeSpan.FromSeconds(Math.Clamp(wait ?? 0, 0, PluginEventHub.MaxWaitSeconds)), linked.Token);

            // 中身は使う人の操作なので、間に入る物に残させない（ETag も付けない：毎回違う答え）
            NoStore(context);
            return Results.Json(answer, json.PluginEventsAnswer);
        })
            .Produces<PluginEventsAnswer>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("ソフトへの出来事を取る（ソフトのトークンで。after より後を返し、無ければ wait 秒〔0〜30〕まで待って空を返す）");

        app.MapPut(SubscriptionsPath, async (PluginEventHub hub, HttpContext context) =>
        {
            var client = RequireClient(context);
            var body = await JsonSerializer.DeserializeAsync(context.Request.Body, json.PluginSubscriptionsBody)
                ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");
            if (body.Kinds is not { } requested)
            {
                throw QueryException.Invalid("invalidRequest", "kinds に、購読する種類の配列を書いてください（止めるときは空の配列）。");
            }

            var kinds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kind in requested)
            {
                if (kind is null || !PluginEventHub.Kinds.Contains(kind))
                {
                    throw QueryException.Invalid("unknownKind", $"kinds に知らない種類があります。使えるのは {string.Join("・", PluginEventHub.Kinds)} です。");
                }

                // 見られる操作は許しの窓で人が選ぶ（ユーザ判断 2026-11-12「選ばせましょう」）。許していない種類は購読させない
                var scope = ApiScopes.WatchOf(kind);
                if (!client.Scopes.Contains(scope, StringComparer.Ordinal))
                {
                    throw new QueryException(403, "scopeNotGranted",
                        $"このソフトには「{ApiScopes.Label(scope)}」を見ることが許されていません（{scope}）。もう一度許可を求めるか、Chmonos の設定の「API」の「つないでいるソフト」で許してください。");
                }

                kinds.Add(kind);
            }

            var (result, last) = hub.Subscribe(client.Id, kinds);
            switch (result)
            {
                case PluginSubscribeResult.Gone:
                    // 本文を待つ間に取り消された：次の要求と同じ答え（トークンがもう通らない）
                    throw new QueryException(401, "unauthorized", "トークンが違います。保存先の api.json の token か、許可を求めて受け取ったトークンを Authorization: Bearer で送ってください。");
                case PluginSubscribeResult.NotGranted:
                    // 本文を待つ間に許しが外された：上の確かめと同じ答え
                    throw new QueryException(403, "scopeNotGranted",
                        "このソフトには、求めた種類を見ることが許されていません。もう一度許可を求めるか、Chmonos の設定の「API」の「つないでいるソフト」で許してください。");
            }

            Core.Diagnostics.AppLog.Note(
                "外から使う API", $"ソフト「{client.Name}」が購読した（{(kinds.Count == 0 ? "なし" : string.Join("・", PluginEventHub.Kinds.Where(kinds.Contains)))}）");
            NoStore(context);
            return Results.Json(new PluginSubscriptionsAnswer(hub.Epoch, last, hub.KindsOf(client.Id)), json.PluginSubscriptionsAnswer);
        })
            .Accepts<PluginSubscriptionsBody>("application/json")
            .Produces<PluginSubscriptionsAnswer>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("見る画面の操作を選ぶ（ソフトのトークンで。丸ごと置き換え、空の配列で止める。許しの窓で許した種類だけ）");

        ApiOpenApi.NoteRequestType(typeof(PluginSubscriptionsBody));

        app.MapGet(MenuPath, (ApiState state, ApiClientRequests requests, HttpContext context) =>
        {
            var client = RequireClient(context);
            var (stateName, pending) = requests.MenuStateOf(client.Id);
            NoStore(context);
            return Results.Json(new PluginMenuAnswer(stateName, state.Clients.MenuOf(client.Id), pending), json.PluginMenuAnswer);
        })
            .Produces<PluginMenuAnswer>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithSummary("今出している項目の宣言と、人に聞いている宣言（ソフトのトークンで）");

        app.MapPut(MenuPath, async (ApiState state, ApiClientRequests requests, HttpContext context) =>
        {
            var client = RequireClient(context);
            IReadOnlyList<PluginMenuItemRef> items;
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                items = PluginMenus.Parse(document.RootElement);
            }
            catch (JsonException)
            {
                throw QueryException.Invalid("invalidJson", "送られた JSON が読めません。");
            }

            var current = state.Clients.MenuOf(client.Id);
            NoStore(context);

            // 同じ宣言の送り直しは聞かない（ソフトは起動のたびに送ってよい。ユーザ判断 2026-11-12）。聞いている途中の別の宣言も取り下げる
            if (PluginMenus.Same(current, items))
            {
                requests.WithdrawMenu(client.Id);
                return Results.Json(new PluginMenuAnswer(ApiClientRequests.Approved, current, null), json.PluginMenuAnswer);
            }

            // 消えるだけなら聞かずに効かせる（出す物が減るだけで、人が見ていない項目は増えない。空の items で消すのも同じ）
            var change = PluginMenus.Diff(current, items);
            if (change.OnlyRemoves)
            {
                requests.WithdrawMenu(client.Id);
                await state.Clients.SetMenuAsync(client.Id, items);
                Core.Diagnostics.AppLog.Note("外から使う API", $"ソフト「{client.Name}」が項目を消した（{change.Removed.Count}個）");
                return Results.Json(new PluginMenuAnswer(ApiClientRequests.Approved, items, null), json.PluginMenuAnswer);
            }

            // 拒否が続いて止めたソフトは窓を出さずに断る（許しを求めるのと同じ数え方・同じ文）
            if (state.Clients.IsBlocked(client.Name, client.ExePath, client.Origin))
            {
                throw new QueryException(403, "requestBlocked", "このソフトの求めは、拒否が続いたため止めています。Chmonos の設定の「API」で止めを外してから、もう一度求めてください。");
            }

            if (!requests.CanAsk)
            {
                throw new QueryException(503, "approvalUnavailable", "Chmonos の画面を開いてから、もう一度送ってください。");
            }

            if (!requests.SubmitMenu(client.Id, items))
            {
                throw new QueryException(429, "tooManyRequests", "答えを待っている求めが多すぎます。先の求めに答えてから、もう一度送ってください。");
            }

            Core.Diagnostics.AppLog.Note("外から使う API", $"ソフト「{client.Name}」が項目の宣言を変えたいと求めた");
            return Results.Json(new PluginMenuAnswer(ApiClientRequests.Pending, current, items), json.PluginMenuAnswer, statusCode: StatusCodes.Status202Accepted);
        })
            .Produces<PluginMenuAnswer>(StatusCodes.Status200OK, "application/json")
            .Produces<PluginMenuAnswer>(StatusCodes.Status202Accepted, "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("右クリックなどに足す項目を宣言する（ソフトのトークンで。丸ごと置き換え。足す・変わるときは人に聞き直し、許すまでは前の宣言のまま）");

        app.MapPost(ResultPath, async (PluginEventHub hub, HttpContext context, long seq) =>
        {
            var client = RequireClient(context);
            var body = await JsonSerializer.DeserializeAsync(context.Request.Body, json.PluginResultBody)
                ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");
            if (body.Ok is not { } ok)
            {
                throw QueryException.Invalid("invalidRequest", "ok に true か false を書いてください。");
            }

            NoStore(context);
            return Results.Json(hub.AcceptResult(client, seq, string.IsNullOrEmpty(body.Epoch) ? null : body.Epoch, ok, body.Message), json.PluginResultAnswer);
        })
            .Accepts<PluginResultBody>("application/json")
            .Produces<PluginResultAnswer>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("押された項目の結果を返す（ソフトのトークンで。Chmonos は下の帯の「送りました」をこの文に差し替える）");

        ApiOpenApi.NoteRequestType(typeof(PluginResultBody));
    }

    public const string MenuPath = "/v1/plugin/menu";

    public const string ResultPath = "/v1/plugin/events/{seq:long}/result";

    /// <summary>ソフトごとのトークンの要求か。api.json のトークンはどのソフトか分からず、列を決められないので断る。</summary>
    private static ApiClientRecord RequireClient(HttpContext context)
        => ApiCallerInfo.Of(context).Client
            ?? throw new QueryException(403, "clientTokenRequired",
                "出来事の流れは、ソフトごとのトークンで使います。/v1/clients/request で許可を求めて、受け取ったトークンを使ってください。");

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}
