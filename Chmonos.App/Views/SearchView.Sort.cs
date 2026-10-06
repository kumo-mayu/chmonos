using System.Windows;
using System.Windows.Input;
using Chmonos.App.Controls;

namespace Chmonos.App.Views;

/// <summary>
/// 検索画面：表示順のボタンが開くメニュー（ユーザ判断 2026-10-06）。並びと印は SearchViewModel.SortMenu、
/// 開き方は改変の「Unity ▾」と同じ MenuButton。Tab で止まり、Enter・Space・↓ で開き、矢印で選び、→ で「属性 ▸」の子を開き、Esc で閉じる
/// </summary>
public partial class SearchView
{
    private void OnSortFieldMenuClick(object sender, RoutedEventArgs e) => MenuButton.OpenBelow(sender, e);

    private void OnSortFieldMenuKeyDown(object sender, KeyEventArgs e) => MenuButton.OpenOnArrowDown(sender, e);

    private void OnSortFieldMenuOpened(object sender, RoutedEventArgs e) => MenuButton.FocusChecked(sender, e);
}
