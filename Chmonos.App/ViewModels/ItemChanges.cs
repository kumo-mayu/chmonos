using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 変わった欄の印の種類（メモ7-①・色の割り当てはユーザ判断 2026-10-02「変化の種類毎の色はあなたに任せる」）。
/// 色は種類で分ける：変わった＝橙、足された＝緑、消えた・販売終了＝赤、価格＝青。
/// 色だけに頼らないよう、札の文字も種類で変える（ui-rules.md「見た目の決まり」）
/// </summary>
public enum ChangeTone
{
    Changed,
    Added,
    Removed,
    Price,
}

/// <summary>欄に付ける印1つ（札の文字と、乗せたときに出す前の値）。</summary>
public sealed class ChangeMark
{
    public required ChangeTone Tone { get; init; }

    public required string Label { get; init; }

    public required string Tip { get; init; }
}

/// <summary>
/// 1つの欄に付く印の束。バリエーションの欄には「価格」と「数」の2つが載ることがあるので、束にして札を並べる。
/// 欄の左の線の色は先頭の印で決める
/// </summary>
public sealed class ChangeSlot
{
    public static ChangeSlot Empty { get; } = new([]);

    public ChangeSlot(IReadOnlyList<ChangeMark> marks) => Marks = marks;

    public IReadOnlyList<ChangeMark> Marks { get; }

    public bool IsMarked => Marks.Count > 0;

    /// <summary>左の線の色。印が無ければ null（線を出さない）。</summary>
    public ChangeTone? Edge => IsMarked ? Marks[0].Tone : null;
}

/// <summary>
/// 商品の未読の更新の知らせ（<see cref="NotificationKind.ItemUpdated"/>）を、商品ページのどの欄に印を付けるかへ振り分ける（メモ7-①）。
///
/// **欄の名前で引く**：差の欄の名前は <see cref="BoothChanges"/> が決める（商品名・価格・バリエーション・画像・販売状況・説明文、
/// それ以外は説明文の見出し）。見出しは正規化した名前で突き合わせる（<see cref="BoothChanges"/> と同じ）。
/// 消えた見出しは元の位置に見出しごと並べる（<see cref="RemovedSections"/>。メモ17）。
/// 印は「既読にする」を押すまで残す（ユーザ判断 2026-10-02）ので、ここは知らせを読むだけで何も書かない
/// </summary>
internal sealed class ItemChanges
{
    public static ItemChanges None { get; } = new();

    private ItemChanges()
    {
    }

    public ChangeSlot Name { get; private init; } = ChangeSlot.Empty;

    /// <summary>バリエーションの欄。価格もここ（商品ページで値段が出ているのはバリエーションの行だけ）。</summary>
    public ChangeSlot Variations { get; private init; } = ChangeSlot.Empty;

    public ChangeSlot Gallery { get; private init; } = ChangeSlot.Empty;

    /// <summary>「記録していること」の「BOOTHでの販売」。</summary>
    public ChangeSlot Sale { get; private init; } = ChangeSlot.Empty;

    /// <summary>「商品説明」の見出し。見出しごとの印が畳んだ欄の中に隠れても、変わったことが分かるように。</summary>
    public ChangeSlot Description { get; private init; } = ChangeSlot.Empty;

    /// <summary>説明文の見出しごとの印（鍵は正規化した見出し）。</summary>
    public IReadOnlyDictionary<string, ChangeSlot> Sections { get; private init; } = new Dictionary<string, ChangeSlot>();

    /// <summary>説明文の見出しごとの、変わった行（鍵は正規化した見出し。メモ13-②）。行を持たない知らせの見出しは入らない。</summary>
    public IReadOnlyDictionary<string, ChangedLines> SectionLines { get; private init; } = new Dictionary<string, ChangedLines>();

    /// <summary>見出しの無い商品の説明文の、変わった行。</summary>
    public ChangedLines DescriptionLines { get; private init; } = ChangedLines.None;

    /// <summary>前の商品名（最初の前の値）。変わっていなければ null。前の名前の行を赤の帯で並べる（メモ17）。</summary>
    public string? NameBefore { get; private init; }

    /// <summary>足された・消えたバリエーションの名前（メモ17）。行を持たない前の形の知らせでは空。</summary>
    public ChangedLines VariationLines { get; private init; } = ChangedLines.None;

