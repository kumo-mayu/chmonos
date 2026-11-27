using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 外のソフトの情報（拡張データ）の宣言（docs/spec/local-api-extensions.md「URL と答えの形」）。
/// 読むのは許したソフトならどれも（api.json のトークンも）、書くのは名前空間の持ち主のソフトだけ。
/// 宣言は名前空間ごとに1つで、丸ごと置き換える。確かめは錠の中で今の宣言に当てる（<see cref="UiCommand.ChangeExtensionSchema"/>）
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapExtensions(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapGet("/v1/extensions", (ApiState state, HttpContext context) =>
        {
            var caller = ApiCallerInfo.Of(context).Client;
            var list = state.Store.Extensions.LoadAllSchemas()
                .Select(pair => SchemaRef(pair.Namespace, pair.Schema, showHidden: false, caller))
                .ToList();
            return ApiHost.Json(new ExtensionSchemaList(list), json.ExtensionSchemaList);
        })
            .Answers<ExtensionSchemaList>("ソフトが宣言した情報の欄の一覧（名前空間・表示名・欄・ソフトの名前。宣言から外した欄は入れない）");

        app.MapGet("/v1/extensions/{ns}", (ApiState state, HttpContext context, string ns, string? include) =>
        {
            var name = RequireNamespace(ns);
            var schema = state.Store.Extensions.LoadSchema(name)
                ?? throw QueryException.NotFound("extensionNotFound", $"名前空間「{name}」の宣言はありません。");
            var caller = ApiCallerInfo.Of(context).Client;
            return ApiHost.Json(SchemaRef(name, schema, ShowHidden(include, caller, name), caller), json.ExtensionSchemaRef);
        })
            .Answers<ExtensionSchemaRef>("ソフトが宣言した情報の欄（1つ）。持ち主のソフトは ?include=hidden で宣言から外した欄も読める");

        app.MapPut("/v1/extensions/{ns}/schema", async (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string ns) =>
        {
            var name = RequireNamespace(ns);
            var owner = RequireOwner(writer, context, name);
            var body = await ReadAsync(context.Request, json.ExtensionSchemaRequest);
            var (fields, label, invalid) = ExtensionSchemas.Check(body.Label, body.Fields);
            if (invalid is not null)
            {
                throw Problem(invalid);
            }

            // 型を変えたかは錠の中で今の宣言に当てて決める（写しや錠の外で読んだ宣言で決めると、重なった置き換えを見落とす）
            ExtensionProblem? refused = null;
            var command = new UiCommand.ChangeExtensionSchema(name, current =>
            {
                var (file, problem) = ExtensionSchemas.Replace(current, label, owner.Name, fields!);
                refused = problem;
                return file;
            });
            var (_, result) = await writer.RunWithResultAsync(command, "extensions", _ => ([], []), context.RequestAborted);
            if (refused is not null)
            {
                throw Problem(refused);
            }

            var written = (result as CommandResult.ExtensionSchemaChanged)?.Schema
                ?? throw new QueryException(500, "commandFailed", "宣言を書けませんでした。");

            // ほかのソフトが宣言を読み直せるように知らせる（spec「宣言を変えた時」）
            feed.Publish("extensions", [name, "schema"], [], null, state.Clock());
            return ApiHost.Json(SchemaRef(name, written, showHidden: true, owner), json.ExtensionSchemaRef);
        })
            .Takes<ExtensionSchemaRequest>()
            .Answers<ExtensionSchemaRef>("情報の欄の宣言を丸ごと置き換える（持ち主のソフトだけ。外した欄の値は残して隠す・型を変えると 409 fieldTypeChanged。書き込み extensions）")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>URL の名前空間を決まりの形にする（大文字は小文字にする）。合わなければ 400。</summary>
    private static string RequireNamespace(string ns)
        => ExtensionNamespace.Normalize(ns)
            ?? throw QueryException.Invalid("invalidNamespace", "名前空間の形が合いません（3〜40字の英小文字・数字・ハイフンで、英小文字で始まる名前）。");

    /// <summary>
    /// 書いてよいか（書き込みの種類 extensions・この名前空間の持ち主か）を確かめ、持ち主のソフトを返す。
    /// api.json のトークンは <see cref="ApiWriter.Guard"/> が断る（名前空間を持たない）
    /// </summary>
    private static ApiClientRecord RequireOwner(ApiWriter writer, HttpContext context, string ns)
    {
        writer.Guard(context, ApiScopes.Extensions);
        var client = ApiCallerInfo.Of(context).Client!;
        return string.Equals(client.Namespace, ns, StringComparison.Ordinal)
            ? client
            : throw new QueryException(403, "notOwner", $"名前空間「{ns}」には、このソフトは書けません（書けるのは、許しを求めた時に名乗った名前空間だけです）。");
    }

    /// <summary>外した欄を見せるのは、持ち主のソフトが頼んだ時だけ（ほかのソフトの答えに出さない。spec「宣言を変えた時」）。</summary>
    private static bool ShowHidden(string? include, ApiClientRecord? caller, string ns)
        => caller is not null && string.Equals(caller.Namespace, ns, StringComparison.Ordinal)
            && (include ?? string.Empty).Split(',', StringSplitOptions.TrimEntries).Contains("hidden", StringComparer.Ordinal);

    private static ExtensionSchemaRef SchemaRef(string ns, ExtensionSchemaFile schema, bool showHidden, ApiClientRecord? caller)
        => new(
            ns,
            schema.Label,
            schema.Software,
            caller is not null && string.Equals(caller.Namespace, ns, StringComparison.Ordinal),
            showHidden ? schema.Fields : [.. schema.Fields.Select(field => field.HiddenColumns is null ? field : field with { HiddenColumns = null })],
            showHidden ? schema.Hidden : null);

    private static QueryException Problem(ExtensionProblem problem) => new(problem.Status, problem.Code, problem.Message);
}
