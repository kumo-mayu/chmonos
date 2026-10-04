using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>検索画面：保存した検索の節のキーボード（探す欄から行へ・書いた後の止まり直し）。</summary>
public partial class SearchView
{
    /// <summary>探す欄で ↓：行の並びの先頭に止まる（打ってから Tab を探さずに選べるように。条件の並びの入口と同じ止まり方）。</summary>
    private void OnSavedFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (FrameList.FramesOf(SavedSearchList).FirstOrDefault() is { } first)
        {
            first.Focus();
            first.BringIntoView();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 保存した検索を書いた後（並べ替え・名前の変更・上書き・削除）：行は作り直されるので、頼まれた行（空なら「今の検索を保存」）に止まり直す。
    /// 節の外に止まっていたとき（マウスで操作して、ほかの欄に移った等）は奪わない
    /// </summary>
    private void OnSavedRowFocusRequested(string name)
    {
        if (Keyboard.FocusedElement is UIElement { IsVisible: true } focused && IsInside(focused) && !IsWithin(SavedSearchSection, focused))
        {
            return;
        }

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
