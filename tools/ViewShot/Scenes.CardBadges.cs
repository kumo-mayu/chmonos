using Chmonos.App.ViewModels;

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