    /// <summary>値段の変わったバリエーション（鍵は BOOTH のバリエーションの ID。メモ27-⑤）。種類ごとの値段を持たない知らせでは空。</summary>
    public IReadOnlyDictionary<long, NotificationPrice> VariationPrices { get; private init; } = new Dictionary<long, NotificationPrice>();

    /// <summary>今のページに無い、消えた見出し（元の位置に並べる。メモ17）。</summary>
    public IReadOnlyList<RemovedSection> RemovedSections { get; private init; } = [];

    /// <summary>
    /// ページに見せる欄の無い変化（今のページに無いのに「変わった」とある見出し。ページを取り直す前の知らせなど）。
    /// 上の帯の並びに、押せない語として出す
    /// </summary>
    public IReadOnlyList<string> Others { get; private init; } = [];

    /// <summary>既読にする知らせ。</summary>
    public IReadOnlyList<string> NotificationIds { get; private init; } = [];

    public bool HasAny => NotificationIds.Count > 0;

    /// <summary>この商品の、印を付ける知らせか（ショップの「変更あり」と同じ数え方：未読かつ未解消の商品の更新）。</summary>
    public static bool IsUnreadUpdateOf(NotificationRecord record, string itemId)
        => !record.IsRead
            && !record.IsResolved
            && record.Kind == NotificationKind.ItemUpdated
            && string.Equals(record.ItemId, itemId, StringComparison.Ordinal);

    /// <param name="records">この商品の未読の更新の知らせ（<see cref="IsUnreadUpdateOf"/>）。</param>
    /// <param name="sectionKeys">今のページにある説明文の見出し（正規化した名前）。</param>
    public static ItemChanges From(IEnumerable<NotificationRecord> records, IEnumerable<string> sectionKeys)
    {
        var ordered = records.OrderBy(record => record.CreatedAt).ToList();
        if (ordered.Count == 0)
        {
            return None;
        }

        var keys = sectionKeys.ToHashSet(StringComparer.Ordinal);
        var name = new List<ChangeMark>();
        var variations = new List<ChangeMark>();
        var gallery = new List<ChangeMark>();
        var sale = new List<ChangeMark>();
        var description = new List<ChangeMark>();
        var sections = new Dictionary<string, ChangeSlot>(StringComparer.Ordinal);
        var sectionLines = new Dictionary<string, ChangedLines>(StringComparer.Ordinal);
        var descriptionLines = ChangedLines.None;
        var variationLines = ChangedLines.None;
        var prices = new Dictionary<long, NotificationPrice>();
        string? nameBefore = null;
        var changedHeadings = new List<string>();
        var removedSections = new List<RemovedSection>();
        var others = new List<string>();

        foreach (var diff in Merge(ordered))
        {
            switch (diff.Field)
            {
                case BoothChanges.NameField:
                    name.Add(Mark(ChangeTone.Changed, "変更", diff));
                    nameBefore = diff.Before;
                    break;

                case BoothChanges.PriceField:
                    // 商品の価格の文字は一番安い値段だけなので、高い方だけが変わると前後が同じになる。そのときは前の値を言わない
                    variations.Add(string.Equals(diff.Before, diff.After, StringComparison.Ordinal) && diff.Prices is { Count: > 0 }
                        ? new ChangeMark { Tone = ChangeTone.Price, Label = "価格変更", Tip = "バリエーションの価格が変更されました。" }
                        : Mark(ChangeTone.Price, "価格変更", diff));
                    foreach (var price in diff.Prices ?? [])
                    {
                        prices[price.Id] = price;
                    }

                    break;

                case BoothChanges.VariationsField:
                    variations.Add(CountMark(diff));
                    variationLines = ChangedLines.From(diff);
                    break;

                case BoothChanges.ImagesField:
                    gallery.Add(CountMark(diff));
                    break;

                case BoothChanges.SaleField:
                    sale.Add(diff.After == "販売終了"
                        ? Mark(ChangeTone.Removed, "販売終了", diff)
                        : Mark(ChangeTone.Added, "販売再開", diff));
                    break;

                // 見出しの無い商品は説明文の全体で比べている。見出しの名前が「説明文」になる商品（見出しが空）は、見出しの方で引く
                case BoothChanges.DescriptionField when !keys.Contains(diff.Field):
                    description.Add(Mark(ChangeTone.Changed, "変更", diff));
                    descriptionLines = ChangedLines.From(diff);
                    break;

                default:
                    if (keys.Contains(diff.Field))
                    {
                        sections[diff.Field] = new ChangeSlot([diff.Before is null
                            ? Mark(ChangeTone.Added, "追加", diff)
                            : Mark(ChangeTone.Changed, "変更", diff)]);
                        changedHeadings.Add(diff.Field);
                        if (ChangedLines.From(diff) is { HasAny: true } lines)
                        {
                            sectionLines[diff.Field] = lines;
                        }
                    }
                    else if (diff.After is null)
                    {
                        // 今のページに無い見出し＝消えた見出し。前は上の帯の1行にまとめていたが、元の位置に見出しごと赤の帯で並べる（メモ17）
                        removedSections.Add(new RemovedSection(
                            diff.Field,
                            diff.Follows,
                            ChangedLines.From(diff),
                            Mark(ChangeTone.Removed, "削除", diff)));
                        changedHeadings.Add(diff.Field);
                    }
                    else
                    {
                        others.Add($"見出し「{diff.Field}」");
                    }

                    break;
            }
        }

        if (changedHeadings.Count > 0)
        {
            description.Add(new ChangeMark
            {
                Tone = ChangeTone.Changed,
                Label = "変更",
                Tip = $"変わった見出し：{string.Join("、", changedHeadings)}",
            });
        }

        return new ItemChanges
        {
            Name = Slot(name),
            Variations = Slot(variations),
            Gallery = Slot(gallery),
            Sale = Slot(sale),
            Description = Slot(description),
            Sections = sections,
            SectionLines = sectionLines,
            DescriptionLines = descriptionLines,
            NameBefore = nameBefore,
            VariationLines = variationLines,
            VariationPrices = prices,
            RemovedSections = removedSections,
            Others = others,
            NotificationIds = ordered.Select(record => record.Id).Distinct().ToList(),
        };
    }

