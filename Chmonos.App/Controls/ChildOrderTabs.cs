using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Chmonos.App.Controls;

/// <summary>
/// 入れ物（Panel）の中の Tab の順を、**書いた子の順**にする（ユーザ指摘 2026-10-06：検索の「新しい順／古い順」の Tab の順がおかしい）。
///
/// WPF の Tab は描く順に子をたどり、Panel は重なりの順（<see cref="Panel.ZIndexProperty"/>）の大きい子を後ろへ回して描く。
/// 重ねるためだけに ZIndex を上げた子（繋がったボタンの選んでいる方・境の上に重ねたつまみ）が、Tab では後ろへ回っていた
/// （「新しい順」を選んでいると「古い順 → 新しい順」と通り、絞り込みのつまみは結果の後ろで止まった）。
///
/// 入れ物を Tab の区切り（<see cref="KeyboardNavigationMode.Local"/>）にし、子に書いた順の番号（TabIndex）を振る。
/// 区切りにしないと、番号は画面全体の既定（最後）の物と比べられ、この子たちが画面の先頭へ飛ぶ。
/// **子そのものが止まる部品の入れ物に使う**（繋がったボタン）。WPF は区切りでない入れ物の番号を見ず、中の部品の番号で比べるので、
/// 子が入れ物（Border・StackPanel）だと番号を振っても効かない。
/// <see cref="IsEnabledProperty"/> は入れ物に、<see cref="ForParentProperty"/> は型から子に付ける（繋がったボタンの型 SegmentButton）
/// </summary>
public static class ChildOrderTabs
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ChildOrderTabs), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>付けた部品の親の入れ物に <see cref="IsEnabledProperty"/> を付ける（親を書く所が画面ごとに散る型のため）。</summary>
    public static readonly DependencyProperty ForParentProperty = DependencyProperty.RegisterAttached(
        "ForParent", typeof(bool), typeof(ChildOrderTabs), new PropertyMetadata(false, OnForParentChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetForParent(DependencyObject element) => (bool)element.GetValue(ForParentProperty);

    public static void SetForParent(DependencyObject element, bool value) => element.SetValue(ForParentProperty, value);

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Panel panel || e.NewValue is not true)
        {
            return;
        }

        Apply(panel);
        // 子は後から足されることもある（束縛で中身が入る・型が組まれる）。読み込みのたびに振り直す
        panel.Loaded += (_, _) => Apply(panel);
    }

    private static void OnForParentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        if (!TryParent(element))
        {
            element.Loaded += OnChildLoaded;
        }
    }

    private static void OnChildLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (TryParent(element))
        {
            element.Loaded -= OnChildLoaded;
        }
    }

    private static bool TryParent(FrameworkElement element)
    {
        if (element.Parent is not Panel panel)
        {
            return false;
        }

        if (GetIsEnabled(panel))
        {
            Apply(panel);
        }
        else
        {
            SetIsEnabled(panel, true);
        }

        return true;
    }

    private static void Apply(Panel panel)
    {
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Local);
        for (var index = 0; index < panel.Children.Count; index++)
        {
            KeyboardNavigation.SetTabIndex(panel.Children[index], index);
        }
    }
}
