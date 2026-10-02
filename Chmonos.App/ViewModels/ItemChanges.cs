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
/// ページに無い欄（消えた見出しなど）は、ページの上の1行「ほかに変わったところ」にまとめる。
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

    /// <summary>ページに対応する欄の無い変化（「ほかに変わったところ：」の後に並べる語）。</summary>
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
        var changedHeadings = new List<string>();
        var others = new List<string>();

        foreach (var diff in Merge(ordered))
        {
            switch (diff.Field)
            {
                case BoothChanges.NameField:
                    name.Add(Mark(ChangeTone.Changed, "変更", diff));
                    break;

                case BoothChanges.PriceField:
                    variations.Add(Mark(ChangeTone.Price, "価格変更", diff));
                    break;

                case BoothChanges.VariationsField:
                    variations.Add(CountMark(diff));
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
                    break;

                default:
                    if (keys.Contains(diff.Field))
                    {
                        sections[diff.Field] = new ChangeSlot([diff.Before is null
                            ? Mark(ChangeTone.Added, "追加", diff)
                            : Mark(ChangeTone.Changed, "変更", diff)]);
                        changedHeadings.Add(diff.Field);
                    }
                    else
                    {
                        // 今のページに無い見出し＝消えた見出し。見せる欄が無いので1行にまとめる
                        others.Add(diff.After is null ? $"消えた見出し「{diff.Field}」" : $"見出し「{diff.Field}」");
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
            Others = others,
            NotificationIds = ordered.Select(record => record.Id).Distinct().ToList(),
        };
    }

    /// <summary>
    /// 同じ商品の未読は、新しい知らせが古い方を差し替える作り（<c>ItemService.NoteChangesAsync</c>）なので普通は1件。
    /// 手で直した JSON などで2件以上あっても、欄ごとに「いちばん古い前」と「いちばん新しい後」にまとめる
    /// </summary>
    private static IEnumerable<NotificationDiff> Merge(IReadOnlyList<NotificationRecord> ordered)
    {
        var merged = new Dictionary<string, NotificationDiff>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var diff in ordered.SelectMany(record => record.Diffs ?? []))
        {
            if (merged.TryGetValue(diff.Field, out var first))
            {
                merged[diff.Field] = new NotificationDiff { Field = diff.Field, Before = first.Before, After = diff.After };
            }
            else
            {
                merged[diff.Field] = diff;
                order.Add(diff.Field);
            }
        }

        return order.Select(field => merged[field]);
    }

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

    /// <summary>ページの上の1行。無ければ空。</summary>
    public string OthersText => Others.Count == 0 ? string.Empty : $"ほかに変わったところ：{string.Join("、", Others)}";
}
