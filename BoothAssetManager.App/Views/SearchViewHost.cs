using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 検索の画面（<see cref="SearchView"/>）を、アプリの間1つだけ作って持ち回す入れ物。
///
/// 主画面は画面を DataTemplate で出すので、検索へ戻るたびに View が丸ごと作り直され、
/// 絞り込みの欄・カードの行・見えているカードの中身（<see cref="Controls.DeferredCardHost"/> が4枚ずつ作る）を毎回組み直していた。
/// 検索の ViewModel は元から1つを持ち回しているので、View も同じ寿命にする。
/// 条件・並び・スクロール位置・入力欄の中身は前と同じ所に戻る（位置は View 自身が持ち続ける）。
///
/// 画面の履歴は画面そのものを持たない決め事（`docs/spec/architecture.md`）だが、検索は ViewModel と同じく例外。
/// 代わりに、よそへ移っている間も見えていたカードの部品と絵（行の上下1画面ぶん）を持ち続ける。
/// </summary>
public sealed class SearchViewHost : Decorator
{
    /// <summary>主画面は1つなので、持ち回す View も1つ。</summary>
    private static SearchView? _kept;

    public SearchViewHost()
    {
        var view = _kept ??= new SearchView();

        // 前の入れ物がまだ手放していなければ（外される知らせより先に次の入れ物が作られたとき）、ここで外す。
        // 部品は親を1つしか持てず、付け替えないと例外になる
        if (view.Parent is Decorator previous && !ReferenceEquals(previous, this))
        {
            previous.Child = null;
        }

        Child = view;

        // View の DataContext は受け継がせず、ViewModel を直に渡す。受け継がせると、外している間に null になり、
        // 戻るたびに全部の結び付けを付け直すことになる（作り直すのと変わらない重さが残る）
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is SearchViewModel search)
            {
                view.DataContext = search;
            }
        };

        // よその画面へ移ったら手放す。次に戻ったときの入れ物が同じ View を抱え直す
        Unloaded += (_, _) =>
        {
            if (ReferenceEquals(Child, view))
            {
                Child = null;
            }
        };

        // 外された後に同じ入れ物が付け直された（窓の中の置き直し）ときは、空のまま出さずに抱え直す
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
