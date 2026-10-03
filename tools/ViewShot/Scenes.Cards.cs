using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

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

        // 左の欄は検索と追加を1本にした形（2026-10-02）。打った名前が無ければ欄の下に追加の行が出て、あれば出ずに一覧で強調される
        new Scene("tag-manage-typed-new", "タグの管理：左の欄に、まだ無い名前（既存の名前の一部）を打った所。欄の下に追加の行", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            context.Screen<TagManageViewModel>().FilterText = "衣";
            await context.SettleAsync();
            return new Shot(root);
        }),

        // 並べ替えのつかみ（六点）は「候補の並べ替え」のときだけ出す（2026-10-02）。名前順の絵（tag-manage-typed-new など）と見比べる
        new Scene("tag-manage-manual", "タグの管理：表示順を「候補の並べ替え」にした所。左の一覧の行に並べ替えのつかみが出る", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            context.Screen<TagManageViewModel>().Sort = TagSortMode.Manual;
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("tag-manage-typed-existing","タグの管理：左の欄に、今ある大分類の名前を打った所。追加の行は無く、一覧で強調", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            context.Screen<TagManageViewModel>().FilterText = "衣装";
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("attribute-manage-typed-new", "属性の管理：左の欄に、まだ無い名前を打った所。欄の下に追加の行", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            context.Screen<AttributeManageViewModel>().FilterText = "かわい";
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("attribute-manage-typed-existing", "属性の管理：左の欄に、今ある属性の名前を打った所。追加の行は無く、一覧で強調", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            context.Screen<AttributeManageViewModel>().FilterText = "かわいさ";
            await context.SettleAsync();
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
