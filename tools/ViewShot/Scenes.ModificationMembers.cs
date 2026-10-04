using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 改変の「使ったもの」を逆の順で見せた所（メモ26-①）と、手で足すときに使ったファイルを選ぶ窓（メモ26-②）。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> ModificationMembers =>
    [
        new Scene("modification-members-reversed", "改変の詳細：使ったものを逆の順で見せた所（見出しの右の切り替え・番号は入れた順のまま・説明の文）", async context =>
        {
            var selected = await SeedMemberOrderAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(
                ModificationHubLevel.Modification,
                new ModificationHubSelection(ModificationHubSelectionKind.Modification, selected));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            await SceneContext.UntilAsync(() => Look.View<ModificationView>(root) is not null, "選んだ改変が右の欄に出る");
            var detail = (ModificationViewModel)Look.View<ModificationView>(root)!.DataContext;
            await SceneContext.UntilAsync(() => detail.Members.Count == 3, "使ったものが並ぶ");
            detail.ShowReverseOrderCommand.Execute(null);
            await context.SettleAsync();

            return new Shot(root);
        })
        {
            Width = 1280,
            Height = 1500,
        },

        new Scene("pick-member-files-dialog", "使ったものを名前で足す窓：zip の中に unitypackage が2つある商品（最初は何も選ばない）", context =>
        {
            var model = new MemberFilePickViewModel([TwoPackageItem("9900401", "作り物の衣装セット")]);
            var window = new PickMemberFilesDialog(model, "使ったものを追加", "「作り物の衣装セット」をこの改変に追加します。");
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },

        new Scene("pick-modification-files-dialog", "改変に追加の窓：選んだ3件をまとめて足す（商品ごとの使ったファイル・1つだけの商品は選んである）", context =>
        {
            var items = new[]
            {
                TwoPackageItem("9900401", "作り物の衣装セット"),
                OnePackageItem("9900402", "作り物のシェーダー"),
                OnePackageItem("9900403", "作り物のとても長い名前の髪型セット：ロングとショートと三つ編みの3種"),
            };
            var files = new MemberFilePickViewModel(items);
            var model = new PickModificationDialogViewModel(
                "改変に追加",
                $"選んだ {items.Length} 件を改変に追加します。",
                ModificationPicking.FilesContextText(files, string.Empty),
                [],
                [],
                _ => null)
            {
                ExistingLabel = "今ある改変に追加",
                CommitLabel = "追加",
                EmptyText = "改変がまだありません。新しく作って、そこに追加できます。",
                Files = files,
            };
            var window = new PickModificationDialog(model);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },

        // メモ41：今ある改変を探す欄。「作り物」で、プロジェクト名・アバター名に当たった行は何で当たったかが出る
        new Scene("pick-modification-search-dialog", "改変に追加の窓：今ある改変を探す欄（「作り物」で絞った所。アバター・プロジェクトで当たった理由が行に出る）", context =>
        {
            static PickModificationRowViewModel Row(string id, string avatar, string name, string? project, int members) => new()
            {
                Record = new ModificationRecord
                {
                    Id = id,
                    AvatarItemId = "9900001",
                    Name = name,
                    UnityProject = project,
                    Members = [.. Enumerable.Range(0, members).Select(index => new ModificationMember { ItemId = $"99001{index:00}" })],
                },
                AvatarText = avatar,
                ProjectName = ModificationHubViewModel.ProjectNameOf(project),
            };

            var model = new PickModificationDialogViewModel(
                "改変に追加",
                "「作り物の衣装」を改変に追加します。",
                string.Empty,
                [
                    Row("mod-1", "作り物のアバター「ミナト」", "普段着", @"D:\Unity\作り物プロジェクト", 3),
                    Row("mod-2", "作り物のアバター「ミナト」", "制服", null, 0),
                    Row("mod-3", "別のアバター", "作り物の水着", @"D:\Unity\Sandbox", 1),
                    Row("mod-4", "別のアバター", "冬服", @"D:\Unity\作り物プロジェクト", 2),
                ],
                [],
                _ => null)
            {
                ExistingLabel = "今ある改変に追加",
                CommitLabel = "追加",
                EmptyText = "改変がまだありません。",
                FilterText = "作り物",
            };
            var window = new PickModificationDialog(model);
            return Task.FromResult(new Shot(SceneContext.Unwrap(window)));
        })
        {
            Width = null,
            Height = null,
        },
    ];

    private static ItemRecord TwoPackageItem(string id, string name) => FileItem(id, name,
        Fake.FileRecord(@"D:\作り物\costume_full_v1.2.zip") with { Contents = ["Costume/Costume_Body.unitypackage", "Costume/Costume_Option.unitypackage"] });

    private static ItemRecord OnePackageItem(string id, string name) => FileItem(id, name,
        Fake.FileRecord($@"D:\作り物\{id}.zip") with { Contents = [$"{id}.unitypackage"] });

    private static ItemRecord FileItem(string id, string name, LocalFileRecord file) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = name },
        Local = new LocalBlock { LocalFiles = [file] },
    };

    /// <summary>使ったもの3件（入れた順：シェーダー → 衣装 → 髪）の改変。衣装だけ使ったファイルを記録してある。</summary>
    private static async Task<string> SeedMemberOrderAsync(SceneContext context)
    {
        var avatar = await context.Fake.ItemAsync("9900201", "作り物のアバター「ミナト」", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\minato_v1.0.zip", "minato.unitypackage"))] },
        });
        var shader = await context.Fake.ItemAsync("9900211", "作り物のシェーダー", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\shader_v2.zip", "shader.unitypackage"))] },
        });
        var costume = await context.Fake.ItemAsync("9900212", "作り物の衣装セット", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_full_v1.2.zip", "costume.unitypackage"))] },
        });
        var hair = await context.Fake.ItemAsync("9900213", "作り物の髪型", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\hair_v1.zip", "hair.unitypackage"))] },
        });
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = avatar.Id, DisplayName = "ミナト", BoothName = avatar.Booth.Name }],
        });

        var created = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
        var id = ModificationId.For(avatar.Id, "夏の普段着", created);
        await context.Seed.Modifications.SaveAsync(new ModificationRecord
        {
            Id = id,
            AvatarItemId = avatar.Id,
            Name = "夏の普段着",
            CreatedAt = created,
            UpdatedAt = created,
            Members =
            [
                new ModificationMember { ItemId = shader.Id, AddedAt = created },
                new ModificationMember
                {
                    ItemId = costume.Id,
                    FileHash = costume.Local.LocalFiles[0].Hash,
                    Package = "costume.unitypackage",
                    AddedAt = created,
                },
                new ModificationMember { ItemId = hair.Id, AddedAt = created },
            ],
        });
        return id;
    }
}
