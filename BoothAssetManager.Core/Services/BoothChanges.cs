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
    /// <summary>
    /// 変わったところを並べる。何も変わっていなければ空。
    ///
    /// **スキ数は見ない。**必ず動くので、毎週全商品が「変わった」になる。
    /// **説明文の全文も見ない。**出品者が誤字を直しただけで出てしまう。
    /// 説明文の構造が壊れた場合は別の枠（<see cref="NotificationKind.PageStructureChanged"/>）が既にある。
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
                Field = "種類",
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

        return diffs;
    }

    /// <summary>要確認の1行にする。開かなくても判断できるように、変わったところを並べる。</summary>
    public static string Summarize(IReadOnlyList<NotificationDiff> diffs)
        => string.Join(" / ", diffs.Select(diff => diff.Before is null && diff.After is null
            ? diff.Field
            : $"{diff.Field} {diff.Before ?? "（無し）"} → {diff.After ?? "（無し）"}"));
}
