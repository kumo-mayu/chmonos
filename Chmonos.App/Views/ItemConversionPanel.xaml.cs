using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Views;

/// <summary>「変換扱い」の欄。商品ページとアバターの管理の「変換」で同じ部品を使う（DataContext は ItemConversionEditor）。</summary>
public partial class ItemConversionPanel : UserControl
{
    public ItemConversionPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// つなぎ先の札に乗せたとき、アバターか共通素体の絵の入った吹き出しをその場で作る（ユーザ指示 2026-11-09）。
    /// 対応アバターの札と同じ作り：札ごとに先に読むと、プロファイルの多い商品で開くのが遅れる。札の中の ✓・× の説明のときは作らない
    /// </summary>
    private void OnTargetToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)
            || sender is not FrameworkElement { DataContext: ViewModels.ConversionTargetRow row } chip
            || chip.ToolTip is not string)
        {
            return;
        }

        var panel = new StackPanel { MaxWidth = 260 };
        if (row.IconFactory?.Invoke() is { } icon)
        {
            panel.Children.Add(new Image
            {
                Source = icon,
                Width = 160,
                Height = 160,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }

        panel.Children.Add(new TextBlock { Text = row.TooltipText, TextWrapping = TextWrapping.Wrap });
        chip.ToolTip = panel;
    }
}
