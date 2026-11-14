using System.IO;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 通しで見る場面（2026-11-14・v1.3.0 を配る前にユーザが全部の画面を見るため。<c>ViewShot tour</c> で撮る）。
///
/// ほかの場面は「1つの状態を、見たい所で切り出して」描く。こちらは**窓の中身を切らずに、使う人の量のデータで**描き、
/// 窓の大きさ（幅と高さの組）・明暗・データの量（空・少ない・多い）・流した位置を替えて並べる。
/// 実際に使ったときにどう見えるか（一覧の伸び方・折り返し・はみ出し・余白の偏り）を、比べて見るための物。
///
/// データ：少ない＝BOOTH のページ用の写し（<c>showcase</c>・24 件）、多い＝<c>tools/SandboxGen/tour-dense.mjs</c> で作った写し
/// （277 件・長い名前・対応アバター 20 体超の衣装・ファイル 7 本の商品・通知 150 件）、空＝何も無い保存先。
/// 写しの場所は環境変数 <c>CHMONOS_TOUR_FEW</c>・<c>CHMONOS_TOUR_MANY</c>、無ければ <c>%LOCALAPPDATA%\Chmonos-sandboxes\showcase</c>・<c>tour-dense</c>。
/// 設定は既定のまま（見せるための札や大きな絵は入れない。使う人が最初に見る姿にする）。
///
/// 名前は <c>tour-&lt;量&gt;-&lt;画面&gt;[-&lt;流した位置や商品&gt;]</c>。場面は狙った件数が出たかを結果の行に書く（0 件のまま撮らないため）。
/// </summary>
internal static partial class Scenes
{
    private sealed record TourDensity(string Key, string Title, string? Store);

    private static string SandboxRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chmonos-sandboxes");

    private static TourDensity[] TourDensities =>
    [
        new("empty", "空", null),
        new("few", "少ない", Environment.GetEnvironmentVariable("CHMONOS_TOUR_FEW") ?? Path.Combine(SandboxRoot, "showcase")),
        new("many", "多い", Environment.GetEnvironmentVariable("CHMONOS_TOUR_MANY") ?? Path.Combine(SandboxRoot, "tour-dense")),
    ];

    /// <summary>
    /// 撮る場面の量のうち、写しが無い物（「少ない（場所）」の形）。tour が撮り始める前に見て、作り方を言って止まる
    /// （無いまま流すと、その量の場面が全部同じ理由で失敗し、索引が赤い行で埋まる）
    /// </summary>
    internal static IReadOnlyList<string> MissingTourStores(IEnumerable<string> sceneNames) =>
        TourDensities
            .Where(density => density.Store is not null && !Directory.Exists(Path.Combine(density.Store, "items")))
            .Where(density => sceneNames.Any(name => name.StartsWith($"{Catalog.TourPrefix}{density.Key}-", StringComparison.Ordinal)))
            .Select(density => $"{density.Title}（{density.Store}）")
            .ToList();

    /// <summary>通しの場面の既定の大きさ。窓の既定（1440×900。<c>MainWindow.xaml</c>）。撮るときは <c>--size</c> で替える</summary>
    private const double TourWidth = 1440;

    private const double TourHeight = 900;

    private static IEnumerable<Scene> TourScenes => TourDensities.SelectMany(TourScenesFor);

