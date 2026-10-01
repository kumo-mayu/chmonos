using System.Collections.ObjectModel;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 画面に出している一覧を、目当ての並びへ寄せる。**消して足し直さず、要らない物を抜き・動かし・足りない物を差す。**
///
/// 仮想化しない一覧（要確認の束・改変の左の一覧など）は、中身を丸ごと差し替えると部品を全部作り直す
/// （`docs/dev/wpf.md`「一覧と速さ」）。残った物は同じ部品のまま使われるので、1件の出し入れなら1件ぶんの作り直しで済む。
/// 比べるのは参照なので、行の ViewModel を読み直しの間で使い回す側と組にして使う。
/// 属性の管理の <c>SyncItems</c> と同じ手順を、ほかの画面でも使えるように分けたもの。
/// </summary>
public static class CollectionSync
{
    public static void Apply<T>(ObservableCollection<T> list, IReadOnlyList<T> target)
        where T : class
    {
        var keep = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(list[i]))
            {
                list.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var at = IndexFrom(list, target[i], i);
            if (at == i)
            {
                continue;
            }

            if (at > i)
            {
                list.Move(at, i);
            }
            else
            {
                list.Insert(i, target[i]);
            }
        }
    }

    private static int IndexFrom<T>(ObservableCollection<T> list, T item, int start)
        where T : class
    {
        for (var i = start; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
