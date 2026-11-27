using System.Windows;
using System.Windows.Controls;

namespace Chmonos.App.Views;

public partial class ModificationHubView : UserControl
{
    public ModificationHubView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 改変の行の右クリックに、プラグインの <c>modification</c> の項目を足す（docs/spec/plugins-menu.md）。
    /// 押せるかはソフトの応答で決まるので、開くたびに作り直す
    /// </summary>
    private void OnModificationMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ViewModels.HubModificationRow row } target } menu)
        {
            PluginMenuItems.AttachTo(menu, ItemCardResources.PluginMenusOf(target), [Chmonos.Api.PluginMenuSpot.Modification(row.Record.Id)]);
        }
    }
}
