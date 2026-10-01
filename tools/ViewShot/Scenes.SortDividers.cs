using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索の並べ替えの区切りの札（ユーザ判断 2026-10-01：図書館やビデオショップの分類の札）。
/// カテゴリ順・ショップ順で、まとまりの最初の商品の前に、カードと同じ升（リストでは同じ高さの行）で札が入る。
/// 幅を変えて（--width 900,1280）札が段のどこに来ても崩れないかを見る
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SortDividers =>
    [
        SortScene("search-sort-category", "検索：カテゴリ順のカード（親付きの札・カテゴリなしの札）", SortKind.Category, list: false),
        SortScene("search-sort-shop", "検索：ショップ順のカード（ショップごとの札・ショップなしの札）", SortKind.Shop, list: false),
        SortScene("search-sort-category-list", "検索：カテゴリ順のリスト（札は列をまたぐ1行）", SortKind.Category, list: true),
        SortScene("search-sort-shop-list", "検索：ショップ順のリスト（札は列をまたぐ1行）", SortKind.Shop, list: true),
        // 比べる相手：札を出さない並べ替え（名前順）のリスト。札の行が列の幅や横の流しを変えていないかを見る
        SortScene("search-sort-name-list", "検索：名前順のリスト（札なし。札の入ったリストと比べる）", SortKind.Name, list: true),
    ];

    /// <summary>
    /// 10件：衣装4・装飾品2・とても長い名前のカテゴリ1（表に無い）・カテゴリなし3。ショップは2つと、ショップの無い商品1件
    /// </summary>
    private static Scene SortScene(string name, string description, SortKind kind, bool list) => new(name, description, async context =>
    {
        string?[] categories = ["3D衣装", "3D衣装", "3D装飾品", null, null, "3D衣装", "作り物のとても長い名前のカテゴリで札の中で折り返す分け方", "3D装飾品", "3D衣装", null];
        await SeedLibraryAsync(context, count: categories.Length, (index, item) =>
        {
            var booth = item.Booth with
            {
                Category = categories[index] is { } category ? new BoothCategory { Id = 1, Name = category } : null,
                Shop = index == 9 ? null : item.Booth.Shop,
            };
            return item with
            {
                Booth = booth,
                Local = item.Local with { AcquiredAt = new DateOnly(2026, 9, 1 + index) },
            };
        });

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == categories.Length, "商品を読み終える");

        // 保存された条件が無い保存先では、最初の条件「お気に入りのみ」で0件になる（search-cards の注記）。条件を外してから並べ替える
        main.Search.ShowRecentlyAddedFirst();
        main.Search.SortField = main.Search.SortFields.First(field => field.Kind == kind);
        main.Search.IsListMode = list;
        await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == categories.Length, "商品が並ぶ");
        await context.SettleAsync();
        return new Shot(root);
    });
}
