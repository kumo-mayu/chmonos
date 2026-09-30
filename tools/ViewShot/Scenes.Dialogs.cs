using System.Windows;
using System.Windows.Controls;

namespace ViewShot;

internal static partial class Scenes
{
    // 小窓は中身の大きさで描く（窓の題の帯と枠は Windows が描くので、この台では出ない）
    private static IEnumerable<Scene> Dialogs =>
    [
        Notice(
            "notice-okcancel-long",
            "確認の窓：OK／キャンセル・長い本文（折り返しと、既定がキャンセルのときのボタン）",
            "統合しますか",
            "「かわいい」を「可愛い」に統合します。「かわいい」が付いている 128 件の商品は、すべて「可愛い」に付け替わります。\n\n"
            + "小分類の「ふわふわ」「もこもこ」「ゆるい」は、同じ名前の小分類があればそこへ、無ければ「可愛い」の下へ移ります。\n\n"
            + "統合すると元に戻せません。",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel),

        Notice(
            "notice-yesno-warning",
            "確認の窓：はい／いいえ・警告の印・空白の無い長いパス",
            "保存先が見つかりません",
            "データの保存先が見つかりません。\n\n"
            + @"E:\VeryLongFolderNameWithoutAnySpaces\AnotherVeryLongFolderNameWithoutAnySpaces\Chmonos\data" + "\n\n"
            + "外付けドライブを外している場合は、つないでからもう一度開いてください。\n\n"
            + "［はい］既定の場所（%LOCALAPPDATA%）で開きます。保存先の設定はそちらに変わります。\n"
            + "［いいえ］何もせずに終了します。つなぎ直してから開き直せます。",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No),

        Notice(
            "notice-error-short",
            "知らせの窓：OK だけ・エラーの印・短い本文（最小の幅）",
            "Chmonos",
            "送るのを中止しました。",
            MessageBoxButton.OK,
            MessageBoxImage.Error),
    ];

    private static Scene Notice(
        string name, string title, string caption, string text, MessageBoxButton button, MessageBoxImage icon,
        MessageBoxResult defaultResult = MessageBoxResult.None)
        => new(name, title, context =>
        {
            var window = Backdoor.NewNotice(text, caption, button, icon, defaultResult);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        };

    private static IEnumerable<Scene> Parts =>
    [
        new Scene("calendar", "カレンダー（検索の日付の絞り込み）：月の表示と年の表示", context =>
        {
            // 日付は決め打ち。今日を使うと、日が変わるたびに前後の比べで差が出る
            var day = new DateTime(2026, 9, 17);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var mode in new[] { CalendarMode.Month, CalendarMode.Year })
            {
                row.Children.Add(new Calendar
                {
                    DisplayMode = mode,
                    DisplayDate = day,
                    SelectedDate = day.AddDays(3),
                    Margin = new Thickness(0, 0, 16, 0),
                });
            }

            return Task.FromResult(new Shot(SceneContext.OnSurface(row)));
        })
        {
            Width = null,
            Height = null,
        },
    ];
}
