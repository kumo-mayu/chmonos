using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace Chmonos.App;

/// <summary>
/// 画面内検索（U20）。**どの画面でも** Ctrl+F（「画面の中を探す」の割り当て）を押すと、
/// 右上に帯を出して画面の中の文字を探す（ユーザ判断 2026-09-20・B2。
/// 以前は検索とショップだけ検索画面へ移していて、同じキーで見ていた画面を失っていた）。
///
/// 画面の見た目だけの話で、データには触らないので UiCommand は通さない。
/// </summary>
public partial class MainWindow
{
    private FindHighlightAdorner? _findAdorner;
    private List<FindMatch> _findMatches = [];
    private int _findIndex = -1;

    private void HookFind()
    {
        // 画面を移ったら閉じる。前の画面の一致を持ち越すと、印が宙に浮く
        DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl))
            .AddValueChanged(ScreenHost, (_, _) => CloseFind());

        // 流したら印を描き直す（印は画面の座標で描いているので、流すとずれる）
        ScreenHost.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => _findAdorner?.InvalidateVisual()));
        ScreenHost.SizeChanged += (_, _) => _findAdorner?.InvalidateVisual();
    }

    private void OpenFind()
    {
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();

        if (FindBox.Text.Trim().Length > 0)
        {
            RunFind();
        }
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        if (_findAdorner is not null)
        {
            AdornerLayer.GetAdornerLayer(ScreenHost)?.Remove(_findAdorner);
            _findAdorner = null;
        }

        _findMatches = [];
        _findIndex = -1;
    }

    private void RunFind()
    {
        var needle = FindBox.Text.Trim();

        // 畳んだ説明の中に一致があれば開いてから探す（ユーザ判断）。畳んだ中身は画面に作られていない
        if (needle.Length > 0 && ScreenHost.Content is ItemViewModel item)
        {
            item.RevealMatches(needle);
            ScreenHost.UpdateLayout();
        }

        // 商品の説明は、画面の外の見出しを後から足す（ProgressiveItems）。開いた直後に探すと、まだ無い見出しの中の一致を数え落とす。
        // 探す前に足し切って、並べる（並べないと、足した行の文字の箱がまだ作られていない）。
        // 組み込んだ商品ページ（フォルダビュー・改変の画面）も ScreenHost の中なので、ここで拾える
        if (needle.Length > 0 && Controls.ProgressiveItems.FeedAllNow(ScreenHost))
        {
            ScreenHost.UpdateLayout();
        }

        _findMatches = FindInPage.Find(ScreenHost, needle);
        _findIndex = _findMatches.Count > 0 ? 0 : -1;
        ShowFindResult();
    }

    private void StepFind(int delta)
    {
        if (_findMatches.Count == 0)
        {
            return;
        }

        _findIndex = (_findIndex + delta + _findMatches.Count) % _findMatches.Count;
        ShowFindResult();
    }

    private void ShowFindResult()
    {
        FindCount.Text = FindBox.Text.Trim().Length == 0
            ? string.Empty
            : _findMatches.Count == 0
                ? "見つかりません"
                : $"{_findIndex + 1} / {_findMatches.Count}";

        if (_findAdorner is null && AdornerLayer.GetAdornerLayer(ScreenHost) is { } layer)
        {
            _findAdorner = new FindHighlightAdorner(ScreenHost);
            layer.Add(_findAdorner);
        }

        if (_findAdorner is not null)
        {
            _findAdorner.Matches = _findMatches;
            _findAdorner.Current = _findIndex;
            _findAdorner.InvalidateVisual();
        }

        if (_findIndex >= 0)
        {
            FindInPage.BringIntoView(_findMatches[_findIndex]);
        }
    }

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) => RunFind();

    /// <summary>Enter で次、Shift+Enter で前、Esc で閉じる（ブラウザと同じ）。</summary>
    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StepFind((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
    }

    private void OnFindPrevious(object sender, RoutedEventArgs e) => StepFind(-1);

    private void OnFindNext(object sender, RoutedEventArgs e) => StepFind(1);

    private void OnFindClose(object sender, RoutedEventArgs e) => CloseFind();
}
