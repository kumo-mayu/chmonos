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
    /// 答えは MCP の文字の答えの中身で、HTML に埋めないので、逃がさなくてよい。
    /// 全角空白・絵文字は <c>UnsafeRelaxedJsonEscaping</c> でも逃げるので、自前の <see cref="PlainJsonEncoder"/> を使う
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions Json = new()
    {
        Encoder = PlainJsonEncoder.Instance,
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
    /// AI に渡さない欄。絵の場所（AI は手元の絵を開けず、場所だけで文脈を食う）と、説明文の HTML（同じ中身の素の文を別に返す）。
    /// 改変の blueprint ID も落とす：API が返すのは「同じ PC の道具には隠しても守りにならない」から（local-api.md・2026-11-02）で、
    /// AI に渡すと AI の提供元へ出ていく。それは別の話なので、使う人が決めるまで安全側に倒す（docs/spec/mcp.md）
    /// </summary>
    private static readonly HashSet<string> Dropped = new(StringComparer.Ordinal) { "thumbnail", "imagePath", "images", "html", "blueprintId" };

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
    /// 1つの物（商品の詳細など）を <paramref name="budget"/> 字に収める。入れ子の一覧を段々に短くし、それでも長ければ文字列も短くする。
    /// どの段でも JSON として読める形を保つ（途中で切った文字列を返さない）。
    /// 一番短い段でも収まらない（欄が数百ある横に広い答えなど。2026-11-13 の点検 M3）ときは、中身を写さない省略の答えにする。
    /// 隠す決まり（<see cref="Dropped"/>・呼び手の <c>Visibility.Scrub</c>）は削る前に当たり、省略の答えは元の欄名も値も持たないので、隠した物は出ない
    /// </summary>
    /// <param name="retryHint">省いたときに、どう引き直せば収まるか（道具ごとに違う。無ければ言わない）。</param>
    public static string Fit(JsonNode? node, int budget = MaxAnswerChars, string? retryHint = null)
    {
        foreach (var (nested, chars) in new[] { (MaxNestedItems, MaxStringChars), (10, MaxStringChars), (5, 120), (3, 60), (1, 40) })
        {
            var text = Slim(node, nested, chars)?.ToJsonString(AnswerShaper.Json) ?? "null";
            if (text.Length <= budget)
            {
                return text;
            }
        }

        return Omitted(retryHint);
    }

    /// <summary>
    /// 道具の答えの共通の出口（<see cref="ChmonosMcpServer"/>）。各道具は自分で上限に収めるが、収め損ねた答えも AI に渡さない最後の網。
    /// 文字列を途中で切ると JSON が壊れ、続きの印も合わなくなるので、切らずに省略の答えに置き換える
    /// </summary>
    public static string Guard(string text) => text.Length <= MaxAnswerChars ? text : Omitted();

    /// <summary>
    /// 上限に収まらない答えの代わり。元の答えの欄も値も写さない（何が入っていても短く、隠す物を漏らさない）。
    /// 引き直し方は道具ごとに渡す（ユーザ判断 2026-11-13）。前はどの道具でも「件数（limit）を減らして」と言い、limit の無い道具（商品1件など）で
    /// AI が無い引数を付けて呼び直しかねなかった。一覧はここへ来る前に件数を1件まで減らしているので、limit を減らしても収まらない
    /// </summary>
    public static string Omitted(string? retryHint = null)
        => new JsonObject
        {
            ["omitted"] = true,
            ["message"] = $"答えが長すぎて上限（{MaxAnswerChars}字）に収まらないので省きました。{retryHint}",
        }.ToJsonString(AnswerShaper.Json);

    /// <summary><c>get_item</c> で省いたときの引き直し方（include で足した欄が長さの元になる）。</summary>
    public const string ItemRetryHint = "include で足す欄を減らすか、外して引き直してください。";

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

    /// <summary>
    /// <c>search_items</c> の1回の件数の上限。検索の要約は1件およそ200字あり、8,000字に収まるのは30件ほど（ユーザ判断 2026-11-13）。
    /// 前は50で、本物の要約では収まらず件数を半分にして引き直し、ほぼ25件で返っていた（50と頼んで25件が来るのは AI に分かりにくい）。
    /// ツールの説明（<c>ChmonosTools</c> の入力の形）の maximum も同じ値にそろえる
    /// </summary>
    public const int SearchMaxLimit = 30;

    /// <summary>
    /// 1件にしても上限を超える一覧の答えで、その1件だけを縮める。続きの印（<c>nextCursor</c>・<c>nextOffset</c>）と件数の欄は削らない
    /// （封筒ごと <see cref="Fit"/> に通すと、長い続きの印が切られて続きを引けなくなる）。
    /// 1件に使える字数は、上限から封筒の長さを引いた残り（1件の分だけ縮めて上限ぎりぎりにすると、封筒を足して超える）
    /// </summary>
    public static string FitSingle(JsonObject answer)
    {
        var items = answer["items"]!.AsArray();
        var only = items.Count > 0 ? items[0]?.DeepClone() : null;
        answer["items"] = new JsonArray();
        var envelope = answer.ToJsonString(AnswerShaper.Json).Length;
        answer["items"] = new JsonArray(JsonNode.Parse(Fit(only, Math.Max(0, MaxAnswerChars - envelope))));
        return answer.ToJsonString(AnswerShaper.Json);
    }
}
