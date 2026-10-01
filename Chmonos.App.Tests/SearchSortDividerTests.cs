using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の並べ替えの区切りの札（ユーザ判断 2026-10-01「図書館やビデオショップの分類の為の偽アイテム」）。
/// 札はまとまりの最初の商品の前に、カードと同じ升で入る。商品ではないので、件数・選ぶ・まとめて操作・キーボードの止まりに入らない。
/// </summary>
public class SearchSortDividerTests
{
    private static ItemRecord WithCategory(ItemRecord item, string category)
        => item with { Booth = item.Booth with { Category = new BoothCategory { Id = 1, Name = category } } };

    private static ItemRecord Acquired(ItemRecord item, int day)
        => item with { Local = item.Local with { AcquiredAt = new DateOnly(2026, 9, day) } };

    private static void SortBy(SearchViewModel search, SortKind kind)
        => search.SortField = search.SortFields.First(field => field.Kind == kind);

    /// <summary>並びを「札の名前 n」「商品のID」の文字にする（札と商品の混ざり方を1つの式で比べる）。</summary>
    private static string[] Shown(SearchViewModel search) => search.DisplayItems.Select(entry => entry switch
    {
        SortDivider divider => $"[{divider.Label} {divider.Count}]",
        ItemCardViewModel card => card.Item.Id,
        _ => "?",
    }).ToArray();

    private static async Task<SearchViewModel> ShopsAsync(TestApp app)
    {
        // 作り物の店A（2件）・作り物の店B（1件）・ショップなし（1件）。同じ店の中は入手日の新しい順
        await app.AddItemAsync(Acquired(Make.Item("1000001", "作り物の商品1", shop: "作り物の店A"), 1));
        await app.AddItemAsync(Acquired(Make.Item("1000002", "作り物の商品2", shop: "作り物の店B"), 2));
        await app.AddItemAsync(Acquired(Make.Item("1000003", "作り物の商品3", shop: "作り物の店A"), 3));
        var noShop = Make.Item("1000004", "作り物の商品4");
        await app.AddItemAsync(noShop with { Booth = noShop.Booth with { Shop = null } });
        var search = (await app.StartAsync()).Search;
        SortBy(search, SortKind.Shop);
        return search;
    }

