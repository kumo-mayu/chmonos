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

        // 「全部入れた後」と、その1つ手前（甲の改変が1つ残る）。2枚を上から甲の2段目の欄まで切って比べ、欄が消えて下がずれないことを見る
        // （ユーザ指摘 2026-10-06）。改変と甲には絵を置き、札の頭の小さな絵も見る
        new Scene("search-modification-one-left", "検索の絞り込み：改変（甲の改変が1つ残る・乙は改変を選ばない。札の頭に改変の絵）", async context =>
        {
            await SeedSearchModificationsAsync(context, pictures: true);
            var (search, root) = await StartCatalogFiltersAsync(context);
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");
            module.AddCommand.Execute("学園の制服");
            module.AddCommand.Execute("作り物アバター乙");

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-modification-all-added", "検索の絞り込み：改変（候補を全部入れた後。どちらの段の欄も残る）", async context =>
        {
            await SeedSearchModificationsAsync(context, pictures: true);
            var (search, root) = await StartCatalogFiltersAsync(context);
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");
            module.AddCommand.Execute("学園の制服");
            module.AddCommand.Execute("作り物アバター乙");
            foreach (var text in module.Suggestions.ToList())
            {
                module.AddCommand.Execute(text);
            }

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        // 候補の入れ物はポップアップで撮れないので、中身を外して並べる。絵は条件の本物の選び方（IconSelector）で読む
        new Scene("search-modification-suggest-icons", "検索の絞り込み：改変の候補の頭の絵（左：1段目の候補／右：甲の2段目の候補。写真のある改変は写真、無い改変はアバターの絵）", async context =>
        {
            await SeedSearchModificationsAsync(context, pictures: true);
            var (search, _) = await StartCatalogFiltersAsync(context);
            var module = (ModificationModule)AddModule(search, SearchModuleKind.Modification);
            await SceneContext.UntilAsync(() => module.Suggestions.Count > 0, "改変の候補を読む");
            var top = module.Suggestions.ToList();
            var row = module.AddAvatar("9900901");
            var host = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            host.Children.Add(SuggestPanel("1段目（空の入力）", top, string.Empty, module.InfoSelector, ModificationModule.Headings,
                matcher: module.Matcher, icons: module.IconSelector));
            host.Children.Add(SuggestPanel("甲の2段目（空の入力）", row.Suggestions.ToList(), string.Empty, _ => null, null, icons: row.IconSelector));

            await context.SettleAsync();
            return new Shot(SceneContext.OnSurface(host, 20));
        })
        {
            Width = null,
            Height = null,
        },

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

        new Scene("search-unpriced-flags", "検索の絞り込み：有料・無料（4択の最後に両方・「価格が設定されていない商品も表示」）と、価格の同じチェック（別々に持つ）", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var freePaid = (ChoiceModule)AddModule(search, SearchModuleKind.FreePaid);
            freePaid.Flag = true;
            var price = (RangeModule)AddModule(search, SearchModuleKind.Price);
            price.IncludeUnpriced = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-owned-flag", "検索の絞り込み：所持（未所持のみ・「すべてのファイルが見つからなければ未所持とする」を入れた所）", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var owned = (ChoiceModule)AddModule(search, SearchModuleKind.Owned);
            owned.Selected = owned.Options[1];
            owned.Flag = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),

        new Scene("search-updated-match-all", "検索の絞り込み：更新通知あり（種類を2つにして AND。編集状況と同じラジオボタン）", async context =>
        {
            var (search, root) = await StartCatalogFiltersAsync(context);
            var updated = (UpdateNoticeModule)AddModule(search, SearchModuleKind.Updated);
            foreach (var kind in updated.Kinds.Skip(2))
            {
                kind.IsOn = false;
            }

            updated.MatchAll = true;

            await context.SettleAsync();
            return FiltersShot(root);
        }),
    ];

    /// <summary>改変3つ：甲の「夏の普段着」「学園の制服」（プロジェクト付き）と、乙の「夏の普段着」（同じ名前はアバターで見分ける）。</summary>
    /// <param name="pictures">甲の控えの絵と、甲の「学園の制服」の写真を置く（乙は絵なし＝頭文字）。</param>
    private static async Task SeedSearchModificationsAsync(SceneContext context, bool pictures = false)
    {
        var created = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
        async Task SaveAsync(string avatar, string name, string? project, bool photo, params string[] members)
        {
            var id = ModificationId.For(avatar, name, created);
            if (photo)
            {
                Fake.Image(context.Seed.Paths.ModificationImagesDir(id), "photo-1.webp", seed: "modification-photo");
            }

            await context.Seed.Modifications.SaveAsync(new ModificationRecord
            {
                Id = id,
                AvatarItemId = avatar,
                Name = name,
                CreatedAt = created,
                UpdatedAt = created,
                UnityProject = project,
                Members = [.. members.Select(member => new ModificationMember { ItemId = member, AddedAt = created })],
                Images = photo ? [new ModificationImage { FileName = "photo-1.webp" }] : [],
            });
        }

        if (pictures)
        {
            Fake.Image(context.Seed.Paths.AvatarImagesDir("9900901"), "avatar-1.webp", seed: "avatar-kou");
        }

        await SaveAsync("9900901", "夏の普段着", null, false, "9900301", "9900302");
        await SaveAsync("9900901", "学園の制服", @"D:\UnityProjects\学園の撮影", pictures, "9900302", "9900303");
        await SaveAsync("9900902", "夏の普段着", null, false, "9900304");
    }
}
