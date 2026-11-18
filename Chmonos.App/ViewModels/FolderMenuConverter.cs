using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chmonos.App.ViewModels;

/// <summary>
/// フォルダへの操作（検索で開く・エクスプローラ・取り込む・監視）が指す場所。右の頭のボタンは詳細、右クリックは木の行・子フォルダのカード・
/// リストのフォルダの行を渡してくる。どれも同じ <c>node.Path</c> を持つので、同じ結果になる
/// </summary>
internal sealed record FolderTarget(string Path, bool IsOffline)
{
    public static FolderTarget? Of(object? parameter) => parameter switch
    {
        FolderViewDetail detail => new(detail.Path, detail.IsOffline),
        FolderViewRow { IsFolderLike: true } row => new(row.Path, row.IsDim),
        FolderBrowserFolderCard card => new(card.Path, card.IsDim),
        _ => null,
    };
}

/// <summary>
/// フォルダの右クリック（左の木のフォルダの行・右の子フォルダのカード・リストのフォルダの行。open.md 90・92 行）の、
/// 項目の見出し・押せるか・吹き出し・木の行だけの項目を出すか。
///
/// 値は [0] 右クリックした物（行・カード）、[1] フォルダの画面（<see cref="FolderViewModel"/>）、[2] 監視や開閉が変わった回数。
/// メニューは1つを使い回すので、同じ行で開き直すと右クリックした物が変わらず、見出しが読み直されない。
/// 監視を切り替えた後に古い見出しのまま出ないよう、変わった回数も渡して読み直させる
/// </summary>
public sealed class FolderMenuConverter : IMultiValueConverter
{
    public static readonly FolderMenuConverter Instance = new();

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var target = values.Length > 0 ? values[0] : null;
        var owner = values.Length > 1 ? values[1] as FolderViewModel : null;
        var value = owner?.FolderMenuValue((string)parameter!, target);
        if (targetType == typeof(Visibility))
        {
            return value is true ? Visibility.Visible : Visibility.Collapsed;
        }

        if (targetType == typeof(bool))
        {
            return value is true;
        }

        return value;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
