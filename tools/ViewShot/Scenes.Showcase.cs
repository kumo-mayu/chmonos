using System.IO;
using System.Windows;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// BOOTH のページ用の画面（2026-10-07）。確かめ用の作り物ではなく、見せるための写し（<c>SandboxGen showcase</c>）を読み込んで描く。
/// 商品は架空（名前は一般名詞・画像は CC0 の写真）。写しの場所は環境変数 <c>CHMONOS_SHOWCASE_STORE</c>、
/// 無ければ <c>%LOCALAPPDATA%\Chmonos-sandboxes\showcase</c>。2倍で描いて素材にする（<c>--scale 2</c>）
/// </summary>
internal static partial class Scenes
{
    private const int ShowcaseItems = 24;

    private static string ShowcaseStore
        => Environment.GetEnvironmentVariable("CHMONOS_SHOWCASE_STORE")
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chmonos-sandboxes", "showcase");

    private static IEnumerable<Scene> ShowcaseScenes =>
    [
        new Scene("showcase-search", "BOOTH 用：検索の画面（カードの一覧と絞り込み）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowSearch();
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        // 絞り込みの組み合わせ：対応アバター2体を AND・価格の範囲2つ（同じ種類は OR）・カテゴリを除く。
        // 結果はワンピース・エプロン・セーターの3件（うさぎとねこの両方に対応し、価格が範囲に入り、装飾品でない物）
        new Scene("showcase-filters", "BOOTH 用：絞り込みを組んだ検索（対応アバター AND・価格2つ・カテゴリを除く）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowSearch();
            foreach (var module in main.Search.Modules.ToList())
            {
                module.RemoveCommand!.Execute(null);
            }

            var avatar = (ListModule)AddModule(main.Search, SearchModuleKind.Avatar);
            avatar.AddKey("avatar:90000001", text: "うさぎ");
            avatar.AddKey("avatar:90000003", text: "ねこ");
            avatar.MatchAll = true;
            var cheap = (RangeModule)AddModule(main.Search, SearchModuleKind.Price);
            cheap.MinText = "0";
            cheap.MaxText = "800";
            var middle = (RangeModule)AddModule(main.Search, SearchModuleKind.Price);
            middle.MinText = "1500";
            middle.MaxText = "2500";
            var category = (ListModule)AddModule(main.Search, SearchModuleKind.Category);
            category.AddKey("3D装飾品");
            category.IsExcluded = true;
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        new Scene("showcase-folder","BOOTH 用：フォルダの画面（今のフォルダの木と、右に商品のカード）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowFolders();
            var folders = context.Screen<FolderViewModel>();
            await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.CanExpand && !row.IsExpanded) || folders.Rows.Any(row => row.Entry is not null), "木を読み終える");
            while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
            {
                folders.ToggleCommand.Execute(closed);
            }

            folders.ShowItemNamesCommand.Execute(null);
            // 右は衣装のフォルダ（絵の並ぶカードが一番多い）
            var file = folders.Rows.First(row => row.Entry?.Item?.Booth.Category?.Name == "3D衣装");
            folders.Selected = folders.Rows.Take(folders.Rows.IndexOf(file)).Last(row => row.IsFolderLike);
            await SceneContext.UntilAsync(() => Look.View<FolderBrowserView>(root) is { } view && Look.All<Chmonos.App.Controls.ItemCardBorder>(view).Any(), "右のカード");
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        new Scene("showcase-item", "BOOTH 用：商品ページ（画像・対応アバター・手元のファイル）", context => ShowcaseItemAsync(context, "ワンピース")) { Width = 1600, Height = 1000 },

        new Scene("showcase-item-confirm", "BOOTH 用：商品ページ（対応アバターの確認待ちが1つ）", context => ShowcaseItemAsync(context, "傘")) { Width = 1600, Height = 1000 },

        new Scene("showcase-modification", "BOOTH 用：改変の画面（アバター・写真・使ったもの）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            var selected = context.Seed.Modifications.EnumerateIds().Order(StringComparer.Ordinal).First();
            main.ShowModifications(ModificationHubLevel.Modification, new ModificationHubSelection(ModificationHubSelectionKind.Modification, selected));
            await UntilHubLoadedAsync(main);
            await UntilModificationShownAsync(context, root);
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        new Scene("showcase-inbox", "BOOTH 用：通知（更新・販売終了）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowInbox();
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        new Scene("showcase-resolve", "BOOTH 用：未確定（商品を特定できなかったファイル）", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowResolve();
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },

        new Scene("showcase-avatars", "BOOTH 用：アバターの画面", async context =>
        {
            var (main, root) = await OpenShowcaseAsync(context);
            main.ShowAvatars();
            await context.SettleAsync();
            return new Shot(root);
        }) { Width = 1600, Height = 1000 },
    ];

    private static async Task<(MainViewModel Main, FrameworkElement Root)> OpenShowcaseAsync(SceneContext context)
    {
        context.CopyStoreFrom(ShowcaseStore);
        // 写しの設定は、場面の組み立てが書き直す。見せたい物（カードの属性の札・大きめの絵）はここで入れ直す
        var main = await context.StartAsync(settings => settings with { ShowCardAttributes = true, ImageMaxEdgePixels = 1024 });
        // Unity のプロジェクトの一覧は、この PC の Unity Hub を読ませずに作り物にする（実機の一覧が画面に写る）
        UseTools(context, ToolCombinations[0].Tools,
        [
            new UnityProjectCandidate { Path = @"D:\Unity\うさぎの改変", Name = "うさぎの改変", Folder = @"D:\Unity", Version = "2022.3.22f1", Exists = true, IsOpen = false, Source = UnityProjectSource.Hub },
            new UnityProjectCandidate { Path = @"D:\Unity\ねこの改変", Name = "ねこの改変", Folder = @"D:\Unity", Version = "2022.3.22f1", Exists = true, IsOpen = false, Source = UnityProjectSource.Hub },
        ]);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == ShowcaseItems, "商品を読み終える");
        return (main, root);
    }

    private static async Task<Shot> ShowcaseItemAsync(SceneContext context, string name)
    {
        var (main, root) = await OpenShowcaseAsync(context);
        var id = (await context.Seed.Items.LoadAllAsync()).Items.First(candidate => candidate.Booth.Name == name).Id;
        var item = main.Search.FindItem(id)!;
        main.ShowItem(item);
        await SceneContext.UntilAsync(() => Look.View<ItemView>(root) is not null, "商品ページ");
        await context.SettleAsync();
        return new Shot(root);
    }
}
