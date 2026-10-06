using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索の条件の監修（2026-10-06・メモ82〜84）で作り直した、選ぶ形・候補から積む形の条件。
/// 足した直後の姿は網羅の場面（<c>catalog-module-*</c>）が撮るので、ここは値を入れないと見えない所だけ：
/// 改変の2段・ユーザータグの AND の中の「小分類なし」・長いパスの札・選ぶ形の補助のチェック。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> SearchModuleReview =>
    [
        new Scene("search-modification-two-level", "検索の絞り込み：改変（アバター → 改変の2段。改変2つを AND、アバター2体を AND）", async context =>
        {
            await SeedSearchModificationsAsync(context);
            var (search, root) = await StartCatalogFiltersAsync(context);
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");

            // 改変の名前で選ぶと、アバターの枠が立ってその改変が入る
            module.AddCommand.Execute("学園の制服");
            var row = module.Rows.Single();
            row.AddCommand.Execute(row.Suggestions.First());
            row.MatchAll = true;
            module.AddCommand.Execute("作り物アバター乙");
            module.MatchAll = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-modification-avatar-only", "検索の絞り込み：改変（アバターだけを選んだ枠。改変を選ばないときの一言）", async context =>
        {
            await SeedSearchModificationsAsync(context);
            var (search, root) = await StartCatalogFiltersAsync(context);
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");
            module.AddCommand.Execute("作り物アバター甲");

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-user-tag-no-sub-and", "検索の絞り込み：ユーザータグの AND の中の「小分類なし」（必ず0件になるので警告の色）", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var tags = (UserTagModule)AddModule(search, SearchModuleKind.UserTag);
            var top = tags.AddTop("衣装")!;
            top.AddCommand.Execute(UserTagTopRow.NoSubText);
            top.AddCommand.Execute("夏");
            top.MatchAll = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-long-paths", "検索の絞り込み：ファイルの場所とUnityプロジェクトの長いパスの札（間を省いて末尾を残す）", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var path = (ListModule)AddModule(search, SearchModuleKind.Path);
            const string longFolder = @"D:\作り物のライブラリ\アバター用の衣装\とても長い名前のショップのフォルダ\2026年の夏の新作\最後のフォルダ";
            path.AddKey(longFolder, text: longFolder);
            path.AddKey(@"D:\作り物\短い", text: @"D:\作り物\短い");
            var project = (ListModule)AddModule(search, SearchModuleKind.UnityProject);
            const string longProject = @"D:\UnityProjects\作り物のとても長い名前のアバターの改変用プロジェクト\撮影用";
            project.AddKey(longProject, text: $"撮影用（{longProject}）");

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-choice-flags", "検索の絞り込み：ギフト（購入記録の無い商品も含める）と見つからないファイル（未所持も含める）の補助のチェック", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var gift = (ChoiceModule)AddModule(search, SearchModuleKind.Gift);
            gift.Selected = gift.Options[2];
            gift.Flag = true;
            var missing = (ChoiceModule)AddModule(search, SearchModuleKind.MissingFile);
            missing.Flag = false;

            await context.SettleAsync();
            return FiltersShot(root);
        }),
    ];

    /// <summary>改変3つ：甲の「夏の普段着」「学園の制服」（プロジェクト付き）と、乙の「夏の普段着」（同じ名前はアバターで見分ける）。</summary>
    private static async Task SeedSearchModificationsAsync(SceneContext context)
    {
        var created = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
        async Task SaveAsync(string avatar, string name, string? project, params string[] members)
            => await context.Seed.Modifications.SaveAsync(new ModificationRecord
            {
                Id = ModificationId.For(avatar, name, created),
                AvatarItemId = avatar,
                Name = name,
                CreatedAt = created,
                UpdatedAt = created,
                UnityProject = project,
                Members = [.. members.Select(id => new ModificationMember { ItemId = id, AddedAt = created })],
            });

        await SaveAsync("9900901", "夏の普段着", null, "9900301", "9900302");
        await SaveAsync("9900901", "学園の制服", @"D:\UnityProjects\学園の撮影", "9900302", "9900303");
        await SaveAsync("9900902", "夏の普段着", null, "9900304");
    }
}
