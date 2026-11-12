namespace Chmonos.App.ViewModels;

/// <summary>
/// 選んだ商品の帯を持つ画面（検索・フォルダの表示の右側・ショップの中）。
/// Ctrl・Shift を押しながらのクリックで選ぶ入口（ユーザ指示 2026-11-12「Ctrl-クリック・Shift-クリックで複数選択に移れていない。
/// windows の標準的な動作に揃えよう」）。帯の無い画面（アバター・改変・管理）は持たないので、そこでは今までどおり開く。
/// </summary>
public interface ICardClickSelectionHost
{
    /// <summary>
    /// 修飾キーを押しながらカード・行を押した。選び方を変えたら true（呼ぶ側は商品ページを開かない）。
    /// どちらも押していなければ何もせず false
    /// </summary>
    bool SelectByClick(ItemCardViewModel card, bool control, bool shift);

    /// <summary>
    /// 1件だけを切り替えた（左上のチェック・右クリックの「選ぶ」・選んでいる最中のクリックや Enter）。
    /// 次の Shift+クリックの起点にする（Explorer で最後に押した物が起点になるのと同じ）
    /// </summary>
    void NoteSelectionAnchor(ItemCardViewModel card);
}

/// <summary>
/// Explorer に揃えたクリックでの選び方（ユーザ指示 2026-11-12）。検索・フォルダ・ショップの3つの画面が1つずつ持つ。
///
/// - Ctrl+クリック：その1件を切り替え、起点にする（外したときも起点は移る。Explorer と同じ）
/// - Shift+クリック：起点からその1件までを選び、ほかの選択は外す。起点は動かさない（続けて Shift で範囲を伸び縮みさせられる）
/// - Ctrl+Shift+クリック：起点からその1件までを、今の選択に足す
/// - 起点が無い・今の並びに居ない（絞り込みで消えた）ときは、押した1件だけを範囲にし、そこを起点にする
///
/// 範囲は画面の並び（検索の結果の順）で取る。並びは ViewModel が持つ全件なので、仮想化で作られていないカードも入る。
/// まとめて変えるのは画面の <c>ChangeSelectionTogether</c> を通す（1件ごとに知らせると2万件で画面が止まった。外部の点検 2026-10-07）
/// </summary>
internal sealed class CardClickSelection
{
    private ItemCardViewModel? _anchor;

    /// <summary>今の起点（試験で見る）。</summary>
    public ItemCardViewModel? Anchor => _anchor;

    public void NoteAnchor(ItemCardViewModel card) => _anchor = card;

    /// <summary>選択を全部外したとき・一覧を作り直したとき。外した後の Shift は、押した1件から始める</summary>
    public void Forget() => _anchor = null;

    /// <param name="order">画面の並び（商品だけ。札・フォルダは含めない）</param>
    /// <param name="all">選んでいる物を探す全部（絞り込みで隠れた物も。Shift だけのときに外すため）</param>
    /// <param name="together">画面のまとめて変える入口（知らせを最後に1回にする）</param>
    public bool Click(
        ItemCardViewModel card,
        bool control,
        bool shift,
        IReadOnlyList<ItemCardViewModel> order,
        IEnumerable<ItemCardViewModel> all,
        Action<Action> together)
    {
        if (!card.CanSelect || (!control && !shift))
        {
            return false;
        }

        if (!shift)
        {
            card.IsSelected = !card.IsSelected;
            _anchor = card;
            return true;
        }

        var range = Range(order, _anchor, card);
        if (range.Count == 1)
        {
            _anchor = card;
        }

        together(() =>
        {
            if (!control)
            {
                // Explorer の Shift+クリックは、範囲のほかの選択を外す。絞り込みで隠れて選ばれている物も外す
                // （隠れた物だけ残ると、帯の件数と見えている範囲が合わず、まとめて操作で思わぬ物まで送る）
                var inRange = new HashSet<ItemCardViewModel>(range);
                foreach (var other in all.Where(other => other.IsSelected && !inRange.Contains(other)).ToList())
                {
                    other.IsSelected = false;
                }
            }

            foreach (var member in range)
            {
                member.IsSelected = true;
            }
        });
        return true;
    }

    /// <summary>
    /// 起点から押した1件までの並び（両端を含む。上下どちらからでも）。
    /// 起点が無い・並びに居ないとき、押した1件が並びに居ないときは、押した1件だけ
    /// </summary>
    public static IReadOnlyList<ItemCardViewModel> Range(
        IReadOnlyList<ItemCardViewModel> order, ItemCardViewModel? anchor, ItemCardViewModel target)
    {
        var to = IndexOf(order, target);
        var from = anchor is null ? -1 : IndexOf(order, anchor);
        if (to < 0 || from < 0)
        {
            return [target];
        }

        var (start, end) = from <= to ? (from, to) : (to, from);
        var range = new List<ItemCardViewModel>(end - start + 1);
        for (var index = start; index <= end; index++)
        {
            range.Add(order[index]);
        }

        return range;
    }

    private static int IndexOf(IReadOnlyList<ItemCardViewModel> order, ItemCardViewModel card)
    {
        for (var index = 0; index < order.Count; index++)
        {
            if (ReferenceEquals(order[index], card))
            {
                return index;
            }
        }

        return -1;
    }
}
