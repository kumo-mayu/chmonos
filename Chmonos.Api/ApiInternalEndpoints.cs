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
    }
}
