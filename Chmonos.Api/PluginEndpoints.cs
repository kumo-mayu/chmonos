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
                client.Id, after ?? 0, epoch, TimeSpan.FromSeconds(Math.Clamp(wait ?? 0, 0, PluginEventHub.MaxWaitSeconds)), linked.Token);

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

            var last = hub.Subscribe(client.Id, kinds);
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
    }

    /// <summary>ソフトごとのトークンの要求か。api.json のトークンはどのソフトか分からず、列を決められないので断る。</summary>
    private static ApiClientRecord RequireClient(HttpContext context)
        => ApiCallerInfo.Of(context).Client
            ?? throw new QueryException(403, "clientTokenRequired",
                "出来事の流れは、ソフトごとのトークンで使います。/v1/clients/request で許可を求めて、受け取ったトークンを使ってください。");

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";
}
