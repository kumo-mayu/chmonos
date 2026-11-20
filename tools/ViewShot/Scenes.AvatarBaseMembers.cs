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

            // 行の「外す」ボタン（メモ73-②。基本の操作は右クリックと画面の両方）：右クリックと同じ命令・同じ行を持つ
            var buttons = Look.All<Button>(root).Where(button => button.DataContext is AvatarRowViewModel
                && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "AvatarBaseMemberRemove").ToList();
            if (buttons.Count == 0 || buttons.Any(button => !ReferenceEquals(button.Command, avatars.RemoveMemberCommand)
                || !ReferenceEquals(button.CommandParameter, button.DataContext) || !button.IsEnabled))
            {
                throw new InvalidOperationException($"素体の詳細の行の「外す」ボタン：{buttons.Count} 個。命令か引数が右クリックと違う。");
            }

            return new Shot(root);
        }),

        // 素体の中の並べ替え（ユーザ指示 2026-11-04）。本物の道（ドラッグの落とし先で呼ぶ関数）で末尾の行を先頭へ動かし、「既定の順に戻す」が出た所
        new Scene("avatars-base-members-ordered", "アバターの管理：素体の詳細で仲間を並べ替えた所（行の頭のつまみ・一覧の上の「既定の順に戻す」）", async context =>
        {
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowBaseModeCommand.Execute(null);
            avatars.SelectedBase = avatars.Bases.First(row => row.Name == "作り物の素体");
            await context.SettleAsync();
            foreach (var id in new[] { "9900603", "9900604" })
            {
                var row = avatars.Rows.First(other => other.ItemId == id);
                await avatars.AddMemberAsync(AvatarSuggestionText.Format(row.Name, row.ItemId));
            }

            await context.SettleAsync();
            if (avatars.SelectedBaseMembers.Count < 2)
            {
                throw new InvalidOperationException($"並べ替えるには仲間が2体要る：{avatars.SelectedBaseMembers.Count} 体");
            }

            var last = avatars.SelectedBaseMembers[^1];
            await avatars.MoveBaseMemberAsync(last, avatars.SelectedBaseMembers[0], after: false);
            await context.SettleAsync();
            if (!avatars.HasSelectedBaseMemberOrder || avatars.SelectedBaseMembers[0].ItemId != last.ItemId)
            {
                throw new InvalidOperationException("並べ替えた順が残らない");
            }

            return new Shot(root);
        }),

        new Scene("avatars-inferred-base","アバターの管理：名前から素体に推されているアバターの右の欄（欄は空で、下に薄い字で推した素体）", async context =>
        {
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.Selected = avatars.Rows.First(row => row.ItemId == InferredMemberId);
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("avatars-conversion-pairs", "アバターの管理：アバターの詳細の「変換の組」（このアバターが元か先にある組の札・読むだけと「変換で編集」）", async context =>
        {
            await SeedConversionPairsAsync(context);
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.Selected = avatars.Rows.First(row => row.ItemId == "9900602");
            await context.SettleAsync();
            Look.Text(root, "変換の組")!.BringIntoView();
            await context.SettleAsync();
            if (avatars.ConversionPairSummary.Chips.Count != 2)
            {
                throw new InvalidOperationException("このアバターの組の札が2つ並ばない");
            }

            return new Shot(root);
        }),

        new Scene("avatars-conversion-pairs-edit", "アバターの管理：変換の一覧の上の「追加した組」（上に組を追加の行と「追加」、並び、元でまとめた見出し（絵・名前・組の数・1つは畳んだ所）、元と先の絵付きの行と短いメモ。共通素体を元にした組を含む）", async context =>
        {
            await SeedConversionPairsAsync(context, withFolded: true);

            // アオイだけ商品の絵を持つ（絵のある端と、頭の字の端を並べて見る）
            await context.Fake.ItemAsync("9900602", "作り物のアバター「アオイ」", images: 1);
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowConversionModeCommand.Execute(null);
            await context.SettleAsync();
            avatars.ConversionPairs.Groups.Single(group => group.Name == "コハク").IsExpanded = false;
            avatars.ConversionPairs.FromText = "ツムギ";
            await context.SettleAsync();
            if (avatars.ConversionPairs.Rows.Count != 4 || avatars.ConversionPairs.Groups.Count != 3)
            {
                throw new InvalidOperationException("追加した組が4つ・元のまとまりが3つ並ばない");
            }

            return new Shot(root);
        }),

        new Scene("avatars-conversion-pairs-search", "アバターの管理：変換の上の組を探す欄（「ツムギ」で両方を探し、覚えた畳みのコハクのまとまりも開いて見せる）と、選んだ組の右の欄の元・先の絵", async context =>
        {
            await SeedConversionPairsAsync(context, withFolded: true);
            await context.Fake.ItemAsync("9900602", "作り物のアバター「アオイ」", images: 1);
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowConversionModeCommand.Execute(null);
            await context.SettleAsync();
            var pairs = avatars.ConversionPairs;
            pairs.Groups.Single(group => group.Name == "コハク").IsExpanded = false;
            pairs.Query = "ツムギ";
            pairs.SelectedRow = pairs.VisibleRows.First(row => row.FromName == "アオイ");
            await context.SettleAsync();
            if (pairs.VisibleRows.Count != 2 || !pairs.Groups.Single(group => group.Name == "コハク").IsExpanded || !avatars.ShowsConversionPairDetail)
            {
                throw new InvalidOperationException("「ツムギ」で組が2つ・コハクのまとまりが開いて並ばない");
            }

            return new Shot(root);
        }),

        new Scene("avatars-bases-search", "アバターの管理：共通素体の見方の探す欄（素体の名前で絞った所。下に素体を追加する欄）", async context =>
        {
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowBaseModeCommand.Execute(null);
            avatars.BaseQuery = "maru";
            await context.SettleAsync();
            if (avatars.ShownBases.Count != 1 || avatars.SelectedBase?.Name != "MARUBODY")
            {
                throw new InvalidOperationException("「maru」で MARUBODY だけが並んで選ばれない");
            }

            return new Shot(root);
        }),

        new Scene("avatars-collapsed-conversion", "アバターの管理：変換の見方で左の一覧を畳んだ所（帯の名前が「変換」）", async context =>
        {
            await SeedConversionPairsAsync(context);
            var (avatars, root) = await OpenBaseMembersAsync(context);
            avatars.ShowConversionModeCommand.Execute(null);
            avatars.ListPane.ToggleCollapseCommand.Execute(null);
            await context.SettleAsync();
            if (!avatars.ListPane.IsCollapsed || Look.Text(root, "変換") is null)
            {
                throw new InvalidOperationException("畳んだ帯に「変換」が出ない");
            }

            return new Shot(root);
        }),
    ];

    /// <summary>名前から MARUBODY に推されるアバター1体・手で素体を決めたアバター1体・素体の無いアバター2体と、共通素体2つ。</summary>
    /// <summary>
    /// 人が足した変換の組3つ：アオイ→ツムギ（メモ付き）・アオイ→MARUBODY（ツールの名前付き）・作り物の素体→コハク（共通素体が元）。
    /// <paramref name="withFolded"/> のときは、畳んで見せるまとまり用にコハク→ツムギを足す
    /// </summary>
    private static Task SeedConversionPairsAsync(SceneContext context, bool withFolded = false)
        => context.Seed.ConversionPairs.SaveAsync(new ConversionPairFile
        {
            Pairs =
            [
                new ConversionPair { From = ConversionEnd.Avatar("9900602"), To = ConversionEnd.Avatar("9900603"), Memo = "袖の長さを直す" },
                new ConversionPair { From = ConversionEnd.Avatar("9900602"), To = ConversionEnd.Base("MARUBODY"), Tool = "作り物のツール" },
                new ConversionPair { From = ConversionEnd.Base("作り物の素体"), To = ConversionEnd.Avatar("9900604") },
                .. withFolded ? [new ConversionPair { From = ConversionEnd.Avatar("9900604"), To = ConversionEnd.Avatar("9900603") }] : Array.Empty<ConversionPair>(),
            ],
        });

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
