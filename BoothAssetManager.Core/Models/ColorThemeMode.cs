namespace BoothAssetManager.Core.Models;

/// <summary>
/// 表示の色（設定の <c>colorTheme</c>・ユーザ指示 2026-09-29）。既定は Windows に合わせる。
/// 色の表そのものは App の <c>Themes/Light.xaml</c>・<c>Themes/Dark.xaml</c>（docs/spec/ui-colors.md）。
/// </summary>
public enum ColorThemeMode
{
    /// <summary>Windows の「アプリ モードを選ぶ」に合わせる。</summary>
    System,

    Light,

    Dark,
}

public static class ColorTheme
{
    /// <summary>
    /// 暗い表を使うか。
    ///
    /// <paramref name="appsUseLightTheme"/> は Windows の設定（レジストリの <c>AppsUseLightTheme</c>）の値。
    /// 0 のときだけ暗い。読めなかったとき（<c>null</c>。古い Windows には無い）は明るい方にする——
    /// 暗い表を足す前はずっと明るかったので、分からないときは今までと同じ見た目に倒す
    /// </summary>
    public static bool IsDark(ColorThemeMode mode, int? appsUseLightTheme) => mode switch
    {
        ColorThemeMode.Light => false,
        ColorThemeMode.Dark => true,
        _ => appsUseLightTheme == 0,
    };

    /// <summary>設定画面の選択肢の名前。</summary>
    public static string Label(ColorThemeMode mode) => mode switch
    {
        ColorThemeMode.Light => "明るい",
        ColorThemeMode.Dark => "暗い",
        _ => "Windows に合わせる",
    };
}
