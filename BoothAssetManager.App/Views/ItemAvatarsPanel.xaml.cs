using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BoothAssetManager.App.Views;

/// <summary>
/// 対応アバターの欄。商品ページと編集画面で同じ部品を使う（ユーザ判断：JSONに関わる編集は両方で同等にする）。
/// </summary>
public partial class ItemAvatarsPanel : UserControl
{
    public ItemAvatarsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 札に乗せたとき、アバターの絵の入った枠をその場で作る（R3）。札ごとに先に作ると、多い商品で開くのが遅れた。
    /// 札の中の「×」の説明もここへ上がってくるので、札そのものの説明のときだけ作る
    /// </summary>
    private void OnChipToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)
            || sender is not FrameworkElement { DataContext: ViewModels.AvatarRow row } chip
            || chip.ToolTip is not string)
        {
            return;
        }

        var panel = new StackPanel { MaxWidth = 260 };
        if (row.Icon is { } icon)
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

        panel.Children.Add(new TextBlock { Text = row.SourceTooltip, TextWrapping = TextWrapping.Wrap });
        chip.ToolTip = panel;
    }

    /// <summary>「＋ 追加」で入力欄が出たら、すぐ打てるようにフォーカスを移す（押してからもう一度入力欄を押させない）。</summary>
    private void OnAddBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is Controls.SuggestBox box)
        {
            Dispatcher.BeginInvoke(new Action(box.FocusInput), DispatcherPriority.Input);
        }
    }
}
