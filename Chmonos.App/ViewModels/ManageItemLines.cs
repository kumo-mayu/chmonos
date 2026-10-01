namespace Chmonos.App.ViewModels;

/// <summary>
/// 管理の画面（タグ・属性）で、中の商品を横に並べた1段。仮想化の単位。
///
/// 中の商品を WrapPanel に全部並べていた頃は、見えていない商品の行・カードまで全部作っていた
/// （2000件の属性を開くと約8秒固まり、タグの管理で小分類をすべて開くとメモリが約300MB増えた。2026-09-24）。
/// WPF には仮想化する WrapPanel が無いので、検索画面と同じく段に切ってから、段を仮想化した一覧に並べる。
/// 見た目の結び先は段ごとの型で分ける（リストの行とカードで見た目が違う）。
/// </summary>
public abstract class ManageItemLine
{
    public required IReadOnlyList<TagItemRow> Items { get; init; }
}

/// <summary>リスト（詰まった行）の1段。</summary>
public sealed class ManageListLine : ManageItemLine
{
}

/// <summary>カードの1段。</summary>
public sealed class ManageCardLine : ManageItemLine
{
}

/// <summary>
/// 商品を段に切る。段の中の数は、前の並べ方（WrapPanel・FlowGridPanel）が1段に置いていた数と同じにする。
/// </summary>
public static class ManageItemLayout
{
    /// <summary>
    /// リストの行1つが占める幅（<c>ManageItemTemplate</c>：中身 230 ＋ 押せる枠の余白 4×2 ＋ 右の間 10）。
    /// 行の幅は決め打ちなので、前の並べ方でも全部の行が同じ幅だった
    /// </summary>
    public const double ListSlotWidth = 248;

    /// <summary>
    /// 幅 <paramref name="width"/> に、1つ <paramref name="slot"/> の物がいくつ入るか。
    /// WrapPanel も FlowGridPanel も、はみ出す手前までを1段に置く（少なくとも1つ）
    /// </summary>
    public static int ColumnsFor(double width, double slot)
        => width <= 0 || slot <= 0 ? 1 : Math.Max(1, (int)Math.Floor((width + 0.01) / slot));

    /// <summary>
    /// 段に切る。<paramref name="vertical"/> は「上から下へ流して次の列へ」（属性の管理の「縦に並べる」。FlowGridPanel と同じ割り付け：
    /// 段の数を「件数 ÷ 列」で出し、i 番目を (i mod 段, i ÷ 段) に置く）。
    /// </summary>
    public static List<IReadOnlyList<TagItemRow>> Split(IReadOnlyList<TagItemRow> items, int columns, bool vertical)
    {
        var lines = new List<IReadOnlyList<TagItemRow>>();
        if (items.Count == 0)
        {
            return lines;
        }

        columns = Math.Clamp(columns, 1, items.Count);
        var rows = (items.Count + columns - 1) / columns;

        for (var row = 0; row < rows; row++)
        {
            var line = new List<TagItemRow>(columns);
            for (var column = 0; column < columns; column++)
            {
                var index = vertical ? column * rows + row : row * columns + column;
                if (index < items.Count)
                {
                    line.Add(items[index]);
                }
            }

            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// 前に作った段のうち、中身が同じ物は同じ段をそのまま使う。
    /// 段を作り直すと、見えている段の部品（カード）が全部作り直される
    /// </summary>
    private static ManageItemLine Reuse(IReadOnlyList<TagItemRow> items, bool card, IEnumerable<ManageItemLine> previous, HashSet<ManageItemLine> used)
    {
        foreach (var line in previous)
        {
            // 同じ段を一覧に2回置かない（同じ商品が2つの小分類に入っていると、中身の同じ段ができる）
            if (line is ManageCardLine == card && !used.Contains(line) && SameItems(line.Items, items))
            {
                return line;
            }
        }

        return card ? new ManageCardLine { Items = items } : new ManageListLine { Items = items };
    }

    /// <summary>前の段を、先頭の商品で引けるようにしておく（段を探し直すのに全部を舐めない）。</summary>
    public static Dictionary<TagItemRow, List<ManageItemLine>> IndexByFirst(IEnumerable<object> lines)
    {
        var index = new Dictionary<TagItemRow, List<ManageItemLine>>(ReferenceEqualityComparer.Instance);
        foreach (var line in lines.OfType<ManageItemLine>())
        {
            if (line.Items.Count == 0)
            {
                continue;
            }

            if (!index.TryGetValue(line.Items[0], out var list))
            {
                list = [];
                index[line.Items[0]] = list;
            }

            list.Add(line);
        }

        return index;
    }

    /// <summary>
    /// 段を作る。前の段の索引（<see cref="IndexByFirst"/>）から、同じ中身の段は使い回す。
    /// <paramref name="used"/> はこの組み直しで既に置いた段（同じ段を2回置かないため）
    /// </summary>
    public static ManageItemLine Line(
        IReadOnlyList<TagItemRow> items, bool card, Dictionary<TagItemRow, List<ManageItemLine>> previous, HashSet<ManageItemLine> used)
    {
        var line = items.Count > 0 && previous.TryGetValue(items[0], out var candidates)
            ? Reuse(items, card, candidates, used)
            : card ? new ManageCardLine { Items = items } : new ManageListLine { Items = items };
        used.Add(line);
        return line;
    }

    private static bool SameItems(IReadOnlyList<TagItemRow> a, IReadOnlyList<TagItemRow> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// 平らな一覧の最後の1行（開いた商品を囲む枠の下側と、その下の札）。見た目の結び先は画面そのもの（<see cref="Owner"/>）。
/// 画面ごとに型を分けたいときのために、持ち主の型は見た目の側で決める
/// </summary>
public sealed class ManageFoot(object owner)
{
    public object Owner { get; } = owner;
}
