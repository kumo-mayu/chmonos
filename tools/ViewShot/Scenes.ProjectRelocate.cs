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
        ProjectVpmScene(),
    ];

    /// <summary>
    /// 右の欄の「VPM」の行（plan 40）。作り物のプロジェクトのフォルダに manifest と package.json を置き、調べた控えも先に書く
    /// （右の欄は控えから読む。起動後の見張りが調べ直しても、同じフォルダを読むので同じ中身になる）。
    /// 名前は作り物。manifest にだけある物（フォルダが無い）と、manifest に無い埋め込みの物も1つずつ置く
    /// </summary>
    private static Scene ProjectVpmScene()
        => new("hub-project-vpm", "改変の画面（プロジェクトの見方）：右の欄の VPM の行", async context =>
        {
            await SeedModificationsAsync(context);
            var project = Fake.Folder(@"ユニティ\MinatoSummer");
            var packages = Path.Combine(project, "Packages");
            Directory.CreateDirectory(packages);
            File.WriteAllText(Path.Combine(packages, "vpm-manifest.json"), """
                {
                  "dependencies": {
                    "com.example.avatars": { "version": "3.10.4" },
                    "com.example.shader": { "version": "2.3.3" },
                    "com.example.missing": { "version": "1.0.0" }
                  },
                  "locked": {
                    "com.example.avatars": { "version": "3.10.4", "dependencies": {} },
                    "com.example.shader": { "version": "2.3.3", "dependencies": {} },
                    "com.example.missing": { "version": "1.0.0", "dependencies": {} },
                    "com.example.framework": { "version": "1.13.1", "dependencies": {} }
                  }
                }
                """);
            foreach (var (folder, name, display, version) in new[]
                     {
                         ("com.example.avatars", "com.example.avatars", "作り物 SDK - Avatars", "3.10.4"),
                         ("com.example.shader", "com.example.shader", "作り物トゥーン", "2.3.3"),
                         ("com.example.framework", "com.example.framework", "作り物フレームワーク", "1.13.1"),
                         ("MyTool", "com.example.mytool", "手で置いた作り物の道具", "0.2.0"),
                     })
            {
                Directory.CreateDirectory(Path.Combine(packages, folder));
                File.WriteAllText(Path.Combine(packages, folder, "package.json"),
                    $$"""{ "name": "{{name}}", "displayName": "{{display}}", "version": "{{version}}" }""");
            }

            var normalized = Chmonos.Core.Services.UnityProjectPath.Normalize(project)!;
            new Chmonos.Core.Storage.UnityScanStore(Chmonos.Core.Storage.AppPaths.Default).Save(
                normalized, Chmonos.Core.Services.UnityProjectScan.Empty with { Vpm = Chmonos.Core.Services.VpmPackages.Read(project) });
            SeedVpmHistory(normalized);

            var main = await context.StartAsync();
            UseTools(context, ToolCombinations[1].Tools, []);
            main.ShowModifications(ModificationHubLevel.Project, new ModificationHubSelection(ModificationHubSelectionKind.Project, project));
            await UntilHubLoadedAsync(main);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Detail is HubProjectDetail { Vpm: not null, HasVpmRemoved: true }, "右にプロジェクトと VPM が出る");

            // 前の版を広げた行と、外したものを開いた所を撮る（畳んだ姿は他の行で見える）
            var detail = (HubProjectDetail)hub.Detail!;
            detail.VpmRows.First(row => row.HasPast).IsExpanded = true;
            var removed = Look.All<Expander>(root).Single(expander => expander.DataContext == detail && expander.Header is TextBlock);
            removed.IsExpanded = true;
            await context.SettleAsync();
            Console.WriteLine($"  VPM：{detail.Vpm!.Count} 個・外したもの {detail.VpmRemovedRows.Count} 個");
            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Width = 1280,
            Height = 760,
        };

    /// <summary>
    /// 控えに VPM の履歴を書き込む（ユーザ判断 2026-11-12）。日付は決めた作り物。
    /// **最後の行はフォルダの中身と同じにする**：起動後の見張りが調べ直して控えると、違う物には今日の行が足され、撮る日で絵が変わる
    /// </summary>
    private static void SeedVpmHistory(string project)
    {
        static Chmonos.Core.Services.VpmHistoryEntry At(string day, string? version, bool removed = false)
            => new(DateTimeOffset.Parse($"{day}T21:00:00+09:00", System.Globalization.CultureInfo.InvariantCulture), version, removed);

        var file = Directory.GetFiles(Path.Combine(Chmonos.Core.Storage.AppPaths.Default.Root, Chmonos.Core.Storage.AppPaths.UnityScansDirName))
            .Single(path => Chmonos.Core.Storage.JsonStore.Read<Chmonos.Core.Storage.UnityScanFile>(path)?.Project == project);
        var saved = Chmonos.Core.Storage.JsonStore.Read<Chmonos.Core.Storage.UnityScanFile>(file)!;
        Chmonos.Core.Storage.JsonStore.Write(file, new Chmonos.Core.Storage.UnityScanFile
        {
            Project = saved.Project,
            Packages = saved.Packages,
            Images = saved.Images,
            Vpm = saved.Vpm,
            VpmHistory =
            [
                new("com.example.avatars", "作り物 SDK - Avatars", [At("2026-08-30", "3.9.0"), At("2026-10-15", "3.10.4")]),
                new("com.example.framework", "作り物フレームワーク", [At("2026-10-15", "1.13.1")]),
                new("com.example.missing", null, [At("2026-09-20", "1.0.0")]),
                new("com.example.mytool", "手で置いた作り物の道具", [At("2026-11-05", "0.2.0")]),
                new("com.example.oldtool", "外した作り物の道具", [At("2026-09-20", "1.4.0"), At("2026-10-15", null, removed: true)]),
                new("com.example.shader", "作り物トゥーン", [At("2026-09-20", "2.3.2"), At("2026-10-28", null, removed: true), At("2026-11-02", "2.3.3")]),
            ],
        });
    }

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
