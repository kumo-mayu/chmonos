using Chmonos.Api;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// ほかのソフトが足した項目（plan 44 の②・docs/spec/plugins-menu.md）：許し直しの窓・複数選択の帯の「ほかのソフト ▾」・押した後の下の帯・
/// 商品ページの「ほかのソフト ▾」。右クリックのメニューそのものは台で描けないので、中身は試験（ApiPluginMenuTests）で見る。名前・ID は作り物
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> PluginScenes =>
    [
        new Scene("plugin-menu-approval", "メニューの項目の許可の窓：宣言を変えた（追加1・変わる1・なくなる1）・長い exe のパス・あと1回拒否すると止める", context =>
        {
            var change = new PluginMenuChange(
                [new PluginMenuLine("preview", "衣装をプレビュー", null, ["item", "selection"], ["name", "files", "unityPackages", "avatars"], null)],
                [new PluginMenuLine("csv", "CSV", "書き出す", ["selection"], ["name"], null)],
                [new PluginMenuLine("old", "古い書き出し", null, ["item"], [], null)]);
            var model = new PluginMenuApprovalViewModel(new PluginMenuRequestInfo(
                "作り物のプレビュー",
                @"C:\Users\Example\AppData\Local\Programs\VeryLongFolderNameWithoutAnySpaces\SamplePreview\SamplePreview.exe",
                null, change, IsFirst: false, RefusalsLeft: 1));
            var window = new Chmonos.App.Views.PluginMenuApprovalDialog(model, owner: null);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },

        // 上限いっぱい（32個）の最初の宣言：並びだけが縦に送られ、窓が画面からはみ出さないか
        new Scene("plugin-menu-approval-many", "メニューの項目の許可の窓：最初の宣言で上限いっぱいの32個（並びだけ送る）", context =>
        {
            var added = Enumerable.Range(1, PluginMenus.MaxItems)
                .Select(index => new PluginMenuLine($"a{index}", $"作り物の操作 {index}", index % 4 == 0 ? "まとめ" : null, ["item"], ["name"], null))
                .ToList();
            var model = new PluginMenuApprovalViewModel(new PluginMenuRequestInfo(
                "作り物の道具", null, "http://localhost:5173", new PluginMenuChange(added, [], []), IsFirst: true));
            var window = new Chmonos.App.Views.PluginMenuApprovalDialog(model, owner: null);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("plugin-selection-bar", "複数選択の帯：「ほかのソフト ▾」が1つ・下の帯に「送りました」", async context =>
        {
            await SeedLibraryAsync(context, count: 4);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 4, "商品を読み終える");
            foreach (var card in main.Search.ListItems.Take(2))
            {
                card.IsSelected = true;
            }

            main.PluginMenus.Preview(hasItemMenus: true, hasSelectionMenus: true, "作り物のプレビューに送りました。");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("plugin-notice-failed", "下の帯：ソフトがうまくいかなかったと返した（注意の色）", async context =>
        {
            await SeedLibraryAsync(context, count: 2);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 2, "商品を読み終える");
            main.PluginMenus.Preview(hasItemMenus: true, hasSelectionMenus: true, "作り物のプレビュー：プレビューを開けませんでした。Unityのバージョンを確かめてください。", failed: true);
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("plugin-item-page", "商品ページの上の帯：「ほかのソフト ▾」", async context =>
        {
            await SeedLibraryAsync(context, count: 1);
            var main = await context.StartAsync();
            var item = await context.Seed.Items.LoadAsync("9900301") ?? throw new InvalidOperationException("作り物の商品が読めません。");
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();

            // 係を作った時の見直し（画面のスレッドへ渡した物）が済んでから出す
            main.PluginMenus.Preview(hasItemMenus: true, hasSelectionMenus: false);
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];
}
