using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BoothAssetManager.App.ViewModels;

/// <summary>true のときだけ表示する。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 空文字のときだけ見せる。入力欄に重ねる placeholder に使う。
///
/// **空欄は「入力を求めている」と読まれる。**既定が決まっているなら、
/// 空欄のままで何になるかを欄そのものに名乗らせたい。
/// </summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public static readonly EmptyToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>false のときだけ見せる。「まだ○○していない」という案内文に使う。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>真偽を反転する。進捗バーの「件数が分からない＝不定」の切り替えに使う。</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>
/// 空文字なら畳む。補足の1行は、内容が無いときに枠だけ残ると
/// 「何かあるはずなのに空」に見えるので、行ごと消す。
/// </summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public static readonly EmptyToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>空文字のときだけ見せる。入力欄の内側に出す案内文に使う。</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public static readonly EmptyToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>扱わないことにした行を薄くする。消さずに残すが、同じ顔では出さない。</summary>
public sealed class ExcludedOpacityConverter : IValueConverter
{
    public static readonly ExcludedOpacityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 0.45 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 0件なら出さない。ナビのバッジは「残っている作業」を示すものなので、
/// 0が並ぶと、片付いたことではなく数字そのものが目に入ってしまう。
/// </summary>
public sealed class ZeroToCollapsedConverter : IValueConverter
{
    public static readonly ZeroToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 幅から決まった量を引く（引数に引く量）。親の幅に追従させたいが、見出しの左の開け閉めの印などの分だけ狭くしたいとき。
/// 0 より小さくはしない（狭めた窓で負の幅になると例外になる）。
/// </summary>
public sealed class SubtractConverter : IValueConverter
{
    public static readonly SubtractConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double width && double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            ? Math.Max(0, width - amount)
            : DependencyProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
