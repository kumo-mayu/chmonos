using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chmonos.App.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 商品ページの説明の欄の Tab の順を、見た目の上から下の順にする（計画 37・ユーザ判断 2026-11-12「直しましょう」）。
///
/// 前は2つのずれで、説明を読んでいる所から Tab を押すと欄の頭まで流れ戻っていた。
/// - 「すべて折りたたむ」は欄の見出しの右に重ねてあるが、書いた順では見出しの並びの後ろにある（重ねるため Expander の後に書く。前に書くと、
///   見出しの押す所が欄の幅いっぱいに広がっているので、上に重なってボタンが押せなくなる）。節の本文から Tab を押すと、下の見出しを飛ばしてここへ上り、
///   もう1回で右の列の頭の星へ上った
/// - 見出しは並び（<see cref="ArrowGroup"/>）で、Tab で止まるのは1つだけ。本文（Tab で止まらない、マウスで触った所）から Tab を押すと、
///   すぐ下の見出しではなく、並びの止まる1つ（前に触った見出し）か欄の外へ移った
///
/// そこで、「すべて折りたたむ」は Tab の流れから外して欄の三角の次に手でつなぎ、本文からの Tab はすぐ下の見出しへ移す。
/// ブラウザで文の途中を押してから Tab を押すと、押した所より後ろの最初の止まり先へ移るのと同じ考え。
/// 欄の最後の本文（下に見出しが無い）からは WPF の既定のまま、ページの次の止まり先（左の列の後ろ＝右の列の頭の星）へ移る。
/// 左の列の後に右の列を通る順は Windows の普通の Tab の順で、変えるならページ全体の決め事（docs/plan.md）
/// </summary>
internal static class DescriptionTabs
{
    /// <summary>
    /// 本文から Tab で移る見出しの段。<paramref name="fromRow"/> は本文の段（見出しの無い説明の本文は -1、節の本文はその節の番号）、
    /// <paramref name="headerRows"/> は見出しが見えている節の番号（小さい順）。無ければ null（WPF の既定に任せる）。
    /// 前へ（Shift+Tab）はその節の見出しへ戻る。見出しの無い説明の本文から前へは、既定のまま欄の三角へ戻る
    /// </summary>
    internal static int? HeaderRowFromBody(int fromRow, IReadOnlyList<int> headerRows, bool backward)
    {
        if (backward)
        {
            return fromRow >= 0 && headerRows.Contains(fromRow) ? fromRow : null;
        }

        foreach (var row in headerRows)
        {
            if (row > fromRow)
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 説明の欄の中で Tab を押したときに、手で移す先。既定（書いた順）のままでよければ null。
    /// <paramref name="focused"/> は今の止まり（本文の中のリンクなら、そのリンク）
    /// </summary>
    internal static IInputElement? Target(Expander card, Button toggleAll, ItemsControl sections, DependencyObject? focused, bool backward)
    {
        if (focused is null)
        {
            return null;
        }

        var toggle = Descendants(card).OfType<ExpandToggle>().FirstOrDefault();
        var toggleAllShown = toggleAll is { IsVisible: true, IsEnabled: true };

        // 欄の三角 → すべて折りたたむ → 見出しの並び、の順（見た目の順）
        if (ReferenceEquals(focused, toggle))
        {
            return !backward && toggleAllShown ? toggleAll : null;
        }

        if (ReferenceEquals(focused, toggleAll))
        {
            if (backward)
            {
                return toggle;
            }

            // 三角の次（見出しの並びの止まる1つ）。見出しが無ければ既定のまま欄の外へ
            return ArrowGroup.MembersOf(sections).FirstOrDefault(member => member.IsVisible && KeyboardNavigation.GetIsTabStop(member));
        }

        var headers = Headers(sections);
        if (focused is UIElement element && headers.Any(header => ReferenceEquals(header.Element, element)))
        {
            // 見出しから前へは、並びの前の「すべて折りたたむ」へ（Tab の流れから外したので、既定では三角へ飛ぶ）。後ろへは既定のまま欄の外へ
            return backward && toggleAllShown ? toggleAll : null;
        }

        if (BodyOf(focused) is not { } body)
        {
            return null;
        }

        var fromRow = sections.ContainerFromElement(body) is { } container
            ? sections.ItemContainerGenerator.IndexFromContainer(container)
            : -1;
        var row = HeaderRowFromBody(fromRow, headers.Select(header => header.Row).ToList(), backward);
        return row is { } target ? headers.First(header => header.Row == target).Element : null;
    }

    /// <summary>見えている見出しと、その節の番号（小さい順）。消えた見出しの行（隠した物）は数えない。</summary>
    private static List<(UIElement Element, int Row)> Headers(ItemsControl sections)
    {
        var result = new List<(UIElement, int)>();
        foreach (var member in ArrowGroup.MembersOf(sections))
        {
            if (member.IsVisible && sections.ContainerFromElement(member) is { } container
                && sections.ItemContainerGenerator.IndexFromContainer(container) is var row and >= 0)
            {
                result.Add((member, row));
            }
        }

        return result.OrderBy(header => header.Item2).ToList();
    }

    /// <summary>本文（選べる文の部品）。止まりが本文の中のリンクのときも、そのリンクを持つ本文を返す。</summary>
    private static RichTextBox? BodyOf(DependencyObject focused)
    {
        for (DependencyObject? node = focused; node is not null;)
        {
            if (node is RichTextBox box)
            {
                // Tab で止まる入力欄は対象にしない（本文は型で Tab の止まりから外してある）
                return KeyboardNavigation.GetIsTabStop(box) ? null : box;
            }

            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }
}
