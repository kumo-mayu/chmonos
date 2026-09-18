using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 取り直した結果、何が変わったかを読む。
///
/// **何を変化と呼ぶかを決めないと、要確認が毎週埋まる。**
/// 見るのは「人が気にする変化」だけで、必ず動くものと、動いても困らないものは外す。
///
/// 旧データのスナップショットは保存していないので、取得した瞬間にここで比べる。
/// </summary>
public static class BoothChanges
{
    /// <summary>説明文の中身を出すときの長さ。1行に収めたいので、これを超えたら切って「…」を付ける。</summary>
    private const int ExcerptLength = 70;

    /// <summary>
    /// 変わったところを並べる。何も変わっていなければ空。
    ///
    /// **スキ数は見ない。**必ず動くので、毎週全商品が「変わった」になる。
    ///
    /// 説明文は**見出し（セクション）ごとに**比べる（2026-09-18 に設計へ合わせた）。
    /// 全文で比べると誤字直しでも出てしまうが、見出し単位なら「どこが変わったか」を名指しできる。
    /// 更新履歴の見出しの変化は強い通知にする（<see cref="HasStrongChange"/>）。
    /// 見出しが取れない商品だけ、説明文そのものを比べる（それしか手掛かりが無いので）。
    /// </summary>
    public static IReadOnlyList<NotificationDiff> Describe(BoothBlock before, BoothBlock after)
    {
        var diffs = new List<NotificationDiff>();

        // 販売終了は「もう買えない」なので、いちばん知りたい
        if (!before.IsEndOfSale && after.IsEndOfSale)
        {
            diffs.Add(new NotificationDiff { Field = "販売状況", Before = "販売中", After = "販売終了" });
        }
        else if (before.IsEndOfSale && !after.IsEndOfSale)
        {
            diffs.Add(new NotificationDiff { Field = "販売状況", Before = "販売終了", After = "販売中" });
        }

        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
        {
            diffs.Add(new NotificationDiff { Field = "商品名", Before = before.Name, After = after.Name });
        }

        // 価格は整形済みの文字列（"¥ 2,500"）。そのまま見せる方が読みやすい
        if (!string.Equals(before.PriceText, after.PriceText, StringComparison.Ordinal))
        {
            diffs.Add(new NotificationDiff { Field = "価格", Before = before.PriceText, After = after.PriceText });
        }

        if (before.Variations.Count != after.Variations.Count)
        {
            diffs.Add(new NotificationDiff
            {
                Field = "バリエーション",
                Before = $"{before.Variations.Count}件",
                After = $"{after.Variations.Count}件",
            });
        }

        if (before.Images.Count != after.Images.Count)
        {
            diffs.Add(new NotificationDiff
            {
                Field = "画像",
                Before = $"{before.Images.Count}枚",
                After = $"{after.Images.Count}枚",
            });
        }

        diffs.AddRange(DescribeDescription(before, after));

        return diffs;
    }

    /// <summary>
    /// 説明文の変化。見出しごとに比べ、見出しが片方にしか無ければ足された／消えたとして出す。
    ///
    /// 取り直しでHTMLが取れなかったときは見出しが0件になる。**そのときは何も言わない**
    /// （取れなかっただけで「全部消えた」と知らせると嘘になる）。
    /// </summary>
    private static IEnumerable<NotificationDiff> DescribeDescription(BoothBlock before, BoothBlock after)
    {
        if (before.H2Sections.Count == 0 && after.H2Sections.Count == 0)
        {
            // 見出しを持たない商品。手掛かりが説明文しかないので、そのまま比べる
            var beforeText = Normalize(before.Description);
            var afterText = Normalize(after.Description);
            if (!string.Equals(beforeText, afterText, StringComparison.Ordinal))
            {
                yield return new NotificationDiff
                {
                    Field = "説明文",
                    Before = Excerpt(beforeText),
                    After = Excerpt(afterText),
                };
            }

            yield break;
        }

        if (after.H2Sections.Count == 0)
        {
            yield break;
        }

        var beforeSections = Group(before.H2Sections);
        var afterSections = Group(after.H2Sections);

        foreach (var (heading, text) in afterSections)
        {
            if (!beforeSections.TryGetValue(heading, out var previous))
            {
                yield return new NotificationDiff { Field = heading, After = Excerpt(text) };
            }
            else if (!string.Equals(previous, text, StringComparison.Ordinal))
            {
                yield return new NotificationDiff { Field = heading, Before = Excerpt(previous), After = Excerpt(text) };
            }
        }

        // 消えた見出し。before に見出しがあるのは、前回HTMLが取れていたということ
        foreach (var (heading, text) in beforeSections)
        {
            if (!afterSections.ContainsKey(heading))
            {
                yield return new NotificationDiff { Field = heading, Before = Excerpt(text) };
            }
        }
    }

    /// <summary>
    /// 更新履歴の見出しが動いたか。**強い通知にするのはここだけ**
    /// （新しい版が出たのに気付かないと、古いファイルを使い続けることになる）。
    /// </summary>
    public static bool HasStrongChange(IReadOnlyList<NotificationDiff> diffs)
        => diffs.Any(diff => Booth.H2SectionExtractor.IsUpdateHistoryHeading(diff.Field));

    /// <summary>見出しの原文は装飾記号付きなので、正規化した見出しで突き合わせる（同じ見出しが2つあれば後ろを足す）。</summary>
    private static Dictionary<string, string> Group(IReadOnlyList<H2Section> sections)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            var heading = section.NormalizedHeading.Length > 0 ? section.NormalizedHeading : "説明文";
            var text = Normalize(section.Text);
            map[heading] = map.TryGetValue(heading, out var existing) ? $"{existing}\n{text}" : text;
        }

        return map;
    }

    private static string Normalize(string? text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Excerpt(string text)
        => text.Length <= ExcerptLength ? text : text[..ExcerptLength] + "…";

    /// <summary>要確認の1行にする。開かなくても判断できるように、変わったところを並べる。</summary>
    public static string Summarize(IReadOnlyList<NotificationDiff> diffs)
        => string.Join(" / ", diffs.Select(diff => diff.Before is null && diff.After is null
            ? diff.Field
            : $"{diff.Field} {diff.Before ?? "（無し）"} → {diff.After ?? "（無し）"}"));
}
