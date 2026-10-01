using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Chmonos.App.Services;

/// <summary>
/// ボタンとメニューの項目の UI Automation の名前から、「_」が消えないようにする。
///
/// WPF は、中身が文字のボタン（<c>Content="開く ▾"</c>）とメニューの項目の名前を返すとき、アクセスキーの印として最初の「_」を1つ消す
/// （<c>AccessText.RemoveAccessKeyMarker</c>。「__」は「_」に戻す）。名前を <c>AutomationProperties.Name</c> で付けていても消す。
/// ファイル名を入れた名前「file_000.pngを開く」が「file000.pngを開く」で読まれ、確かめの道具は名前で探せなかった（2026-09-30）。
/// 画面の表示は消えない（このアプリのボタンの型は、文字をアクセスキーとして読まない）。
///
/// 消されるのと同じ決まりで、先に「_」を「__」にしておく。名前を付ける所ごとに書くと、付け忘れた所だけ黙って欠けるので、
/// 部品の型（ボタンの仲間・メニューの項目）に1回で掛ける。中身が文字でない部品は WPF が消さないので、そのまま渡す。
/// </summary>
internal static class AutomationNames
{
    private static bool _registered;

    /// <summary>アプリの最初に1回呼ぶ（部品を作る前）。</summary>
    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        AutomationProperties.NameProperty.OverrideMetadata(
            typeof(ButtonBase), new FrameworkPropertyMetadata(string.Empty, null, CoerceName));
        AutomationProperties.NameProperty.OverrideMetadata(
            typeof(MenuItem), new FrameworkPropertyMetadata(string.Empty, null, CoerceName));

        // 中身が替わる（結び付けが後から届く・行が使い回される）と、消されるかどうかも、名前の元になる文字も変わる。そのたびに決め直す
        ContentControl.ContentProperty.OverrideMetadata(
            typeof(ButtonBase), new FrameworkPropertyMetadata(null, Recoerce));

        // メニューの項目は、見出しが替わった知らせを型ごとに受ける口が無い（MenuItem が自分で使っている）。
        // 付けた名前だけを守り、読み込まれた時点で決め直す（結び付けた見出しが、名前より後に届く順がある）
        EventManager.RegisterClassHandler(typeof(MenuItem), FrameworkElement.LoadedEvent, new RoutedEventHandler(RecoerceOnLoaded));
    }

    private static void Recoerce(DependencyObject element, DependencyPropertyChangedEventArgs e)
        => element.CoerceValue(AutomationProperties.NameProperty);

    private static void RecoerceOnLoaded(object sender, RoutedEventArgs e)
        => ((DependencyObject)sender).CoerceValue(AutomationProperties.NameProperty);

    /// <summary>
    /// 名前を付けていない部品は、WPF が中身の文字を名前にして、同じく「_」を消す（名前の候補のボタン・文にフォルダ名の入るボタン）。
    /// 中身の文字に「_」があるときだけ、その文字を名前として渡す（無ければ今までどおり空のまま＝WPF が中身から決める）。
    /// </summary>
    private static object CoerceName(DependencyObject element, object value)
    {
        if (value is not string name)
        {
            return value;
        }

        var text = TextOf(element);
        if (text is null)
        {
            return name;
        }

        if (name.Length > 0)
        {
            return Escape(name, losesMarker: true);
        }

        // 見出しが替わったときに決め直せないメニューの項目は、前の見出しの名前が残らないよう、中身からは作らない
        return element is ButtonBase && text.Contains('_') ? Escape(text, losesMarker: true) : name;
    }

    /// <summary>WPF が名前から「_」を消す部品なら、その中身の文字（中身が文字のボタンの仲間・見出しが文字のメニューの項目）。</summary>
    private static string? TextOf(DependencyObject element) => element switch
    {
        ButtonBase button => button.Content as string,
        MenuItem item => item.Header as string,
        _ => null,
    };

    /// <summary>消される部品なら、消された後に元の名前へ戻るように「_」を重ねる。</summary>
    internal static string Escape(string name, bool losesMarker)
        => losesMarker && name.Contains('_') ? name.Replace("_", "__") : name;
}
