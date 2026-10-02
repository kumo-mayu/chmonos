using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// アバターの管理（2026-10-02 のメモ9 の直しで足した）。リストとカードの切り替え・右の欄・共通素体の一覧。
/// </summary>
internal static partial class Scenes
{
    private const string OwnedAvatarId = "9900301";

    private static IEnumerable<Scene> AvatarScenes =>
    [
        // メモ9-①「リストにしてもカードが混ざる」の確かめ。カードで並べて流した後にリストへ切り替え、一覧を端から端まで流しながら、
        // 作られた行のどれかにカードが出ていないかを数える。混ざっていたら場面ごと落とす（絵を見なくても分かるように）
        new Scene("avatars-list", "アバターの管理：カードで流した後にリストへ切り替え、端まで流した所（カードが混ざらない）", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            avatars.ShowCardsCommand.Execute(null);
            await context.SettleAsync();
            await ScrollThroughAsync(context, root, avatars, listMode: false);

            // 畳んだ見出しの中の行は画面の木に付いていない。畳んだまま切り替えてから開く
            SetGroupsExpanded(root, false);
            await context.SettleAsync();
            avatars.ShowListCommand.Execute(null);
            await context.SettleAsync();
            SetGroupsExpanded(root, true);
            await context.SettleAsync();
            await ScrollThroughAsync(context, root, avatars, listMode: true);

            // 絞り込みで行を入れ替えてから戻す（行の部品を作り直す・使い回す）
            avatars.Query = "アバター1";
            await context.SettleAsync();
            avatars.Query = string.Empty;
            await context.SettleAsync();
            await ScrollThroughAsync(context, root, avatars, listMode: true);

            // 途中まで流したところで切り替える（上へ戻さずに）。行き来を2回
            for (var round = 0; round < 2; round++)
            {
                avatars.ShowCardsCommand.Execute(null);
                await context.SettleAsync();
                await ScrollThroughAsync(context, root, avatars, listMode: false, stopAtMiddle: true);
                avatars.ShowListCommand.Execute(null);
                await context.SettleAsync();
                await ScrollThroughAsync(context, root, avatars, listMode: true, fromCurrent: true);
            }

            return new Shot(root);
        }),

