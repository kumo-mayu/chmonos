using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// ソフトごとの許し（plan 31・docs/spec/api-clients.md）：ソフトが許しを求め、人が画面で許したときだけ、そのソフトだけのトークンを1回返す。
/// 求めるのと結果を取るのはトークンなし（トークンを持たないソフトが来る道）。Host とオリジンの守りは他の道と同じ
/// </summary>
internal static class ApiClientEndpoints
{
    public const string RequestPath = "/v1/clients/request";

    public const string ResultPath = "/v1/clients/request/result";

    /// <summary>名前の長さの上限。窓とログに出す物で、長い名前で窓を埋められないように（ふつうのソフトの名前は数十字に収まる）。</summary>
    internal const int MaxNameLength = 64;

    /// <summary>結果を待つ上限（秒）。道具の HTTP の時間切れ（HttpClient の既定は100秒）より十分短く。</summary>
    internal const int MaxWaitSeconds = 30;

    public static void Map(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapPost(RequestPath, async (ApiState state, ApiClientRequests requests, IConnectionOwners owners, HttpContext context) =>
        {
            var body = await ReadAsync(context.Request, json.ClientRequestBody);
            var name = (body.Name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > MaxNameLength || name.Any(char.IsControl))
            {
                throw QueryException.Invalid("invalidName", $"name に、ソフトの名前を{MaxNameLength}字までで書いてください（改行などの制御文字は使えません）。");
            }

            var scopes = new List<string>();
            foreach (var scope in body.Scopes ?? [])
            {
                // 読むのはいつも許すので、read は書いてあっても無くてもよい
                if (scope == "read")
                {
                    continue;
                }

                if (scope is null || !ApiScopes.IsKnown(scope))
                {
                    throw QueryException.Invalid("unknownScope", $"scopes に知らない種類があります。使えるのは read と {string.Join("・", ApiScopes.All)} です。");
                }

                if (!scopes.Contains(scope))
                {
                    scopes.Add(scope);
                }
            }

            // 接続を開いた exe は、答えを返す前に引く（返して接続が閉じると TCP の表から消える）。
            // ブラウザのページなら、オリジンも添える（許可したオリジンの決まりは守りで通っている）。
            // 止めているかを見るのに要るので、画面の有無より先に引く
            var exePath = owners.ExePathOf(context.Connection.LocalPort, context.Connection.RemotePort);
            var origin = context.Request.Headers.Origin is { Count: > 0 } header ? header.ToString() : null;

            // 人が拒否を続けたソフトは、窓を出さずに断る（2026-11-11。人が設定で止めを外せば、また聞く）
            if (state.Clients.IsBlocked(name, exePath, origin))
            {
                throw new QueryException(403, "requestBlocked", "このソフトの求めは、拒否が続いたため止めています。Chmonos の設定の「API」で止めを外してから、もう一度求めてください。");
            }

            // 画面を出していない（--headless で窓が無い）なら、待たせずに断る（まとめ役の判断 2026-11-11）
            if (!requests.CanAsk)
            {
                throw new QueryException(503, "approvalUnavailable", "Chmonos の画面を開いてから、もう一度求めてください。");
            }

            var orderedScopes = ApiScopes.All.Where(scopes.Contains).ToList();
            var requestId = requests.Submit(new ApiClientRequestInfo(
                    name, exePath, origin, orderedScopes, state.Clients.HasSame(name, exePath, origin), state.Clients.RefusalsLeft(name, exePath, origin)))
                ?? throw new QueryException(429, "tooManyRequests", "答えを待っている求めが多すぎます。先の求めに答えてから、もう一度求めてください。");

            Core.Diagnostics.AppLog.Note("外から使う API", $"ソフト「{name}」が許しを求めた");
            NoStore(context);
            return Results.Json(new ClientRequestAccepted(requestId, ApiClientRequests.Pending), json.ClientRequestAccepted, statusCode: StatusCodes.Status202Accepted);
        })
            .Takes<ClientRequestBody>()
            .Produces<ClientRequestAccepted>(StatusCodes.Status202Accepted, "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("ソフトが許しを求める（トークンなし。Chmonos の画面に窓を出し、人が許すか拒否するかを選ぶ）");

        app.MapPost(ResultPath, async (ApiClientRequests requests, HttpContext context) =>
        {
            var body = await ReadAsync(context.Request, json.ClientResultRequest);
            if (string.IsNullOrEmpty(body.RequestId))
            {
                throw QueryException.Invalid("invalidRequest", "requestId を書いてください。");
            }

            var wait = TimeSpan.FromSeconds(Math.Clamp(body.Wait ?? 0, 0, MaxWaitSeconds));
            var answer = await requests.CollectAsync(body.RequestId, wait, context.RequestAborted)
                ?? throw QueryException.NotFound("requestNotFound", "この requestId の求めはありません。時間が経ったか、後の許可・取り消しで無効になったか、Chmonos が起動し直しました。もう一度求めてください。");

            // トークンが入る答えは、間に入る物に残させない
            NoStore(context);
            return Results.Json(answer, json.ClientResultAnswer);
        })
            .Takes<ClientResultRequest>()
            .Produces<ClientResultAnswer>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("許しの結果を取る（トークンなし。許されたら、そのソフトだけのトークンを1回だけ返す。wait 秒まで決まるのを待つ）");

        app.MapGet("/v1/clients/me", (ApiState state, HttpContext context) =>
        {
            var caller = ApiCallerInfo.Of(context);
            var answer = caller.Client is { } client
                ? new ClientSelfAnswer("client", client.Id, client.Name, ["read", .. client.Scopes])
                : new ClientSelfAnswer("legacy", null, null, state.AllowWrites ? ["read", .. ApiScopes.Writes] : ["read"]);
            return ApiHost.Json(answer, json.ClientSelfAnswer);
        })
            .Produces<ClientSelfAnswer>(StatusCodes.Status200OK, "application/json")
            .WithSummary("今のトークンが誰で、何を許されているか（legacy は api.json のトークン）");
    }

    private static void NoStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    /// <summary>仕様書の印：要求の本文の型（省ける欄を必須にしないよう、ほかの道と同じく型を知らせる）。</summary>
    private static RouteHandlerBuilder Takes<T>(this RouteHandlerBuilder builder)
        where T : notnull
    {
        ApiOpenApi.NoteRequestType(typeof(T));
        return builder.Accepts<T>("application/json");
    }

    private static async Task<T> ReadAsync<T>(HttpRequest request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
        => await JsonSerializer.DeserializeAsync(request.Body, type)
            ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");
}