    private static IEnumerable<Scene> TourScenesFor(TourDensity density)
    {
        var d = density.Key;
        var label = density.Title;
        Scene Make(string screen, string title, Func<SceneContext, Task<Shot>> build)
            => new($"tour-{d}-{screen}", $"通し（{label}）：{title}", build) { Width = TourWidth, Height = TourHeight };

        // 流す画面は、上・中ほど・下の3つ（長い画面の下の方は、上だけ撮ると誰も見ない）
        IEnumerable<Scene> Scrolled(string screen, string title, Func<SceneContext, Task<Shot>> build)
        {
            yield return Make(screen, title, build);
            yield return Make(screen + "-mid", title + "（中ほどまで流した）", async context => Scroll(await build(context), 0.5));
            yield return Make(screen + "-end", title + "（下まで流した）", async context => Scroll(await build(context), 1.0));
        }

        foreach (var scene in Scrolled("search", "検索（カード）", context => TourSearchAsync(context, density, list: false)))
        {
            yield return scene;
        }

        yield return Make("search-list", "検索（リスト）", context => TourSearchAsync(context, density, list: true));
        if (density.Store is not null)
        {
            yield return Make("search-filters", "検索（条件を組んだ）", context => TourSearchFiltersAsync(context, density));

            foreach (var scene in Scrolled("item", "商品ページ（ふつうの衣装）", context => TourItemAsync(context, density, TourPick.Typical)))
            {
                yield return scene;
            }

            yield return Make("edit", "編集（ふつうの衣装）", context => TourEditAsync(context, density, TourPick.Typical));
            yield return Make("shop", "ショップのページ（商品の多いショップ）", context => TourShopAsync(context, density));
        }

        if (d == "many")
        {
            foreach (var scene in Scrolled("item-avatars", "商品ページ（対応アバターの多い衣装）", context => TourItemAsync(context, density, TourPick.MostAvatars)))
            {
                yield return scene;
            }

            yield return Make("item-files", "商品ページ（ファイルの多い商品）", context => TourItemAsync(context, density, TourPick.MostFiles));
            yield return Make("item-longname", "商品ページ（名前の長い商品）", context => TourItemAsync(context, density, TourPick.LongestName));
            foreach (var scene in Scrolled("item-description", "商品ページ（説明の長い商品）", context => TourItemAsync(context, density, TourPick.LongestDescription)))
            {
                yield return scene;
            }

            yield return Make("edit-avatars", "編集（対応アバターの多い衣装）", context => TourEditAsync(context, density, TourPick.MostAvatars));
            yield return Make("modification-big", "改変（使ったものの多い改変）", context => TourModificationAsync(context, density, biggest: true));
        }

        yield return Make("import", "取り込み", context => TourScreenAsync(context, density, main => main.ShowImport()));
        foreach (var scene in Scrolled("resolve", "未確定", context => TourScreenAsync(context, density, main => main.ShowResolve())))
        {
            yield return scene;
        }

        yield return Make("shops", "ショップの一覧", context => TourScreenAsync(context, density, main => main.ShowShops()));
        yield return Make("folders", "フォルダ", context => TourFoldersAsync(context, density));
        foreach (var scene in Scrolled("avatars", "アバター", context => TourScreenAsync(context, density, main => main.ShowAvatars())))
        {
            yield return scene;
        }

        yield return Make("modifications", "改変", context => TourModificationAsync(context, density, biggest: false));
        yield return Make("tags", "タグの管理", context => TourScreenAsync(context, density, main => main.ShowTagManage()));
        yield return Make("attributes", "属性の管理", context => TourScreenAsync(context, density, main => main.ShowAttributeManage()));
        foreach (var scene in Scrolled("inbox", "通知", context => TourScreenAsync(context, density, main => main.ShowInbox())))
        {
            yield return scene;
        }

        foreach (var scene in Scrolled("stats", "統計", context => TourScreenAsync(context, density, main => main.ShowStats())))
        {
            yield return scene;
        }

        if (d != "empty")
        {
            yield break;
        }

        // 設定はデータの量で変わらないので、空のときだけ撮る（流す3つ）
        foreach (var scene in Scrolled("settings", "設定", context => TourSettingsAsync(context, density)))
        {
            yield return scene;
        }
    }

    private enum TourPick
    {
        Typical,
        MostAvatars,
        MostFiles,
        LongestName,
        LongestDescription,
    }

