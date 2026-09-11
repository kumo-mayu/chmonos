using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;

namespace BoothAssetManager.App;

/// <summary>
/// 画面内検索（U20）。検索とショップ以外の画面で Ctrl+F（「検索欄へ」の割り当て）を押すと、
/// 右上に帯を出して画面の中の文字を探す。
///
/// 画面の見た目だけの話で、データには触らないので UiCommand は通さない。
/// </summary>
public partial class MainWindow
{
    private FindHighlightAdorner? _findAdorner;
    private List<FindMatch> _findMatches = [];
    private int _findIndex = -1;

    /// <summary>検索とショップの画面は Ctrl+F で検索欄へ入る（今どおり・ユーザ判断）。</summary>
    private static bool UsesSearchBox(object? screen) => screen is SearchViewModel or ShopsViewModel or ShopViewModel;

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
