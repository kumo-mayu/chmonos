using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 条件の AND／OR を、1つしか無い間も隠さず薄く押せなくした姿（ユーザ判断 2026-10-06「消えたりついたりすると縦に揺れるので意味を成さない時はグレーアウト」）。
/// 条件ごとに「1つ（薄い）」と「2つ（押せる）」の2枚を撮る。2枚の差が、増やした項目と AND／OR の濃さの所だけで、その下が縦にずれていないことを diff で見る
/// （属性・ユーザータグの大分類は、増やすと行が1つ増えるので下がるのは行のぶん。AND／OR が出たり消えたりしないことを見る）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> MatchModeDimScenes =>
    [
        .. MatchDim("edit-status", "編集状況", (search, two) =>
        {
            var status = (UneditedModule)AddModule(search, SearchModuleKind.Unedited);
            status.Fields.First(toggle => toggle.Field == Chmonos.Core.Services.EditField.Memo).IsOn = two;
            return Task.CompletedTask;
        }),
        .. MatchDim("updated", "更新通知あり", (search, two) =>
        {
            var updated = (UpdateNoticeModule)AddModule(search, SearchModuleKind.Updated);
            foreach (var kind in updated.Kinds.Skip(two ? 2 : 1))
            {
                kind.IsOn = false;
            }

            return Task.CompletedTask;
        }),
        .. MatchDim("avatar", "対応アバター", (search, two) =>
        {
            var avatar = (ListModule)AddModule(search, SearchModuleKind.Avatar);
            avatar.AddKey("9900901");
            if (two)
            {
                avatar.AddKey("9900902");
            }

            return Task.CompletedTask;
        }),
        .. MatchDim("user-tag", "ユーザータグ（大分類1つ・小分類1つ／2つ）", (search, two) =>
        {
            var tags = (UserTagModule)AddModule(search, SearchModuleKind.UserTag);
            var top = tags.AddTop("衣装")!;
            top.AddCommand.Execute("夏");
            if (two)
            {
                top.AddCommand.Execute("制服");
            }

            return Task.CompletedTask;
        }),
        .. MatchDim("user-tag-top", "ユーザータグ（大分類1つ／2つ）", (search, two) =>
        {
            var tags = (UserTagModule)AddModule(search, SearchModuleKind.UserTag);
            tags.AddTop("衣装");
            if (two)
            {
                tags.AddTop("髪");
            }

            return Task.CompletedTask;
        }),
        .. MatchDim("attribute", "属性（1つ／2つ）", (search, two) =>
        {
            var attributes = (AttributeModule)AddModule(search, SearchModuleKind.Attribute);
            attributes.AddRow("かわいい", 30, 80);
            if (two)
            {
                attributes.AddRow("軽さ", 0, 60);
            }

            return Task.CompletedTask;
        }),
        .. MatchDim("modification", "改変（アバター1体・改変1つ／2つ）", async (search, two) =>
        {
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");
            module.AddCommand.Execute("学園の制服");
            if (two)
            {
                var row = module.Rows.Single();
                row.AddCommand.Execute(row.Suggestions.First());
            }
        }, modifications: true),
    ];

    private static IEnumerable<Scene> MatchDim(string name, string title, Func<SearchViewModel, bool, Task> fill, bool modifications = false)
        => new[] { false, true }.Select(two => new Scene($"match-dim-{name}-{(two ? "two" : "one")}",
            $"検索の絞り込み：{title}の AND／OR（{(two ? "2つ＝押せる" : "1つ＝薄く押せない")}）", async context =>
            {
                if (modifications)
                {
                    await SeedSearchModificationsAsync(context);
                }

                var (search, root) = await StartCatalogFiltersAsync(context);
                await fill(search, two);
                await context.SettleAsync();
                return FiltersShot(root);
            }));
}
