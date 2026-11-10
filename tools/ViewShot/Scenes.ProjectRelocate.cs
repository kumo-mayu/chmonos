using System.IO;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// Unity プロジェクトの場所の付け替え（ユーザ判断 2026-11-10）の確かめ：改変の画面のプロジェクトの見方で、
/// フォルダが見つからないプロジェクトの行と、それを選んだ右の欄。
/// Hub・VCC の一覧は空の作り物にする（差し替えないと、この PC の Hub の一覧を読み、PC ごとに違う絵になり実名が写る）。
/// 並ぶのは改変から紐付けたプロジェクトだけで、そのうち1つは在らないフォルダを指す
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ProjectRelocateScenes =>
    [
        ProjectRelocateScene("hub-project-missing", "改変の画面（プロジェクトの見方）：フォルダが見つからないプロジェクトの行と、それを選んだ右の欄", 1280),
        ProjectRelocateScene("hub-project-missing-narrow", "改変の画面（プロジェクトの見方）：フォルダが見つからないプロジェクトの行・窓の最小の幅", 900),
    ];

    private static Scene ProjectRelocateScene(string name, string title, int width)
        => new(name, title, async context =>
        {
            await SeedModificationsAsync(context);

            // 在らないフォルダ（作らない）。移した・名前を変えたあとの古い場所の役
            var missing = Path.Combine(Isolation.FilesRoot, @"ユニティ\MinatoOld");
            var created = new DateTimeOffset(2026, 9, 18, 21, 0, 0, TimeSpan.FromHours(9));
            await context.Seed.Modifications.SaveAsync(new ModificationRecord
            {
                Id = ModificationId.For("9900201", "前のプロジェクトの改変", created),
                AvatarItemId = "9900201",
                Name = "前のプロジェクトの改変",
                CreatedAt = created,
                UpdatedAt = created,
                UnityProject = missing,
                Members = [new ModificationMember { ItemId = "9900202", AddedAt = created }],
            });

            var main = await context.StartAsync();
            UseTools(context, ToolCombinations[1].Tools, []);
            main.ShowModifications(
                ModificationHubLevel.Project, new ModificationHubSelection(ModificationHubSelectionKind.Project, missing));
            await UntilHubLoadedAsync(main);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Detail is HubProjectDetail, "右に見つからないプロジェクトが出る");
            await context.SettleAsync();

            // 台ではメニューを開けないので、見出しの右クリックの項目・命令と、紐付けていない見出しでは開かないことを調べる
            var headers = Look.All<Border>(root).Where(entry => entry.ContextMenu is not null && entry.DataContext is HubProjectGroup).ToList();
            var project = headers.FirstOrDefault(entry => ((HubProjectGroup)entry.DataContext).IsProject)
                ?? throw new InvalidOperationException("プロジェクトの見出しに右クリックのメニューがありません。");
            project.ContextMenu!.PlacementTarget = project;
            var items = project.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().ToList();
            if (items.Count != 1 || (string)items[0].Header != "場所を付け替える…" || !ReferenceEquals(items[0].Command, hub.RelocateProjectCommand))
            {
                throw new InvalidOperationException("プロジェクトの見出しの右クリックの項目が違います。");
            }

            if (headers.Any(entry => !((HubProjectGroup)entry.DataContext).IsProject && System.Windows.Controls.ContextMenuService.GetIsEnabled(entry)))
            {
                throw new InvalidOperationException("紐付けていない改変の見出しで右クリックのメニューが開きます。");
            }

            Console.WriteLine("  見出しの右クリック：場所を付け替える…（紐付けていない見出しでは開かない）");
            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Width = width,
            Height = 700,
        };
}
