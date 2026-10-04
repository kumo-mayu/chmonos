using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>検索画面：保存した条件の一覧（「条件を追加」の隣のボタンから開く）のキーボード・開閉・書いた後の止まり直し。</summary>
public partial class SearchView
{
    /// <summary>ボタンで ↓：一覧を開く（Enter・Space はボタン本来の動きで開く。メニューのボタンと同じ入口）。</summary>
    private void OnSavedButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None)
        {
            SavedSearchesButton.IsChecked = true;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 開いたら先頭の行（無ければ「今の検索を保存」）に止まる。開いたのに止まり先がボタンのままだと、
    /// 矢印を押しても何も選べない
    /// </summary>
    private void OnSavedPopupOpened(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FrameList.FramesOf(SavedSearchList).FirstOrDefault() is { } first)
            {
                first.Focus();
                first.BringIntoView();
            }
            else
            {
                SaveCurrentSearchButton.Focus();
            }
        });

    private void OnSavedPopupClosed(object? sender, EventArgs e)
    {
        // 外を押して閉じたときは、押した先を奪わない。止まり先が行き場を失った（窓へ落ちた）ときだけボタンへ戻す
        if (Keyboard.FocusedElement is null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (Keyboard.FocusedElement is null)
                {
                    SavedSearchesButton.Focus();
                }
            });
        }
    }

    /// <summary>探す欄で ↓：行の並びの先頭に止まる。一覧の中で Esc：閉じてボタンへ戻る。</summary>
    private void OnSavedPanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSavedPopup();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None
            && e.OriginalSource is DependencyObject source && IsWithin(SavedFilterBox, source)
            && FrameList.FramesOf(SavedSearchList).FirstOrDefault() is { } first)
        {
            first.Focus();
            first.BringIntoView();
            e.Handled = true;
        }
    }

    private void CloseSavedPopup()
    {
        SavedSearchesButton.IsChecked = false;
        SavedSearchesButton.Focus();
    }

    /// <summary>
    /// 保存した条件を書いた後（並べ替え・名前の変更・上書き・削除）：行は作り直されるので、頼まれた行（空なら「今の検索を保存」）に止まり直す。
    /// 小窓・確認の窓が出ると一覧が閉じることがあるので、閉じていれば開き直す。
    /// ほかの欄に移っていたとき（マウスで操作して、ほかの欄に移った等）は奪わない
    /// </summary>
    private void OnSavedRowFocusRequested(string name)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        var ours = focused is null
            || SavedSearchPopup.IsOpen
            || ReferenceEquals(focused, SavedSearchesButton)
            || IsWithin(SavedSearchPanel, focused);
        if (!ours)
        {
            return;
        }

        SavedSearchesButton.IsChecked = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var frame = name.Length == 0
                ? null
                : FrameList.FramesOf(SavedSearchList).FirstOrDefault(candidate =>
                    candidate.DataContext is SavedSearchRow row && Core.Services.SavedSearches.SameName(row.Name, name));
            if (frame is not null)
            {
                frame.Focus();
                frame.BringIntoView();
            }
            else
            {
                SaveCurrentSearchButton.Focus();
            }
        });
    }

    private static bool IsWithin(DependencyObject ancestor, DependencyObject element)
    {
        for (var node = element; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
