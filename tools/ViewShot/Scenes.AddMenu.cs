using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ViewShot;

/// <summary>
/// 「＋ 条件を追加」のメニュー（ユーザ判断 2026-10-06・open.md の「条件を追加の並びの監修」）。
/// メニューはポップアップで台では描けないので、本物の MenuItem の型が作った中身（見出しの一覧と、見出しごとの下の段）を
/// ポップアップから外して横に並べる。項目の名前・群の間の区切り線・グレーは、画面と同じ型と束縛のまま。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AddMenuScenes =>
    [
        new Scene("search-add-menu", "検索の「＋ 条件を追加」のメニュー：左が見出しの一覧、右へ見出しごとの下の段（群の間に区切り線）", async context =>
        {
            var (_, root) = await StartCatalogFiltersAsync(context);
            var top = Look.All<MenuItem>(root).First(item => AutomationProperties.GetAutomationId(item) == "SearchAddModule");

            var host = new StackPanel { Orientation = Orientation.Horizontal };
            host.Children.Add(Column("条件を追加", Detach(top)));
            var shot = SceneContext.OnSurface(host, 20);
            await context.PresentAsync(shot);

            for (var index = 0; index < top.Items.Count; index++)
            {
                if (top.ItemContainerGenerator.ContainerFromIndex(index) is not MenuItem heading)
                {
                    throw new InvalidOperationException("見出しの項目が作られていません。");
                }

                host.Children.Add(Column((string)heading.Header, Detach(heading)));
            }

            await context.SettleAsync();
            return new Shot(shot);
        })
        {
            Width = null,
            Height = null,
        },
    ];

    /// <summary>MenuItem の型の中のポップアップから、下の段の入れ物（枠・中の一覧）を外す。</summary>
    private static UIElement Detach(MenuItem item)
    {
        item.ApplyTemplate();
        var popup = item.Template.FindName("PART_Popup", item) as Popup
            ?? throw new InvalidOperationException($"「{item.Header}」の型にポップアップがありません。");
        var child = popup.Child ?? throw new InvalidOperationException($"「{item.Header}」のポップアップが空です。");
        popup.Child = null;
        return child;
    }

    private static FrameworkElement Column(string title, UIElement menu)
    {
        var column = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
        var caption = new TextBlock { Text = title, Margin = new Thickness(2, 0, 0, 6), FontSize = 11 };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        column.Children.Add(caption);
        column.Children.Add(menu);
        return column;
    }
}