    [Fact]
    public Task ショップ順では_まとまりの最初の商品の前に札が入り_件数を数える() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);

        Assert.Equal(
            ["[作り物の店A 2]", "1000003", "1000001", "[作り物の店B 1]", "1000002", "[ショップなし 1]", "1000004"],
            Shown(search));
        Assert.All(search.ShownDividers, divider => Assert.Equal("ショップ", divider.Kind));
        Assert.Equal("ショップ：作り物の店A、2 件", search.ShownDividers.First().AutomationName);
    });

    [Fact]
    public Task 札は商品ではないので_件数と商品の並びと全部を選ぶに入らない() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);

        Assert.Equal("4 件", search.ResultSummary);
        Assert.Equal(4, search.ListItems.Count);
        Assert.All(search.ListItems, card => Assert.IsType<ItemCardViewModel>(card));

        search.SelectAllCommand.Execute(null);

        Assert.Equal(4, search.SelectedCount);
        Assert.Equal("4 件を選択中", search.SelectionText);
    });

    [Fact]
    public Task カテゴリ順では_カテゴリごとに札が入り_カテゴリの無い商品の札は最後() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(WithCategory(Make.Item("1000001", "作り物の衣装"), "3D衣装"));
        await app.AddItemAsync(WithCategory(Make.Item("1000002", "作り物の髪型"), "3D装飾品"));
        await app.AddItemAsync(Make.Item("1000003", "カテゴリの無い作り物"));
        await app.AddItemAsync(WithCategory(Make.Item("1000004", "作り物の衣装2"), "3D衣装"));
        var search = (await app.StartAsync()).Search;

        SortBy(search, SortKind.Category);

        var dividers = search.ShownDividers.ToList();
        Assert.Equal(["3D衣装", "3D装飾品", "カテゴリなし"], dividers.Select(divider => divider.Label));
        Assert.Equal([2, 1, 1], dividers.Select(divider => divider.Count));
        Assert.Equal(7, search.DisplayItems.Count);
        Assert.IsType<SortDivider>(search.DisplayItems[0]);
        Assert.Equal("1000003", ((ItemCardViewModel)search.DisplayItems[^1]).Item.Id);

        // 親は同梱のカテゴリの表から引く（表が読めない環境では親を出さない）
        if (app.Services.Categories.IsAvailable)
        {
            Assert.Equal("3Dモデル", dividers[0].Parent);
            Assert.Equal("カテゴリ：3Dモデル / 3D衣装、2 件", dividers[0].AutomationName);
        }

        Assert.Null(dividers[2].Parent);
    });

    [Fact]
    public Task 公開日順では_年と月で札が入り_公開日の無い商品は1つにまとめる() => TestApp.Run(async app =>
    {
        static ItemRecord Published(ItemRecord item, int? month)
            => item with { Booth = item.Booth with { PublishedAt = month is { } m ? new DateTimeOffset(2026, m, 10, 0, 0, 0, TimeSpan.FromHours(9)) : null } };

        await app.AddItemAsync(Published(Make.Item("1000001", "作り物の商品1"), 9));
        await app.AddItemAsync(Published(Make.Item("1000002", "作り物の商品2"), 8));
        await app.AddItemAsync(Published(Make.Item("1000003", "作り物の商品3"), 9));
        await app.AddItemAsync(Published(Make.Item("1000004", "作り物の商品4"), null));
        var search = (await app.StartAsync()).Search;

        SortBy(search, SortKind.PublishedAt);

        Assert.Equal(["2026年9月", "2026年8月", "公開日なし"], search.ShownDividers.Select(divider => divider.Label));
        Assert.Equal([2, 1, 1], search.ShownDividers.Select(divider => divider.Count));
    });

    [Fact]
    public Task 既定の入手日順と名前順では_札を出さない() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);

        SortBy(search, SortKind.AcquiredAt);
        Assert.Empty(search.ShownDividers);
        Assert.Equal(search.ListItems, search.DisplayItems.Cast<ItemCardViewModel>());

        SortBy(search, SortKind.Name);
        Assert.Empty(search.ShownDividers);
    });

    [Fact]
    public Task 設定で切ると_札を出さない() => TestApp.Run(async app =>
    {
        await app.ChangeSettingsAsync(settings => settings with { ShowSortDividers = false });
        var search = await ShopsAsync(app);

        Assert.Empty(search.ShownDividers);
        Assert.Equal(4, search.DisplayItems.Count);
    });

    [Fact]
    public Task 札はカードと同じ升に入り_段の中で商品と並ぶ() => TestApp.Run(async app =>
    {
        var search = await ShopsAsync(app);

        // 1段に3つ入る幅：札も1つの升を取る（7つ → 3段）
        search.SetViewportWidth(36 + (Chmonos.App.Services.CardMetrics.SlotWidth * 3) + 1);

        Assert.Equal(3, search.Rows.Count);
        Assert.IsType<SortDivider>(search.Rows[0].Cards[0]);
        Assert.Equal(3, search.Rows[0].Cards.Count);
        Assert.IsType<SortDivider>(search.Rows[1].Cards[0]);
        Assert.Single(search.Rows[2].Cards);
    });

    [Fact]
    public Task 列の数を変えても_絞り込んでも_同じまとまりの札は使い回し_件数だけ替える() => TestApp.Run(async app =>
    {
        // 札を作り直すと段の組み方が同じ物と見なせず、最初の札から後ろの段を全部抜き差しする（固まる）
        var search = await ShopsAsync(app);
        var first = search.ShownDividers.First();

        search.SetViewportWidth(1);
        Assert.Same(first, search.ShownDividers.First());
        Assert.Same(first, search.Rows[0].Cards[0]);

        search.QueryText = "作り物の商品3";
        await app.SettleAsync();

        Assert.Same(first, search.ShownDividers.Single());
        Assert.Equal(1, first.Count);
        Assert.Equal("1 件", first.CountText);
    });

    // ---- 画面の部品 ----

    private const string SlotTemplate =
        """
        <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                      xmlns:controls='clr-namespace:Chmonos.App.Controls;assembly=Chmonos'>
            <controls:CardRowItems ItemsSource='{Binding}'>
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation='Horizontal' />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
            </controls:CardRowItems>
        </DataTemplate>
        """;

    /// <summary>カードの代わりのボタンと、札の部品（中に文字が3つ）を段に並べた一覧。</summary>
    private static CardRowsListBox CardsWithDivider()
    {
        static UIElement Divider()
        {
            var face = new SortDividerBorder { Width = 60, Height = 40, Child = new StackPanel() };
            AutomationProperties.SetName(face, "ショップ：作り物の店、2 件");
            foreach (var text in new[] { "ショップ", "作り物の店", "2 件" })
            {
                ((StackPanel)face.Child).Children.Add(new TextBlock { Text = text });
            }

            return face;
        }

        object[][] rows =
        [
            [Divider(), new Button { Content = "カード1", Width = 60 }, new Button { Content = "カード2", Width = 60 }],
            [new Button { Content = "カード3", Width = 60 }],
        ];
        // 行（段の入れ物）は止まらない（検索の画面の ItemContainerStyle と同じ）
        var list = new CardRowsListBox
        {
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)XamlReader.Parse(SlotTemplate),
            ItemContainerStyle = new Style(typeof(ListBoxItem)) { Setters = { new Setter(UIElement.FocusableProperty, false) } },
        };
        list.BeginInit();
        list.EndInit();
        list.Measure(new Size(400, 300));
        list.Arrange(new Rect(0, 0, 400, 300));
        list.UpdateLayout();
        return list;
    }

    [Fact]
    public Task 読み上げでは_札は名前を持つ文字1つとして出て_押せない() => UiThread.Run(() =>
    {
        var list = CardsWithDivider();

        var children = UIElementAutomationPeer.CreatePeerForElement(list)!.GetChildren();

        Assert.Equal(["ショップ：作り物の店、2 件", "カード1", "カード2", "カード3"], children.Select(child => child.GetName()));
        Assert.Equal(AutomationControlType.Text, children[0].GetAutomationControlType());
        Assert.Null(children[0].GetChildren());
        Assert.Null(children[0].GetPattern(PatternInterface.Invoke));
    });

    [Fact]
    public Task 札はキーボードの止まりに入らず_並びは商品だけを通る() => UiThread.Run(() =>
    {
        // 並びは画面に載った一覧でだけ働く。出さない窓口（親がメッセージ専用）に載せる（ArrowGroupTests と同じ）
        var list = CardsWithDivider();
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SearchSortDividerTests")
        {
            WindowStyle = 0,
            ParentWindow = new IntPtr(-3),
            Width = 1,
            Height = 1,
        })
        {
            RootVisual = new Canvas { Children = { list } },
        };
        list.UpdateLayout();
        ArrowGroup.RefreshNow(list);

        var members = ArrowGroup.MembersOf(list);

        Assert.Equal(["カード1", "カード2", "カード3"], members.Select(member => ((Button)member).Content));
        Assert.Single(members, KeyboardNavigation.GetIsTabStop);
    });
}
