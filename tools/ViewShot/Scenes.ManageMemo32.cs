using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// メモ32（2026-10-04）の確かめ：改変の画面の三角の押せる範囲・アバターの候補の区切り・リストの見方の小分類の縦1列。
/// </summary>
internal static partial class Scenes
{
    /// <summary>押せる範囲に半透明の枠を重ねる（見た目は変えず、当たり判定の四角だけを見せる）。</summary>
    private sealed class HitAdorner(UIElement target) : Adorner(target)
    {
        protected override void OnRender(DrawingContext context)
        {
            var size = ((FrameworkElement)AdornedElement).RenderSize;
            context.DrawRectangle(
                new SolidColorBrush(Color.FromArgb(70, 230, 40, 40)),
                new Pen(new SolidColorBrush(Color.FromRgb(200, 0, 0)), 1),
                new Rect(0, 0, size.Width, size.Height));
        }
    }

    private static async Task<Shot> HubToggleHitAsync(SceneContext context, ModificationHubLevel level)
    {
        await SeedModificationsAsync(context);
        var main = await context.StartAsync();
        main.ShowModifications(level);
        var root = context.MainWindow();
        await context.PresentAsync(root);

        var hub = context.Screen<ModificationHubViewModel>();
        await SceneContext.UntilAsync(() => hub.Lines.Count > 0, "一覧が並ぶ");
        await context.SettleAsync();

        foreach (var toggle in Look.All<ExpandToggle>(root).Where(toggle => toggle.IsVisible && toggle.ActualWidth > 0))
        {
            AdornerLayer.GetAdornerLayer(toggle)?.Add(new HitAdorner(toggle));
            Console.WriteLine($"  三角の押せる範囲 {toggle.ActualWidth:0.#} x {toggle.ActualHeight:0.#}（{System.Windows.Automation.AutomationProperties.GetAutomationId(toggle)}）");
        }

        await context.SettleAsync();
        return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
    }

    private static IEnumerable<Scene> ManageMemo32Scenes =>
    [
        new Scene("hub-toggle-hit-modification", "改変の画面（改変の見方）：折り畳みの三角の押せる範囲を赤い枠で重ねた所", context => HubToggleHitAsync(context, ModificationHubLevel.Modification))
        {
            Width = 900,
            Height = 560,
        },

        new Scene("hub-toggle-hit-avatar", "改変の画面（アバターの見方）：折り畳みの三角の押せる範囲を赤い枠で重ねた所", context => HubToggleHitAsync(context, ModificationHubLevel.Avatar))
        {
            Width = 900,
            Height = 560,
        },

        // 候補の入れ物はポップアップ（別の窓）で撮れないので、ポップアップの中身だけを外して並べる。並びと区切りは本物の Arrange の答え
        new Scene("suggest-avatar-divider", "改変を選ぶ窓の「どのアバターの改変か」の候補：持っているアバター → 区切り線 → ほかのアバター（空の入力と「ア」で絞った所）", context =>
        {
            string[] avatars =
            [
                "作り物のアバターA", "作り物のアバターC", "作り物のアバターE（1234567）",
                "作り物のアバターB", "作り物のアバターD", "アバターF",
            ];
            var host = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var text in new[] { "", "ア" })
            {
                var box = new SuggestBox { Width = 300, Margin = new Thickness(0, 0, 24, 0) };
                var popup = (System.Windows.Controls.Primitives.Popup)box.FindName("DropDown");
                var list = (ListBox)box.FindName("Candidates");
                list.ItemsSource = SuggestBox.Arrange(avatars, text, primaryCount: 3)
                    .Select(pair => new Suggestion { Value = pair.Entry, Display = pair.Entry, HasDividerAbove = pair.DividerAbove })
                    .ToList();
                list.SelectedIndex = 0;
                var panel = (FrameworkElement)popup.Child;
                popup.Child = null;
                panel.Width = 300;
                panel.VerticalAlignment = VerticalAlignment.Top;
                host.Children.Add(panel);
            }

            return Task.FromResult(new Shot(SceneContext.OnSurface(host)));
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("tag-manage-list-subs-column", "タグの管理（リストの見方）：小分類が6つ。縦1列に並ぶ（並べ替えの落とし先が上下だけで分かる）", context => TagManageSubsAsync(context, cards: false))
        {
            Width = 2000, // 右の幅が 540 の2列分（1080）を超える幅。1280 では右が約 730 で、どちらも1列になり差が出ない
            Height = 1100,
        },

        new Scene("tag-manage-cards-subs-columns", "タグの管理（カードの見方）：小分類が6つ。幅に応じて列に並ぶ（今のまま）", context => TagManageSubsAsync(context, cards: true))
        {
            Width = 2000, // 右の幅が 540 の2列分（1080）を超える幅。1280 では右が約 730 で、どちらも1列になり差が出ない
            Height = 1100,
        },
    ];

    private static async Task<Shot> TagManageSubsAsync(SceneContext context, bool cards)
    {
        string[] subs = ["冬", "夏", "春", "秋", "和装", "洋装"];
        await SeedLibraryAsync(context, count: 8, change: (index, record) => index >= 6 ? record : record with
        {
            Local = record.Local with { UserTags = [new UserTagAssignment { Top = "衣装", Subs = [subs[index]] }] },
        });
        await context.Seed.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = [new UserTagTop { Name = "衣装", Subs = subs.Select(name => new UserTagSub { Name = name }).ToList() }],
        });
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
        screen.Sort = TagSortMode.Manual;
        await context.SettleAsync();
        return new Shot(root);
    }
}
