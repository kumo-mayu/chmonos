namespace Chmonos.App.ViewModels;

/// <summary>
/// カードを行に切って並べる（WPF には仮想化する WrapPanel が無いので、列数を決めて行を仮想化の単位にする）。
///
/// **並びの合っているカードには触らず、ずれた所だけを抜き差しする。**行を全部作り直すと、見えている行のカードの見た目が
/// 全部作り直されて画面が固まる（検索で 146〜380ms・U28）。一覧の右下のスライダーでカードの大きさを変えると
/// 列数が続けて変わるので、ショップとフォルダも同じ形にした（2026-09-29。前は行を丸ごと作り直していた）。
/// </summary>
internal static class CardRowLayout
{
    public static void Apply<TRow, TCard>(
        IList<TRow> rows,
        IReadOnlyList<TCard> items,
        int columns,
        Func<TRow> newRow,
        Func<TRow, IList<TCard>> cardsOf)
        where TCard : class
    {
        columns = Math.Max(1, columns);
        var needed = (items.Count + columns - 1) / columns;

        while (rows.Count > needed)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        while (rows.Count < needed)
        {
            rows.Add(newRow());
        }

        for (var row = 0; row < needed; row++)
        {
            var cards = cardsOf(rows[row]);
            var start = row * columns;
            var count = Math.Min(columns, items.Count - start);

            for (var index = 0; index < count; index++)
            {
                var card = items[start + index];
                if (index < cards.Count && ReferenceEquals(cards[index], card))
                {
                    continue;
                }

                // 同じ行の後ろにあるなら、手前のずれた物を抜いて詰める（列が増えたときの普通の形）
                var later = -1;
                for (var look = index + 1; look < cards.Count; look++)
                {
                    if (ReferenceEquals(cards[look], card))
                    {
                        later = look;
                        break;
                    }
                }

                if (later > 0)
                {
                    for (var remove = later - 1; remove >= index; remove--)
                    {
                        cards.RemoveAt(remove);
                    }
                }
                else
                {
                    cards.Insert(index, card);
                }
            }

            while (cards.Count > count)
            {
                cards.RemoveAt(cards.Count - 1);
            }
        }
    }
}
