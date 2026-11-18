using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// カードの下の段の札（メモ52・ユーザ判断 2026-10-06）。札が入り切らないとき、容量を先に取り、札は入るだけ出して残りを後ろから色の丸にするのを、
/// カードの幅（スライダーの両端と中ほど）で見る。1枚目は札が少ない商品、2枚目以降は札がそろう商品（見つからない・更新あり・未編集・所持）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> CardBadges =>
    [
        CardBadgesScene("card-badges-160", "カードの下の段：幅160で札が多い商品（容量を先に取り、残りは色の丸）", 160),
        CardBadgesScene("card-badges-200", "カードの下の段：幅200で札が多い商品", 200),
        CardBadgesScene("card-badges-228", "カードの下の段：幅228で札が多い商品", 228),
    ];

    // 小分類まで出したユーザータグの長い札（メモ166）。札の字の上限（84）で切れたままだったのを、段に余った幅まで伸ばす。
    // 幅の広い（360）カードと狭い（228）カードで見比べる：広いカードでは全文、狭いカードは今までと同じ「…」と「+n」
    private static IEnumerable<Scene> CardLongTagScenes =>
    [
        CardLongTagScene("card-long-tags-360", "カードの下の段：幅360で、小分類まで出した長いユーザータグの札", 360),
        CardLongTagScene("card-long-tags-228", "カードの下の段：幅228で、小分類まで出した長いユーザータグの札（今までと同じに切れて「+n」）", 228),
    ];

    private static Scene CardLongTagScene(string name, string title, int width)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 4, change: (index, record) => record with
            {
                Local = record.Local with
                {
                    UserTags =
                    [
                        new UserTagAssignment { Top = "作り物の長い大分類", Subs = ["最初の小分類", "二番目の小分類", "三番目の小分類"] },
                        new UserTagAssignment { Top = "短い", Subs = ["冬"] },
                        new UserTagAssignment { Top = "もう一つ", Subs = [] },
                    ],
                },
            });
            var main = await context.StartAsync(settings => settings with { CardWidth = width, ShowSubTagsInList = true });
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 4, "商品を読み終える");
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == 4, "カードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        });

    private static Scene CardBadgesScene(string name, string title, int width)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 4, change: (index, record) => index == 0 ? record : record with
            {
                Local = record.Local with
                {
                    LocalFiles = record.Local.LocalFiles.Select(file => file with { MissingSince = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) }).ToList(),
                },
            });
            var main = await context.StartAsync(settings => settings with { CardWidth = width });
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 4, "商品を読み終える");
            main.Search.ShowRecentlyAddedFirst();
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == 4, "カードが並ぶ");

            foreach (var card in main.Search.Rows.SelectMany(row => row.Cards.OfType<ItemCardViewModel>()).Skip(1))
            {
                card.HasUpdate = true;
            }

            await context.SettleAsync();
            return new Shot(root);
        });
}
