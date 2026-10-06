using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chmonos.App.ViewModels;
using Chmonos.Core.Resolution;

namespace ViewShot;

/// <summary>
/// 「IDを変える」の自動検索（2026-10-06）。窓の候補あり・0件と、商品ページの「BOOTHで見つかりません」の知らせ。
/// **BOOTH へは行かない**：窓には作り物の結果を渡して並べる（<see cref="ChangeItemIdDialogViewModel.ShowSearchResult"/>）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ChangeIdScenes =>
    [
        new Scene("change-id-search-candidates", "IDを変える窓：自動検索の候補3件（同じショップの札・絵の無い候補・長い名前）", async context =>
        {
            await context.StartAsync();
            var model = SearchDialog(context);
            model.ShowSearchResult(new ReplacementProposal(
            [
                new ReplacementCandidate
                {
                    ItemId = "9900302", Name = "作り物のコート【2026年版】", ShopName = "作り物ショップ", ShopSubdomain = "viewshot-a",
                    IsSameShop = true, Score = 5, FoundBy = "前の商品名", Image = SolidPng(Color.FromRgb(0x9a, 0xb8, 0xd8)),
                },
                new ReplacementCandidate
                {
                    ItemId = "9900303", Name = "作り物のコート 冬の着せ替えセット（複数のアバターに対応した、とても長い名前の商品）",
                    ShopName = "別の作り物ショップ", ShopSubdomain = "viewshot-b", Score = 3, FoundBy = "「Fake_Coat_v1.zip」の名前",
                    Image = SolidPng(Color.FromRgb(0xd8, 0xa8, 0x9a)),
                },
                new ReplacementCandidate
                {
                    ItemId = "9900304", Name = "作り物のマフラー", ShopName = "作り物ショップ", ShopSubdomain = "viewshot-a",
                    IsSameShop = true, Score = 1, FoundBy = "前の商品名",
                },
            ], BoothUnreachable: false, Searches: 2));
            PrepareDialogOwner();
            var root = SceneContext.Unwrap(new Chmonos.App.Views.ChangeItemIdDialog(model));
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("change-id-search-empty", "IDを変える窓：自動検索の候補が0件（次にやることの文）", async context =>
        {
            await context.StartAsync();
            var model = SearchDialog(context);
            model.ShowSearchResult(new ReplacementProposal([], BoothUnreachable: false, Searches: 4));
            PrepareDialogOwner();
            var root = SceneContext.Unwrap(new Chmonos.App.Views.ChangeItemIdDialog(model));
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("item-page-not-found-on-booth", "商品ページ：BOOTHで見つからなくなった商品の知らせと「同じ商品をBOOTHで探す」", async context =>
        {
            var item = await context.Fake.ItemAsync("9900301", "作り物のコート", item => item with
            {
                Local = item.Local with { IsDelisted = true, ConsecutiveNotFoundCount = 3 },
            });
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        }),
    ];

    private static ChangeItemIdDialogViewModel SearchDialog(SceneContext context)
        => new(context.Services, "9900301", "作り物のコート",
            new ReplacementClues("作り物のコート", "viewshot-a", [@"D:\files\Fake_Coat_v1.zip"]));

    /// <summary>候補の絵の代わり（1色の 120×120）。</summary>
    private static byte[] SolidPng(Color color)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, 120, 120));
        }

        var bitmap = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
