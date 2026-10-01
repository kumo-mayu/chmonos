using System.Windows.Controls;
using Chmonos.App.Services;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// Unity Hub・VCC・ALCOM の入り方の組み合わせごとの見た目（公開前の点検 2026-10-01）。
///
/// 改変の画面と設定の画面は、この PC に何が入っているかで出る物が変わる。作り手の PC には3つとも入っていて、
/// 入っていない PC の見た目は撮れなかった。「入っているか」と「プロジェクトの一覧」を作り物に差し替えて描く
/// ——どちらもアプリの側に差し替え口（<c>AppServiceContainer.DetectUnityTools</c>・<c>DiscoverUnityProjects</c>）があり、
/// 試験と同じ口を使う。差し替えないと、この PC のレジストリと Hub の一覧を読み、PC ごとに違う絵になる。
/// </summary>
internal static partial class Scenes
{
    // 欄にせず毎回作る：場面の一覧（Scenes.All）は別のファイルの静的な欄で、ファイルをまたぐと初期化の順が決まらない
    private static (string Name, string Title, UnityTools Tools)[] ToolCombinations =>
    [
        ("none", "Hub・VCC・ALCOM のどれも無い", new(HasHub: false, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false)),
        ("hub", "Unity Hub だけ", new(HasHub: true, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false)),
        ("vcc", "VCC だけ", new(HasHub: false, HasVcc: true, HasAlcom: false, LinkOpensAlcom: false)),
        ("alcom", "ALCOM だけ", new(HasHub: false, HasVcc: false, HasAlcom: true, LinkOpensAlcom: true)),
        ("both-link-vcc", "VCC と ALCOM・vcc:// は VCC", new(HasHub: false, HasVcc: true, HasAlcom: true, LinkOpensAlcom: false)),
        ("both-link-alcom", "VCC と ALCOM・vcc:// は ALCOM", new(HasHub: false, HasVcc: true, HasAlcom: true, LinkOpensAlcom: true)),
    ];

    private static IEnumerable<Scene> UnityToolScenes =>
    [
        .. ToolCombinations.Select(combination => HubToolsEmpty(combination.Name, combination.Title, combination.Tools)),
        .. ToolCombinations.Select(combination => SettingsTools(combination.Name, combination.Title, combination.Tools, choice: null)),

        // 選んだ方を後で消した：設定は「VCC」のまま、手元には ALCOM だけ
        SettingsTools("alcom-chose-vcc", "ALCOM だけ・設定は VCC を選んだまま", ToolCombinations[3].Tools, ProjectManagerChoice.Vcc),

        // ALCOM だけの PC でも、一覧は VCC と同じ所に書かれる（alcom.md §2-2）。読んだ一覧の名前が右の欄にどう出るか
        HubToolsProject("alcom", "ALCOM だけ・VCC の一覧に載ったプロジェクトを選んだ右の欄", ToolCombinations[3].Tools, UnityProjectSource.Vcc),
        HubToolsLinkedOnly(),
    ];

    /// <summary>
    /// どれも無い PC で、改変から紐付けたプロジェクトだけが並ぶ所。Hub・VCC の一覧は空でも、紐付けた先は出す（§9-3）。
    /// 一覧の作り物は空にし、改変の記録の紐付けだけで並ばせる（作り物の候補で「改変から紐付けた」を作ると、改変の無い食い違った絵になる）
    /// </summary>
    private static Scene HubToolsLinkedOnly()
        => new("hub-tools-none-linked", "改変の画面：どれも無い・改変から紐付けたプロジェクトを選んだ右の欄", async context =>
        {
            await SeedModificationsAsync(context);
            var path = Fake.Folder(@"ユニティ\MinatoSummer");
            var main = await context.StartAsync();
            UseTools(context, ToolCombinations[0].Tools, []);
            main.ShowModifications(
                ModificationHubLevel.Project, new ModificationHubSelection(ModificationHubSelectionKind.Project, path));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Detail is HubProjectDetail, "選んだプロジェクトが右の欄に出る");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Height = 640,
        };

    /// <summary>改変の画面の Unityプロジェクトの見方で、一覧が空のとき（上のボタンと空の文）。</summary>
    private static Scene HubToolsEmpty(string name, string title, UnityTools tools)
        => new($"hub-tools-{name}", $"改変の画面・プロジェクトの見方が空：{title}", async context =>
        {
            var main = await context.StartAsync();
            UseTools(context, tools, []);
            main.ShowModifications(ModificationHubLevel.Project);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.EmptyText != "読み込んでいます…", "改変の画面の読み込みが済む");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Height = 640,
        };

    /// <summary>プロジェクトを1つ並べ、選んで右の欄に出す（情報元の値と「Unityを開く」）。</summary>
    private static Scene HubToolsProject(string name, string title, UnityTools tools, UnityProjectSource source)
        => new($"hub-tools-{name}-project", $"改変の画面：{title}", async context =>
        {
            var path = Fake.Folder(@"ユニティ\SampleProject");
            var main = await context.StartAsync();
            UseTools(context, tools,
            [
                new UnityProjectCandidate
                {
                    Path = path,
                    Name = "SampleProject",
                    Folder = System.IO.Path.GetDirectoryName(path) ?? path,
                    Version = "2022.3.22f1",
                    Exists = true,
                    IsOpen = false,
                    Source = source,
                    LastWrite = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9)),
                },
            ]);
            main.ShowModifications(
                ModificationHubLevel.Project, new ModificationHubSelection(ModificationHubSelectionKind.Project, path));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Detail is HubProjectDetail, "選んだプロジェクトが右の欄に出る");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Height = 640,
        };

    /// <summary>設定の「プロジェクトの管理に使うアプリ」の行と、その下の説明。</summary>
    private static Scene SettingsTools(string name, string title, UnityTools tools, ProjectManagerChoice? choice)
        => new($"settings-tools-{name}", $"設定の「プロジェクトの管理に使うアプリ」：{title}", async context =>
        {
            var main = await context.StartAsync(choice is { } chosen ? settings => settings with { ProjectManager = chosen } : null);
            UseTools(context, tools, []);
            main.ShowSettings();
            var settings = context.Screen<SettingsViewModel>();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => !settings.IsLoading, "設定を読み終わる");
            await SceneContext.UntilAsync(() => settings.ProjectManagerNote.Length > 0, "VCC・ALCOM を調べ終わる");
            await context.SettleAsync();

            // 見つからない方を選んでいると欄は「vcc:// に合わせる」を見せるが、保存した設定は書き換えない。
            // 欄の結び付け（TwoWay）が見せた値を書き戻して保存してしまわないかを、本物の ComboBox で確かめる
            if (choice is { } saved && context.Services.Settings.ProjectManager != saved)
            {
                throw new InvalidOperationException(
                    $"設定が書き換わった：{saved} → {context.Services.Settings.ProjectManager}");
            }

            // 行の下の説明の文も入るよう、行の四角の周りを広めに取る
            return new Shot(root)
            {
                Focus = () => Look.Ancestor<StackPanel>(Look.Text(root, "プロジェクトの管理に使うアプリ")),
                FocusMargin = 48,
            };
        })
        {
            Height = SettingsHeight,
        };

    /// <summary>
    /// 「入っているか」とプロジェクトの一覧を作り物にする。画面を開く前に入れる（開いたときと窓が手前に戻ったときに読む）。
    /// </summary>
    private static void UseTools(SceneContext context, UnityTools tools, IReadOnlyList<UnityProjectCandidate> projects)
    {
        context.Services.DetectUnityTools = () => tools;
        context.Services.DiscoverUnityProjects = () => projects;
    }
}
