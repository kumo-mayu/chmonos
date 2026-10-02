using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Chmonos.App.Controls;

/// <summary>
/// 枠の並び（検索の条件）を、Tab では1回だけ止まる塊にし、枠の間は ↑↓ で移り、Enter（か Tab）で枠の中へ入れるようにする
/// （ユーザ判断 2026-10-02・メモ1-③「tab移動で検索モジュールが大量だと使いにくいかも」→「条件の並びを1つの止まりにする」）。
/// 一覧（<see cref="ItemsControl"/>）に <c>controls:FrameList.IsEnabled="True"</c> を付け、項目の枠を <see cref="FocusFrame"/> にする。
///
/// - 外から Tab で入ると、前にいた枠（初めてなら Tab で先頭・Shift+Tab で末尾の枠）に止まる
/// - 枠の上：↑↓ で隣の枠、Home・End で端の枠（端では止まる）。Enter か Tab で枠の中の最初の部品へ。Shift+Tab で並びの前へ出る
/// - 枠の中：Tab・Shift+Tab は今までどおり部品から部品へ（中の部品の順は変えない）。Shift+Tab で最初の部品から戻ると枠に止まる。
///   **最後の部品から Tab で並びの後ろへ出る**（次の枠へは入らない）。Esc で枠へ戻る（中の部品が Esc を使ったとき＝候補を閉じたときは戻らない）
///
/// カードの一覧などの「並び」（<see cref="ArrowGroup"/>）を使わないのは、並びは中の止まり先を全部1段に並べて矢印で渡る作りで、
/// 枠の中に入力欄・選ぶ欄・スライダーがある条件では、矢印がその部品の物（文字を動かす・選び直す・値を動かす）と食い合うため
/// （`ui-input.md`「入力欄のある行の一覧は並びにしない」）。ここでは矢印を枠の上でだけ使い、中は Tab のままにする2段にした。
/// </summary>
public static class FrameList
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FrameList), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(FrameList), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>並びの枠を、並びの順に（試験・確かめ用）。</summary>
    public static IReadOnlyList<FocusFrame> FramesOf(ItemsControl list) => Frames(list);

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ItemsControl list)
        {
            return;
        }

        if ((bool)e.NewValue && list.GetValue(StateProperty) is null)
        {
            var state = new State(list);
            list.SetValue(StateProperty, state);
            list.PreviewKeyDown += state.OnPreviewKeyDown;
            list.KeyDown += state.OnKeyDown;
            list.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(state.OnGotFocus), handledEventsToo: true);
            list.Loaded += state.OnLoaded;
            list.Unloaded += state.OnUnloaded;
            if (list.IsLoaded)
            {
                state.OnLoaded(list, new RoutedEventArgs());
            }
        }
        else if (!(bool)e.NewValue && list.GetValue(StateProperty) is State state)
        {
            list.PreviewKeyDown -= state.OnPreviewKeyDown;
            list.KeyDown -= state.OnKeyDown;
            list.RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(state.OnGotFocus));
            list.Loaded -= state.OnLoaded;
            list.Unloaded -= state.OnUnloaded;
            state.OnUnloaded(list, new RoutedEventArgs());
            list.ClearValue(StateProperty);
        }
    }

    private static List<FocusFrame> Frames(ItemsControl list)
    {
        var frames = new List<FocusFrame>();
        for (var index = 0; index < list.Items.Count; index++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container
                && Find<FocusFrame>(container) is { IsVisible: true } frame)
            {
                frames.Add(frame);
            }
        }

        return frames;
    }

    private static T? Find<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found)
            {
                return found;
            }

            if (Find<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private static FocusFrame? FrameOf(DependencyObject? element)
    {
        for (var node = element; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is FocusFrame frame)
            {
                return frame;
            }
        }

        return null;
    }

    private static bool IsWithin(DependencyObject ancestor, DependencyObject? element)
    {
        for (var node = element; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>並びの中の、Tab で止まる最後の部品（文書の順）。並びの後ろへ出るときの起点にする。</summary>
    private static UIElement? LastStop(DependencyObject parent)
    {
        for (var index = VisualTreeHelper.GetChildrenCount(parent) - 1; index >= 0; index--)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is UIElement { IsVisible: false })
            {
                continue;
            }

            if (LastStop(child) is { } deeper)
            {
                return deeper;
            }

            if (child is UIElement { Focusable: true, IsEnabled: true } element && KeyboardNavigation.GetIsTabStop(element))
            {
                return element;
            }
        }

        return null;
    }

    private sealed class State(ItemsControl list)
    {
        /// <summary>前に止まっていた枠の項目（枠の部品は作り直されるので、項目で覚える）。</summary>
        private object? _item;

        /// <summary>自分で止まり先を移している間（その移動でもう一度向きを決め直さない）。</summary>
        private bool _moving;

        /// <summary>
        /// Tab の押下を処理している間（と向き）。窓の根で先に聞いておく——並びの外から Tab で入ってきたときは、キーの知らせが並びを通らない。
        /// 実際のキーの状態（Keyboard.IsKeyDown）は読まない：入力の口から送ったキー（確かめの道具）でも同じに動くように
        /// </summary>
        private bool _tabbing;

        private bool _tabBackward;

        private UIElement? _root;

        public void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_root is not null)
            {
                return;
            }

            _root = PresentationSource.FromVisual(list)?.RootVisual as UIElement;
            _root?.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnRootPreviewKeyDown), handledEventsToo: true);
        }

        public void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _root?.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnRootPreviewKeyDown));
            _root = null;
        }

        private void OnRootPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab || _tabbing)
            {
                return;
            }

            _tabbing = true;
            _tabBackward = (e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => _tabbing = false);
        }

        public void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is not FocusFrame frame || !ReferenceEquals(Keyboard.FocusedElement, frame))
            {
                return;
            }

            var modifiers = Keyboard.Modifiers;
            switch (e.Key)
            {
                case Key.Tab when modifiers == ModifierKeys.Shift:
                    ExitBefore();
                    break;
                case Key.Tab when modifiers == ModifierKeys.None:
                case Key.Enter when modifiers == ModifierKeys.None:
                    Enter(frame);
                    break;
                case Key.Down when modifiers == ModifierKeys.None:
                    Step(frame, +1);
                    break;
                case Key.Up when modifiers == ModifierKeys.None:
                    Step(frame, -1);
                    break;
                case Key.Home when modifiers == ModifierKeys.None:
                    Land(Frames(list).FirstOrDefault());
                    break;
                case Key.End when modifiers == ModifierKeys.None:
                    Land(Frames(list).LastOrDefault());
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        /// <summary>枠の中で Esc：中の部品が使わなかった（候補を閉じるなどしなかった）ときだけ、枠へ戻る。</summary>
        public void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled || e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None
                || e.OriginalSource is FocusFrame || FrameOf(e.OriginalSource as DependencyObject) is not { } frame)
            {
                return;
            }

            Land(frame);
            e.Handled = true;
        }

        /// <summary>
        /// Tab で止まり先が移った後に、向きを決め直す。外から入ったら前の枠へ、枠の中の最後の部品から出て次の枠へ入ったら並びの後ろへ。
        /// Tab が押されているときだけ見る（マウスで押した・コードが止めた所は動かさない。足した条件の入力欄に止まるなど）
        /// </summary>
        public void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            var now = e.NewFocus as DependencyObject;
            var landed = FrameOf(now);
            var before = e.OldFocus as DependencyObject;
            var tabbing = !_moving && _tabbing;
            var from = FrameOf(before);

            // 外から Tab で入った／枠の中の最後の部品から Tab で次の枠へ入った：どちらもこの後で止まり先を移し直す
            var tabbedIn = tabbing && !IsWithin(list, before);
            var tabbedOut = tabbing && !_tabBackward && !tabbedIn && from is not null
                            && !ReferenceEquals(before, from) && !ReferenceEquals(landed, from);

            // 止まった枠を覚える。移し直す途中の止まり先は覚えない（覚えると前の枠を忘れる）
            if (landed is not null && !tabbedIn && !tabbedOut)
            {
                _item = landed.DataContext;
            }

            if (tabbedIn)
            {
                // 外から入った：前にいた枠（初めてなら入った向きの端の枠）
                var frames = Frames(list);
                var target = frames.FirstOrDefault(frame => ReferenceEquals(frame.DataContext, _item))
                             ?? (_tabBackward ? frames.LastOrDefault() : frames.FirstOrDefault());
                if (target is not null && !ReferenceEquals(target, now))
                {
                    MoveSoon(target);
                }
            }
            else if (tabbedOut)
            {
                // 枠の中の最後の部品から Tab：次の枠へは入らず、並びの後ろへ出る
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, ExitAfter);
            }
        }

        private void Enter(FocusFrame frame)
            => frame.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

        private void Step(FocusFrame frame, int delta)
        {
            var frames = Frames(list);
            var index = frames.IndexOf(frame);
            if (index >= 0 && index + delta >= 0 && index + delta < frames.Count)
            {
                Land(frames[index + delta]);
            }
        }

        private void Land(FocusFrame? frame)
        {
            if (frame is null)
            {
                return;
            }

            _moving = true;
            try
            {
                frame.Focus();
                frame.BringIntoView();
            }
            finally
            {
                _moving = false;
            }
        }

        private void MoveSoon(FocusFrame frame)
            => Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, () => Land(frame));

        /// <summary>並びの前へ：先頭の枠の前の止まり先（枠そのものから数える）。</summary>
        private void ExitBefore()
        {
            if (Frames(list).FirstOrDefault() is { } first)
            {
                _moving = true;
                try
                {
                    first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                }
                finally
                {
                    _moving = false;
                }
            }
        }

        /// <summary>並びの後ろへ：並びの中で最後に止まる部品の、次の止まり先。</summary>
        private void ExitAfter()
        {
            if (LastStop(list) is { } last)
            {
                _moving = true;
                try
                {
                    last.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
                finally
                {
                    _moving = false;
                }
            }
        }
    }
}

/// <summary>
/// <see cref="FrameList"/> の枠1つ（検索の条件の枠）。見た目は Border のまま、キーボードで止まれ、読み上げでは名前の付いたまとまり（Group）として出る。
/// 中の部品は今までどおり木に出す。
/// </summary>
public sealed class FocusFrame : Border
{
    public FocusFrame()
    {
        Focusable = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(FocusFrame owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(FocusFrame);

        protected override bool IsKeyboardFocusableCore() => owner.Focusable;
    }
}
