using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 手元に控えた情報を読む道（plan 32・docs/spec/local-api-stored.md）。どれも読むだけで、読む許しはどのトークンにもある（書き込みの種類を問わない）。
/// 控えはアプリが書く物で、API は控えを作らない・書き足さない（zip を解かない・BOOTH に問い合わせない）。
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapStored(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapGet("/v1/items/{id}/unitypackage-assets", (ApiState state, string id) =>
        {
            var snapshot = state.Snapshot;
            return ApiHost.Json(PackageContents.OfItem(snapshot, FindItem(snapshot, id)), json.IReadOnlyListPackageContentsRef);
        })
            .Answers<IReadOnlyList<PackageContentsRef>>("商品のファイルの中の unitypackage と、その中身（GUID と Unity 上のパス。まだ読んでいなければ assets が null）");

        app.MapPost("/v1/unitypackages/lookup", async (ApiState state, HttpRequest request) =>
        {
            var body = await ReadAsync(request, json.GuidLookupRequest);
            var guids = PackageContents.NormalizeGuids(body.Guids);
            return ApiHost.Json(state.PackageGuids.Lookup(state.Snapshot, guids), json.GuidLookupAnswer);
        })
            .Takes<GuidLookupRequest>().Answers<GuidLookupAnswer>("GUID をまとめて引き、入っている unitypackage と商品を返す（Missing の参照から入れ直す商品を探す）");
    }
}
