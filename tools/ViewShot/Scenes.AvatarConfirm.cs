using System.Windows;
using System.Windows.Input;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 対応アバターの「確認済みにする」（ユーザ判断 2026-10-06・メモ83）と、カード・リストの商品名の頭の「R-18」（メモ82）。
/// 確認待ちの札の ✓ は乗せたとき・フォーカスがあるときだけ出るので、札にフォーカスを置いた場面も撮る。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AvatarConfirmScenes =>
    [
        new Scene("item-page-avatars-unconfirmed", "商品ページ：確認待ち3体・確認済み2体の対応アバター。見出しに「確認待ち 3」と［すべて確認済みにする］", async context =>
        {
            var main = await OpenUnconfirmedAsync(context);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
        }),

        new Scene("item-page-avatars-unconfirmed-focus", "商品ページ：確認待ちの札にフォーカスを置いた所（乗せたときと同じに ✓ と × が出る）", async context =>
        {
            await OpenUnconfirmedAsync(context);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            var chip = Look.All<PressableBorder>(root)
                .First(border => border.DataContext is AvatarRow { IsUnconfirmed: true });
            chip.Focus();
            Keyboard.Focus(chip);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
        }),

        AdultScene("search-cards-adult", "検索のカード：R-18 の商品の名前の頭にピンクの「R-18」（2枚目と4枚目）", list: false),
        AdultScene("search-list-adult", "検索のリスト：R-18 の商品の名前の頭にピンクの「R-18」（2行目と4行目）", list: true),
    ];

    private static async Task<MainViewModel> OpenUnconfirmedAsync(SceneContext context)
    {
        static AvatarLink Link(int index, bool confirmed) => new()
        {
            AvatarItemId = (9800100 + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Name = $"作り物のアバター{index:00}",
            Source = confirmed ? AvatarLinkSource.SupportSection : AvatarLinkSource.H2Link,
            Confirmed = confirmed,
        };

        var item = await context.Fake.ItemAsync(
            "9900104",
            "作り物の衣装（確認待ちのある商品）",
            record => record with
            {
                Local = record.Local with
                {
                    Avatars = [Link(1, true), Link(2, true), Link(3, false), Link(4, false), Link(5, false)],
                    AvatarsDetectedAt = DateTimeOffset.Now,
                },
            },
            images: 1);

        var main = await context.StartAsync();
        main.ShowItem(item);
        return main;
    }

    private static Scene AdultScene(string name, string title, bool list)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 4, change: (index, record) => index % 2 == 1
                ? record with { Booth = record.Booth with { IsAdult = true } }
                : record);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 4, "商品を読み終える");
            main.Search.ShowRecentlyAddedFirst();
            main.Search.IsListMode = list;
            await context.SettleAsync();
            return new Shot(root);
        });
}