    /// <summary>写しを場面の保存先へ写して組み、窓の中身を載せて、商品を読み終えるまで待つ。</summary>
    private static async Task<(MainViewModel Main, FrameworkElement Root)> OpenTourAsync(SceneContext context, TourDensity density)
    {
        if (density.Store is { } store)
        {
            context.CopyStoreFrom(store);
        }

        var count = density.Store is null ? 0 : context.Seed.Items.EnumerateItemIds().Count;
        var main = await context.StartAsync();
        // Unity のプロジェクトの一覧は、この PC の Unity Hub を読ませずに作り物にする（実機の一覧が画面に写る）
        UseTools(context, ToolCombinations[0].Tools,
        [
            new UnityProjectCandidate { Path = @"D:\Unity\うさぎの改変", Name = "うさぎの改変", Folder = @"D:\Unity", Version = "2022.3.22f1", Exists = true, IsOpen = false, Source = UnityProjectSource.Hub },
            new UnityProjectCandidate { Path = @"D:\Unity\ねこの改変", Name = "ねこの改変", Folder = @"D:\Unity", Version = "2022.3.22f1", Exists = true, IsOpen = false, Source = UnityProjectSource.Hub },
        ]);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == count, $"商品 {count} 件を読み終える");
        Console.WriteLine($"  データ：{density.Title}・商品 {count} 件");
        return (main, root);
    }

    private static async Task<Shot> TourScreenAsync(SceneContext context, TourDensity density, Action<MainViewModel> show)
    {
        var (main, root) = await OpenTourAsync(context, density);
        show(main);
        if (main.CurrentViewModel is ModificationHubViewModel)
        {
            await UntilHubLoadedAsync(main);
        }

        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourSearchAsync(SceneContext context, TourDensity density, bool list)
    {
        var (main, root) = await OpenTourAsync(context, density);
        main.ShowSearch();
        main.Search.IsListMode = list;
        await context.SettleAsync();
        return new Shot(root);
    }

    /// <summary>条件を組んだ検索：対応アバター（2体を AND）・カテゴリを除く。多いときは価格の範囲も足す</summary>
    private static async Task<Shot> TourSearchFiltersAsync(SceneContext context, TourDensity density)
    {
        var (main, root) = await OpenTourAsync(context, density);
        main.ShowSearch();
        var items = (await context.Seed.Items.LoadAllAsync()).Items;
        string IdOf(string name) => items.First(item => item.Booth.Name == name).Id;
        var avatar = (ListModule)AddModule(main.Search, SearchModuleKind.Avatar);
        avatar.AddKey($"avatar:{IdOf("うさぎ")}", text: "うさぎ");
        avatar.AddKey($"avatar:{IdOf("ねこ")}", text: "ねこ");
        avatar.MatchAll = density.Key != "many";
        var category = (ListModule)AddModule(main.Search, SearchModuleKind.Category);
        category.AddKey("3Dテクスチャ");
        category.IsExcluded = true;
        if (density.Key == "many")
        {
            var price = (RangeModule)AddModule(main.Search, SearchModuleKind.Price);
            price.MinText = "500";
            price.MaxText = "3000";
        }

        await context.SettleAsync();
        Console.WriteLine($"  結果：{main.Search.ResultSummary}");
        return new Shot(root);
    }

    private static async Task<string> PickItemAsync(SceneContext context, TourPick pick)
    {
        var items = (await context.Seed.Items.LoadAllAsync()).Items;
        var chosen = pick switch
        {
            // ふつうの衣装：showcase の「ワンピース」（多い写しにも同じ名前で入っている）
            TourPick.Typical => items.First(item => item.Booth.Name == "ワンピース"),
            // バリエーションは対応アバターごとに作ってある（tour-dense.mjs）ので、バリエーションの数で選ぶ
            TourPick.MostAvatars => items.MaxBy(item => item.Booth.Variations.Count)!,
            TourPick.MostFiles => items.MaxBy(item => item.Local.LocalFiles.Count)!,
            TourPick.LongestName => items.MaxBy(item => item.Booth.Name.Length)!,
            _ => items.MaxBy(item => item.Booth.Description?.Length ?? 0)!,
        };
        Console.WriteLine($"  商品：バリエーション {chosen.Booth.Variations.Count}・ファイル {chosen.Local.LocalFiles.Count}・名前 {chosen.Booth.Name.Length} 字・説明 {chosen.Booth.Description?.Length ?? 0} 字");
        return chosen.Id;
    }

    private static async Task<Shot> TourItemAsync(SceneContext context, TourDensity density, TourPick pick)
    {
        var (main, root) = await OpenTourAsync(context, density);
        var id = await PickItemAsync(context, pick);
        main.ShowItem(main.Search.FindItem(id)!);
        await SceneContext.UntilAsync(() => Look.View<ItemView>(root) is not null, "商品ページ");
        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourEditAsync(SceneContext context, TourDensity density, TourPick pick)
    {
        var (main, root) = await OpenTourAsync(context, density);
        var id = await PickItemAsync(context, pick);
        await main.ShowEditAsync([id]);
        await SceneContext.UntilAsync(() => Look.View<EditView>(root) is not null, "編集の画面");
        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourShopAsync(SceneContext context, TourDensity density)
    {
        var (main, root) = await OpenTourAsync(context, density);
        var items = (await context.Seed.Items.LoadAllAsync()).Items;
        var shop = items.Where(item => item.Booth.Shop?.Subdomain is not null)
            .GroupBy(item => item.Booth.Shop!.Subdomain!).MaxBy(group => group.Count())!;
        Console.WriteLine($"  ショップの商品 {shop.Count()} 件");
        // 台は通信を止めてあるので、バナーが手元に無いと「確認しています」と再試行の帯のまま撮れる（2026-11-14 の通しで出た）。
        // 使う人の多くは一度開いてバナーを持っている姿なので、作り物のバナーを置いてから開く
        var banner = context.Seed.Paths.ShopBannerFile(shop.Key);
        Fake.Image(Path.GetDirectoryName(banner)!, Path.GetFileName(banner), seed: "banner", width: 960, height: 320);
        await main.ShowShopAsync(shop.Key);
        var screen = context.Screen<ShopViewModel>();
        await SceneContext.UntilAsync(() => screen.IsListReady && screen.HasBanner, "商品とバナーが並ぶ");
        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourFoldersAsync(SceneContext context, TourDensity density)
    {
        var (main, root) = await OpenTourAsync(context, density);
        main.ShowFolders();
        var folders = context.Screen<FolderViewModel>();
        if (density.Store is not null)
        {
            // 木を開き、ファイルのいちばん多いフォルダを選ぶ（右にカードが並ぶ姿）
            await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.CanExpand), "木を読み終える");
            while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
            {
                folders.ToggleCommand.Execute(closed);
            }

            var busiest = folders.Rows.Select((row, index) => (row, index))
                .Where(pair => pair.row.IsFolderLike)
                .MaxBy(pair => folders.Rows.Skip(pair.index + 1).TakeWhile(next => !next.IsFolderLike).Count()).row;
            folders.Selected = busiest;
        }

        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourModificationAsync(SceneContext context, TourDensity density, bool biggest)
    {
        var (main, root) = await OpenTourAsync(context, density);
        var records = (await context.Seed.Modifications.LoadAllAsync()).Modifications.ToList();
        if (records.Count == 0)
        {
            main.ShowModifications();
            await UntilHubLoadedAsync(main);
            await context.SettleAsync();
            return new Shot(root);
        }

        var chosen = biggest ? records.MaxBy(record => record.Members.Count)! : records.OrderBy(record => record.Id, StringComparer.Ordinal).First();
        Console.WriteLine($"  改変 {records.Count} 件・選んだ改変の使ったもの {chosen.Members.Count} 件");
        main.ShowModifications(ModificationHubLevel.Modification, new ModificationHubSelection(ModificationHubSelectionKind.Modification, chosen.Id));
        await UntilHubLoadedAsync(main);
        await UntilModificationShownAsync(context, root);
        await context.SettleAsync();
        return new Shot(root);
    }

    private static async Task<Shot> TourSettingsAsync(SceneContext context, TourDensity density)
    {
        var (main, root) = await OpenTourAsync(context, density);
        main.ShowSettings();
        var settings = context.Screen<SettingsViewModel>();
        await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");
        await context.SettleAsync();
        return new Shot(root);
    }

    /// <summary>
    /// 画面のいちばん長く流せる欄を、流せる長さの <paramref name="at"/>（0〜1）の所へ流す。大きさを替えるたびに当て直す
    /// （窓が広いと流せる長さが縮む）。欄は大きさで変わり得るので、そのたびに探す
    /// </summary>
    private static Shot Scroll(Shot shot, double at) => shot with
    {
        Arrange = () =>
        {
            var viewer = Look.All<ScrollViewer>(shot.Root)
                .Where(candidate => candidate.IsVisible && candidate.ActualHeight > 150)
                .MaxBy(candidate => candidate.ScrollableHeight);
            if (viewer is { ScrollableHeight: > 0 })
            {
                viewer.ScrollToVerticalOffset(viewer.ScrollableHeight * at);
            }
            else
            {
                Console.WriteLine("  流せる欄が無かった（全部が窓に収まっている）");
            }

            return Task.CompletedTask;
        },
    };
}
