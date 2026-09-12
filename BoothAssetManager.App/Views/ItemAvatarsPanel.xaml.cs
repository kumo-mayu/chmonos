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

    /// <summary>「＋ 追加」で入力欄が出たら、すぐ打てるようにフォーカスを移す（押してからもう一度入力欄を押させない）。</summary>
    private void OnAddBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is Controls.SuggestBox box)
        {
            Dispatcher.BeginInvoke(new Action(box.FocusInput), DispatcherPriority.Input);
        }
    }
}
