using System.Windows;
using System.Windows.Controls;

namespace BoothAssetManager.App.Services;

/// <summary>一覧の1行。名前と、その下に小さく出す補足（場所・入っている zip など）。</summary>
public sealed record ListChoiceItem(string Label, string? Detail = null);

/// <summary>
/// 一覧から1つを選ばせる窓（送り先の Unity・どのファイルを開くか・どの unitypackage を送るか）。
///
/// **候補が2つ以上のときだけ出す。**呼ぶ側が1つなら聞かずに進める（ユーザ指示 2026-09-19：複数あるときの動きが決まっていなかった。
/// 最初の1つを黙って使うと、別のファイルを開いた・送ったことに気付けない）。
/// 前は送り先の Unity を選ぶ所にだけ直に書いてあった
/// </summary>
public static class ListChoice
{
    /// <summary>「飛ばす」を押したときに返す値（順に聞いている途中で、この1件だけを見送る）。</summary>
    public const int Skipped = -1;

    /// <param name="skipText">
    /// 順に聞くとき（検索で複数選んで送る）に、この1件だけを見送るボタンの名前。null なら出さない。
    /// 「やめる」は全体をやめる意味になる
    /// </param>
    /// <returns>選んだ位置。飛ばしたら <see cref="Skipped"/>。やめたら null。</returns>
    public static int? Ask(string title, string message, IReadOnlyList<ListChoiceItem> items, string okText, string? skipText = null)
    {
        var skipped = false;
        var list = new ListBox { Margin = new Thickness(0, 10, 0, 12), MinHeight = 90, MaxHeight = 360 };
        foreach (var item in items)
        {
            var row = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
            row.Children.Add(new TextBlock { Text = item.Label, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrEmpty(item.Detail))
            {
                row.Children.Add(new TextBlock
                {
                    Text = item.Detail,
                    FontSize = 11,
                    Opacity = 0.65,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }

            // 切れた場所も読めるように、補足ごとツールチップに出す
            list.Items.Add(new ListBoxItem
            {
                Content = row,
                ToolTip = string.IsNullOrEmpty(item.Detail) ? item.Label : $"{item.Label}\n{item.Detail}",
            });
        }

        list.SelectedIndex = 0;

        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow,
        };

        var ok = new Button { Content = okText, IsDefault = true, Padding = new Thickness(12, 3, 12, 3) };
        var cancel = new Button { Content = "やめる", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
        ok.Click += (_, _) => window.DialogResult = true;

        // 行をダブルクリックしても決める（選んでから決めるボタンまで動かなくて済む）
        list.MouseDoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                window.DialogResult = true;
            }
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        if (skipText is not null)
        {
            var skip = new Button { Content = skipText, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 3, 12, 3) };
            skip.Click += (_, _) =>
            {
                skipped = true;
                window.DialogResult = true;
            };
            buttons.Children.Add(skip);
        }

        buttons.Children.Add(cancel);

        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(list);
        body.Children.Add(buttons);
        window.Content = body;

        if (window.ShowDialog() != true)
        {
            return null;
        }

        return skipped ? Skipped : list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
}
