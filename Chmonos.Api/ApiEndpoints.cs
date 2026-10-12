using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Storage;
using Chmonos.Query;
using Chmonos.Query.Search;

namespace Chmonos.Api;

/// <summary>URL と答えをつなぐ（docs/spec/local-api-shapes.md の URL の表）。答えの中身は Chmonos.Query が作る。</summary>
internal static partial class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapGet("/v1/health", (ApiState state) => ApiHost.Json(
            new HealthAnswer(state.TooNew ? "tooNew" : "ok", state.LoadedAt, state.Snapshot.Items.Count, state.Snapshot.FailedItemIds),
            json.HealthAnswer));

        app.MapGet("/v1/version", (ApiState state) => ApiHost.Json(
            new VersionAnswer(ApiHost.ApiVersion, ApiHost.AppVersion, state.StoreFormat, StoreFormat.Current, ServiceText.Notice),
            json.VersionAnswer));

        app.MapGet("/v1/display-settings", (ApiState state) => ApiHost.Json(Catalogs.DisplaySettings(state.Snapshot), json.DisplaySettingsRef));

        // 商品
        app.MapGet("/v1/items", (ApiState state, string? cursor, int? limit, string? sort, bool? includeHidden, bool? includeAdult) =>
            ApiHost.Json(
                SearchEngine.Search(state.Snapshot, new SearchRequest
                {
                    Cursor = cursor,
                    Limit = limit,
                    Sort = ParseSort(sort),
                    IncludeHidden = includeHidden ?? true,
                    IncludeAdult = includeAdult ?? true,
                }, state.Clock()),
                json.SearchPage));

        app.MapGet("/v1/items/{id}", (ApiState state, string id, string? include) =>
            ApiHost.Json(ItemMapper.Detail(FindItem(state, id), state.Snapshot, Includes.ParseItem(include)), json.ItemDetail));

        app.MapPost("/v1/items/batch", async (ApiState state, HttpRequest request) =>
        {
            var body = await ReadAsync(request, json.ItemsBatchRequest);
            var includes = Includes.ParseItem(body.Include);
            var ids = body.Ids ?? [];
            if (ids.Count > SearchEngine.MaxLimit)
            {
                throw QueryException.Invalid("invalidLimit", $"一度に引けるのは {SearchEngine.MaxLimit} 件までです。");
            }

            var found = ids.Where(state.Snapshot.ItemsById.ContainsKey).Distinct(StringComparer.Ordinal)
                .Select(id => ItemMapper.Detail(state.Snapshot.ItemsById[id], state.Snapshot, includes)).ToList();
            var missing = ids.Where(id => !state.Snapshot.ItemsById.ContainsKey(id)).ToList();
            return ApiHost.Json(new ItemsBatchAnswer(found, missing), json.ItemsBatchAnswer);
        });

        app.MapGet("/v1/items/{id}/modifications", (ApiState state, string id, string? include) =>
        {
            FindItem(state, id);
            return ApiHost.Json(Catalogs.ModificationsUsing(state.Snapshot, id, Includes.ParseFlag(include, "detached")), json.IReadOnlyListModificationRef);
        });

        app.MapGet("/v1/items/{id}/notifications", (ApiState state, string id) =>
        {
            FindItem(state, id);
            return ApiHost.Json(Catalogs.Notifications(state.Snapshot, id), json.IReadOnlyListNotificationRef);
        });

        // 検索
        app.MapPost("/v1/search", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(SearchEngine.Search(state.Snapshot, await ReadAsync(request, json.SearchRequest), state.Clock()), json.SearchPage));

        app.MapGet("/v1/search/conditions", () => ApiHost.Json(Catalogs.ConditionKinds(), json.IReadOnlyListConditionKindRef));

        app.MapPost("/v1/search/facets", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(SearchEngine.Facets(state.Snapshot, await ReadAsync(request, json.FacetsRequest), state.Clock()), json.FacetsAnswer));

        // アバター・分類・ショップ
        app.MapGet("/v1/avatars", (ApiState state) => ApiHost.Json(Catalogs.Avatars(state.Snapshot), json.IReadOnlyListAvatarEntryRef));

        app.MapGet("/v1/avatars/{id}", (ApiState state, string id) =>
            ApiHost.Json(
                Catalogs.Avatars(state.Snapshot).FirstOrDefault(avatar => avatar.Id == id)
                    ?? throw QueryException.NotFound("avatarNotFound", $"登録簿にないアバターです: {id}"),
                json.AvatarEntryRef));

        app.MapGet("/v1/avatar-bases", (ApiState state, string? include) =>
            ApiHost.Json(Catalogs.AvatarBases(state.Snapshot, Includes.ParseFlag(include, "rejected")), json.IReadOnlyListAvatarBaseRef));

        app.MapGet("/v1/user-tags", (ApiState state) => ApiHost.Json(Catalogs.UserTags(state.Snapshot), json.IReadOnlyListUserTagTopRef));
        app.MapGet("/v1/attributes", (ApiState state) => ApiHost.Json(Catalogs.Attributes(state.Snapshot), json.IReadOnlyListAttributeEntryRef));
        app.MapGet("/v1/categories", (ApiState state) => ApiHost.Json(Catalogs.Categories(state.Snapshot), json.IReadOnlyListCategoryEntryRef));
        app.MapGet("/v1/booth-tags", (ApiState state) => ApiHost.Json(Catalogs.BoothTags(state.Snapshot), json.IReadOnlyListCountRef));
        app.MapGet("/v1/shops", (ApiState state) => ApiHost.Json(Catalogs.Shops(state.Snapshot), json.IReadOnlyListShopEntryRef));
        app.MapGet("/v1/shops/{key}", (ApiState state, string key) => ApiHost.Json(Catalogs.Shop(state.Snapshot, key), json.ShopEntryRef));

        // ファイル・Unity
        app.MapGet("/v1/files/lookup", (ApiState state, string? path, string? hash) =>
            ApiHost.Json(Catalogs.LookupFiles(state.Snapshot, path, hash), json.IReadOnlyListFileOwnerRef));

        app.MapGet("/v1/unitypackages", (ApiState state, string? avatar) =>
            ApiHost.Json(Catalogs.UnityPackages(state.Snapshot, avatar), json.IReadOnlyListUnityPackageEntryRef));

        // 改変・通知
        app.MapGet("/v1/modifications", (ApiState state, string? include) =>
            ApiHost.Json(Catalogs.Modifications(state.Snapshot, Includes.ParseFlag(include, "detached")), json.IReadOnlyListModificationRef));

        app.MapGet("/v1/modifications/{id}", (ApiState state, string id, string? include) =>
            ApiHost.Json(Catalogs.Modification(state.Snapshot, id, Includes.ParseFlag(include, "detached")), json.ModificationRef));

        app.MapGet("/v1/unity-projects", (ApiState state) => ApiHost.Json(Catalogs.UnityProjects(state.Snapshot), json.IReadOnlyListUnityProjectRef));
        app.MapGet("/v1/notifications", (ApiState state) => ApiHost.Json(Catalogs.Notifications(state.Snapshot, null), json.IReadOnlyListNotificationRef));

        // 画像の本体（ブラウザの画面のため）。返すのは写しが知っている画像の名前だけ（送られた名前で場所を組まない）
        app.MapGet("/v1/images/{itemId}/{name}", (ApiState state, string itemId, string name) =>
        {
            FindItem(state, itemId);
            var file = (state.Snapshot.ImageFiles.GetValueOrDefault(itemId) ?? [])
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            return file is not null && File.Exists(file)
                ? Results.File(file, ContentTypeOf(file))
                : throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
        });

        app.MapGet("/v1/modifications/{id}/images/{name}", (ApiState state, string id, string name) =>
        {
            var record = state.Snapshot.Modifications.Records.FirstOrDefault(record => record.Id == id)
                ?? throw QueryException.NotFound("modificationNotFound", $"改変が見つかりません: {id}");
            var image = record.Images.FirstOrDefault(image => string.Equals(image.FileName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
            var file = Path.Combine(state.Paths.ModificationImagesDir(record.Id), image.FileName);
            return SafeName().IsMatch(image.FileName) && File.Exists(file)
                ? Results.File(file, ContentTypeOf(file))
                : throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
        });
    }

    private static Core.Models.ItemRecord FindItem(ApiState state, string id)
        => state.Snapshot.ItemsById.GetValueOrDefault(id) ?? throw QueryException.NotFound("itemNotFound", $"商品が見つかりません: {id}");

    private static async Task<T> ReadAsync<T>(HttpRequest request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
        => await JsonSerializer.DeserializeAsync(request.Body, type)
            ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");

    /// <summary><c>sort=wishCount:desc,name</c> の形。</summary>
    private static IReadOnlyList<SortRequest>? ParseSort(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split(':', 2) is [var field, var order] ? new SortRequest(field, order) : new SortRequest(part))
                .ToList();

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>画像のファイル名として受ける形（区切り・<c>..</c> を含まない）。記録を手で書き換えられても保存先の外を返さない。</summary>
    [GeneratedRegex(@"\A[\w\-]+(\.[\w\-]+)*\z")]
    private static partial Regex SafeName();
}
