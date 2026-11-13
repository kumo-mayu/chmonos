using System.Text.Json.Nodes;

namespace Chmonos.Mcp;

/// <summary>
/// 使う人の表示の設定に AI の答えも従う（docs/spec/mcp.md「表示の設定」・2026-11-13）。
/// 非表示にした商品はいつも、R-18 は設定の「R-18 を表示しない」のとき、AI に渡さない（画面で見せない物を、AI の答えで見せない）。
/// 設定は呼び出しのたびに読む（変えたらすぐ効く）。
/// </summary>
internal sealed class Visibility
{
    private Visibility(bool showAdult, HashSet<string> hiddenIds)
    {
        ShowAdult = showAdult;
        HiddenIds = hiddenIds;
    }

    public bool ShowAdult { get; }

    /// <summary>AI に渡さない商品の ID（非表示と、隠している R-18）。</summary>
    public HashSet<string> HiddenIds { get; }

    /// <summary>
    /// 隠す商品の ID を集める上限の頁数（1頁 200 件＝API の上限）。100頁で2万件（今の計測の大きい写しと同じ件数）。
    /// 非表示と R-18 だけを引くので、ふつうはずっと少ない
    /// </summary>
    private const int MaxPages = 100;

    /// <summary>設定だけ読む（検索は API の <c>includeHidden</c>・<c>includeAdult</c> で絞れるので、ID の一覧は要らない）。</summary>
    public static async Task<bool> ShowAdultAsync(ChmonosBridge bridge, CancellationToken cancellationToken)
        => (await Tool.CallAsync(bridge.GetAsync("/v1/display-settings", cancellationToken)))?["showAdult"]?.GetValue<bool>() ?? false;

    /// <summary>設定と、隠す商品の ID を読む（商品の ID を含む一覧から落とすため）。</summary>
    public static async Task<Visibility> LoadAsync(ChmonosBridge bridge, CancellationToken cancellationToken)
    {
        var showAdult = await ShowAdultAsync(bridge, cancellationToken);
        var hidden = new JsonObject { ["condition"] = new JsonObject { ["kind"] = "hidden", ["choice"] = "hidden" } };
        JsonNode where = showAdult
            ? hidden
            : new JsonObject { ["or"] = new JsonArray(hidden, new JsonObject { ["condition"] = new JsonObject { ["kind"] = "adult", ["choice"] = "adult" } }) };

        var ids = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var body = new JsonObject
            {
                ["where"] = where.DeepClone(),
                ["includeHidden"] = true,
                ["includeAdult"] = true,
                ["limit"] = 200,
                ["cursor"] = cursor,
            };
            var answer = await Tool.CallAsync(bridge.PostAsync("/v1/search", body, cancellationToken));
            foreach (var item in answer?["items"]?.AsArray() ?? [])
            {
                if (item?["id"]?.GetValue<string>() is { } id)
                {
                    ids.Add(id);
                }
            }

            cursor = answer?["nextCursor"]?.GetValue<string>();
            if (cursor is null)
            {
                break;
            }
        }

        return new Visibility(showAdult, ids);
    }

    /// <summary>商品1件の詳細（<c>isHidden</c>・<c>isAdult</c>）が隠す物なら、AI に渡す断りの文を返す。</summary>
    public static string? RefusalFor(JsonNode? item, bool showAdult)
    {
        if (item?["isHidden"]?.GetValue<bool>() == true)
        {
            return "この商品は Chmonos で非表示にしているので、答えません。";
        }

        if (!showAdult && item?["isAdult"]?.GetValue<bool>() == true)
        {
            return "この商品は R-18 で、Chmonos の設定で R-18 を表示しないことにしているので、答えません。";
        }

        return null;
    }

    /// <summary>入れ子のどこにあっても、<paramref name="key"/> が隠す商品の ID の物を一覧から落とす。落とした数を返す。</summary>
    public int Remove(JsonNode? node, string key)
    {
        var removed = 0;
        switch (node)
        {
            case JsonArray array:
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonObject element && element[key] is JsonValue value && value.TryGetValue<string>(out var id) && HiddenIds.Contains(id))
                    {
                        array.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        removed += Remove(array[i], key);
                    }
                }

                break;

            case JsonObject obj:
                foreach (var (_, value) in obj.ToList())
                {
                    removed += Remove(value, key);
                }

                break;
        }

        return removed;
    }
}
