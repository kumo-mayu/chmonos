using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace Chmonos.App.Views;

/// <summary>
/// 主の窓の商品ページ（<see cref="ItemView"/>）を、アプリの間1つだけ作って持ち回す入れ物（<see cref="SearchViewHost"/> と同じ形）。
///
/// 主画面は画面を DataTemplate で出すので、検索から商品を開くたびに商品ページが丸ごと作り直され、
/// 中の部品の型（テンプレート）も毎回読み直していた。dotnet-trace で、押してから最初に描くまで（確かめの道具の分を引いて約0.18秒）のうち、
/// 型の読み直しが約47ms・部品を作るのが約13ms だった（実測 2026-11-07）。
///
/// 商品から商品へ移るときは、前から WPF が同じ画面を使い回して中身だけを替えている。その道（<see cref="ItemView"/> の
/// DataContextChanged：進むときは先頭へ・戻る・進むは離れた位置へ）を、検索などを挟んだときにも通す。
/// 中身は替えるまで前の商品のまま持つ（外すたびに外すと、結び付けを全部付け直すことになり、作り直すのと変わらない）。
/// </summary>
public sealed class ItemViewHost : Decorator
{
    /// <summary>主画面は1つなので、持ち回す画面も1つ。</summary>
    private static ItemView? _kept;

    public ItemViewHost()
    {
        var view = _kept ??= new ItemView();

        // 前の入れ物がまだ手放していなければ（外される知らせより先に次の入れ物が作られたとき）、ここで外す。
        // 部品は親を1つしか持てず、付け替えないと例外になる
        if (view.Parent is Decorator previous && !ReferenceEquals(previous, this))
        {
            previous.Child = null;
        }

        Child = view;

        // 受け継がせず、ViewModel を直に渡す。受け継がせると、外している間に null になり、
        // 次に開いたときに「作りたて」と見て、前の商品の流した位置のまま出てしまう
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ItemViewModel item)
            {
                view.DataContext = item;
            }
        };

        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Child, view))
            {
                Child = null;
            }
        };

        Loaded += (_, _) =>
        {
            if (Child is null)
            {
                if (view.Parent is Decorator other)
                {
                    other.Child = null;
                }

                Child = view;
            }
        };
    }
}
