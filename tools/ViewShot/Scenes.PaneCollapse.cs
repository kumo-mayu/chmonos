using System.Windows;
using Chmonos.App.ViewModels;

namespace ViewShot;

/// <summary>
/// 左右に分かれた画面の左の一覧を畳むつまみ（open.md 51）。6つの画面の開いた姿と畳んだ姿。
/// つまみは境の線に半分はみ出すので、右の欄の頭の字やボタンに重ならないかを見る。畳んだ姿は縦の見出しの帯だけになる。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> PaneCollapseScenes =>
    [
        .. PaneCollapsePair("modifications", "改変の画面", async context =>
        {
            await SeedModificationsAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(ModificationHubLevel.Modification);
            await UntilHubLoadedAsync(main);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Lines.Count > 0, "改変の一覧が並ぶ");
            return (root, hub.ListPane);
        }),
        .. PaneCollapsePair("folder", "フォルダビュー", async context =>
        {
            var shot = await FolderTreeAsync(context, itemNames: false);
            return (shot.Root, context.Screen<FolderViewModel>().ListPane);
        }),
        .. PaneCollapsePair("avatars", "アバターの管理", async context =>
        {
            var (avatars, root) = await OpenAvatarsAsync(context);
            return (root, avatars.ListPane);
        }),
        .. PaneCollapsePair("tags", "タグの管理", async context =>
        {
            var root = await OpenTagManageAsync(context, cards: false);
            return (root, context.Screen<TagManageViewModel>().ListPane);
        }),
        .. PaneCollapsePair("attributes", "属性の管理", async context =>
        {
            var root = await OpenAttributeManageAsync(context, cards: false);
            return (root, context.Screen<AttributeManageViewModel>().ListPane);
        }),
        .. PaneCollapsePair("resolve", "未確定", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\作り物の髪飾り_v1.0.zip", "hair.unitypackage"), contents: ["hair.unitypackage"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\作り物の指輪.zip", "ring.unitypackage"), contents: ["ring.unitypackage"]),
            ]);
            var (root, screen) = await OpenResolveAsync(context, 2);
            screen.Selected = screen.Files[0];
            return (root, screen.ListPane);
        }),
    ];

    private static IEnumerable<Scene> PaneCollapsePair(
        string key, string title, Func<SceneContext, Task<(FrameworkElement Root, PaneColumn Pane)>> open)
    {
        foreach (var collapsed in new[] { false, true })
        {
            yield return new Scene(
                $"pane-collapse-{key}-{(collapsed ? "closed" : "open")}",
                $"{title}：左の一覧を{(collapsed ? "畳んだ所（縦の見出しの帯とつまみ）" : "開いた所（境の線の上のつまみ）")}",
                async context =>
                {
                    var (root, pane) = await open(context);
                    if (collapsed)
                    {
                        pane.ToggleCollapseCommand.Execute(null);
                    }

                    await context.SettleAsync();
                    if (pane.IsCollapsed != collapsed)
                    {
                        throw new InvalidOperationException("左の一覧の畳み具合が、場面の求める形になっていません。");
                    }

                    return new Shot(root);
                });
        }
    }
}
