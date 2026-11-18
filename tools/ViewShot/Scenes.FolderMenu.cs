using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;

namespace ViewShot;

/// <summary>
/// open.md 90・92 行（2026-11-18）の確かめ：フォルダの右クリック。木のフォルダの行・子フォルダのカード・リストのフォルダの行で、
/// 同じメニューが Tag から画面の命令を引けているか（束縛の道が途切れると、項目が黙って押せなくなる）。
/// 台ではメニューを開けない（ポップアップは描けない）ので、開いたときに WPF が渡す宛先と DataContext だけを手で渡して、項目を読む。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> FolderMenuScenes =>
    [
        new Scene("folder-menu", "フォルダビュー：木のフォルダの行・子フォルダのカード・リストのフォルダの行の右クリック（項目・押せるか・命令を調べる。絵は木と右の欄）", async context =>
        {
            var (main, root) = await OpenLibraryAsync(context);
            main.ShowFolders();
            var folders = context.Screen<FolderViewModel>();
            await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.IsFolderLike), "フォルダの行が並ぶ");

            // いちばん上のドライブの行を選ぶと、右に子フォルダのカードが並ぶ
            var top = folders.Rows.First(row => row.IsFolderLike);
            folders.Selected = top;
            await SceneContext.UntilAsync(() => Look.View<FolderBrowserView>(root) is { } view
                && Look.All<Button>(view).Any(button => button.DataContext is FolderBrowserFolderCard), "右に子フォルダのカードが並ぶ");
            await context.SettleAsync();

            var treeItem = Look.All<ListBoxItem>(root).First(box => ReferenceEquals(box.DataContext, top));
            var treeMenu = await FolderMenuOfAsync(context, treeItem);
            ExpectFolderMenu(treeMenu, "木の行",
                ["折りたたむ", "下の階層まで開く", "下の階層まで折りたたむ", "このフォルダの商品を検索で開く", "エクスプローラで開く", "このフォルダの商品を取り込む", "監視フォルダにする"]);
            if (!ReferenceEquals(FolderMenuItem(treeMenu, "FolderMenu.Toggle").Command, folders.ToggleCommand)
                || !ReferenceEquals(FolderMenuItem(treeMenu, "FolderMenu.Watch").Command, folders.ToggleWatchCommand))
            {
                throw new InvalidOperationException("木の行のメニューの命令が、フォルダの画面の物と違います。");
            }

            var view = Look.View<FolderBrowserView>(root)!;
            var cardButton = Look.All<Button>(view).First(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "FolderCard");
            var cardMenu = await FolderMenuOfAsync(context, cardButton);
            ExpectFolderMenu(cardMenu, "子フォルダのカード",
                ["このフォルダの商品を検索で開く", "エクスプローラで開く", "このフォルダの商品を取り込む", "監視フォルダにする"]);
            if (!ReferenceEquals(FolderMenuItem(cardMenu, "FolderMenu.Import").Command, folders.ImportHereCommand))
            {
                throw new InvalidOperationException("カードのメニューの命令が、右の頭のボタンと違います。");
            }

            // リストに替えると、フォルダの行も同じメニュー（ItemListRowStyle の IsFolder の切り替え）
            var detail = (FolderViewDetail)folders.Detail!;
            detail.IsListMode = true;
            await SceneContext.UntilAsync(() => Look.All<ListViewItem>(view).Any(row => row.DataContext is FolderBrowserFolderCard), "リストにフォルダの行が並ぶ");
            await context.SettleAsync();
            var listRow = Look.All<ListViewItem>(view).First(row => row.DataContext is FolderBrowserFolderCard);
            ExpectFolderMenu(await FolderMenuOfAsync(context, listRow), "リストのフォルダの行",
                ["このフォルダの商品を検索で開く", "エクスプローラで開く", "このフォルダの商品を取り込む", "監視フォルダにする"]);
            detail.IsListMode = false;

            // 探している間は、開閉の3つを押せなくして理由を言う
            folders.Filter = "作り物";
            await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.Entry?.Item is not null), "探した行が並ぶ");
            await context.SettleAsync();
            var filtered = folders.Rows.First(row => row.IsFolderLike);
            var filteredItem = Look.All<ListBoxItem>(root).First(box => ReferenceEquals(box.DataContext, filtered));
            var filteredMenu = await FolderMenuOfAsync(context, filteredItem);
            var toggle = FolderMenuItem(filteredMenu, "FolderMenu.Toggle");
            if (toggle.IsEnabled || Chmonos.App.Controls.MenuTips.GetDisabled(toggle) is not { Length: > 0 } tip)
            {
                throw new InvalidOperationException("探している間に、開閉の項目が押せるか、理由が出ていません。");
            }

            Console.WriteLine("  探している間の開閉の吹き出し＝" + tip);
            folders.Filter = string.Empty;
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>開いたときに WPF が渡す宛先と DataContext だけを手で渡す（項目の命令は宛先の Tag を引く束縛のまま確かめる）。</summary>
    private static async Task<ContextMenu> FolderMenuOfAsync(SceneContext context, FrameworkElement target)
    {
        var menu = target.ContextMenu ?? throw new InvalidOperationException($"{target.GetType().Name} に右クリックのメニューがありません。");
        if (System.Windows.Automation.AutomationProperties.GetAutomationId(menu) != "FolderMenu")
        {
            throw new InvalidOperationException($"{target.GetType().Name} のメニューがフォルダのメニューではありません。");
        }

        menu.PlacementTarget = target;
        menu.DataContext = target.DataContext;
        await context.SettleAsync();
        return menu;
    }

    private static MenuItem FolderMenuItem(ContextMenu menu, string id)
        => menu.Items.OfType<MenuItem>().First(entry => System.Windows.Automation.AutomationProperties.GetAutomationId(entry) == id);

    private static void ExpectFolderMenu(ContextMenu menu, string where, string[] headers)
    {
        var shown = menu.Items.OfType<MenuItem>().Where(entry => entry.Visibility == Visibility.Visible).ToList();
        var notes = string.Join("、", shown.Select(entry => $"{entry.Header}({(entry.IsEnabled ? "可" : "不可")}{(entry.Command is null ? "・命令無し" : "")})"));
        Console.WriteLine($"  {where}：{notes}");
        if (!shown.Select(entry => entry.Header as string).SequenceEqual(headers) || shown.Any(entry => entry.Command is null))
        {
            throw new InvalidOperationException($"{where}のメニューが違います：{notes}");
        }
    }
}