        new Scene("avatars-cards", "アバターの管理：リストで流した後にカードへ切り替え、端まで流した所（行が混ざらない）", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            avatars.ShowListCommand.Execute(null);
            await context.SettleAsync();
            await ScrollThroughAsync(context, root, avatars, listMode: true);

            avatars.ShowCardsCommand.Execute(null);
            await context.SettleAsync();
            await ScrollThroughAsync(context, root, avatars, listMode: false);
            return new Shot(root);
        }),

        // 右の欄の ID（押すとコピー。メモ9-②）と「BOOTHの名前に戻す」（メモ9-③）
        new Scene("avatars-detail", "アバターの管理：名前を付けた持っているアバターを選んだ右の欄（ID・BOOTHの名前に戻す）", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            avatars.ShowListCommand.Execute(null);
            avatars.Selected = avatars.Rows.First(row => row.ItemId == OwnedAvatarId);
            await context.SettleAsync();
            await CheckRowMenuAsync(context, root, avatars);
            return new Shot(root);
        }),

        // 共通素体の一覧（足す欄・行の右クリック）と右の欄。既定の素体（名前だけの種）は絵を持たない（メモ3-①）
        new Scene("avatars-bases", "アバターの管理：共通素体の一覧と、選んだ素体の右の欄", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            avatars.ShowBaseModeCommand.Execute(null);
            avatars.SelectedBase = avatars.Bases.First();
            await context.SettleAsync();

            // 行の右クリック（メモ9-⑦）：「削除」だけがあり、その行の削除（右の欄の「削除」と同じ確認の窓）につながる
            var list = Look.All<ListBox>(root).First(box => ReferenceEquals(box.ItemsSource, avatars.Bases));
            var container = Look.All<ListBoxItem>(list).First(item => ReferenceEquals(item.DataContext, avatars.Bases[1]));
            var shown = await MenuItemsAsync(context, container);
            if (shown.Count != 1 || (string)shown[0].Header != "削除" || !ReferenceEquals(shown[0].Command, avatars.Bases[1].DeleteCommand))
            {
                throw new InvalidOperationException($"共通素体の行の右クリック：{string.Join("、", shown.Select(item => item.Header))}");
            }

            Console.WriteLine("  共通素体の行の右クリック：削除（その行の削除につながる）");
            return new Shot(root);
        }),
    ];

    /// <summary>
    /// 持っているアバター3体（1体は名前を付けてある）・名前が挙がっただけのアバター40体・扱わない物1体・共通素体2つ。
    /// 一覧が窓の高さを何画面ぶんも超え、流すと行が使い回される数にする
    /// </summary>
    private static async Task<(AvatarsViewModel Avatars, FrameworkElement Root)> OpenAvatarsAsync(SceneContext context)
    {
        var entries = new List<AvatarRegistryEntry>();
        foreach (var (id, name, manual) in new[]
                 {
                     (OwnedAvatarId, "作り物のアバター「ミナト」", "ミナト（付けた名前）"),
                     ("9900302", "作り物のアバター「ソラ」", (string?)null),
                     ("9900303", "作り物のアバター「ハル」", null),
                 })
        {
            var item = await context.Fake.ItemAsync(id, name, record => record with
            {
                Booth = record.Booth with { Category = new BoothCategory { Id = 208, Name = "3Dキャラクター", ParentName = "3Dモデル" } },
                Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip($@"ライブラリ\avatar_{id}.zip", "avatar.unitypackage"))] },
            });
            entries.Add(new AvatarRegistryEntry
            {
                ItemId = item.Id,
                BoothName = name,
                DisplayName = manual,
                Category = "3Dキャラクター",
                BaseName = id == "9900302" ? "作り物の素体A" : null,
            });
        }

        entries.AddRange(Enumerable.Range(1, 40).Select(index => new AvatarRegistryEntry
        {
            ItemId = (9900400 + index).ToString(CultureInfo.InvariantCulture),
            BoothName = $"作り物の名前だけのアバター{index:00}",
            AvatarOverride = true,
            BaseName = index % 7 == 0 ? "作り物の素体A" : null,
        }));
        entries.Add(new AvatarRegistryEntry { ItemId = "9900499", BoothName = "作り物の扱わない物", AvatarOverride = false });

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = entries,
            BaseGroups =
            [
                new AvatarBaseGroup { Name = "作り物の素体A" },
                new AvatarBaseGroup { Name = "作り物の素体B" },
            ],
        });

        var main = await context.StartAsync();
        // 検索が商品を読み終えるのを待たずに開く（起動直後に開くのと同じ）。前は行を作る時点で商品を引けず、
        // 持っているアバターまで名前だけの札になって、回すたびに絵が変わった（2026-10-02 にこの台で見つけた）。
        // 今は検索が読み終えたら行が商品を引き直すので、待たなくても同じ絵になる
        main.ShowAvatarsCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var avatars = context.Screen<AvatarsViewModel>();
        await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.Rows.Count == entries.Count, "アバターが並ぶ");
        await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 3, "商品を読み終える");
        await context.SettleAsync();

        var withoutCard = avatars.Rows.Where(row => row.IsOwned && !row.HasCard).Select(row => row.Name).ToList();
        if (withoutCard.Count > 0)
        {
            throw new InvalidOperationException($"持っているアバターが名前だけの札のまま：{string.Join("、", withoutCard)}");
        }
        return (avatars, root);
    }

    /// <summary>
    /// アバターの一覧を上から下まで流し、作られた行がどれも今の見せ方（リストかカード）だけを出しているかを確かめる。
    /// 流すと行が使い回されるので、使い回した行で見せ方が外れていれば、ここで見つかる
    /// </summary>
    private static async Task ScrollThroughAsync(
        SceneContext context, FrameworkElement root, AvatarsViewModel avatars, bool listMode, bool stopAtMiddle = false, bool fromCurrent = false)
    {
        var list = Look.All<ListBox>(root).First(box => ReferenceEquals(box.ItemsSource, avatars.Rows));
        var viewer = Look.All<ScrollViewer>(list).First();
        var wrong = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var offset = fromCurrent ? viewer.VerticalOffset : 0.0; ; offset += Math.Max(40, viewer.ViewportHeight / 2))
        {
            if (stopAtMiddle && offset >= viewer.ScrollableHeight / 2)
            {
                viewer.ScrollToVerticalOffset(offset);
                await context.SettleAsync();
                break;
            }

            viewer.ScrollToVerticalOffset(offset);
            await context.SettleAsync();

            foreach (var row in Look.All<ListBoxItem>(list).Where(row => row.IsVisible))
            {
                if (row.DataContext is not AvatarRowViewModel avatar)
                {
                    continue;
                }

                seen.Add(avatar.ItemId);
                if (ShowsCard(row) == listMode)
                {
                    wrong.Add(avatar.ItemId);
                }
            }

            if (offset >= viewer.ScrollableHeight)
            {
                break;
            }
        }

        if (!stopAtMiddle)
        {
            viewer.ScrollToVerticalOffset(0);
            await context.SettleAsync();
        }

        if (wrong.Count > 0)
        {
            throw new InvalidOperationException(
                $"{(listMode ? "リスト" : "カード")}で並べた一覧に、もう片方の見せ方の行が {wrong.Count} 件（見た行 {seen.Count} 件）：{string.Join("、", wrong.Order())}");
        }

        Console.WriteLine($"  {(listMode ? "リスト" : "カード")}：見た行 {seen.Count} 件、混ざり 0 件");
    }

    /// <summary>
    /// アバターの行の右クリック（メモ9-④）：持っているアバターには「お気に入りに入れる」が出て、押すと星が付く。
    /// 名前が挙がっただけのアバター（商品が無い）には出ない。どちらにも「選ぶ」は出ない（1つだけ選ぶ一覧）
    /// </summary>
    private static async Task CheckRowMenuAsync(SceneContext context, FrameworkElement root, AvatarsViewModel avatars)
    {
        var list = Look.All<ListBox>(root).First(box => ReferenceEquals(box.ItemsSource, avatars.Rows));
        var owned = avatars.Rows.First(row => row.ItemId == OwnedAvatarId);
        var ownedRow = Look.All<ListBoxItem>(list).First(item => ReferenceEquals(item.DataContext, owned));
        var headers = (await MenuItemsAsync(context, ownedRow)).Select(item => item.Header?.ToString() ?? string.Empty).ToList();
        if (!headers.Contains("お気に入りに入れる") || headers.Contains("選ぶ"))
        {
            throw new InvalidOperationException($"持っているアバターの行の右クリック：{string.Join("、", headers)}");
        }

        // 押すと、カードの星と同じ道で星が付く。もう一度押して戻す（絵を変えない）
        var favorite = (await MenuItemsAsync(context, ownedRow)).First(item => item.Header?.ToString() == "お気に入りに入れる");
        favorite.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await SceneContext.UntilAsync(() => owned.Card?.IsFavorite == true, "星が付く");
        var unfavorite = (await MenuItemsAsync(context, ownedRow)).First(item => item.Header?.ToString() == "お気に入りから外す");
        unfavorite.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await SceneContext.UntilAsync(() => owned.Card?.IsFavorite == false, "星が外れる");

        var seen = avatars.Rows.First(row => row.Item is null && !row.IsExcluded);
        list.ScrollIntoView(seen);
        await context.SettleAsync();
        var seenRow = Look.All<ListBoxItem>(list).First(item => ReferenceEquals(item.DataContext, seen));
        var seenHeaders = (await MenuItemsAsync(context, seenRow)).Select(item => item.Header?.ToString() ?? string.Empty).ToList();
        if (seenHeaders.Any(header => header.Contains("お気に入り", StringComparison.Ordinal)) || seenHeaders.Contains("選ぶ"))
        {
            throw new InvalidOperationException($"名前だけのアバターの行の右クリック：{string.Join("、", seenHeaders)}");
        }

        list.ScrollIntoView(owned);
        await context.SettleAsync();
        Console.WriteLine($"  持っているアバターの行の右クリック：{string.Join("、", headers)}");
        Console.WriteLine($"  名前だけのアバターの行の右クリック：{string.Join("、", seenHeaders)}");
    }

    /// <summary>
    /// 行の右クリックのメニューに、開いたときと同じ宛先（その行）を渡し、見えている項目を返す。
    /// 台ではメニューを開けない（ポップアップは描けない）ので、開いたときに WPF が渡す物を手で渡す
    /// </summary>
    private static async Task<IReadOnlyList<MenuItem>> MenuItemsAsync(SceneContext context, ListBoxItem row)
    {
        var menu = row.ContextMenu ?? throw new InvalidOperationException("行に右クリックのメニューがありません。");
        menu.PlacementTarget = row;
        menu.DataContext = row.DataContext;
        await context.SettleAsync();
        return menu.Items.OfType<MenuItem>().Where(item => item.Visibility == Visibility.Visible).ToList();
    }

    private static void SetGroupsExpanded(FrameworkElement root, bool expanded)
    {
        foreach (var expander in Look.All<Expander>(root).Where(expander => expander.DataContext is System.Windows.Data.CollectionViewGroup))
        {
            expander.IsExpanded = expanded;
        }
    }

    /// <summary>行にカード（商品のカードか、名前だけの札）が見えているか。</summary>
    private static bool ShowsCard(ListBoxItem row)
        => Look.All<FrameworkElement>(row).Any(element => element.IsVisible
            && (element.DataContext is ItemCardViewModel
                || element is TextBlock { Text: "この名前の商品は手元にありません" }));
}
