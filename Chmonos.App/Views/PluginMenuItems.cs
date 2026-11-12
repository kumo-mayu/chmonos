using System.Windows.Automation;
using System.Windows.Controls;
using Chmonos.Api;
using Chmonos.App.Services;

namespace Chmonos.App.Views;

/// <summary>
/// ほかのソフトが足した項目を、メニューの項目にする（plan 44 の②）。本物の項目と取り違えないよう、全部を
/// 「ほかのソフト ▸ {ソフトの名前} ▸ …」の下にまとめる（名前はソフトが付けた物で、本人の確かめにならない）。
/// 押せない項目も出して吹き出しで理由を言う（右クリックの決まり）。子が全部押せない親は押せなくして、最初の子の理由を言う
/// </summary>
internal static class PluginMenuItems
{
    public const string RootHeader = "ほかのソフト";

    /// <summary>カードの右クリックに足した物の印（開くたびに外して作り直す）。</summary>
    private const string AddedTag = "PluginMenu.Added";

    /// <summary>ソフトごとの段（「ほかのソフト ▾」のボタンから開くメニューの中身）。</summary>
    public static List<MenuItem> SoftwareItems(PluginMenuService service, IReadOnlyList<PluginMenuSoftware> softwares, string where, IReadOnlyList<string> itemIds)
        => softwares.Select(software =>
        {
            var item = Item(software.Name, software.Reason, software.Name, "PluginMenu.Software");
            foreach (var entry in software.Items)
            {
                item.Items.Add(EntryItem(service, software, entry, where, itemIds));
            }

            return item;
        }).ToList();

    /// <summary>「ほかのソフト ▸」の1項目（出すソフトが無ければ null：項目ごと出さない）。</summary>
    public static MenuItem? Root(PluginMenuService service, IReadOnlyList<PluginMenuSoftware> softwares, string where, IReadOnlyList<string> itemIds)
    {
        if (softwares.Count == 0)
        {
            return null;
        }

        var root = new MenuItem { Header = RootHeader };
        AutomationProperties.SetAutomationId(root, "PluginMenu.Root");
        foreach (var item in SoftwareItems(service, softwares, where, itemIds))
        {
            root.Items.Add(item);
        }

        if (softwares.All(software => !software.Reason.IsOk))
        {
            root.IsEnabled = false;
            root.ToolTip = PluginMenuService.ReasonText(softwares[0].Reason, softwares[0].Name);
        }

        return root;
    }

    /// <summary>
    /// カードの右クリックの末尾に、区切りと「ほかのソフト ▸」を足す（無ければ足さない。区切りも一緒に出し入れし、区切りが端に残らない）。
    /// 項目は XAML に置かない：カードのメニューは全カードで1つを使い回し、ソフトの有無と押せるかは開いた商品で決まる
    /// </summary>
    public static void AttachTo(ContextMenu menu, PluginMenuService? service, string? itemId)
    {
        foreach (var added in menu.Items.OfType<Control>().Where(item => Equals(item.Tag, AddedTag)).ToList())
        {
            menu.Items.Remove(added);
        }

        if (service is null || itemId is null)
        {
            return;
        }

        if (Root(service, service.Build(PluginMenus.WhereItem, [itemId]), PluginMenus.WhereItem, [itemId]) is not { } root)
        {
            return;
        }

        root.Tag = AddedTag;
        menu.Items.Add(new Separator { Tag = AddedTag });
        menu.Items.Add(root);
    }

    /// <summary>
    /// 「ほかのソフト ▾」のボタンの下に、その時の中身でメニューを開く。閉じたらボタンから外す：残すと、ボタンの右クリックで
    /// 前に作ったメニュー（前の選択・前の商品）が開き、古い物を送ってしまう
    /// </summary>
    public static void OpenOnce(Button button, ContextMenu menu, System.Windows.RoutedEventArgs e)
    {
        button.ContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(button.ContextMenu, menu))
            {
                button.ContextMenu = null;
            }
        };
        Controls.MenuButton.OpenBelow(button, e);
    }

    private static MenuItem EntryItem(PluginMenuService service, PluginMenuSoftware software, PluginMenuEntry entry, string where, IReadOnlyList<string> itemIds)
    {
        var item = Item(entry.Label, entry.Reason, software.Name, "PluginMenu.Item");
        if (entry.Children.Count > 0)
        {
            foreach (var child in entry.Children)
            {
                item.Items.Add(EntryItem(service, software, child, where, itemIds));
            }

            return item;
        }

        item.Click += (_, _) => service.PressAsync(software, entry, where, itemIds).Forget();
        return item;
    }

    private static MenuItem Item(string label, PluginMenuReason reason, string softwareName, string automationId)
    {
        // ソフトが付けた名前の「_」をアクセスキーに読ませない（そのまま渡すと「_」が消えて次の字に下線が付く）
        var item = new MenuItem { Header = label.Replace("_", "__", StringComparison.Ordinal) };
        AutomationProperties.SetAutomationId(item, automationId);
        AutomationProperties.SetName(item, label);
        if (!reason.IsOk)
        {
            item.IsEnabled = false;
            item.ToolTip = PluginMenuService.ReasonText(reason, softwareName);
        }

        return item;
    }
}
