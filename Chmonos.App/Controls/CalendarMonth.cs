using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Chmonos.App.Controls;

/// <summary>
/// 吹き出し（<see cref="Popup"/>）の中のカレンダーを、**開くたびに選んである日の月で見せる**（ユーザ判断 2026-10-06・メモ82・84）。
///
/// WPF の <see cref="Calendar"/> は、選んだ日（<see cref="Calendar.SelectedDate"/>）を外から入れても見せる月（<see cref="Calendar.DisplayDate"/>）を動かさない。
/// 見せる月は作られた日（今日）のままなので、入力欄が「2024-06-01」でもカレンダーは今月で開き、そこまで何十回も送ることになっていた。
/// 前に開いて別の月へ送ったまま閉じた場合も、次に開くときは欄の日付の月へ戻す（欄に書いてある日が、いつも見えている所にあるように）。
/// 選んである日が無ければ今日の月。
/// </summary>
public static class CalendarMonth
{
    public static readonly DependencyProperty FollowsSelectionProperty = DependencyProperty.RegisterAttached(
        "FollowsSelection", typeof(bool), typeof(CalendarMonth), new PropertyMetadata(false, OnFollowsSelectionChanged));

    public static bool GetFollowsSelection(DependencyObject element) => (bool)element.GetValue(FollowsSelectionProperty);

    public static void SetFollowsSelection(DependencyObject element, bool value) => element.SetValue(FollowsSelectionProperty, value);

    private static void OnFollowsSelectionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Popup popup)
        {
            return;
        }

        popup.Opened -= OnOpened;
        if ((bool)e.NewValue)
        {
            popup.Opened += OnOpened;
        }
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is Popup { Child: Calendar calendar })
        {
            ShowSelectedMonth(calendar);
        }
    }

    /// <summary>月の表に戻し、選んである日（無ければ今日）の月を見せる。</summary>
    internal static void ShowSelectedMonth(Calendar calendar)
    {
        calendar.DisplayMode = CalendarMode.Month;
        calendar.DisplayDate = calendar.SelectedDate ?? DateTime.Today;
    }
}
