using System.Windows;
using System.Windows.Controls;
using Chmonos.App.Controls;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// メモ46（2026-10-05）の確かめ：共通素体の詳細のアバターを足す欄・移した知らせ・行の右クリック「この素体から外す」と、
/// アバターの詳細で名前から推した素体を薄い字で出す所。
/// </summary>
internal static partial class Scenes
{
    private const string InferredMemberId = "9900601";

    private static IEnumerable<Scene> AvatarBaseMemberScenes =>
    [
        new Scene("avatars-base-members", "アバターの管理：素体の詳細のアバターを足す欄と、ほかの素体から移した知らせ（行の右クリックは「この素体から外す」）", async context =>
        {
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowBaseModeCommand.Execute(null);
            avatars.SelectedBase = avatars.Bases.First(row => row.Name == "作り物の素体");
            await context.SettleAsync();

            // 移した知らせは本物の道（候補を選ぶ）で出す。名前から MARUBODY に推されているアバターを移す
            var moving = avatars.Rows.First(row => row.ItemId == InferredMemberId);
            await avatars.AddMemberAsync(AvatarSuggestionText.Format(moving.Name, moving.ItemId));
            await context.SettleAsync();
            if (!avatars.SelectedBase!.MemberNote.Text.Contains("から移しました", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"移した知らせが出ない：{avatars.SelectedBase.MemberNote.Text}");
            }

            // 行の右クリック：「この素体から外す」だけがあり、押せて、画面の外す命令とその行につながる
            var member = Look.All<PressableBorder>(root).First(border => border.DataContext is AvatarRowViewModel);
            var menu = member.ContextMenu ?? throw new InvalidOperationException("素体の詳細の行に右クリックのメニューがありません。");
            menu.PlacementTarget = member;
            menu.DataContext = null;
            menu.DataContext = member.DataContext;
            await context.SettleAsync();
            var items = menu.Items.OfType<MenuItem>().Where(item => item.Visibility == Visibility.Visible).ToList();
            if (items.Count != 1 || (string)items[0].Header != "この素体から外す"
                || !ReferenceEquals(items[0].Command, avatars.RemoveMemberCommand)
                || !ReferenceEquals(items[0].CommandParameter, member.DataContext) || !items[0].IsEnabled)
            {
                throw new InvalidOperationException($"素体の詳細の行の右クリック：{string.Join("、", items.Select(item => item.Header))}");
            }

            return new Shot(root);
        }),

        new Scene("avatars-inferred-base", "アバターの管理：名前から素体に推されているアバターの右の欄（欄は空で、下に薄い字で推した素体）", async context =>
        {
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.Selected = avatars.Rows.First(row => row.ItemId == InferredMemberId);
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>名前から MARUBODY に推されるアバター1体・手で素体を決めたアバター1体・素体の無いアバター2体と、共通素体2つ。</summary>
    private static async Task<(AvatarsViewModel Avatars, FrameworkElement Root)> OpenBaseMembersAsync(SceneContext context)
    {
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = InferredMemberId, BoothName = "作り物のアバター「ユキ」 #MARUBODY", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "9900602", BoothName = "作り物のアバター「アオイ」", Category = "3Dキャラクター", BaseName = "作り物の素体" },
                new AvatarRegistryEntry { ItemId = "9900603", BoothName = "作り物のアバター「ツムギ」", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "9900604", BoothName = "作り物のアバター「コハク」", Category = "3Dキャラクター" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "MARUBODY" }, new AvatarBaseGroup { Name = "作り物の素体", IsManual = true }],
        });

        var main = await context.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var avatars = context.Screen<AvatarsViewModel>();
        await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.Rows.Count == 4, "アバターが並ぶ");
        await context.SettleAsync();
        return (avatars, root);
    }
}
