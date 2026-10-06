using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 並び（札の並び・行の一覧）を、Tab では1回だけ止まる1つの塊にし、中は矢印で移れるようにする（ユーザ判断 2026-10-01）。
/// 繰り返しの一覧（<see cref="ItemsControl"/>）に <c>controls:ArrowGroup.IsEnabled="True"</c> を付ける。
///
/// 前は札・× ・行のボタンの1つずつに Tab が止まり、対応アバター29体の商品で欄を抜けるのに62回、除外400件の一覧で400回押した。
/// - Tab で並びに入ると、前にいた所（初めてなら先頭）に止まる。もう1回 Tab で並びの外へ出る
/// - ←→ は並びの順（札 → その × → 次の札）に1つずつ。↑↓ は上下の段の近い札（行の一覧では隣の行の同じボタン）。Home・End は端へ。端では止まる
/// - Enter・Space は止まっている物（札・ボタン）がそのまま受ける
/// - 止まっていた行が一覧から消えたら（「除外を解除」）、次の行、無ければ前の行に止まり直す
///
/// WPF の <c>KeyboardNavigation.TabNavigation="Once"</c> を使わないのは、並びの中にあっても別に Tab で止まりたい物があるため
/// （対応アバターの末尾の「＋ 追加」とその入力欄・「閉じる」。<see cref="IsOutsideProperty"/>）。Once は中を丸ごと1つにするので、
/// 「＋ 追加」へ矢印でしか届かず、入力欄から「閉じる」へ Tab で移れなくなる。
/// 代わりに、並びの中の止まり先のうち1つだけを Tab で止まる物にし、ほかは止まらないようにする（フォーカスは受けられるので、矢印で移れる）。
/// 行を見える分だけ作る一覧（仮想化）では、作られていない行へも番号で移る。
/// </summary>
public static class ArrowGroup
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ArrowGroup), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>並びの中にあっても、並びとは別に Tab で止まる所（中は数えない）。</summary>
    public static readonly DependencyProperty IsOutsideProperty = DependencyProperty.RegisterAttached(
        "IsOutside", typeof(bool), typeof(ArrowGroup), new PropertyMetadata(false));

    // 止まり先として数えた印。Tab で止まらないようにした（IsTabStop を偽にした）後も、止まり先として数え続けるため
    private static readonly DependencyProperty IsMemberProperty = DependencyProperty.RegisterAttached(
        "IsMember", typeof(bool), typeof(ArrowGroup), new PropertyMetadata(false));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(GroupState), typeof(ArrowGroup), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsOutside(DependencyObject element) => (bool)element.GetValue(IsOutsideProperty);

    public static void SetIsOutside(DependencyObject element, bool value) => element.SetValue(IsOutsideProperty, value);

    /// <summary>並びの中の止まり先を、並びの順に（試験・確かめの道具が、どれが Tab で止まる物かを見る）。</summary>
    public static IReadOnlyList<UIElement> MembersOf(ItemsControl list)
        => list.GetValue(StateProperty) is GroupState state ? state.Members().Select(member => member.Element).ToList() : [];

    /// <summary>
    /// この部品が並びの中の止まり先で、矢印を並びが受けるか。画面全体で矢印に役を持たせている所（商品ページの左右の絵送り）が、先に横取りしないために見る
    /// </summary>
    public static bool OwnsArrows(DependencyObject? element)
        => element is not null && (bool)element.GetValue(IsMemberProperty) && GroupOf(element) is not null;

    private static ItemsControl? GroupOf(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ItemsControl list && GetIsEnabled(list))
            {
                return list;
            }
        }

        return null;
    }

    /// <summary>
    /// 並びの中に、行より後から部品ができた（カードの中身は画面が空いたときに4枚ずつ作る：<see cref="DeferredCardHost"/>）。
    /// 行が作られたときの合わせ直しはもう済んでいるので、そのままだと新しいカードが1枚ずつ Tab で止まる。
    /// 並びに Tab で止まる物が既にあれば、できた所だけを止まらない物にする（並び全体を数え直すと、4枚ごとに全部のカードの中を下りることになる）
    /// </summary>
    public static void Adopt(UIElement part)
    {
        if (GroupOf(part) is { } list && list.GetValue(StateProperty) is GroupState state)
        {
            state.Adopt(part);
        }
    }

    /// <summary>並びが落ち着いた後の形にすぐ合わせる（試験用。アプリでは配置の後に自分で合わせる）。</summary>
    public static void RefreshNow(ItemsControl list) => (list.GetValue(StateProperty) as GroupState)?.Refresh();

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl list)
        {
            return;
        }

        if ((bool)e.NewValue && list.GetValue(StateProperty) is null)
        {
            var state = new GroupState(list);
            list.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (!(bool)e.NewValue && list.GetValue(StateProperty) is GroupState state)
        {
            state.Detach();
            list.ClearValue(StateProperty);
        }
    }

    private sealed record Member(UIElement Element, int Index, int Slot, bool IsInner);

    private sealed class GroupState(ItemsControl list)
    {
        // 前に止まっていた所。部品ではなく行（項目）と、行の中の何番目かで覚える——
        // 見える分だけ作る一覧は部品を使い回すので、部品で覚えると別の行の物を指してしまう
        private object? _item;
        private int _index = -1;
        private int _slot;
        private string? _slotId;
        private WeakReference<UIElement>? _focused;

        private UIElement? _stop;
        private bool _queued;

        // 並びの止まり先を持っていた行の型（商品の段など）。Tab で並びから出るとき、この型の行は作らせずに飛ばす（PrepareTabOut）
        private readonly HashSet<Type> _memberTypes = [];

        public void Attach()
        {
            list.AddHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocus), handledEventsToo: true);
            list.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
            list.ItemContainerGenerator.StatusChanged += OnGeneratorStatusChanged;
            ((INotifyCollectionChanged)list.Items).CollectionChanged += OnItemsChanged;
            list.Loaded += OnLoaded;
            Queue();
        }

        public void Detach()
        {
            list.RemoveHandler(UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocus));
            list.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
            list.ItemContainerGenerator.StatusChanged -= OnGeneratorStatusChanged;
            ((INotifyCollectionChanged)list.Items).CollectionChanged -= OnItemsChanged;
            list.Loaded -= OnLoaded;
            Hook(null);
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => Queue();

        private void OnGeneratorStatusChanged(object? sender, EventArgs e)
        {
            if (list.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
            {
                Queue();
            }
        }

        /// <summary>行が作られた・消えた・隠れたとき、Tab で止まる物を1つに合わせ直す（配置の後にまとめて1回）。</summary>
        private void Queue()
        {
            if (_queued)
            {
                return;
            }

            _queued = true;
            list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Refresh);
        }

        public void Refresh()
        {
            _queued = false;
            var members = Members();
            var stop = PickStop(members);
            foreach (var member in members)
            {
                SetTabStop(member.Element, ReferenceEquals(member.Element, stop));
            }

            Hook(stop);
        }

        public void Adopt(UIElement part)
        {
            if (_stop is not { IsVisible: true } stop || !list.IsAncestorOf(stop))
            {
                Queue();
                return;
            }

            var found = new List<Member>();
            var slot = 0;
            CollectFrom(part, 0, ref slot, found);
            foreach (var member in found)
            {
                SetTabStop(member.Element, ReferenceEquals(member.Element, stop));
            }
        }

        private static void SetTabStop(UIElement element, bool value)
        {
            if (KeyboardNavigation.GetIsTabStop(element) != value)
            {
                KeyboardNavigation.SetIsTabStop(element, value);
            }
        }

        /// <summary>Tab で入ったときに止まる物：前にいた所。消えていれば同じ位置の行、無ければ見えている先頭。</summary>
        private UIElement? PickStop(List<Member> members)
        {
            if (members.Count == 0)
            {
                return null;
            }

            var rowIndex = _item is not null ? IndexOf(_item) : _index >= 0 ? Math.Min(_index, list.Items.Count - 1) : -1;
            if (rowIndex >= 0 && SlotIn(members.Where(member => member.Index == rowIndex).ToList()) is { } remembered)
            {
                return remembered.Element;
            }

            if (ScrollHost() is { } scroll)
            {
                foreach (var member in members)
                {
                    if (member.Element.IsDescendantOf(scroll)
                        && member.Element.TranslatePoint(new Point(0, 0), scroll).Y >= -0.5)
                    {
                        return member.Element;
                    }
                }
            }

            return members[0].Element;
        }

        /// <summary>
        /// 行の中で、覚えた物と同じ物（同じ ID、無ければ同じ番目、それも無ければ行の先頭）。
        /// 先頭にするのは、要確認の行の操作のように止まった行でだけ出るボタンから移ったとき、行そのものに止まるため
        /// </summary>
        private Member? SlotIn(List<Member> row)
        {
            if (row.Count == 0)
            {
                return null;
            }

            // カードの段は同じ ID の物が並ぶので、同じ ID で同じ番目の物を先に見る（ID だけで探すと段の先頭のカードへ戻る）
            var sameId = _slotId is { Length: > 0 } id
                ? row.Where(member => AutomationProperties.GetAutomationId(member.Element) == id).ToList()
                : [];
            return sameId.FirstOrDefault(member => member.Slot == _slot)
                ?? sameId.FirstOrDefault()
                ?? row.FirstOrDefault(member => member.Slot == _slot)
                ?? row[0];
        }

        private int IndexOf(object item)
        {
            var items = list.Items;
            if (_index >= 0 && _index < items.Count && ReferenceEquals(items[_index], item))
            {
                return _index;
            }

            return items.IndexOf(item);
        }

        private void Hook(UIElement? stop)
        {
            if (ReferenceEquals(_stop, stop))
            {
                return;
            }

            if (_stop is not null)
            {
                _stop.IsVisibleChanged -= OnStopVisibleChanged;
            }

            _stop = stop;
            if (stop is not null)
            {
                stop.IsVisibleChanged += OnStopVisibleChanged;
            }
        }

        /// <summary>
        /// 止まる物が隠れた。アバター名で絞って札が隠れたときは、Tab で入る先を見えている物へ移す（そのままだと並びに入れなくなる）。
        /// フォーカスがあったまま隠れた（× で外した札は、捨てずに隠す）ときは、次の札（無ければ前の札）へ止まり直す
        /// </summary>
        private void OnStopVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(bool)e.NewValue && sender is UIElement stop
                && (stop.IsKeyboardFocusWithin || ReferenceEquals(Keyboard.FocusedElement, stop)))
            {
                var index = _index;
                _item = null;
                list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusNear(index));
            }

            Queue();
        }

        private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (e.NewFocus is not UIElement element)
            {
                return;
            }

            var members = Members();
            var member = members.FirstOrDefault(candidate => ReferenceEquals(candidate.Element, element));
            if (member is null)
            {
                return;
            }

            _item = list.Items[member.Index];
            _index = member.Index;
            _slot = member.Slot;
            _slotId = AutomationProperties.GetAutomationId(element);
            _focused = new WeakReference<UIElement>(element);
            foreach (var other in members)
            {
                SetTabStop(other.Element, ReferenceEquals(other.Element, element));
            }

            Hook(element);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!e.Handled && e.Key == Key.Tab && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                if (TabOutTarget(Keyboard.Modifiers == ModifierKeys.Shift) is { } next)
                {
                    next.Focus();
                    e.Handled = true;
                }

                return;
            }

            // 修飾キー付きの矢印（Alt+← の戻る・設定で割り当てた Ctrl+Shift+矢印）は画面の物
            if (e.Handled || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            ArrowMove? move = e.Key switch
            {
                Key.Left => ArrowMove.Previous,
                Key.Right => ArrowMove.Next,
                Key.Up => ArrowMove.Up,
                Key.Down => ArrowMove.Down,
                Key.Home => ArrowMove.First,
                Key.End => ArrowMove.Last,
                _ => null,
            };
            if (move is not { } step || Keyboard.FocusedElement is not UIElement focused)
            {
                return;
            }

            var members = Members();
            var current = members.FindIndex(member => ReferenceEquals(member.Element, focused));
            if (current < 0)
            {
                // 並びの中の入力欄（「＋ 追加」の欄）など。矢印はその部品の物
                return;
            }

            // 端で押したときも受けて止める。受けないと WPF の既定の矢印の移動が並びの外の部品へ飛ばす
            e.Handled = true;
            UIElement? target;
            if (!IsRowList)
            {
                target = MoveWithin(members, current, step);
            }
            else if (step is ArrowMove.Up or ArrowMove.Down or ArrowMove.First or ArrowMove.Last)
            {
                target = MoveRow(members[current], step);
            }
            else
            {
                // 行の中だけを数え直す（カードの段は、隣のカードの中身がまだ作られていないことがある。数え直すときに作らせる）
                var row = RowMembers(members[current].Index);
                target = MoveWithin(row, row.FindIndex(member => ReferenceEquals(member.Element, focused)), step);
            }

            target?.Focus();
        }

        /// <summary>
        /// 並びの中から Tab で出るときの行き先が、同じ一覧の中の並びでない行（管理の画面の見出し・下の枠：入力欄とボタンのある行）にあれば、それ。
        /// 無ければ null（WPF の Tab の決まりに任せる）。
        /// 見える分だけ作る一覧では、その行がまだ作られていないと Tab は飛ばして一覧の外へ出る
        /// （カードで並べた属性の管理で、下の「編集画面に最初から並べる」へ行けず、画面の先頭へ戻った）。
        /// 前は商品の1件ずつに止まり、止まるたびに流れて次の行が作られていたので起きなかった。
        /// 並びの行と同じ型の行（商品の段）は作らずに飛ばす（2000件の属性で、段を全部作らせないため）
        /// </summary>
        private UIElement? TabOutTarget(bool backward)
        {
            if (!IsRowList || Keyboard.FocusedElement is not UIElement focused
                || Members().FirstOrDefault(member => ReferenceEquals(member.Element, focused)) is not { } from)
            {
                return null;
            }

            var count = list.Items.Count;
            for (var index = from.Index + (backward ? -1 : 1); index >= 0 && index < count; index += backward ? -1 : 1)
            {
                if (list.Items[index] is { } item && _memberTypes.Contains(item.GetType()))
                {
                    continue;
                }

                if (list.ItemContainerGenerator.ContainerFromIndex(index) is null && ItemsHost() is VirtualizingStackPanel panel)
                {
                    panel.BringIndexIntoViewPublic(index);
                    list.UpdateLayout();
                }

                if (list.ItemContainerGenerator.ContainerFromIndex(index) is UIElement container)
                {
                    var stops = new List<UIElement>();
                    TabStopsIn(container, stops);
                    if (stops.Count > 0)
                    {
                        return backward ? stops[^1] : stops[0];
                    }
                }
            }

            return null;
        }

        /// <summary>行の中の、Tab で止まる物を木の順に（入れ子の並びは、その並びの止まる物だけが Tab で止まる物なので、そのまま数える）。</summary>
        private static void TabStopsIn(DependencyObject parent, List<UIElement> stops)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                if (VisualTreeHelper.GetChild(parent, i) is not UIElement { IsVisible: true } child)
                {
                    continue;
                }

                if (child.Focusable && child.IsEnabled && KeyboardNavigation.GetIsTabStop(child))
                {
                    stops.Add(child);
                    if (child is TextBoxBase or ComboBox)
                    {
                        continue;
                    }
                }

                TabStopsIn(child, stops);
            }
        }

        private UIElement? MoveWithin(List<Member> members, int current, ArrowMove step)
        {
            var spots = members.Select(member => new ArrowSpot(BoundsOf(member.Element), member.IsInner)).ToList();
            if (ArrowStep.Next(spots, current, step) is not { } next)
            {
                return null;
            }

            // 見える分だけ作る一覧の左右は、同じ行の中だけ（行をまたぐのは上下）
            return IsRowList && members[next].Index != members[current].Index ? null : members[next].Element;
        }

        private UIElement? MoveRow(Member from, ArrowMove step)
        {
            var count = list.Items.Count;
            int start;
            if (step is ArrowMove.First or ArrowMove.Last)
            {
                // 端の行が今の行でも、その行の端の物へ移る（カードが1段に収まるショップ一覧で、End が何もしなかった）
                start = step == ArrowMove.First ? 0 : count - 1;
            }
            else if (ArrowStep.Row(from.Index, count, step) is { } next)
            {
                start = next;
            }
            else
            {
                return null;
            }

            // 隣の行を作らせると、今の行が流れて外れることがある。横の位置は先に測っておく
            var here = BoundsOf(from.Element);
            var centerX = here.IsEmpty ? 0 : here.Left + here.Width / 2;

            // 押す物の無い行（状況の文・束の終わり）は飛ばして、その先の行へ
            var direction = step is ArrowMove.Up or ArrowMove.Last ? -1 : 1;
            for (var index = start; index >= 0 && index < count; index += direction)
            {
                if (index == from.Index && step is ArrowMove.Up or ArrowMove.Down)
                {
                    break;
                }

                if (RowMembers(index) is { Count: > 0 } row)
                {
                    var target = SameRole(row, from, centerX, step).Element;
                    return ReferenceEquals(target, from.Element) ? null : target;
                }
            }

            return null;
        }

        /// <summary>
        /// 隣の行の、今の物と同じ役の物（同じ ID、無ければ同じ番目、それも無ければ行の先頭）。
        /// 同じ ID が2つ以上ある行（カードの段）は、上下なら横の位置がいちばん近い物、Home・End なら端の物
        /// </summary>
        private Member SameRole(List<Member> row, Member from, double centerX, ArrowMove step)
        {
            var id = AutomationProperties.GetAutomationId(from.Element);
            var same = id.Length > 0 ? row.Where(member => AutomationProperties.GetAutomationId(member.Element) == id).ToList() : [];
            if (same.Count == 0)
            {
                // ID の無い物（フォルダのカード）は、同じ段の外側の物（中の星などでない物）から選ぶ。商品のカードとフォルダのカードが同じ段に並ぶ
                same = row.Where(member => !member.IsInner).ToList();
                if (same.Count <= 1)
                {
                    return row.FirstOrDefault(member => member.Slot == from.Slot) ?? row[0];
                }
            }

            if (same.Count == 1)
            {
                return same[0];
            }

            var spots = same.Select(member => new ArrowSpot(BoundsOf(member.Element), member.IsInner)).ToList();
            return same[ArrowStep.InRow(spots, centerX, step) ?? 0];
        }

        /// <summary>その行の止まり先。作られていない行は、番号で見える所まで流して作らせる。</summary>
        private List<Member> RowMembers(int index)
        {
            var row = new List<Member>();
            if (index < 0 || index >= list.Items.Count)
            {
                return row;
            }

            if (list.ItemContainerGenerator.ContainerFromIndex(index) is null && ItemsHost() is VirtualizingStackPanel panel)
            {
                panel.BringIndexIntoViewPublic(index);
                list.UpdateLayout();
            }

            if (list.ItemContainerGenerator.ContainerFromIndex(index) is UIElement container)
            {
                // 外のスクロールの中の一覧（ShopView）は、見えている所から遠い段の中身を作っていない（ViewportHold）。End で末尾へ飛ぶときなど。
                // 先にその段まで流してから控えを外す。流さずに外すだけだと、次の並べ直しで「遠い段」としてまた控えに戻される
                if (ViewportHold.GetIsHeld(container) && container is FrameworkElement held)
                {
                    held.BringIntoView();
                    list.UpdateLayout();
                    ViewportHold.Release(container);
                    list.UpdateLayout();
                }

                // カードの中身は画面が空いたときに後から作る。流して入ったばかりの段は、まだ白い枠だけで止まれる物が無い
                if (DeferredCardHost.RealizeWithin(container))
                {
                    list.UpdateLayout();
                }

                var slot = 0;
                CollectFrom(container, index, ref slot, row);
            }

            return row;
        }
        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_item is not null && Removed(e))
            {
                var index = e.Action == NotifyCollectionChangedAction.Reset ? _index : e.OldStartingIndex;
                var hadFocus = FocusIsHere();
                _item = null;
                _index = index;
                if (hadFocus)
                {
                    // 行の部品が作り直された後に止まり直す。行と一緒にフォーカスが消えると窓そのものへ落ち、
                    // 次の Tab が画面の先頭から始まって一覧の中の位置を失う
                    list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusNear(index));
                }
            }

            Queue();
        }

        private bool Removed(NotifyCollectionChangedEventArgs e) => e.Action switch
        {
            NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace => Contains(e.OldItems, _item),
            NotifyCollectionChangedAction.Reset => !list.Items.Contains(_item),
            _ => false,
        };

        private static bool Contains(IList? items, object? item)
        {
            if (items is null)
            {
                return false;
            }

            foreach (var candidate in items)
            {
                if (ReferenceEquals(candidate, item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// フォーカスが今もこの並びにあるか。消える行のボタンにまだ残っている・使い回された部品にある・
        /// 行と一緒に消えて窓そのものへ落ちた、のどれか。別の所へ自分で移った後なら奪わない
        /// </summary>
        private bool FocusIsHere()
        {
            var now = Keyboard.FocusedElement as DependencyObject;
            if (now is null)
            {
                return _focused is not null;
            }

            if (_focused is not null && _focused.TryGetTarget(out var last) && ReferenceEquals(now, last))
            {
                return true;
            }

            if (now is Visual visual && list.IsAncestorOf(visual))
            {
                return true;
            }

            return now is Window || (PresentationSource.FromDependencyObject(list) is { } source && ReferenceEquals(source.RootVisual, now));
        }

        /// <summary>
        /// 止まっていた行が消えた・隠れた後に、その位置の行（次の行。無ければ前の行）へ止まり直す。
        /// 押す物の無い行・隠れた行は飛ばす
        /// </summary>
        private void FocusNear(int removedIndex)
        {
            var count = list.Items.Count;
            if (ArrowStep.AfterRemoval(removedIndex, count) is not { } start)
            {
                FocusAround();
                return;
            }

            for (var index = start; index < count; index++)
            {
                if (RowMembers(index) is { Count: > 0 } row)
                {
                    SlotIn(row)?.Element.Focus();
                    return;
                }
            }

            for (var index = start - 1; index >= 0; index--)
            {
                if (RowMembers(index) is { Count: > 0 } row)
                {
                    SlotIn(row)?.Element.Focus();
                    return;
                }
            }

            FocusAround();
        }

        /// <summary>
        /// 並びに止まる物が1つも残らなかった（最後の小分類・付けたファイルの最後の1つを ✕ で外した）。並びを含む欄の中で、
        /// 並びのすぐ後ろの止まり先（小分類なら「小分類を追加」の欄）、後ろに無ければすぐ前の止まり先（付けたファイルなら同じバリエーションの［追加…］）へ止まり直す。
        /// 前を後回しにするのは、前には欄そのものを消すボタン（小分類の札なら大分類の「外す」）があり、続けて Enter を押すと消えるため。
        /// 外へ外へと欄を広げて探し、画面の外へは出ない。何もしないと窓そのものへ落ち、次の Tab が画面の先頭から始まる
        /// </summary>
        private void FocusAround()
        {
            for (var node = VisualTreeHelper.GetParent(list); node is not null and not Window; node = VisualTreeHelper.GetParent(node))
            {
                if (node is UIElement { IsVisible: true } scope)
                {
                    var before = new List<UIElement>();
                    var after = new List<UIElement>();
                    var passed = false;
                    CollectStops(scope, before, after, ref passed);
                    if ((after.FirstOrDefault() ?? before.LastOrDefault()) is { } target)
                    {
                        target.Focus();
                        return;
                    }
                }

                if (node is UserControl)
                {
                    return;
                }
            }
        }

        /// <summary>Tab で止まる物を、並びより前と後ろに分けて木の順に集める（並びの中は数えない）。</summary>
        private void CollectStops(DependencyObject parent, List<UIElement> before, List<UIElement> after, ref bool passed)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                if (VisualTreeHelper.GetChild(parent, i) is not UIElement child)
                {
                    continue;
                }

                if (ReferenceEquals(child, list))
                {
                    passed = true;
                    continue;
                }

                if (!child.IsVisible)
                {
                    continue;
                }

                if (child.Focusable && child.IsEnabled && KeyboardNavigation.GetIsTabStop(child))
                {
                    (passed ? after : before).Add(child);

                    // 入力欄の中の部品は、その欄の物
                    if (child is TextBoxBase or ComboBox)
                    {
                        continue;
                    }
                }

                CollectStops(child, before, after, ref passed);
            }
        }
        /// <summary>
        /// 行を見える分だけ作る一覧か。上下は隣の行へ番号で、左右は行の中だけで移る（画面の外の行は部品が無く、場所で探せない）。
        /// 全部の行を作る一覧（札の並び・ローカルファイルの行）は、上下も見えている場所で近い物へ移る——
        /// ファイルの行の中に「ほかの場所」「Unityへ送れるもの」の行が入れ子に並ぶので、番号で隣の行へ飛ぶと、その下の行へ降りられない
        /// 外のスクロールの中で、見えている辺りの行だけ中身を作る一覧（<see cref="ViewportHold"/>。ショップの中）も、行を見える分だけ作る一覧と同じに扱う。
        /// 全部の行を作る一覧の扱いにすると、End が作ってある中の最後のカードで止まった
        /// </summary>
        private bool IsRowList => (ItemsHost() is VirtualizingStackPanel { Orientation: Orientation.Vertical } && VirtualizingPanel.GetIsVirtualizing(list))
            || ViewportHold.GetIsHeld(list);

        private Rect BoundsOf(UIElement element)
            => element.IsDescendantOf(list)
                ? element.TransformToAncestor(list).TransformBounds(new Rect(element.RenderSize))
                : Rect.Empty;

        /// <summary>今作られている行の止まり先を、行の番号の順に。</summary>
        public List<Member> Members()
        {
            var result = new List<Member>();
            if (ItemsHost() is not { } host)
            {
                return result;
            }

            // 見える分だけ作る一覧では、板の子の並びは行の順と限らず、使い回しを待つ部品も混ざる。行の番号で並べ、番号の無い物を除く
            var containers = new List<(int Index, UIElement Container)>();
            foreach (UIElement child in host.Children)
            {
                var index = list.ItemContainerGenerator.IndexFromContainer(child);
                if (index >= 0)
                {
                    containers.Add((index, child));
                }
            }

            foreach (var (index, container) in containers.OrderBy(pair => pair.Index))
            {
                var slot = 0;
                var before = result.Count;
                CollectFrom(container, index, ref slot, result);
                if (result.Count > before && list.Items[index] is { } item)
                {
                    _memberTypes.Add(item.GetType());
                }
            }

            return result;
        }

        private static void CollectFrom(UIElement container, int index, ref int slot, List<Member> result)
        {
            if (!container.IsVisible)
            {
                return;
            }

            var isMember = IsCandidate(container);
            if (isMember)
            {
                container.SetValue(IsMemberProperty, true);
                result.Add(new Member(container, index, slot++, IsInner: false));
            }

            Collect(container, index, ref slot, isMember, result);
        }

        private static void Collect(DependencyObject node, int index, ref int slot, bool inner, List<Member> result)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is not UIElement element || !element.IsVisible)
                {
                    continue;
                }

                // 別に止まる所・入れ子の並びは、その中を数えない
                if (GetIsOutside(element) || GetIsEnabled(element))
                {
                    continue;
                }

                var isMember = IsCandidate(element);
                if (isMember)
                {
                    element.SetValue(IsMemberProperty, true);
                    result.Add(new Member(element, index, slot++, inner));
                }

                // 入力欄・選ぶ欄の中の部品は、その欄の物
                if (element is TextBoxBase or ComboBox or RangeBase)
                {
                    continue;
                }

                Collect(element, index, ref slot, inner || isMember, result);
            }
        }

        /// <summary>
        /// 止まり先になる物：フォーカスを受けられて、Tab で止まる物（またはこの仕組みが止まらなくした物）。
        /// 入力欄・選ぶ欄・スライダーは数えない——そこでの矢印は文字・選んだ物・値を動かすキーで、並びの中を移るキーではない
        /// </summary>
        private static bool IsCandidate(UIElement element)
            => element is not (TextBoxBase or ComboBox or RangeBase)
                && element.Focusable
                && element.IsEnabled
                && (KeyboardNavigation.GetIsTabStop(element) || (bool)element.GetValue(IsMemberProperty));

        private Panel? ItemsHost() => FindItemsHost(list);

        private static Panel? FindItemsHost(DependencyObject parent)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Panel { IsItemsHost: true } panel)
                {
                    return panel;
                }

                if (FindItemsHost(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        private ScrollViewer? ScrollHost() => ContentItemsControl.FindScrollViewer(list);
    }
}
