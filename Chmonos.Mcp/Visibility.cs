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
    /// 非表示と R-18 だけを引くので、ふつうはずっと少ない。超えたら集めた分で決めずに断る（<see cref="LoadAsync"/>）
    /// </summary>
    private const int MaxPages = 100;

    /// <summary>隠す商品を集めきれなかったときに AI に渡す断りの文（集めた分だけで「見せてよい」を決めると、集めそこねた物を見せる）。</summary>
    public const string IncompleteRefusal =
        "Chmonos の表示の設定で隠す商品を集めきれなかったので、商品を含む答えは返しません（隠す商品が2万件を超えたか、Chmonos の答えが読めませんでした）。";

    /// <summary>設定だけ読む（商品1件の R-18 の断りに使う。隠す商品の ID の一覧が要る答えは <see cref="LoadAsync"/>）。</summary>
    public static async Task<bool> ShowAdultAsync(ChmonosBridge bridge, CancellationToken cancellationToken)
        => (await Tool.CallAsync(bridge.GetAsync("/v1/display-settings", cancellationToken)))?["showAdult"]?.GetValue<bool>() ?? false;

    /// <summary>
    /// 設定と、隠す商品の ID を読む（商品の ID を含む答えから落とすため）。
    /// 集めきれなかったら（上限の頁で続きが残る・<c>items</c> が欠ける・ID が読めない・続きの印が変）断る
    /// （fail-closed。集めた分だけで決めると、集めそこねた隠す商品を見せる。2026-11-13 の点検 M2）
    /// </summary>
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
            if (answer is not JsonObject obj || obj["items"] is not JsonArray items)
            {
                throw new ToolProblemException(IncompleteRefusal);
            }

            foreach (var item in items)
            {
                ids.Add(TextOf(item?["id"]) ?? throw new ToolProblemException(IncompleteRefusal));
            }

            // API は終わりを null で書く（Chmonos.Query の SearchPage・QueryJson は null を省かない）。
            // 欄が無い・文字列でない・前と同じ印は、続きがあるかを決められないので断る
            if (!obj.TryGetPropertyValue("nextCursor", out var next))
            {
                throw new ToolProblemException(IncompleteRefusal);
            }

            if (next is null)
            {
                return new Visibility(showAdult, ids);
            }

            var following = TextOf(next);
            if (following is null || following == cursor)
            {
                throw new ToolProblemException(IncompleteRefusal);
            }

            cursor = following;
        }

        throw new ToolProblemException(IncompleteRefusal);
    }

    private static string? TextOf(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    /// <summary>隠す商品の ID か（ID の文字列でない値・null は偽）。</summary>
    public bool IsHidden(JsonNode? node) => TextOf(node) is { } id && HiddenIds.Contains(id);

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

    /// <summary>
    /// 一覧の行を指す鍵。行の主語がその商品（通知・ファイル・包み・改変の使った物・対応アバター・改変のアバター）なので、行ごと落とす。
    /// 対応アバターの行は ID と名前の組なので、ID だけ消すと名前が残る
    /// </summary>
    private static readonly string[] RowKeys = ["itemId", "avatarId"];

    /// <summary>商品の ID を文字列で並べる欄（検索の要約と包みの <c>avatarIds</c>、共通素体の <c>members</c>）。隠す ID だけを抜く。</summary>
    private static readonly HashSet<string> IdListKeys = new(StringComparer.Ordinal) { "avatarIds", "members" };

    /// <summary>
    /// 入れ子のどこにあっても、隠す商品への参照を落とす（2026-11-13 の点検 M1。欄の形ごとに当てる）：
    /// 配列の中で <c>itemId</c>・<c>avatarId</c> が隠す ID の行は行ごと、<c>avatarIds</c>・<c>members</c> の文字列は1つずつ。落とした数を返す。
    /// 配列の外の1つの値（改変1件の <c>avatarId</c>・共通素体の <c>itemId</c>）は、欄の意味が答えごとに違うので呼び手が扱う
    /// </summary>
    public int Scrub(JsonNode? node) => Scrub(node, null);

    private int Scrub(JsonNode? node, string? key)
    {
        var removed = 0;
        switch (node)
        {
            case JsonArray array:
                var idList = key is not null && IdListKeys.Contains(key);
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    var element = array[i];
                    var drop = element switch
                    {
                        JsonObject row => RowKeys.Any(rowKey => IsHidden(row[rowKey])),
                        JsonValue => idList && IsHidden(element),
                        _ => false,
                    };
                    if (drop)
                    {
                        array.RemoveAt(i);
                        removed++;
                    }
                    else
                    {
                        removed += Scrub(element, null);
                    }
                }

                break;

            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    removed += Scrub(value, name);
                }

                break;
        }

        return removed;
    }

    /// <summary>一覧の行のうち、<paramref name="key"/> が隠す商品の ID の物を落とす（アバターの一覧の <c>id</c>）。落とした数を返す。</summary>
    public int RemoveRows(JsonArray rows, string key)
    {
        var removed = 0;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i] is JsonObject row && IsHidden(row[key]))
            {
                rows.RemoveAt(i);
                removed++;
            }
        }

        return removed;
    }
}
