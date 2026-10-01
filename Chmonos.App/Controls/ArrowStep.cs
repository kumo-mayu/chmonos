using System.Windows;

namespace Chmonos.App.Controls;

/// <summary>並びの中で矢印・Home・End を押したときの行き先（<see cref="ArrowGroup"/>）。</summary>
public enum ArrowMove
{
    Previous,
    Next,
    Up,
    Down,
    First,
    Last,
}

/// <summary>
/// 並びの中の止まり先（札・札の ×・行のボタン）の、並びの中での場所。
/// <see cref="IsInner"/> は、止まれる物の中にある止まれる物（札の中の ×）。上下に移るときは札そのものへ移り、× へは降りない
/// </summary>
public readonly record struct ArrowSpot(Rect Bounds, bool IsInner);

/// <summary>
/// 並び（札の並び・行の一覧）の中で、矢印を押したら次にどこへ止まるかの計算。部品を作らずに試験で確かめられるよう、場所だけを受け取る。
/// </summary>
public static class ArrowStep
{
    /// <summary>
    /// 左右は並びの順（札 → その × → 次の札）に1つずつ。上下は、上・下の段のうち、横の位置がいちばん近い札。
    /// 端では止まる（回り込まない）——回り込むと、最後の札から最初の札へ飛んだのか、何も起きないのかが見分けにくい。
    /// 行き先が無ければ null（押しても動かない）。
    /// </summary>
    public static int? Next(IReadOnlyList<ArrowSpot> spots, int current, ArrowMove move)
    {
        if (spots.Count == 0 || current < 0 || current >= spots.Count)
        {
            return null;
        }

        return move switch
        {
            ArrowMove.Previous => current > 0 ? current - 1 : null,
            ArrowMove.Next => current < spots.Count - 1 ? current + 1 : null,
            ArrowMove.First => current == 0 ? null : 0,
            ArrowMove.Last => current == spots.Count - 1 ? null : spots.Count - 1,
            ArrowMove.Up => Vertical(spots, current, down: false),
            ArrowMove.Down => Vertical(spots, current, down: true),
            _ => null,
        };
    }

    private static int? Vertical(IReadOnlyList<ArrowSpot> spots, int current, bool down)
    {
        var from = spots[current].Bounds;
        var centerX = from.Left + from.Width / 2;

        // 「別の段」は、今の物と縦に重ならない物。× は札の中にあるので、札と同じ段に数える
        var others = Enumerable.Range(0, spots.Count)
            .Where(index => !spots[index].IsInner)
            .Where(index => down ? spots[index].Bounds.Top >= from.Bottom - 0.5 : spots[index].Bounds.Bottom <= from.Top + 0.5)
            .ToList();
        if (others.Count == 0)
        {
            return null;
        }

        // いちばん近い段：段の上端がいちばん近い物と、縦に重なっている物
        var nearest = down
            ? others.MinBy(index => spots[index].Bounds.Top)
            : others.MaxBy(index => spots[index].Bounds.Bottom);
        var line = spots[nearest].Bounds;
        return others
            .Where(index => spots[index].Bounds.Top < line.Bottom && spots[index].Bounds.Bottom > line.Top)
            .MinBy(index => Math.Abs(spots[index].Bounds.Left + spots[index].Bounds.Width / 2 - centerX));
    }

    /// <summary>
    /// 1件1行の一覧（行を見える分だけ作る物）で、上下・Home・End を押したときの行の番号。
    /// 作られていない行へも番号で移る（行の部品をたどる計算では、画面の外の行へ進めない）。
    /// </summary>
    public static int? Row(int current, int count, ArrowMove move)
    {
        if (count == 0 || current < 0 || current >= count)
        {
            return null;
        }

        var target = move switch
        {
            ArrowMove.Up => current - 1,
            ArrowMove.Down => current + 1,
            ArrowMove.First => 0,
            ArrowMove.Last => count - 1,
            _ => current,
        };
        return target >= 0 && target < count && target != current ? target : null;
    }

    /// <summary>
    /// 隣の行に同じ役の物が2つ以上あるとき（カードの段：1段にカードが何枚も並ぶ）に、どれへ止まるか。
    /// 上下は横の位置がいちばん近い物、Home は先頭、End は最後。<paramref name="candidates"/> が空なら null。
    /// 段の中のカードは横に並ぶので、番目で選ぶと、最後の段のように枚数の少ない段で左へずれる（今までのカードの矢印と同じ「縦は近い位置」）
    /// </summary>
    public static int? InRow(IReadOnlyList<ArrowSpot> candidates, double centerX, ArrowMove move)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return move switch
        {
            ArrowMove.First => 0,
            ArrowMove.Last => candidates.Count - 1,
            _ => Enumerable.Range(0, candidates.Count)
                .MinBy(index => Math.Abs(candidates[index].Bounds.Left + candidates[index].Bounds.Width / 2 - centerX)),
        };
    }

    /// <summary>
    /// フォーカスのあった行が一覧から消えた（「除外を解除」で行が無くなった）あとに止まる行。
    /// 次の行（消えた行の位置に詰めて来た行）、無ければ前の行。一覧が空なら null。
    /// 窓そのものへ落とすと、次の Tab が画面の先頭（ナビの「戻る」）から始まり、一覧の中の位置を失う
    /// </summary>
    public static int? AfterRemoval(int removedIndex, int countAfter)
    {
        if (countAfter <= 0)
        {
            return null;
        }

        return Math.Clamp(removedIndex, 0, countAfter - 1);
    }
}
