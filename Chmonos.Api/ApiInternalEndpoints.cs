using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 今まで API が出していなかった内部の情報を読む道（2026-11-27 に出すと決めた物。docs/spec/local-api-shapes.md「出す物を決めた」）。
/// どれも読むだけで、読む許しはどのトークンにもある。答えは写しから組み、写しは1回の問い合わせで1回だけ取る。
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapInternal(WebApplication app)
    {
        var json = QueryJson.Context;

        app.MapGet("/v1/recent", (ApiState state, string? cursor, int? limit) =>
            ApiHost.Json(RecentList.Page(state.Snapshot, cursor, limit), json.RecentPage))
            .Answers<RecentPage>("「最近」の足跡（商品ごとの取り込んだ・使った・開いた日時。商品 ID の順・cursor で続き）");

        app.MapGet("/v1/folders", (ApiState state, string? cursor, int? limit) =>
            ApiHost.Json(FolderList.Page(state.Snapshot, cursor, limit), json.FolderPage))
            .Answers<FolderPage>("商品として登録したフォルダ（全部の商品から。場所の順・state は取り込みが見た present／missing・cursor で続き）");

        app.MapGet("/v1/excluded", (ApiState state, string? cursor, int? limit) =>
            ApiHost.Json(ExcludedList.Page(state.Snapshot, cursor, limit), json.ExcludedPage))
            .Answers<ExcludedPage>("管理から外したファイル（ハッシュ・場所の全部・日時・理由。外した日時の新しい順・cursor で続き。戻すのは画面で）");

        app.MapGet("/v1/unresolved", (ApiState state, string? cursor, int? limit) =>
            ApiHost.Json(state.UnresolvedList.Page(state.Snapshot, cursor, limit), json.UnresolvedPage))
            .Answers<UnresolvedPage>("商品が決まっていないファイル（場所・大きさ・推定の候補 ID・BOOTH に無かった記録。場所の順・cursor で続き。自動検索の控えは /v1/unresolved/candidates?hash=。決めるのは画面で）");
    }
}
