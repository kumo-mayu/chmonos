using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 中身をまとめて差し替え、知らせを1回（Reset）で済ませる一覧。
/// **1行ずつ足すと、1行ごとに一覧へ知らせが飛ぶ**（フォルダビューで1つのフォルダに1000本並ぶと、開くたびに1000回）。
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
