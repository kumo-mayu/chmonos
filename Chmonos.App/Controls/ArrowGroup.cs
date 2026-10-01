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

            return (_slotId is { Length: > 0 } id ? row.FirstOrDefault(member => AutomationProperties.GetAutomationId(member.Element) == id) : null)
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
            // 修飾キー付きの矢印（Alt+← の戻る・Ctrl+Shift+→ のスキップ）は画面の物
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
            var target = IsRowList && step is ArrowMove.Up or ArrowMove.Down or ArrowMove.First or ArrowMove.Last
                ? MoveRow(members[current], step)
                : MoveWithin(members, current, step);
            target?.Focus();
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
            if (ArrowStep.Row(from.Index, count, step) is not { } start)
            {
                return null;
            }

            // 押す物の無い行（状況の文・束の終わり）は飛ばして、その先の行へ
            var direction = step is ArrowMove.Up or ArrowMove.Last ? -1 : 1;
            for (var index = start; index >= 0 && index < count && index != from.Index; index += direction)
            {
                if (RowMembers(index) is { Count: > 0 } row)
                {
                    var id = AutomationProperties.GetAutomationId(from.Element);
                    return (row.FirstOrDefault(member => id.Length > 0 && AutomationProperties.GetAutomationId(member.Element) == id)
                        ?? row.FirstOrDefault(member => member.Slot == from.Slot)
                        ?? row[0]).Element;
                }
            }

            return null;
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
        }
        /// <summary>
        /// 行を見える分だけ作る一覧か。上下は隣の行へ番号で、左右は行の中だけで移る（画面の外の行は部品が無く、場所で探せない）。
        /// 全部の行を作る一覧（札の並び・ローカルファイルの行）は、上下も見えている場所で近い物へ移る——
        /// ファイルの行の中に「ほかの場所」「Unityへ送れるもの」の行が入れ子に並ぶので、番号で隣の行へ飛ぶと、その下の行へ降りられない
        /// </summary>
        private bool IsRowList => ItemsHost() is VirtualizingStackPanel { Orientation: Orientation.Vertical } && VirtualizingPanel.GetIsVirtualizing(list);

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
                CollectFrom(container, index, ref slot, result);
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
