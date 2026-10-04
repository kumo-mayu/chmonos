using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 未読の更新の知らせに、後から来た更新の差を重ねる（ユーザ判断 2026-10-02「重ねましょう」）。
///
/// 前は新しい知らせが古い未読を差し替えていて、既読にする前に2回変わると、1回目の差（変わった行も）が消えていた。
/// 重ねた結果は「最初の前 → 最後の後」の差として読めるようにする。途中の値は人が判断するのに要らない。
///
/// 前の本文は保存していない（知らせには変わった行しか残らない）ので、説明文は行の差どうしを重ねる。
/// </summary>
public static class ChangeStack
{
    /// <summary>
    /// <paramref name="earlier"/>（未読の知らせの差）に <paramref name="later"/>（新しく取り直した差）を重ねる。
    /// 欄の並びは前の知らせの順、新しく変わった欄はその後ろ。戻って元と同じになった欄は外す（全部外れたら空）。
    /// </summary>
    public static IReadOnlyList<NotificationDiff> Stack(IReadOnlyList<NotificationDiff> earlier, IReadOnlyList<NotificationDiff> later)
    {
        var merged = new Dictionary<string, NotificationDiff>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var diff in earlier.Concat(later))
        {
            if (merged.TryGetValue(diff.Field, out var first))
            {
                merged[diff.Field] = Combine(first, diff);
            }
            else
            {
                merged[diff.Field] = diff;
                order.Add(diff.Field);
            }
        }

        return order.Select(field => merged[field]).Where(diff => !IsBackToStart(diff)).ToList();
    }

    /// <summary>
    /// 同じ欄の2回の変化を1つにする。短い値（商品名・価格・件数・販売）は「最初の前 → 最後の後」。
    /// 説明文の見出しの頭の抜き出しも同じで、変わった行は <see cref="StackLines"/> で重ねる。
    /// </summary>
    private static NotificationDiff Combine(NotificationDiff first, NotificationDiff second)
    {
        if (first.Lines is null && second.Lines is null)
        {
            return new NotificationDiff
            {
                Field = first.Field,
                Before = first.Before,
                After = second.After,
                MoreAdded = Sum(first.MoreAdded, second.MoreAdded),
                MoreRemoved = Sum(first.MoreRemoved, second.MoreRemoved),
                Follows = first.Follows ?? second.Follows,
                Prices = StackPrices(first.Prices, second.Prices),
            };
        }

        var lines = StackLines(first.Lines ?? [], second.Lines ?? []);

        // 上限は種類ごとに当て直す（重ねると30行を超えることがある）。超えた分は数だけ残す。BoothChanges.WithLines と同じ切り方
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
            Field = first.Field,
            Before = first.Before,
            After = second.After,
            Lines = kept,
            MoreAdded = Sum(Sum(first.MoreAdded, second.MoreAdded), Over(added)),
            MoreRemoved = Sum(Sum(first.MoreRemoved, second.MoreRemoved), Over(removed)),
            Follows = first.Follows ?? second.Follows,
        };
    }

    /// <summary>
    /// 行の差どうしを重ねる。
    /// - 後で消えた行が、前に足した行にあれば、足した側から取り除く（足して消した＝初めから無かった）
    /// - 後で足した行が、前に消した行にあれば、消した側から取り除く（消して戻した＝元のまま）
    /// - どちらでもなければ後ろに続ける
    ///
    /// 同じ文の行が何回あるかは1本ずつ数える（同じ行を2本足して1本消したら、1本足したが残る）。
    /// 上限を超えて数だけ残した行（<see cref="NotificationDiff.MoreAdded"/>）は文が無いので打ち消せない。そのときは両方が残る
    /// </summary>
    public static List<NotificationLine> StackLines(IReadOnlyList<NotificationLine> first, IReadOnlyList<NotificationLine> second)
    {
        var result = first.ToList();
        foreach (var line in second)
        {
            var opposite = line.Kind == NotificationLineKind.Added ? NotificationLineKind.Removed : NotificationLineKind.Added;
            var index = result.FindIndex(entry => entry.Kind == opposite && string.Equals(entry.Text, line.Text, StringComparison.Ordinal));
            if (index >= 0)
            {
                result.RemoveAt(index);
            }
            else
            {
                result.Add(line);
            }
        }

        return result;
    }

    /// <summary>
    /// バリエーションの値段の変化を重ねる（ID ごとに「最初の前 → 最後の後」）。戻って元の値段になった物は外す。
    /// 並びは前の知らせの順、新しく変わった物はその後ろ。名前は後の知らせの物（今の名前に近い方）
    /// </summary>
    private static IReadOnlyList<NotificationPrice>? StackPrices(IReadOnlyList<NotificationPrice>? first, IReadOnlyList<NotificationPrice>? second)
    {
        if (first is null && second is null)
        {
            return null;
        }

        var merged = new List<NotificationPrice>(first ?? []);
        foreach (var price in second ?? [])
        {
            var index = merged.FindIndex(entry => entry.Id == price.Id);
            if (index >= 0)
            {
                merged[index] = new NotificationPrice { Id = price.Id, Name = price.Name ?? merged[index].Name, Before = merged[index].Before, After = price.After };
            }
            else
            {
                merged.Add(price);
            }
        }

        merged.RemoveAll(price => price.Before == price.After);
        return merged.Count > 0 ? merged : null;
    }

    /// <summary>
    /// 戻って元と同じになった欄か：前と後が同じで、変わった行も残っていない。
    /// 頭の抜き出しが同じでも行が残っていれば外さない（見出しの後ろの方だけが変わった、が前の知らせの困りごとだった）
    /// </summary>
    private static bool IsBackToStart(NotificationDiff diff)
        => string.Equals(diff.Before, diff.After, StringComparison.Ordinal)
            && (diff.Lines is null || diff.Lines.Count == 0)
            && (diff.MoreAdded ?? 0) == 0
            && (diff.MoreRemoved ?? 0) == 0
            && (diff.Prices is null || diff.Prices.Count == 0);

    private static int? Over(int count) => count > LineDiff.MaxLines ? count - LineDiff.MaxLines : null;

    private static int? Sum(int? first, int? second) => first is null && second is null ? null : (first ?? 0) + (second ?? 0);
}
