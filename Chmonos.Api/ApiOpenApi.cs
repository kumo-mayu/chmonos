using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Chmonos.Query;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Chmonos.Api;

/// <summary>
/// 仕様書（OpenAPI）を、実際の受け方・答え方に合わせる（2026-10-14。Unity の使い手の試作が、仕様書から作ったコードで困った：API-GAPS 3-3〜3-5）。
/// 仕様書はコードから作るが、作り手は record の引数を全部「必須」と見なし、書き込みの道の誤りの答えも知らない。
/// </summary>
internal static class ApiOpenApi
{
    /// <summary>要求の本文の型（<c>Takes&lt;T&gt;</c> で印を付けた物）。答えの型とは「必須」の決め方が違う。</summary>
    private static readonly ConcurrentDictionary<Type, bool> RequestTypes = new();

    public static void NoteRequestType(Type type) => RequestTypes[type] = true;

    /// <summary>
    /// 「必須」を実際に合わせる。
    /// - 頼まれたときだけ出す欄（null なら書かない：<c>include=detached</c> の <c>detached</c> など）は、答えでも必須にしない
    /// - 要求の型では、null を許す欄と既定値のある欄を必須にしない（使い方の説明は「省けるときは {}」）
    /// </summary>
    public static Task TransformSchema(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var info = context.JsonTypeInfo;
        if (schema.Required is { Count: > 0 } required && info.Kind == System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
        {
            var isRequest = RequestTypes.ContainsKey(info.Type);
            foreach (var property in info.Properties)
            {
                var optional = property.ShouldSerialize is not null
                    || (isRequest && !property.IsRequired
                        && (property.AssociatedParameter is not { } parameter || parameter.HasDefaultValue || parameter.IsNullable || property.IsSetNullable));
                if (optional)
                {
                    required.Remove(property.Name);
                }
            }
        }

        // 送る状態の値は一か所（UnitySendStates）で決めている。仕様書でも列挙し、使い手が値を推し量らなくて済むように
        if ((info.Type == typeof(UnitySendStatus) || info.Type == typeof(UnitySendStep))
            && schema.Properties?.TryGetValue("state", out var state) == true && state is OpenApiSchema stateSchema)
        {
            stateSchema.Enum = UnitySendStates.All.Select(value => (JsonNode)JsonValue.Create(value)!).ToList();
        }

        return Task.CompletedTask;
    }

    /// <summary>本文で読むだけの POST（書き込みではない）。</summary>
    private static readonly HashSet<string> ReadingPosts = new(StringComparer.Ordinal) { "/v1/search", "/v1/search/facets", "/v1/items/batch" };

    /// <summary>
    /// 書き込みの道に、誤りの答え（problem+json）を載せる。読む道は Answers&lt;T&gt; で 400・401・404 を載せているが、書き込みの道は成功だけだった。
    /// 起こりうる code の一覧は、各道の説明と manual/40-api.md「データを変える」にある
    /// </summary>
    public static void AddWriteProblems(OpenApiDocument document)
    {
        foreach (var (path, item) in document.Paths)
        {
            foreach (var (method, operation) in item.Operations ?? [])
            {
                if (method == HttpMethod.Get)
                {
                    continue;
                }

                int[] codes = ReadingPosts.Contains(path) ? [400, 401] : [400, 401, 403, 404, 409, 503];
                operation.Responses ??= [];
                foreach (var code in codes)
                {
                    var key = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (!operation.Responses.ContainsKey(key))
                    {
                        operation.Responses[key] = new OpenApiResponse
                        {
                            Description = code switch
                            {
                                400 => "要求の形が違う（code と detail）",
                                401 => "トークンが無いか違う",
                                403 => "書き込みを許していない・許していないオリジン",
                                404 => "商品・改変などが見つからない",
                                409 => "今は書けない（編集画面で開いている・送っている途中など）",
                                _ => "今は答えられない（引越しの途中など）",
                            },
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/problem+json"] = new() { Schema = new OpenApiSchemaReference("ProblemDetails", document) },
                            },
                        };
                    }
                }
            }
        }
    }
}
