using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chmonos.Api;
using Chmonos.App.Services;

namespace Chmonos.App.Views;

/// <summary>
/// プラグインが足した項目を、メニューの項目にする（plan 44 の②）。本物の項目と取り違えないよう、全部を
/// 「プラグイン ▸ {ソフトの名前} ▸ …」の下にまとめる（名前はソフトが付けた物で、本人の確かめにならない）。
/// 押せない項目も出して吹き出しで理由を言う（右クリックの決まり）。子が全部押せない親は押せなくして、最初の子の理由を言う
/// </summary>
internal static class PluginMenuItems
{
    public const string RootHeader = "プラグイン";

    /// <summary>カードの右クリックに足した物の印（開くたびに外して作り直す）。</summary>
    private const string AddedTag = "PluginMenu.Added";

    /// <summary>
    /// アバターの管理の行（カードと同じメニューを使う）に付ける印。付いていれば、商品の項目に加えてアバターの場所（<c>avatar</c>）の項目も出す。
    /// メニューは全カードで1つなので、どの一覧の行かを行の側に持たせる
    /// </summary>
    public static readonly DependencyProperty IsAvatarRowProperty = DependencyProperty.RegisterAttached(
        "IsAvatarRow", typeof(bool), typeof(PluginMenuItems), new PropertyMetadata(false));

    public static bool GetIsAvatarRow(DependencyObject element) => (bool)element.GetValue(IsAvatarRowProperty);

    public static void SetIsAvatarRow(DependencyObject element, bool value) => element.SetValue(IsAvatarRowProperty, value);

    /// <summary>ソフトごとの段（「プラグイン ▾」のボタンから開くメニューの中身）。</summary>
    public static List<MenuItem> SoftwareItems(PluginMenuService service, IReadOnlyList<PluginMenuSoftware> softwares, string where, IReadOnlyList<string> itemIds)
        => SoftwareItems(service, [(PluginMenuSpot.Items(where, itemIds), softwares)]);

    /// <summary>
    /// 幾つかの場所の項目を、ソフトごとに1つの段へまとめる（アバターの行は商品でもあるので、avatar と item の両方を出す。「プラグイン」を2本並べない）。
    /// 同じ ID の項目が両方の場所にあれば、先に渡した場所（より狭い場所）の物だけを出す：同じ項目が2つ並ぶと、どちらがどう送るか分からない
    /// </summary>
    private static List<MenuItem> SoftwareItems(PluginMenuService service, IReadOnlyList<(PluginMenuSpot Spot, IReadOnlyList<PluginMenuSoftware> Softwares)> parts)
        => Merge(parts).Select(software =>
        {
            var item = Item(software.Name, software.Reason, software.Name, "PluginMenu.Software");
            foreach (var node in software.Nodes)
            {
                item.Items.Add(EntryItem(service, software.Software, node));
            }

            return item;
        }).ToList();

    /// <summary>「プラグイン ▸」の1項目（出すソフトが無ければ null：項目ごと出さない）。</summary>
    public static MenuItem? Root(PluginMenuService service, IReadOnlyList<PluginMenuSoftware> softwares, string where, IReadOnlyList<string> itemIds)
        => Root(service, [(PluginMenuSpot.Items(where, itemIds), softwares)]);

