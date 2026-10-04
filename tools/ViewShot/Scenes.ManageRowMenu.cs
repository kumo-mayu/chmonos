using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// メモ35（2026-10-05）の確かめ：タグの管理・属性の管理の左の一覧の行の右クリックのメニュー。
/// 台ではメニューを開けない（ポップアップは描けない）ので、開く前の出来事（ContextMenuOpening）を手で起こし、
/// 行が選ばれることと、項目の並び・押せるか・吹き出しを調べる。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ManageRowMenuScenes =>
    [
        new Scene("tag-manage-row-menu", "タグの管理：左の一覧の行の右クリック。開く前に行が選ばれ、右の欄と同じ命令の項目が並ぶ（小分類のある大分類は小分類にするが押せない）", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            var screen = context.Screen<TagManageViewModel>();
            var row = screen.Tops.First(top => top.Name == "衣装");
            screen.Selected = null;
            await context.SettleAsync();

            var item = Look.All<ListBoxItem>(root).First(box => ReferenceEquals(box.DataContext, row));
            RaiseContextMenuOpening(item);
            await context.SettleAsync();
            if (!ReferenceEquals(screen.Selected, row))
            {
                throw new InvalidOperationException("右クリックのメニューを開く前に、その行が選ばれていません。");
            }

            var menu = OpenedRowMenu(item);
            ExpectRowMenu(menu, ["名前を変更", "別の大分類の小分類にする", "検索で開く", "この大分類を削除"],
                [true, false, true, true]);
            if (!ReferenceEquals(RowMenuItem(menu, "名前を変更").Command, screen.RenameTopCommand)
                || !ReferenceEquals(RowMenuItem(menu, "この大分類を削除").Command, screen.DeleteTopCommand)
                || !ReferenceEquals(RowMenuItem(menu, "検索で開く").Command, screen.ShowItemsCommand))
            {
                throw new InvalidOperationException("メニューの命令が、右の欄の見出しと違います。");
            }

            Console.WriteLine("  タグ：押せない項目の吹き出し＝" + RowMenuTip(RowMenuItem(menu, "別の大分類の小分類にする")));
            return new Shot(root);
        }),

        new Scene("attribute-manage-row-menu", "属性の管理：左の一覧の行の右クリック。開く前に行が選ばれ、右の欄と同じ命令の項目が並ぶ", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            var screen = context.Screen<AttributeManageViewModel>();
            var row = screen.Rows.First(entry => entry.Name == "かわいさ");
            screen.Selected = null;
            await context.SettleAsync();

            var item = Look.All<ListBoxItem>(root).First(box => ReferenceEquals(box.DataContext, row));
            RaiseContextMenuOpening(item);
            await context.SettleAsync();
            if (!ReferenceEquals(screen.Selected, row))
            {
                throw new InvalidOperationException("右クリックのメニューを開く前に、その行が選ばれていません。");
            }

            var menu = OpenedRowMenu(item);
            ExpectRowMenu(menu, ["名前を変更", "検索で開く", "この属性を削除"], [true, true, true]);
            if (!ReferenceEquals(RowMenuItem(menu, "名前を変更").Command, screen.AskRenameCommand)
                || !ReferenceEquals(RowMenuItem(menu, "この属性を削除").Command, screen.DeleteCommand)
                || !ReferenceEquals(RowMenuItem(menu, "検索で開く").Command, screen.ShowItemsCommand))
            {
                throw new InvalidOperationException("メニューの命令が、右の欄の見出しと違います。");
            }

            return new Shot(root);
        }),
    ];

    /// <summary>ContextMenuEventArgs は外から作れないので、中身の作り方を借りて起こす。</summary>
    private static void RaiseContextMenuOpening(FrameworkElement target)
    {
        var args = (ContextMenuEventArgs)typeof(ContextMenuEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(ctor => ctor.GetParameters().Length == 4)
            .Invoke([target, true, 0.0, 0.0]);
        args.RoutedEvent = FrameworkElement.ContextMenuOpeningEvent;
        target.RaiseEvent(args);
    }

    /// <summary>開いたときに WPF が渡す宛先だけを手で渡す（メニューの DataContext は、行の Tag を引く束縛のまま確かめる）。</summary>
    private static ContextMenu OpenedRowMenu(ListBoxItem item)
    {
        var menu = item.ContextMenu ?? throw new InvalidOperationException("行に右クリックのメニューがありません。");
        menu.PlacementTarget = item;
        return menu;
    }

    private static MenuItem RowMenuItem(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().First(entry => (string)entry.Header == header);

    private static string? RowMenuTip(MenuItem item) => Chmonos.App.Controls.MenuTips.GetDisabled(item);

    private static void ExpectRowMenu(ContextMenu menu, string[] headers, bool[] enabled)
    {
        var items = menu.Items.OfType<MenuItem>().ToList();
        if (!items.Select(entry => (string)entry.Header).SequenceEqual(headers)
            || !items.Select(entry => entry.IsEnabled).SequenceEqual(enabled))
        {
            throw new InvalidOperationException(
                "メニューの項目が違います：" + string.Join("、", items.Select(entry => $"{entry.Header}({(entry.IsEnabled ? "可" : "不可")})")));
        }
    }
}
