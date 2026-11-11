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

        app.MapPost("/v1/files/lookup", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(FileLocations.Lookup(state.Snapshot, await ReadAsync(request, json.FilesLookupRequest)), json.FilesLookupAnswer))
            .Takes<FilesLookupRequest>().Answers<FilesLookupAnswer>("ハッシュ・場所をまとめて引き、それを持つ商品を返す（GET /v1/files/lookup を合わせて200件まで）");

        app.MapPost("/v1/files/locations", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(FileLocations.Locations(state.Snapshot, await ReadAsync(request, json.FileLocationsRequest)), json.FileLocationsAnswer))
            .Takes<FileLocationsRequest>().Answers<FileLocationsAnswer>("ハッシュから、最後に数えたときの場所を引く（商品のファイルと、商品が決まっていないファイルだけ）");

        app.MapPost("/v1/zip-images/lookup", async (ApiState state, HttpRequest request) =>
        {
            var body = await ReadAsync(request, json.ZipImagesLookupRequest);
            return ApiHost.Json(state.ZipImages.Lookup(state.Snapshot, ZipImageIndex.Normalize(body.Images)), json.ZipImagesLookupAnswer);
        })
            .Takes<ZipImagesLookupRequest>().Answers<ZipImagesLookupAnswer>("画像の CRC-32 と大きさから、同じ画像が入っている zip と商品を引く（1000件まで）");

        app.MapGet("/v1/notifications/archive", (ApiState state) =>
            ApiHost.Json(ArchivedNotifications.Months(state.Snapshot), json.IReadOnlyListString))
            .Answers<IReadOnlyList<string>>("通知の保管庫にある月（YYYY-MM・新しい順）");

        app.MapGet("/v1/notifications/archive/{month}", async (ApiState state, string month, string? name, string? cursor, int? limit, CancellationToken cancellationToken) =>
            ApiHost.Json(await state.ArchivedNotifications.PageAsync(state.Snapshot, month, name, cursor, limit, cancellationToken), json.ArchivedNotificationsPage))
            .Answers<ArchivedNotificationsPage>("保管した通知を1か月ずつ（新しい順・name で商品名を絞る・cursor で続き）。誤り：404 monthNotFound");

        app.MapGet("/v1/unresolved/candidates", (ApiState state, string? hash) =>
            ApiHost.Json(UnresolvedCandidates.All(state.Snapshot, hash), json.IReadOnlyListCandidateSearchRef))
            .Answers<IReadOnlyList<CandidateSearchRef>>("未確定の自動検索の控え（検索した対象ごとの候補。hash で未確定のファイル1つに絞る。推定なので決まった物ではない）");

        app.MapGet("/v1/unity-scans", (ApiState state, string? project) =>
            ApiHost.Json(UnityScans.All(state.Snapshot, project), json.IReadOnlyListUnityScanRef))
            .Answers<IReadOnlyList<UnityScanRef>>("Unity プロジェクトを調べた結果の控え（入っている商品と割合・手で置いた画像・Packages に入っている VPM。savedAt の時点の物で、API は調べ直さない）");
    }
}