    private static MenuItem? Root(PluginMenuService service, IReadOnlyList<(PluginMenuSpot Spot, IReadOnlyList<PluginMenuSoftware> Softwares)> parts)
    {
        var softwares = Merge(parts);
        if (softwares.Count == 0)
        {
            return null;
        }

        var root = new MenuItem { Header = RootHeader };
        AutomationProperties.SetAutomationId(root, "PluginMenu.Root");
        foreach (var item in SoftwareItems(service, parts))
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
    /// カードの右クリックの末尾に、区切りと「プラグイン ▸」を足す（無ければ足さない。区切りも一緒に出し入れし、区切りが端に残らない）。
    /// 項目は XAML に置かない：カードのメニューは全カードで1つを使い回し、ソフトの有無と押せるかは開いた商品で決まる
    /// </summary>
    public static void AttachTo(ContextMenu menu, PluginMenuService? service, string? itemId)
        => AttachTo(menu, service, itemId is null ? [] : [PluginMenuSpot.Items(PluginMenus.WhereItem, [itemId])]);

    /// <summary>
    /// 右クリックの末尾に、<paramref name="spots"/>（右クリックした物。狭い場所から順に）の項目を「プラグイン ▸」で足す。
    /// ファイル・unitypackage・改変・ショップの行のメニューも、商品のカードと同じ部品で出す
    /// </summary>
    public static void AttachTo(ContextMenu menu, PluginMenuService? service, IReadOnlyList<PluginMenuSpot> spots)
    {
        foreach (var added in menu.Items.OfType<Control>().Where(item => Equals(item.Tag, AddedTag)).ToList())
        {
            menu.Items.Remove(added);
        }

        if (service is null || spots.Count == 0)
        {
            return;
        }

        if (Root(service, spots.Select(spot => (spot, service.Build(spot))).ToList()) is not { } root)
        {
            return;
        }

        root.Tag = AddedTag;

        // 元の項目が無いメニュー（使うまで空の行）では、区切りを頭に置かない
        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator { Tag = AddedTag });
        }

        menu.Items.Add(root);
    }

    /// <summary>
    /// 「プラグイン ▾」のボタンの下に、その時の中身でメニューを開く。閉じたらボタンから外す：残すと、ボタンの右クリックで
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

    /// <summary>まとめたソフト1つ。<see cref="Software"/> は最初の場所の物（押した時の宛先はクライアントの ID で決まる）。</summary>
    private sealed record MergedSoftware(PluginMenuSoftware Software, string Name, PluginMenuReason Reason, List<Node> Nodes);

    /// <summary>項目1つと、押した時に送る場所。</summary>
    private sealed record Node(PluginMenuEntry Entry, PluginMenuSpot Spot, List<Node> Children);

    private static List<MergedSoftware> Merge(IReadOnlyList<(PluginMenuSpot Spot, IReadOnlyList<PluginMenuSoftware> Softwares)> parts)
    {
        var result = new List<MergedSoftware>();
        foreach (var (spot, softwares) in parts)
        {
            foreach (var software in softwares)
            {
                var index = result.FindIndex(merged => merged.Software.ClientId == software.ClientId);
                if (index < 0)
                {
                    result.Add(new MergedSoftware(software, software.Name, software.Reason, []));
                    index = result.Count - 1;
                }
                else if (!result[index].Reason.IsOk && software.Reason.IsOk)
                {
                    // どれかの場所で押せる項目があれば、ソフトの段は押せる
                    result[index] = result[index] with { Reason = software.Reason };
                }

                AddNodes(result[index].Nodes, software.Items, spot);
            }
        }

        return result;
    }

    private static void AddNodes(List<Node> nodes, IReadOnlyList<PluginMenuEntry> entries, PluginMenuSpot spot)
    {
        foreach (var entry in entries)
        {
            var existing = nodes.Find(node => node.Entry.Id == entry.Id);
            if (existing is null)
            {
                var node = new Node(entry, spot, []);
                AddNodes(node.Children, entry.Children, spot);
                nodes.Add(node);
            }
            else if (entry.Children.Count > 0)
            {
                // 下の段を持つ親は、場所ごとに出せる子が違い得るので、子を合わせる
                AddNodes(existing.Children, entry.Children, spot);
            }
        }
    }

    private static MenuItem EntryItem(PluginMenuService service, PluginMenuSoftware software, Node node)
    {
        var entry = node.Entry;
        var reason = node.Children.Count > 0 && node.Children.Any(child => child.Entry.Reason.IsOk) ? PluginMenuReason.Ok : entry.Reason;
        var item = Item(entry.Label, reason, software.Name, "PluginMenu.Item");
        if (node.Children.Count > 0)
        {
            foreach (var child in node.Children)
            {
                item.Items.Add(EntryItem(service, software, child));
            }

            return item;
        }

        item.Click += (_, _) => service.PressAsync(software, entry, node.Spot).Forget();
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
