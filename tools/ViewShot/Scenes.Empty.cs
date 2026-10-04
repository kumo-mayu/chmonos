using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 何も無いときの表示を、画面ごとに空の保存先で描く（2026-10-02 メモ6-④：狭い窓で、空の文の右が画面の外へはみ出す）。
/// 幅を狭めて（--width 900,700）見る。画面ごとに1場面にするのは、空の文の置き方が画面ごとに違うので、1枚ずつ比べたいため
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> Empties =>
    [
        EmptyScreen("shops", "ショップ一覧", main => main.ShowShops()),
        EmptyScreen("stats", "統計", main => main.ShowStats()),
        EmptyScreen("inbox", "通知", main => main.ShowInbox()),
        EmptyScreen("folder", "フォルダビュー", main => main.ShowFolders()),
        EmptyScreen("tag-manage", "タグの管理", main => main.ShowTagManage()),
        EmptyScreen("attribute-manage", "属性の管理", main => main.ShowAttributeManage()),
        EmptyScreen("avatars", "アバター", main => main.ShowAvatars()),
        EmptyScreen("resolve", "未確定", main => main.ShowResolve()),
        EmptyScreen("search", "検索", main => main.ShowSearch()),
        EmptyScreen("import", "取り込み", main => main.ShowImport()),
        EmptyScreen("hub-project", "改変の画面（プロジェクト）", main => main.ShowModifications(ModificationHubLevel.Project), hub: true),
        EmptyScreen("hub-avatar", "改変の画面（アバター）", main => main.ShowModifications(ModificationHubLevel.Avatar), hub: true),
        EmptyScreen("hub-modification", "改変の画面（改変）", main => main.ShowModifications(ModificationHubLevel.Modification), hub: true),
    ];

    private static Scene EmptyScreen(string name, string title, Action<MainViewModel> show, bool hub = false)
        => new($"empty-{name}", $"空の表示：{title}（何も保存していない保存先）", async context =>
        {
            var main = await context.StartAsync();
            if (hub)
            {
                // 入れていないと、この PC のレジストリと Hub の一覧を読み、PC ごとに違う絵になる
                UseTools(context, new(HasHub: false, HasVcc: false, HasAlcom: false, LinkOpensAlcom: false), []);
            }

            show(main);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();
            return new Shot(root);
        });
}
