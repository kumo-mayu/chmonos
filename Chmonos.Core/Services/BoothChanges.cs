using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

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

    // 差の欄の名前。商品ページが「どの欄に印を付けるか」をこの名前で引くので、文字を1か所に置く（メモ7-①）
    public const string SaleField = "販売状況";
    public const string NameField = "商品名";
    public const string PriceField = "価格";
    public const string VariationsField = "バリエーション";
    public const string ImagesField = "画像";
    public const string DescriptionField = "説明文";

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
            diffs.Add(new NotificationDiff { Field = SaleField, Before = "販売中", After = "販売終了" });
        }
        else if (before.IsEndOfSale && !after.IsEndOfSale)
        {
            diffs.Add(new NotificationDiff { Field = SaleField, Before = "販売終了", After = "販売中" });
        }

        if (!string.Equals(before.Name, after.Name, StringComparison.Ordinal))
        {
            diffs.Add(new NotificationDiff { Field = NameField, Before = before.Name, After = after.Name });
        }

        // 価格は整形済みの文字列（"¥ 2,500"）。そのまま見せる方が読みやすい。
        // ただし商品の価格は一番安い値段（「¥ 500~」）しか言わないので、どのバリエーションの値段が変わったかを別に持つ（メモ27-⑤）。
        // 高い方だけが変わると商品の価格の文字は同じままなので、バリエーションの値段だけが変わっても知らせる
        var prices = VariationPrices(before, after);
        if (!string.Equals(before.PriceText, after.PriceText, StringComparison.Ordinal) || prices.Count > 0)
        {
            diffs.Add(new NotificationDiff
            {
                Field = PriceField,
                Before = before.PriceText,
                After = after.PriceText,
                Prices = prices.Count > 0 ? prices : null,
            });
        }

        // どのバリエーションが足された・消えたかを名前の行で持つ（メモ17・ユーザ指示 2026-10-03「どれが追加されたのか一発でわかるように」）。
        // 前は数だけを比べていて、1つ消えて1つ足されると数が同じで知らせが出ず、出ても「2 件 → 3 件」ではどれか分からなかった。
        // 並べ替えだけは知らせない（名前の顔ぶれが同じなら、人が気にする変化ではない）
        var beforeNames = VariationNames(before);
        var afterNames = VariationNames(after);
        if (before.Variations.Count != after.Variations.Count
            || !beforeNames.Order(StringComparer.Ordinal).SequenceEqual(afterNames.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            diffs.Add(WithLines(
                new NotificationDiff
                {
                    Field = VariationsField,
                    Before = $"{before.Variations.Count} 件",
                    After = $"{after.Variations.Count} 件",
                },
                string.Join('\n', beforeNames),
                string.Join('\n', afterNames)));
        }

        if (before.Images.Count != after.Images.Count)
        {
            diffs.Add(new NotificationDiff
            {
                Field = ImagesField,
                Before = $"{before.Images.Count} 枚",
                After = $"{after.Images.Count} 枚",
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
                yield return WithLines(
                    new NotificationDiff { Field = DescriptionField, Before = Excerpt(beforeText), After = Excerpt(afterText) },
                    before.Description,
                    after.Description);
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
                yield return WithLines(new NotificationDiff { Field = heading, After = Excerpt(Normalize(text)) }, null, text);
            }
            else if (!string.Equals(Normalize(previous), Normalize(text), StringComparison.Ordinal))
            {
                yield return WithLines(
                    new NotificationDiff { Field = heading, Before = Excerpt(Normalize(previous)), After = Excerpt(Normalize(text)) },
                    previous,
                    text);
            }
        }

        // 消えた見出し。before に見出しがあるのは、前回HTMLが取れていたということ。
        // 商品ページが元の位置に並べられるよう、前のページで直前にあった見出しを添える（メモ17）
        string? previousHeading = null;
        foreach (var (heading, text) in beforeSections)
        {
            if (!afterSections.ContainsKey(heading))
            {
                yield return WithLines(
                    new NotificationDiff { Field = heading, Before = Excerpt(Normalize(text)), Follows = previousHeading },
                    text,
                    null);
            }

            previousHeading = heading;
        }
    }

    /// <summary>
    /// 更新履歴の見出しが動いたか。**強い通知にするのはここだけ**
    /// （新しい版が出たのに気付かないと、古いファイルを使い続けることになる）。
    /// </summary>
    public static bool HasStrongChange(IReadOnlyList<NotificationDiff> diffs)
        => diffs.Any(diff => Booth.H2SectionExtractor.IsUpdateHistoryHeading(diff.Field));

    /// <summary>
    /// 知らせ1件が、どの種類の変化を含むか（検索の条件「更新通知あり」の種類・ユーザ判断 2026-10-06）。
    /// 差の欄の名前で見分ける：販売状況 → 販売の状態、価格 → 価格、バリエーション → バリエーション、
    /// 更新履歴の見出し（強い知らせ）→ 中身の更新、それ以外（商品名・画像・説明文とほかの見出し）→ ページ内容の変更。
    /// 差を持たない知らせでも強い知らせなら中身の更新に数え、それ以外はページ内容の変更に数える（どの種類にも入らない知らせを作らない）。
    /// </summary>
    public static BoothChangeKind KindsOf(NotificationRecord record)
    {
        var kinds = record.IsStrong ? BoothChangeKind.Content : BoothChangeKind.None;
        foreach (var diff in record.Diffs)
        {
            kinds |= diff.Field switch
            {
                SaleField => BoothChangeKind.Sale,
                PriceField => BoothChangeKind.Price,
                VariationsField => BoothChangeKind.Variations,
                _ when Booth.H2SectionExtractor.IsUpdateHistoryHeading(diff.Field) => BoothChangeKind.Content,
                _ => BoothChangeKind.Page,
            };
        }

        // 差を持たない知らせ（差を書けなかった物）も、ページが変わったことは確か。どの種類にも入らないと、
        // カードには札「更新あり」が出るのに、種類を全部入れた条件で出てこない
        return kinds == BoothChangeKind.None ? BoothChangeKind.Page : kinds;
    }

    /// <summary>
    /// 見出しの原文は装飾記号付きなので、正規化した見出しで突き合わせる（同じ見出しが2つあれば後ろを足す）。
    /// 本文は改行を残したまま持つ（行の差を作るため）。変わったかは空白を詰めてから比べる
    /// </summary>
    private static Dictionary<string, string> Group(IReadOnlyList<H2Section> sections)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            var heading = section.NormalizedHeading.Length > 0 ? section.NormalizedHeading : DescriptionField;
            var text = section.Text ?? string.Empty;
            map[heading] = map.TryGetValue(heading, out var existing) ? $"{existing}\n{text}" : text;
        }

        return map;
    }

    /// <summary>
    /// 変わった行（<see cref="LineDiff"/>）を添える。上限を超えた分は数だけ残す。
    /// 頭の抜き出し（Before・After）も残す：要確認の1行の要約（<see cref="Summarize"/>）と、商品ページの「前の値」の吹き出しが使う
    /// </summary>
    private static NotificationDiff WithLines(NotificationDiff diff, string? before, string? after)
    {
        var lines = LineDiff.Compare(before, after);
        if (lines.Count == 0)
        {
            return diff;
        }

        // 上限は種類ごと。並びは保ったまま、それぞれ先頭から残す
        var kept = new List<NotificationLine>();
        var (added, removed) = (0, 0);
        foreach (var line in lines)
        {
            var count = line.Kind == NotificationLineKind.Added ? ++added : ++removed;
            if (count <= LineDiff.MaxLines)
            {
                kept.Add(line);
            }
        }

        return new NotificationDiff
        {
            Field = diff.Field,
            Before = diff.Before,
            After = diff.After,
            Lines = kept,
            MoreAdded = added > LineDiff.MaxLines ? added - LineDiff.MaxLines : null,
            MoreRemoved = removed > LineDiff.MaxLines ? removed - LineDiff.MaxLines : null,
            Follows = diff.Follows,
        };
    }

    /// <summary>
    /// 前後の両方にあるバリエーション（ID で突き合わせる）のうち、値段の変わった物を今の並びで。
    /// 足された・消えたバリエーションはバリエーションの欄の差が言うので、ここには入れない
    /// </summary>
    private static List<NotificationPrice> VariationPrices(BoothBlock before, BoothBlock after)
    {
        var previous = new Dictionary<long, int>();
        foreach (var variation in before.Variations)
        {
            previous.TryAdd(variation.Id, variation.Price);
        }

        var result = new List<NotificationPrice>();
        foreach (var variation in after.Variations)
        {
            if (previous.TryGetValue(variation.Id, out var price) && price != variation.Price)
            {
                result.Add(new NotificationPrice
                {
                    Id = variation.Id,
                    Name = string.IsNullOrWhiteSpace(variation.Name) ? null : variation.Name,
                    Before = price,
                    After = variation.Price,
                });
            }
        }

        return result;
    }

    /// <summary>バリエーションの名前を BOOTH の並びのまま。名前の無いバリエーション（単一の商品）は行にならない（空の行は差に数えない）。</summary>
    private static List<string> VariationNames(BoothBlock block)
        => block.Variations.Select(variation => LineDiff.NormalizeLine(variation.Name ?? string.Empty)).ToList();

    private static string Normalize(string? text)
        => string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Excerpt(string text)
        => text.Length <= ExcerptLength ? text : text[..ExcerptLength] + "…";

    /// <summary>
    /// 要確認の1行にする。開かなくても判断できるように、変わったところを並べる。
    /// 価格はバリエーションごとの値段があればそちらを言う（商品の価格の文字は一番安い値段だけで、「¥ 500~ → ¥ 500~」になり得る）
    /// </summary>
    public static string Summarize(IReadOnlyList<NotificationDiff> diffs)
        => string.Join(" / ", diffs.Select(diff => diff.Prices is { Count: > 0 } prices
            ? $"{diff.Field} {string.Join("、", prices.Select(PriceChangeText))}"
            : diff.Before is null && diff.After is null
                ? diff.Field
                : $"{diff.Field} {diff.Before ?? "（無し）"} → {diff.After ?? "（無し）"}"));

    /// <summary>
    /// バリエーション1つの値段の変化（「通常版 ¥500 → ¥800」。名前の無い単一の商品は値段だけ）。
    /// 要確認の1行・札で同じ形にする
    /// </summary>
    public static string PriceChangeText(NotificationPrice price)
        => price.Name is { Length: > 0 } name ? $"{name} {PriceStep(price)}" : PriceStep(price);

    /// <summary>前の値段 → 今の値段。商品ページのバリエーションの行の値段と同じ「¥1,500」の書き方。</summary>
    public static string PriceStep(NotificationPrice price) => $"¥{price.Before:N0} → ¥{price.After:N0}";
}

/// <summary>
/// 商品の更新の知らせの種類（検索の条件「更新通知あり」で選ぶ5つ・ユーザ判断 2026-10-06）。1件の知らせが複数を含むことがある。
/// </summary>
[Flags]
public enum BoothChangeKind
{
    None = 0,

    /// <summary>中身の更新（更新履歴の見出しが変わった）。</summary>
    Content = 1,

    Variations = 2,

    Price = 4,

    /// <summary>販売の状態（販売中 ⇄ 販売終了）。</summary>
    Sale = 8,

    /// <summary>ページ内容の変更（商品名・画像・説明文。更新履歴の見出しは除く）。</summary>
    Page = 16,
}
