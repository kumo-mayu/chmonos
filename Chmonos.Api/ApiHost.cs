using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Chmonos.Core.Storage;
using Chmonos.Query;
using Chmonos.Query.Search;

namespace Chmonos.Api;

/// <summary>API を組む材料。</summary>
/// <param name="Port">待つポート（0 なら空いている番号。試験で使う）。</param>
/// <param name="Token">合言葉（<c>Authorization: Bearer …</c>）。</param>
/// <param name="AllowedOrigins">CORS を許す住所（自作の Web ページ）。</param>
internal sealed record ApiHostOptions(string StoreRoot, int Port, string Token, IReadOnlyList<string> AllowedOrigins, Func<DateTimeOffset>? Clock = null);

/// <summary>読み込んだ写しと、答えに要る物。</summary>
internal sealed class ApiState(AppPaths paths, Func<DateTimeOffset> clock)
{
    private volatile QuerySnapshot? _snapshot;

    public AppPaths Paths { get; } = paths;

    public Func<DateTimeOffset> Clock { get; } = clock;

    public DateTimeOffset LoadedAt { get; private set; }

    /// <summary>保存先の形の版（読めた印。印が無ければ 1）。</summary>
    public int StoreFormat { get; private set; } = Core.Storage.StoreFormat.Unversioned;

    /// <summary>保存先の形がこの API より新しい（答えない。アプリと API を新しくしてもらう）。</summary>
    public bool TooNew => StoreFormat > Core.Storage.StoreFormat.Current;

    public QuerySnapshot Snapshot => _snapshot ?? throw new InvalidOperationException("まだ読み込んでいません。");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StoreFormat = Core.Storage.StoreFormat.StoreVersion(Paths.Root);
        _snapshot = await QuerySnapshot.LoadAsync(new DataStore(Paths), cancellationToken);
        LoadedAt = Clock();
    }
}

/// <summary>
/// API の口（HTTP）を組む。守り（docs/spec/local-api.md「守り」「ブラウザから」）：
/// 待つのは 127.0.0.1 だけ・Host の確かめ（DNS rebinding）・許した住所にだけ CORS・合言葉（health と version 以外）。
/// 問い合わせの中身はログに残さない（購入履歴と検索の言葉）。
/// </summary>
internal static class ApiHost
{
    public const string ApiVersion = "v1";

    /// <summary>合言葉なしで答える URL（動いているかとバージョンを確かめるだけ。中身は漏れない）。</summary>
    private static readonly HashSet<string> OpenPaths = new(StringComparer.Ordinal) { "/v1/health", "/v1/version" };

    public static string AppVersion { get; } =
        typeof(ApiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static async Task<(WebApplication App, ApiState State)> BuildAsync(ApiHostOptions options, CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, options.Port));

        // 問い合わせの中身（URL の検索の言葉・商品 ID）をログに書かない
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, QueryJson.Context);
            json.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        });

        var state = new ApiState(new AppPaths(options.StoreRoot), options.Clock ?? (() => DateTimeOffset.Now));
        await state.LoadAsync(cancellationToken);
        builder.Services.AddSingleton(state);

        var app = builder.Build();
        var origins = options.AllowedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var token = Encoding.UTF8.GetBytes("Bearer " + options.Token);
        app.Use((context, next) => GuardAsync(context, next, origins, token, state));
        ApiEndpoints.Map(app);
        return (app, state);
    }

    private static async Task GuardAsync(HttpContext context, RequestDelegate next, HashSet<string> origins, byte[] token, ApiState state)
    {
        var request = context.Request;

        // Host が自分（127.0.0.1・localhost と待っているポート）でなければ断る。他所のドメインを 127.0.0.1 へ向け直して
        // 同じ相手だとブラウザに思わせる手口（DNS rebinding）は、Host が他所のドメインのまま届く
        if (request.Host.Host is not ("127.0.0.1" or "localhost") || request.Host.Port != context.Connection.LocalPort)
        {
            await WriteProblemAsync(context, new QueryException(400, "invalidHost", "Host が 127.0.0.1 か localhost ではありません。"));
            return;
        }

        // ブラウザのページから来た問い合わせ。許した住所（自作の画面）にだけ CORS の許可を出し、それ以外は断る
        if (request.Headers.Origin is { Count: > 0 } origin)
        {
            if (!origins.Contains(origin.ToString()))
            {
                await WriteProblemAsync(context, new QueryException(403, "originNotAllowed", "この住所のページからは使えません。API の設定の許す住所に足してください。"));
                return;
            }

            context.Response.Headers.AccessControlAllowOrigin = origin.ToString();
            context.Response.Headers.Vary = "Origin";
            if (HttpMethods.IsOptions(request.Method))
            {
                context.Response.Headers.AccessControlAllowMethods = "GET, POST";
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Last-Event-ID";
                context.Response.Headers.AccessControlMaxAge = "600";
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
        }

        if (!OpenPaths.Contains(request.Path.Value ?? string.Empty))
        {
            // 合言葉は同じ長さの比べ方をする（違う所までの時間で中身を推し量られないように）
            var given = Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString());
            if (!CryptographicOperations.FixedTimeEquals(given, token))
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteProblemAsync(context, new QueryException(401, "unauthorized", "合言葉が違います。保存先の api.json の token を Authorization: Bearer で送ってください。"));
                return;
            }

            if (state.TooNew)
            {
                await WriteProblemAsync(context, new QueryException(503, "storeFormatTooNew", "保存先がこの API より新しいバージョンの Chmonos で使われています。Chmonos を新しくしてください。"));
                return;
            }
        }

        try
        {
            await next(context);
        }
        catch (QueryException exception)
        {
            await WriteProblemAsync(context, exception);
        }
        catch (JsonException)
        {
            await WriteProblemAsync(context, new QueryException(400, "invalidJson", "送られた JSON が読めません。"));
        }
        catch (BadHttpRequestException)
        {
            await WriteProblemAsync(context, new QueryException(400, "invalidRequest", "送られた値が読めません。"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 問い合わせの中身は書かない（購入履歴と検索の言葉）。型と場所だけを残す
            Console.Error.WriteLine($"API の内部の誤り: {exception.GetType().Name}{Environment.NewLine}{exception.StackTrace}");
            await WriteProblemAsync(context, new QueryException(500, "internal", "API の中で誤りが起きました。"));
        }
    }

    public static Task WriteProblemAsync(HttpContext context, QueryException exception)
    {
        if (context.Response.HasStarted)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = exception.Status;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        var problem = new ProblemAnswer("about:blank", ReasonPhrases.Get(exception.Status), exception.Status, exception.Message, exception.Code);
        return JsonSerializer.SerializeAsync(context.Response.Body, problem, QueryJson.Context.ProblemAnswer);
    }

    /// <summary>答えの JSON（型ごとの書き方は source generator の物。実行時に型を調べない）。</summary>
    public static IResult Json<T>(T value, JsonTypeInfo<T> type) => Results.Json(value, type);
}

/// <summary>HTTP の状態の名前（problem+json の title）。</summary>
internal static class ReasonPhrases
{
    public static string Get(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        503 => "Service Unavailable",
        _ => "Error",
    };
}