    /// <summary>
    /// 同じ商品の未読は、新しい変化が前の知らせに重なる作り（<c>ItemService.NoteChangesAsync</c>・<see cref="ChangeStack"/>）なので普通は1件。
    /// 手で直した JSON などで2件以上あっても、古い順に同じ重ね方で1つにする（保存側と画面で見え方が食い違わないように）
    /// </summary>
    private static IReadOnlyList<NotificationDiff> Merge(IReadOnlyList<NotificationRecord> ordered)
        => ordered.Skip(1).Aggregate(
            ordered[0].Diffs ?? [],
            (accumulated, record) => ChangeStack.Stack(accumulated, record.Diffs ?? []));

    private static ChangeSlot Slot(List<ChangeMark> marks) => marks.Count == 0 ? ChangeSlot.Empty : new ChangeSlot(marks);

    /// <summary>乗せると前の値（ユーザ判断 2026-10-02）。前が無い物は、新しく足されたと言う。</summary>
    private static ChangeMark Mark(ChangeTone tone, string label, NotificationDiff diff) => new()
    {
        Tone = tone,
        Label = label,
        Tip = diff.Before is { Length: > 0 } before ? $"前の値：{before}" : "前回の取得の後に追加されました。",
    };

    /// <summary>数の変化（「3 件」→「5 件」）。増えたら足された、減ったら消えた。</summary>
    private static ChangeMark CountMark(NotificationDiff diff)
    {
        var before = LeadingNumber(diff.Before);
        var after = LeadingNumber(diff.After);
        return after > before
            ? Mark(ChangeTone.Added, "追加", diff)
            : after < before
                ? Mark(ChangeTone.Removed, "削除", diff)
                : Mark(ChangeTone.Changed, "変更", diff);
    }

    private static int LeadingNumber(string? text)
    {
        var digits = new string((text ?? string.Empty).TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : 0;
    }

}

/// <summary>消えた見出し1つ（<paramref name="Key"/> は正規化した見出し、<paramref name="Follows"/> は前のページで直前にあった見出し）。</summary>
internal sealed record RemovedSection(string Key, string? Follows, ChangedLines Lines, ChangeMark Mark);
