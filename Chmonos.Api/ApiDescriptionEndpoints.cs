using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 商品の説明文（<c>GET /v1/items/{id}/description</c>。Web の API-GAPS G7）。
/// 一覧や詳細に入れないのは、説明文が長く（1件で数十KB になる物もある）、写しにも持たない物だから（docs/spec/local-api.md のメモリの計測）。
/// 頼まれた1件だけをファイルから読む
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapDescription(WebApplication app)
    {
        var json = QueryJson.Context;

        // 誤り：404 itemNotFound（商品が無い）・503 descriptionUnreadable（ほかのアプリが説明のファイルを開いていて読めない）
        app.MapGet("/v1/items/{id}/description", async (ApiState state, string id, CancellationToken cancellationToken) =>
        {
            var snapshot = state.Snapshot;
            var item = FindItem(snapshot, id);
            ItemDescription description;
            try
            {
                description = await Descriptions.OfAsync(item, snapshot, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 読めないのを「説明が無い」（html: null）と見分けられるよう、誤りで返す。置き換えの一瞬なら、すぐ読み直せば読める
                throw new QueryException(503, "descriptionUnreadable", "説明文のファイルを読めませんでした。少し待ってから読み直してください。");
            }

            return ApiHost.Json(description, json.ItemDescription);
        })
            .Answers<ItemDescription>("商品の説明文（短い説明・見出しごとの文・表示用の HTML）。誤り：404 itemNotFound・503 descriptionUnreadable")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
