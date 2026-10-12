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
/// <param name="AllowedOrigins">CORS を許す住所（自作の Web ページ）。api-settings.json が変われば読み直す。</param>
/// <param name="Watch">保存先のファイルの変化を見張るか（試験では切り、読み直しを直に呼ぶ）。</param>
internal sealed record ApiHostOptions(
    string StoreRoot,
    int Port,
    string Token,
    IReadOnlyList<string> AllowedOrigins,
    Func<DateTimeOffset>? Clock = null,
    bool Watch = true);

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

    /// <summary>CORS を許す住所。api-settings.json が変われば入れ替える（入れ物ごと差し替えるので、照らす途中で揺れない）。</summary>
    public IReadOnlySet<string> AllowedOrigins { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>部分ごとのファイルの印（<see cref="StoreChanges.StampOf"/>）。変わっていない部分は読み直さず、知らせもしない。</summary>
    private readonly Dictionary<StorePart, string?> _stamps = [];

    public async Task LoadAsync(IReadOnlyList<string> origins, CancellationToken cancellationToken = default)
    {
        AllowedOrigins = origins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        StoreFormat = Core.Storage.StoreFormat.StoreVersion(Paths.Root);
        foreach (var part in Enum.GetValues<StorePart>())
        {
            _stamps[part] = StoreChanges.StampOf(Paths, part);
        }

        _snapshot = await QuerySnapshot.LoadAsync(new DataStore(Paths), cancellationToken);
        LoadedAt = Clock();

        // 読み終えていない部分は印を空にしておく（次の見比べで印が違うと見て読み直す。点検31）
        foreach (var part in _snapshot.IncompleteParts)
        {
            _stamps[part] = null;
        }

        Compact();
    }

    /// <summary>
    /// 読み込みの途中で出たごみ（JSON の読み込み・説明文を落とす前の記録）を片付けて、使っていないメモリを OS へ返す。
    /// 2,000 件で専用が 120 → 111MB（2026-10-12 に測った）。全部を読んだときだけ行う（止まる時間は問題にならない）
    /// </summary>
    private static void Compact() => GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

    /// <summary>
    /// 変わった部分を読み直して写しを差し替え、外へ知らせる。部分のファイルの印が変わっていなければ読み直さない。
    /// 呼ぶのは同期の1本の流れ（<see cref="StoreSync"/>）だけ。
    /// </summary>
    public async Task ApplyAsync(StoreChangeSet changes, ChangeFeed feed, CancellationToken cancellationToken = default)
    {
        var effective = new StoreChangeSet();
        if (changes.Overflowed)
        {
            effective.Add(StoreChangeSet.Everything());
        }

        // 新しい印は、読み直しが成功してから確定する（点検30）。先に確定すると、読み直しに失敗したとき
        // 「確かめ済み」になって、2分ごとの見比べでも読み直されず、次にファイルが変わるまで古いまま残る
        var stamps = new Dictionary<StorePart, string?>();
        foreach (var part in changes.Parts)
        {
            if (part is StorePart.Items or StorePart.Images)
            {
                effective.Add(part);
                continue;
            }

            var stamp = StoreChanges.StampOf(Paths, part);
            if (changes.Overflowed || !string.Equals(stamp, _stamps.GetValueOrDefault(part), StringComparison.Ordinal))
            {
                stamps[part] = stamp;
                effective.Add(part);
            }
        }

        foreach (var itemId in changes.ItemIds)
        {
            effective.Add(StorePart.Images, itemId);
        }

        if (effective.Parts.Contains(StorePart.ApiSettings))
        {
            AllowedOrigins = ApiFiles.ReadSettings(Paths).AllowedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Confirm(stamps, StorePart.ApiSettings);
        }

        if (effective.Parts.Contains(StorePart.Format))
        {
            var wasTooNew = TooNew;
            StoreFormat = Core.Storage.StoreFormat.StoreVersion(Paths.Root);
            Confirm(stamps, StorePart.Format);
            if (TooNew && !wasTooNew)
            {
                feed.Publish("formatTooNew", [], [], null, Clock());
            }
        }

        var snapshotParts = effective.Parts.Where(part => part is not (StorePart.Format or StorePart.ApiSettings)).ToList();
        if (snapshotParts.Count == 0)
        {
            return;
        }

        var before = Snapshot;
        var after = await before.UpdateAsync(effective, cancellationToken);
        var itemIds = after.ChangedItemsSince(before);
        var imageIds = after.ImageFiles
            .Where(pair => !before.ImageFiles.TryGetValue(pair.Key, out var old) || !old.SequenceEqual(pair.Value, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key);
        var changedIds = itemIds.Concat(imageIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        // 商品と画像は、中身が本当に変わったときだけ知らせる（ファイルの日時だけ変わった・同じ中身で書き直した、では知らせない）
        var reported = snapshotParts.Where(part => part is not (StorePart.Items or StorePart.Images)).ToList();

        // 改変も中身が変わったときだけ知らせる。読めない改変が残ると、印を確定しないので見比べのたびに読み直す。
        // そのたびに知らせると、外の道具が同じ一覧を2分ごとに取り直し、直近1000件の記録も埋まる（点検32）
        if (reported.Contains(StorePart.Modifications) && SameModifications(before, after))
        {
            reported.Remove(StorePart.Modifications);
        }
        if (itemIds.Count > 0)
        {
            reported.Add(StorePart.Items);
        }

        if (changedIds.Count > itemIds.Count)
        {
            reported.Add(StorePart.Images);
        }

        _snapshot = after;
        LoadedAt = Clock();
        foreach (var part in snapshotParts)
        {
            // 読み終えていない部分（読めなかったファイルを前の写しの記録で埋めた）は確定しない。次の見比べで読み直す（点検31）
            if (after.IncompleteParts.Contains(part))
            {
                _stamps[part] = null;
                continue;
            }

            Confirm(stamps, part);
        }
        if (changes.Overflowed)
        {
            Compact();
            feed.Publish("reloaded", [], [], null, LoadedAt);
            return;
        }

        if (reported.Count > 0)
        {
            feed.Publish("changed", reported.Select(PartName).ToList(), changedIds, null, LoadedAt);
        }
    }

    private static bool SameModifications(QuerySnapshot before, QuerySnapshot after)
    {
        static string Text(QuerySnapshot snapshot)
            => JsonSerializer.Serialize(snapshot.Modifications.Records.OrderBy(record => record.Id, StringComparer.Ordinal).ToList(), Core.Storage.JsonStore.Options);

        return ReferenceEquals(before.Modifications, after.Modifications) || Text(before) == Text(after);
    }

    /// <summary>読み直せた部分の新しい印を確定する。</summary>
    private void Confirm(Dictionary<StorePart, string?> stamps, StorePart part)
    {
        if (stamps.TryGetValue(part, out var stamp))
        {
            _stamps[part] = stamp;
        }
    }

    public static string PartName(StorePart part) => JsonNamingPolicy.CamelCase.ConvertName(part.ToString());
}

/// <summary>
/// API の口（HTTP）を組む。守り（docs/spec/local-api.md「守り」「ブラウザから」）：
/// 待つのは 127.0.0.1 だけ・Host の確かめ（DNS rebinding）・許した住所にだけ CORS・合言葉（health と version 以外）。
/// 問い合わせの中身はログに残さない（購入履歴と検索の言葉）。
/// </summary>
internal static class ApiHost
{
    public const string ApiVersion = "v1";

    /// <summary>合言葉なしで答える URL（動いているか・バージョン・仕様書。どれも保存先の中身は含まない）。</summary>
    private static readonly HashSet<string> OpenPaths = new(StringComparer.Ordinal) { "/v1/health", "/v1/version", "/v1/openapi.json" };

    public static string AppVersion { get; } =
        typeof(ApiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static async Task<(WebApplication App, ApiState State, StoreSync Sync)> BuildAsync(ApiHostOptions options, CancellationToken cancellationToken = default)
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
        await state.LoadAsync(options.AllowedOrigins, cancellationToken);
        var feed = new ChangeFeed();
        var sync = new StoreSync(state, feed, options.Watch);
        // 仕様書（OpenAPI）はコードから作る（手で書くとコードとずれる）。答えの型の書き方は答えと同じ source generator の物
        builder.Services.AddOpenApi("v1", openApi => openApi.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Chmonos API";
            document.Info.Version = ApiVersion;
            document.Info.Description = ServiceText.Notice;

            // 合言葉（Bearer）が要ることを仕様書に載せる。仕様書から作った道具が、合言葉の付け方を機械的に読めるように（点検30）。
            // 合言葉なしで答える URL（OpenPaths）には付けない
            document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "保存先の api.json の token",
            };
            foreach (var (path, item) in document.Paths)
            {
                if (OpenPaths.Contains(path))
                {
                    continue;
                }

                foreach (var operation in item.Operations?.Values ?? Enumerable.Empty<Microsoft.OpenApi.OpenApiOperation>())
                {
                    operation.Security =
                    [
                        new Microsoft.OpenApi.OpenApiSecurityRequirement
                        {
                            [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", document)] = [],
                        },
                    ];
                }
            }

            return Task.CompletedTask;
        }));
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(feed);
        builder.Services.AddSingleton(sync);

        var app = builder.Build();
        var token = Encoding.UTF8.GetBytes("Bearer " + options.Token);
        app.Use((context, next) => GuardAsync(context, next, token, state));
        ApiEndpoints.Map(app);
        app.MapOpenApi("/v1/openapi.json");

        // 止まるときは SSE の聞き手に知らせてから、見張りを止める
        app.Lifetime.ApplicationStopping.Register(() =>
        {
            feed.Publish("stopping", [], [], null, state.Clock());
            sync.Dispose();
        });
        return (app, state, sync);
    }

    private static async Task GuardAsync(HttpContext context, RequestDelegate next, byte[] token, ApiState state)
    {
        var origins = state.AllowedOrigins;
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

            // ページの fetch から ETag を読めるようにする（見せないと、条件付きで取り直せない。点検31）
            context.Response.Headers.AccessControlExposeHeaders = "ETag";
            context.Response.Headers.Vary = "Origin";
            if (HttpMethods.IsOptions(request.Method))
            {
                context.Response.Headers.AccessControlAllowMethods = "GET, POST";
                context.Response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type, Last-Event-ID, If-None-Match";
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

    /// <summary>
    /// 答えの JSON（型ごとの書き方は source generator の物。実行時に型を調べない）。中身から ETag を付け、
    /// If-None-Match が同じなら 304 を返す（外の道具が変わったかを安く確かめられるように。docs/spec/local-api.md「共通の決まり」・点検30）。
    /// </summary>
    public static IResult Json<T>(T value, JsonTypeInfo<T> type) => new TaggedJson<T>(value, type);

    /// <summary>中身の SHA-256 の頭16バイトを ETag にする（同じ答えなら同じ印。include や検索の条件が違えば中身が違うので印も違う）。</summary>
    private sealed class TaggedJson<T>(T value, JsonTypeInfo<T> type) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
            var tag = $"\"{Convert.ToHexString(SHA256.HashData(bytes), 0, 16).ToLowerInvariant()}\"";
            context.Response.Headers.ETag = tag;

            // 手元の写しを使う前に、毎回確かめてもらう（写しは保存先の変化で変わる）
            context.Response.Headers.CacheControl = "no-cache";
            if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)) && Matches(context.Request.Headers.IfNoneMatch, tag))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }

        /// <summary>
        /// If-None-Match の比べ方（弱い比べ方。RFC 9110 §13.1.2）：<c>W/</c> の付いた印も中身が同じなら同じと見る。<c>*</c> は何にでも当たる。
        /// 304 を返すのは GET と HEAD だけ（POST の検索・batch には条件付きの取り直しを用意していない。点検31）。
        /// </summary>
        private static bool Matches(Microsoft.Extensions.Primitives.StringValues header, string tag)
        {
            foreach (var value in header)
            {
                foreach (var one in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (one == "*" || (one.StartsWith("W/", StringComparison.Ordinal) ? one[2..] : one) == tag)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
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
