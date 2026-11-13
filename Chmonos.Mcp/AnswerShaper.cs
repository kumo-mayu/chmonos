using System.Text.Json.Nodes;

namespace Chmonos.Mcp;

/// <summary>
/// AI の文脈を食いすぎないよう、API の答えを削る（docs/spec/mcp.md「上限」）。
/// API の答えの形を1つずつ写し直さず、どの答えにも同じ削り方を当てる（API が欄を足しても、ここを直さずに通る）。
/// </summary>
internal static class AnswerShaper
{
    /// <summary>
    /// 答えの JSON の書き方。日本語を <c>\uXXXX</c> にしない（既定の書き方は ASCII の外を全部逃がすので、AI が読むと字数が6倍になり、読みにくい）。
    /// 答えは MCP の文字の答えの中身で、HTML に埋めないので、逃がさなくてよい
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 1つの答えの文字数の上限。Claude Code は道具の答えが1万トークンを超えると警告を出す（公式の文書 code.claude.com/docs/en/mcp）。
    /// 日本語は1字が1トークン近くになるので、字数で8千に抑えて、日本語ばかりの答えでも警告の手前に収める
    /// </summary>
    public const int MaxAnswerChars = 8_000;

    /// <summary>1つの文字列の上限。メモ・通知の文は長くなり得る。名前・パスは収まる長さ（Windows の普通のパスの上限 260 より少し長い）</summary>
    public const int MaxStringChars = 300;

    /// <summary>入れ子の一覧の上限（対応アバターが数百ある商品・ファイルの多い商品）。足りなければ AI が別の道具で絞って引ける</summary>
    public const int MaxNestedItems = 30;

    /// <summary>
    /// AI に要らない欄。絵の場所（AI は手元の絵を開けず、場所だけで文脈を食う）と、説明文の HTML（同じ中身の素の文を別に返す）
    /// </summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal) { "thumbnail", "imagePath", "images", "html" };

    /// <summary>削った写しを作る（元は変えない）。</summary>
    public static JsonNode? Slim(JsonNode? node, int nestedItems = MaxNestedItems, int stringChars = MaxStringChars)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (!Dropped.Contains(key))
                    {
                        copy[key] = Slim(value, nestedItems, stringChars);
                    }
                }

                return copy;

            case JsonArray array:
                var list = new JsonArray();
                foreach (var value in array.Take(nestedItems))
                {
                    list.Add(Slim(value, nestedItems, stringChars));
                }

                if (array.Count > nestedItems)
                {
                    list.Add($"…ほか {array.Count - nestedItems} 件（省きました）");
                }

                return list;

            case JsonValue value when value.TryGetValue<string>(out var text) && text.Length > stringChars:
                return JsonValue.Create(Cut(text, stringChars));

            default:
                return node?.DeepClone();
        }
    }

    public static string Cut(string text, int chars)
        => text.Length <= chars ? text : text[..chars] + $"…（{text.Length}字のうち先頭{chars}字）";

    /// <summary>
    /// 1つの物（商品の詳細など）を上限に収める。入れ子の一覧を段々に短くし、それでも長ければ文字列も短くする。
    /// どの段でも JSON として読める形を保つ（途中で切った文字列を返さない）
    /// </summary>
    public static string Fit(JsonNode? node)
    {
        foreach (var (nested, chars) in new[] { (MaxNestedItems, MaxStringChars), (10, MaxStringChars), (5, 120), (3, 60) })
        {
            var text = Slim(node, nested, chars)?.ToJsonString(AnswerShaper.Json) ?? "null";
            if (text.Length <= MaxAnswerChars)
            {
                return text;
            }
        }

        return Slim(node, 1, 40)?.ToJsonString(AnswerShaper.Json) ?? "null";
    }

    /// <summary>
    /// 続きの印の無い一覧（アバター・unitypackage・改変・通知…）を、通訳の側で offset と limit で区切る。
    /// 上限を超えれば件数を減らし、<c>nextOffset</c> で続きを示す
    /// </summary>
    public static string Page(IReadOnlyList<JsonNode?> items, int offset, int limit, JsonObject? extra = null)
    {
        offset = Math.Max(0, offset);
        var count = Math.Clamp(limit, 1, ListMaxLimit);
        while (true)
        {
            var slice = items.Skip(offset).Take(count).ToList();
            var answer = new JsonObject
            {
                ["total"] = items.Count,
                ["offset"] = offset,
                ["returned"] = slice.Count,
                ["nextOffset"] = offset + slice.Count < items.Count ? offset + slice.Count : null,
            };
            foreach (var (key, value) in extra ?? [])
            {
                answer[key] = value?.DeepClone();
            }

            answer["items"] = new JsonArray(slice.Select(item => Slim(item)).ToArray());
            var text = answer.ToJsonString(AnswerShaper.Json);
            if (text.Length <= MaxAnswerChars || count == 1)
            {
                return count == 1 && text.Length > MaxAnswerChars ? FitSingle(answer) : text;
            }

            count = Math.Max(1, count / 2);
        }
    }

    /// <summary>通訳が区切る一覧の1回の件数の上限（既定は <see cref="ListDefaultLimit"/>）。文字数の上限で更に減る</summary>
    public const int ListMaxLimit = 100;

    /// <summary>
    /// 既定の件数。一覧の1行は短い（名前・ID・数）ので、30件なら上限の文字数に収まりやすく、AI は続きを offset で引ける
    /// </summary>
    public const int ListDefaultLimit = 30;

    private static string FitSingle(JsonObject answer)
    {
        var items = answer["items"]!.AsArray();
        var only = items.Count > 0 ? items[0] : null;
        answer["items"] = new JsonArray(JsonNode.Parse(Fit(only)));
        return answer.ToJsonString(AnswerShaper.Json);
    }
}
