using BoothAssetManager.App.Controls;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;
using BoothAssetManager.Core.Models;

namespace ViewShot;

/// <summary>
/// カードを段に切って並べる画面（検索のほか）。段の部品（CardRowsListBox・CardRowItems）を替えたときに、
/// 検索以外の画面の並びが変わっていないことを前後で比べるために足した（2026-09-30）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> CardLists =>
    [
        new Scene("shops-cards", "ショップ一覧：ショップのカード2枚", async context =>
        {
            var (main, root) = await OpenLibraryAsync(context);
            main.ShowShops();
            var shops = context.Screen<ShopsViewModel>();
            await SceneContext.UntilAsync(() => shops.Rows.Sum(row => row.Cards.Count) == 2, "ショップのカードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        // ショップ画面（そのショップの商品のカード）の場面は作っていない。開くとショップのページを BOOTH へ取りに行く
        // （台は通信を止めてあるので届かないが、失敗が続いて画面が落ち着かず、同じ絵にならない）。一覧の作りは検索と同じ

        new Scene("folder-cards", "フォルダビュー：フォルダを選んだ右の欄（その直下の商品のカード）", async context =>
        {
            var (main, root) = await OpenLibraryAsync(context);
            main.ShowFolders();
            var folders = context.Screen<FolderViewModel>();

            // 畳んだフォルダの中の行は作られない。絞り込むと、当てはまる行まで開いて並ぶ
            folders.Filter = "作り物";
            await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.Entry?.Item is not null), "商品のファイルの行が並ぶ");

            // 商品のファイルのすぐ上の、フォルダの行
            var file = folders.Rows.First(row => row.Entry?.Item is not null);
            folders.Selected = folders.Rows.Take(folders.Rows.IndexOf(file)).Last(row => row.IsFolderLike);
            await SceneContext.UntilAsync(() => Look.View<FolderBrowserView>(root) is { } view && Look.All<ItemCardBorder>(view).Any(), "右にカードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("attribute-manage-cards", "属性の管理：属性を選び、その商品をカードで並べた所", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: true);
            return new Shot(root);
        }),

        new Scene("attribute-manage-list", "属性の管理：属性を選び、その商品をリスト（詰まった行）で並べた所", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            return new Shot(root);
        }),

        new Scene("tag-manage-cards", "タグの管理：大分類を選び、小分類をすべて開いて、中の商品をカードで並べた所", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: true);
            return new Shot(root);
        }),

        new Scene("tag-manage-list", "タグの管理：大分類を選び、小分類をすべて開いて、中の商品をリスト（詰まった行）で並べた所", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            return new Shot(root);
        }),
    ];

    private static async Task<(MainViewModel Main, System.Windows.FrameworkElement Root)> OpenLibraryAsync(SceneContext context)
    {
        await SeedLibraryAsync(context, count: 8);
        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");
        return (main, root);
    }

    /// <summary>8件のうち6件に属性「かわいさ」を付け（段が2つになる数）、属性の管理でその属性を選ぶ。</summary>
    private static async Task<System.Windows.FrameworkElement> OpenAttributeManageAsync(SceneContext context, bool cards)
    {
        await SeedLibraryAsync(context, count: 8, change: (index, record) => index >= 6 ? record : record with
        {
            Local = record.Local with { Attributes = new Dictionary<string, int> { ["かわいさ"] = 40 + (index * 10) } },
        });
        await context.Seed.Attributes.SaveAsync(new AttributeMaster { Attributes = [new AttributeDefinition { Name = "かわいさ" }] });
        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

        main.ShowAttributeManage();
        var screen = context.Screen<AttributeManageViewModel>();
        await SceneContext.UntilAsync(() => screen.Rows.Count > 0, "属性の一覧が並ぶ");
        screen.Selected = screen.Rows.First(row => row.Name == "かわいさ");
        (cards ? screen.ShowCardsCommand : screen.ShowListCommand).Execute(null);

        // 商品の欄は畳んで始まる（三角を押したのと同じ）
        screen.IsItemsExpanded = true;
        await SceneContext.UntilAsync(() => screen.Lines.OfType<ManageItemLine>().Sum(line => line.Items.Count) == 6, "その属性の商品が並ぶ");
        await context.SettleAsync();
        return root;
    }

    /// <summary>8件のうち6件にタグ「衣装／冬」を付け（段が2つになる数）、タグの管理でその大分類を選んで小分類を開く。</summary>
    private static async Task<System.Windows.FrameworkElement> OpenTagManageAsync(SceneContext context, bool cards)
    {
        await SeedLibraryAsync(context, count: 8, change: (index, record) => index >= 6 ? record : record with
        {
            Local = record.Local with { UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["冬"] }] },
        });
        await context.Seed.UserTags.SaveAsync(new UserTagMaster { Tops = [new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "冬" }] }] });
        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

        main.ShowTagManage();
        var screen = context.Screen<TagManageViewModel>();
        await SceneContext.UntilAsync(() => screen.Tops.Count > 0, "大分類の一覧が並ぶ");
        screen.Selected = screen.Tops.First(row => row.Name == "衣装");
        (cards ? screen.ShowCardsCommand : screen.ShowListCommand).Execute(null);
        await SceneContext.UntilAsync(() => screen.HasSubs, "小分類が並ぶ");
        screen.ToggleAllCommand.Execute(null);
        await SceneContext.UntilAsync(() => screen.Lines.OfType<ManageItemLine>().Sum(line => line.Items.Count) == 6, "小分類の中の商品が並ぶ");
        await context.SettleAsync();
        return root;
    }
}
