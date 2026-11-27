using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 外のソフトの情報の値とメモ欄（docs/spec/local-api-extensions-api.md「URL と答えの形」）。
/// 付け先は商品・改変・ファイル・ショップ・ソフトごとの全体の5つ。読むのは許したソフトならどれも（メモ欄は持ち主だけ）、書くのは持ち主だけ。
/// 書き込みは <see cref="UiCommand.ChangeExtensionValues"/>・<see cref="UiCommand.ChangeExtensionMemo"/> を通し、確かめ（宣言・If-Match・付け先が在るか）は錠の中で当てる
/// </summary>
internal static partial class ApiEndpoints
{
    /// <summary>
    /// 書き込みの体の上限。メモ欄の上限（64KB）に、JSON の書き方の違い（\u 書き・空白）の余裕を4倍見た。
    /// これを超える体は、確かめる前に読み切らずに断る（大きな体を全部読んでから 413 にしない）
    /// </summary>
    private const int ExtensionBodyBytes = ExtensionLimits.MemoBytes * 4;

    private static void MapExtensionValues(WebApplication app)
    {
        // 付け先ごとの URL。鍵は写しで引いた物自身の鍵に直してから場所を組む（URL の文字のまま組まない）
        (string Prefix, Func<QuerySnapshot, string, ExtensionTarget> Resolve)[] targets =
        [
            ("/v1/items/{key}/extensions/{ns}", ResolveItem),
            ("/v1/modifications/{key}/extensions/{ns}", ResolveModification),
            ("/v1/files/{key}/extensions/{ns}", ResolveFile),
            ("/v1/shops/{key}/extensions/{ns}", ResolveShop),
        ];

        foreach (var (prefix, resolve) in targets)
        {
            MapValueRoutes(app, prefix, prefix + "/memo", (snapshot, context) => resolve(snapshot, (string)context.Request.RouteValues["key"]!));
        }

        // 全体：鍵は名前空間そのもの
        MapValueRoutes(app, "/v1/extensions/{ns}/data", "/v1/extensions/{ns}/memo", (_, _) => ExtensionTarget.Global);
    }

