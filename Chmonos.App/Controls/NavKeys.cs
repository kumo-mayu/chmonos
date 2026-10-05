using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chmonos.App.Views;

namespace Chmonos.App.Controls;

/// <summary>
/// ナビ（左の項目の縦の並び）をキーボードで移りやすくする（メモ47・ユーザ判断 2026-10-05「案X」）。
/// ナビの入れ物に <c>controls:NavKeys.IsEnabled="True"</c> を付ける。
///
/// - 戻る・進む・畳む・各画面の項目・「設定」を1つの並びとして、↑↓で1つずつ移る（横に並ぶ戻る・進むの間だけ ←→）。端では止まる。Home・End は端へ
/// - 外から Tab（Shift+Tab）で入ると、今開いている画面の項目に止まる（前は戻るから14回以上 Tab を押して目当ての項目へ着いた）
/// - ナビの中の Tab・Shift+Tab は WPF の既定のまま1つずつ。「設定」から Tab で画面の中身へ、一番上から Shift+Tab で前へ出る
///
/// <see cref="ArrowGroup"/> に寄せなかったのは、向きが逆だから。ArrowGroup は「Tab では1回だけ止まる」塊を作る（中の止まり先を Tab で止まらなくする）が、
/// ナビは Tab でも1つずつ動けるままにしたい。さらに入れ物が2つに分かれる（送る入れ物の中と、送らずに下端へ固定した「設定」・BOOTH の形式の知らせ）ので、
/// ItemsControl の板を前提にする ArrowGroup には載らない。ナビの項目は手で並べた十数個のボタンで、上下の移りは「並びの順に隣へ」で足りる
/// （ArrowGroup の位置で近い物を探す作りは要らない）ので、ここだけの小さな処理にした。
/// </summary>
public static class NavKeys
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NavKeys), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>ナビの止まり先を、並びの順に。見えていて押せる物だけ（履歴が無いときの「戻る」・出ていない知らせは入らない）。</summary>
    public static List<Button> Members(DependencyObject rail)
    {
        var result = new List<Button>();
        Collect(rail, result);
        return result;
    }

    private static void Collect(DependencyObject node, List<Button> result)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is not UIElement { IsVisible: true })
            {
                continue;
            }

            if (child is Button { Focusable: true, IsEnabled: true } button && KeyboardNavigation.GetIsTabStop(button))
            {
                result.Add(button);
                continue;
            }

            Collect(child, result);
        }
    }

    /// <summary>
    /// 矢印・Home・End で、どこへ移るか（並びの中の番号。移れなければ null）。
    /// <paramref name="tops"/> は止まり先の上端の位置。同じ高さの物は1行（戻る・進む）で、↑↓ は行の先頭へ、←→ は行の中の隣へ移る。
    /// 隣の行の「近い物」ではなく先頭にするのは、戻る・進むの下の「畳む」から ↑ で、右側の進むへ寄らず戻るへ着くため
    /// </summary>
    public static int? Move(IReadOnlyList<double> tops, int current, Key key)
    {
        if (current < 0 || current >= tops.Count)
        {
            return null;
        }

        var first = current;
        while (first > 0 && SameRow(tops[first - 1], tops[current]))
        {
            first--;
        }

        var last = current;
        while (last < tops.Count - 1 && SameRow(tops[last + 1], tops[current]))
        {
            last++;
        }

        switch (key)
        {
            case Key.Left:
                return current > first ? current - 1 : null;
            case Key.Right:
                return current < last ? current + 1 : null;
            case Key.Up:
                if (first == 0)
                {
                    return null;
                }

                var upper = first - 1;
                while (upper > 0 && SameRow(tops[upper - 1], tops[upper]))
                {
                    upper--;
                }

                return upper;
            case Key.Down:
                return last < tops.Count - 1 ? last + 1 : null;
            case Key.Home:
                return current == 0 ? null : 0;
            case Key.End:
                return current == tops.Count - 1 ? null : tops.Count - 1;
            default:
                return null;
        }
    }

    private static bool SameRow(double a, double b) => Math.Abs(a - b) < 1;

    /// <summary>
    /// ナビの外から Tab で入ってきたとき、代わりに止まらせる物（今開いている画面の項目）。
    /// <paramref name="oldFocus"/> は今まで止まっていた所、<paramref name="newFocus"/> は WPF の Tab が選んだ行き先。
    /// ナビの中の移りのとき・今の画面の項目が無いとき・もう今の画面の項目に着いたときは null（既定のまま）
    /// </summary>
    public static Button? EntryTarget(UIElement rail, DependencyObject? oldFocus, DependencyObject? newFocus)
    {
        if ((oldFocus is not null && IsInside(rail, oldFocus)) || newFocus is not Button button || !IsInside(rail, button))
        {
            return null;
        }

        var active = Members(rail).FirstOrDefault(member => Nav.GetIsActive(member));
        return active is null || ReferenceEquals(active, button) ? null : active;
    }

    /// <summary>この部品がナビの止まり先で、矢印をナビが受けるか。窓の左右の絵送りが先に横取りしないために見る。</summary>
    public static bool OwnsArrows(DependencyObject? element)
    {
        if (element is not Button)
        {
            return false;
        }

        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement && GetIsEnabled(node))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInside(UIElement rail, DependencyObject element)
        => ReferenceEquals(rail, element) || (element is Visual visual && rail.IsAncestorOf(visual));

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement rail)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            rail.PreviewKeyDown += OnRailKeyDown;
            rail.PreviewGotKeyboardFocus += OnRailPreviewFocus;
        }
        else
        {
            rail.PreviewKeyDown -= OnRailKeyDown;
            rail.PreviewGotKeyboardFocus -= OnRailPreviewFocus;
        }
    }

    // Tab を押している間だけ真。マウスで押したとき・プログラムから移したときと、Tab で移ったときを見分ける
    // （キーの状態を OS に聞く Keyboard.IsKeyDown は、キーの知らせを送るだけの確かめの道具では偽のままになる）
    private static bool _tabDown;

    static NavKeys()
    {
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKeyDown), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.PreviewKeyUpEvent, new KeyEventHandler((_, e) => _tabDown = _tabDown && e.Key != Key.Tab), handledEventsToo: true);
    }

    private static void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab)
        {
            return;
        }

        // 知らせは経路の部品ごとに届くので、立ててあれば何もしない
        if (_tabDown)
        {
            return;
        }

        _tabDown = Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift;

        // 離したキーの知らせが届かなかったとき（窓が非アクティブになったなど）に、次のマウスの移りまで残さない
        ((DispatcherObject)sender).Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _tabDown = false);
    }

    private static void OnRailPreviewFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.Handled || !_tabDown || sender is not UIElement rail || e.Source is not Button)
        {
            return;
        }

        if (EntryTarget(rail, e.OldFocus as DependencyObject, e.NewFocus as DependencyObject) is { } target)
        {
            e.Handled = true;
            target.Focus();
        }
    }

    private static void OnRailKeyDown(object sender, KeyEventArgs e)
    {
        // 修飾キー付き（Alt+← の戻る・設定で割り当てたキー）は窓の物
        if (e.Handled || Keyboard.Modifiers != ModifierKeys.None
            || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
            || sender is not DependencyObject rail || Keyboard.FocusedElement is not Button focused)
        {
            return;
        }

        var members = Members(rail);
        var current = members.IndexOf(focused);
        if (current < 0)
        {
            return;
        }

        // 端で押したときも受けて止める。受けないと WPF の既定の矢印の移動が、ナビの外の部品へ飛ばす
        e.Handled = true;
        var tops = members.Select(member => member.TranslatePoint(new Point(0, 0), (UIElement)rail).Y).ToList();
        if (Move(tops, current, e.Key) is { } next)
        {
            members[next].Focus();
        }
    }
}
