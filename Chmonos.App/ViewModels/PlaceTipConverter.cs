using System.Globalization;
using System.Windows.Data;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 取り込み画面の3つの一覧の「エクスプローラで開く」（ボタンと右クリックの項目）の吹き出し。
/// 場所が見つからない行は押せなくなるので、理由を言う（右クリックの決まり・メモ75）。
/// 値は（行の場所・取り込み画面・確かめの版）。版は確かめが終わったときに吹き出しを引き直すための印。
/// 右クリックの項目は押せるときに言うことが無いので、何も出さない（ConverterParameter が menu）。
/// </summary>
public sealed class PlaceTipConverter : IMultiValueConverter
{
    public static readonly PlaceTipConverter Instance = new();

    internal const string OpenText = "エクスプローラで開きます。";

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var reason = values is [string path, ImportViewModel import, ..] ? import.RevealBlockedReason(path) : null;
        return reason ?? (parameter as string == "menu" ? null : OpenText);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