    private static void MapValueRoutes(WebApplication app, string valuePath, string memoPath, Func<QuerySnapshot, HttpContext, ExtensionTarget> resolve)
    {
        var json = QueryJson.Context;

        app.MapGet(valuePath, (ApiState state, HttpContext context, string ns, string? include) =>
        {
            var name = RequireNamespace(ns);
            var target = resolve(state.Snapshot, context);
            var caller = ApiCallerInfo.Of(context).Client;
            var showHidden = ShowHidden(include, caller, name);
            var file = state.Store.Extensions.LoadValues(name, target);
            var values = ExtensionValues.Visible(state.Store.Extensions.LoadSchema(name), file, showHidden);
            return ApiHost.Json(new ExtensionValuesRef(name, target.Kind, target.Key, values), json.ExtensionValuesRef, ValueTag(file, showHidden));
        })
            .Answers<ExtensionValuesRef>("ソフトの情報の値（無ければ空。持ち主のソフトは ?include=hidden で宣言から外した欄も読める）");

        app.MapPut(valuePath, (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string ns)
            => WriteValuesAsync(state, writer, feed, context, ns, resolve, replace: true))
            .Takes<ExtensionValuesRequest>()
            .Answers<ExtensionValuesRef>("ソフトの情報の値を、今の宣言の欄ごと置き換える（持ち主だけ。宣言から外した欄の値は残す。If-Match で古い値への上書きを 412 で断る。書き込み extensions）")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        app.MapPatch(valuePath, (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string ns)
            => WriteValuesAsync(state, writer, feed, context, ns, resolve, replace: false))
            .Takes<ExtensionValuesRequest>()
            .Answers<ExtensionValuesRef>("ソフトの情報の値を、名指しした欄だけ書く（null で消す。持ち主だけ。書き込み extensions）")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        app.MapDelete(valuePath, async (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string ns) =>
        {
            var name = RequireNamespace(ns);
            RequireOwner(writer, context, name);
            var target = resolve(state.Snapshot, context);
            var ifMatch = context.Request.Headers.IfMatch.ToString();
            await ChangeValuesAsync(state, writer, context, name, target, (_, current) =>
                MatchesValue(ifMatch, current) ? (new ExtensionValueFile(), null) : (null, ExtensionValues.PreconditionFailed()));
            PublishValues(state, feed, name, target);
            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("ソフトの情報の値を全部消す（宣言から外した欄の値も。メモ欄は残す。持ち主だけ。書き込み extensions）");

        app.MapGet(memoPath, (ApiState state, HttpContext context, string ns) =>
        {
            var name = RequireNamespace(ns);
            RequireMemoReader(context, name);
            var target = resolve(state.Snapshot, context);
            var file = state.Store.Extensions.LoadMemo(name, target);
            return ApiHost.Json(new ExtensionMemoRef(name, target.Kind, target.Key, file?.Memo), json.ExtensionMemoRef, ExtensionValues.TagOf(file));
        })
            .Answers<ExtensionMemoRef>("メモ欄（持ち主のソフトだけが読める。無ければ memo は null）")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPut(memoPath, async (ApiState state, ApiWriter writer, HttpContext context, string ns) =>
        {
            var name = RequireNamespace(ns);
            RequireOwner(writer, context, name);
            var target = resolve(state.Snapshot, context);
            var body = await ReadLimitedAsync(context, json.ExtensionMemoRequest);
            var memo = body.Memo is { ValueKind: not System.Text.Json.JsonValueKind.Null } given ? given.Clone() : (System.Text.Json.JsonElement?)null;
            if (memo is { } some && System.Text.Encoding.UTF8.GetByteCount(some.GetRawText()) > ExtensionLimits.MemoBytes)
            {
                throw Problem(ExtensionValues.TooLarge($"メモ欄が大きすぎます（上限 {ExtensionLimits.MemoBytes:N0} バイト）。"));
            }

            var written = await ChangeMemoAsync(state, writer, context, name, target, memo);
            return ApiHost.Json(new ExtensionMemoRef(name, target.Kind, target.Key, written.Memo), json.ExtensionMemoRef, ExtensionValues.TagOf(written));
        })
            .Takes<ExtensionMemoRequest>()
            .Answers<ExtensionMemoRef>("メモ欄を書く（中身は自由な JSON。null で消す。持ち主だけ。If-Match で古いメモへの上書きを 412 で断る。書き込み extensions）")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        app.MapDelete(memoPath, async (ApiState state, ApiWriter writer, HttpContext context, string ns) =>
        {
            var name = RequireNamespace(ns);
            RequireOwner(writer, context, name);
            var target = resolve(state.Snapshot, context);
            await ChangeMemoAsync(state, writer, context, name, target, null);
            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .WithSummary("メモ欄を消す（持ち主だけ。書き込み extensions）");
    }

    private static async Task<IResult> WriteValuesAsync(
        ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string ns, Func<QuerySnapshot, HttpContext, ExtensionTarget> resolve, bool replace)
    {
        var name = RequireNamespace(ns);
        RequireOwner(writer, context, name);
        var target = resolve(state.Snapshot, context);
        var body = await ReadLimitedAsync(context, QueryJson.Context.ExtensionValuesRequest);
        var input = body.Values ?? new Dictionary<string, System.Text.Json.JsonElement>();
        var ifMatch = context.Request.Headers.IfMatch.ToString();

        ExtensionSchemaFile? schema = null;
        var written = await ChangeValuesAsync(state, writer, context, name, target, (currentSchema, current) =>
        {
            schema = currentSchema;
            if (!MatchesValue(ifMatch, current))
            {
                return (null, ExtensionValues.PreconditionFailed());
            }

            var (values, problem) = ExtensionValues.Apply(currentSchema, current, input, replace);
            return values is null ? (null, problem) : (new ExtensionValueFile { Values = values }, null);
        });

        PublishValues(state, feed, name, target);
        return ApiHost.Json(
            new ExtensionValuesRef(name, target.Kind, target.Key, ExtensionValues.Visible(schema, written, showHidden: false)),
            QueryJson.Context.ExtensionValuesRef,
            ValueTag(written, showHidden: false));
    }

    /// <summary>
    /// 値の命令を流す。付け先が在るか・変え方の確かめは錠の中で当て、断った理由は関数の外へ控えて答えにする。
    /// 商品・改変が消えた後の書き込みは 404（消す側は記録を消してから同じ錠で値を消すので、ここで見ればどちらが先でも値が残らない）
    /// </summary>
    private static async Task<ExtensionValueFile> ChangeValuesAsync(
        ApiState state, ApiWriter writer, HttpContext context, string ns, ExtensionTarget target,
        Func<ExtensionSchemaFile?, ExtensionValueFile?, (ExtensionValueFile? File, ExtensionProblem? Problem)> change)
    {
        ExtensionProblem? refused = null;
        var command = new UiCommand.ChangeExtensionValues(ns, target, (schema, current) =>
        {
            if (!state.Store.Extensions.TargetExists(target))
            {
                refused = new ExtensionProblem(404, "targetNotFound", $"付け先 {target} は見つかりません。");
                return null;
            }

            var (file, problem) = change(schema, current);
            refused = problem;
            return file;
        });
        var (_, result) = await writer.RunWithResultAsync(command, "extensions", _ => ([], []), context.RequestAborted);
        if (refused is not null)
        {
            throw Problem(refused);
        }

        return (result as CommandResult.ExtensionValuesChanged)?.Values
            ?? throw new QueryException(500, "commandFailed", "ソフトの情報を書けませんでした。");
    }

    private static async Task<ExtensionMemoFile> ChangeMemoAsync(
        ApiState state, ApiWriter writer, HttpContext context, string ns, ExtensionTarget target, System.Text.Json.JsonElement? memo)
    {
        var ifMatch = context.Request.Headers.IfMatch.ToString();
        ExtensionProblem? refused = null;
        var command = new UiCommand.ChangeExtensionMemo(ns, target, current =>
        {
            if (!state.Store.Extensions.TargetExists(target))
            {
                refused = new ExtensionProblem(404, "targetNotFound", $"付け先 {target} は見つかりません。");
                return null;
            }

            if (!ExtensionValues.Matches(ifMatch, ExtensionValues.TagOf(current)))
            {
                refused = ExtensionValues.PreconditionFailed();
                return null;
            }

            return new ExtensionMemoFile { Memo = memo };
        });
        var (_, result) = await writer.RunWithResultAsync(command, "extensions", _ => ([], []), context.RequestAborted);
        if (refused is not null)
        {
            throw Problem(refused);
        }

        return (result as CommandResult.ExtensionMemoChanged)?.Memo
            ?? throw new QueryException(500, "commandFailed", "メモ欄を書けませんでした。");
    }

    /// <summary>
    /// 値の印。隠した欄を入れた答えは中身が違うので、印の後ろに <c>h</c> を付けて分ける（同じ印のままだと、If-None-Match で
    /// 違う中身に 304 を返す）。If-Match はどちらの印でも同じ値に当てる（<see cref="MatchesValue"/>）
    /// </summary>
    private static string ValueTag(ExtensionValueFile? file, bool showHidden)
    {
        var tag = ExtensionValues.TagOf(file);
        return showHidden ? tag.Insert(tag.Length - 1, "h") : tag;
    }

    private static bool MatchesValue(string? ifMatch, ExtensionValueFile? current)
        => ExtensionValues.Matches(ifMatch, ValueTag(current, showHidden: false)) || ExtensionValues.Matches(ifMatch, ValueTag(current, showHidden: true));

    /// <summary>
    /// ほかのソフトが気付けるように知らせる（spec「誰が読めるか」）。parts は宣言の知らせ <c>[ns, "schema"]</c> にそろえ、
    /// 付け先の種類と鍵を続ける（全体は <c>[ns, "global"]</c>）。商品なら itemIds にも載せる（商品の知らせを見ている道具が拾えるように）。
    /// メモ欄は持ち主しか読めないので流さない
    /// </summary>
    private static void PublishValues(ApiState state, ChangeFeed feed, string ns, ExtensionTarget target)
        => feed.Publish(
            "extensions",
            target.Kind == ExtensionTarget.GlobalKind ? [ns, target.Kind] : [ns, target.Kind, target.Key],
            target.Kind == ExtensionTarget.Items ? [target.Key] : [],
            null,
            state.Clock());

    /// <summary>メモ欄は持ち主のソフトだけが読める（spec「メモ欄」：秘密を入れてもほかのソフトへ漏れない）。api.json のトークンは名前空間を持たない。</summary>
    private static void RequireMemoReader(HttpContext context, string ns)
    {
        if (!string.Equals(ApiCallerInfo.Of(context).Client?.Namespace, ns, StringComparison.Ordinal))
        {
            throw new QueryException(403, "notOwner", $"名前空間「{ns}」のメモ欄は、持ち主のソフトだけが読めます。");
        }
    }

    private static async Task<T> ReadLimitedAsync<T>(HttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (context.Request.ContentLength > ExtensionBodyBytes)
        {
            throw Problem(ExtensionValues.TooLarge($"送られた体が大きすぎます（上限 {ExtensionBodyBytes:N0} バイト）。"));
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
        {
            if (buffer.Length + read > ExtensionBodyBytes)
            {
                throw Problem(ExtensionValues.TooLarge($"送られた体が大きすぎます（上限 {ExtensionBodyBytes:N0} バイト）。"));
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize(buffer, type)
                ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");
        }
        catch (System.Text.Json.JsonException)
        {
            throw QueryException.Invalid("invalidJson", "送られた JSON を読めません。");
        }
    }

    private static ExtensionTarget ResolveItem(QuerySnapshot snapshot, string key)
        => ExtensionTarget.Item(key) is { } target && snapshot.ItemsById.ContainsKey(target.Key)
            ? target
            : throw QueryException.NotFound("itemNotFound", $"商品 {key} は見つかりません。");

    private static ExtensionTarget ResolveModification(QuerySnapshot snapshot, string key)
        => ExtensionTarget.Modification(key) is { } target && Catalogs.Modification(snapshot, target.Key, includeDetached: true) is not null
            ? target
            : throw QueryException.NotFound("modificationNotFound", $"改変 {key} は見つかりません。");

    private static ExtensionTarget ResolveFile(QuerySnapshot snapshot, string key)
        => ExtensionTarget.File(key) is { } target && snapshot.KnowsFileHash(target.Key)
            ? target
            : throw QueryException.NotFound("fileNotFound", $"ハッシュ {key} のファイルは記録にありません。");

    private static ExtensionTarget ResolveShop(QuerySnapshot snapshot, string key)
        => ExtensionTarget.Shop(Catalogs.Shop(snapshot, key).Key) ?? throw QueryException.NotFound("shopNotFound", $"ショップ {key} には情報を置けません。");
}
