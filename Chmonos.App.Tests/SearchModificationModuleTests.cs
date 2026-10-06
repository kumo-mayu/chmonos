using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 条件「改変」の2段の形（ユーザ判断 2026-10-06）：1段目にアバター、2段目にそのアバターの改変。
/// 1段目の欄は「改変に追加」の窓と同じ探し方。作り物の名前と番号（9900000番台）。
/// </summary>
public class SearchModificationModuleTests
{
    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            new AvatarRegistryEntry
            {
                ItemId = "9900101", BoothName = "作り物のアバター", AvatarOverride = true, IsOwnedManually = true,
                Aliases = [new AvatarAlias { Text = "Mzh" }],
            },
            new AvatarRegistryEntry { ItemId = "9900102", BoothName = "ほかのアバター", AvatarOverride = true },
        ],
    };

    /// <summary>
    /// 改変は3つ：作り物のアバターの「普段着」（9900111・9900112）と「制服」（9900112・9900113。プロジェクト付き）、
    /// ほかのアバターの「普段着」（9900114）。9900115 はどの改変にも使っていない。
    /// </summary>
    private static async Task<(SearchViewModel Search, ModificationModule Module, Dictionary<string, string> Ids)> StartAsync(TestApp app)
    {
        foreach (var id in new[] { "9900111", "9900112", "9900113", "9900114", "9900115" })
        {
            await app.AddItemAsync(Make.Item(id, $"作り物の衣装{id}"));
        }

        await app.Store.Avatars.SaveAsync(Registry());
        var ids = new Dictionary<string, string>();
        async Task Create(string avatar, string name, string key, params string[] members)
        {
            var record = (await app.Services.Modifications.CreateAsync(avatar, name))!;
            ids[key] = record.Id;
            foreach (var member in members)
            {
                await app.Services.Modifications.AddMemberAsync(record.Id, new ModificationMember { ItemId = member });
            }
        }

        await Create("9900101", "普段着", "a-casual", "9900111", "9900112");
        await Create("9900101", "制服", "a-uniform", "9900112", "9900113");
        await app.Services.Modifications.SetProjectAsync(ids["a-uniform"], @"D:\作り物\UnityProjects\学園の撮影");
        await Create("9900102", "普段着", "b-casual", "9900114");

        var search = (await app.StartAsync()).Search;
        var module = (ModificationModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Modification);
        await UiThread.Until(() => module.Suggestions.Count > 0, "改変の候補が読まれる");
        return (search, module, ids);
    }

    private static List<string> Shown(SearchViewModel search) => [.. search.ListItems.Select(card => card.Item.Id).Order(StringComparer.Ordinal)];

    [Fact]
    public Task 候補はアバターを先に_改変を後に並べ_同じ名前の改変はアバターで見分ける() => TestApp.Run(async app =>
    {
        var (_, module, _) = await StartAsync(app);

        // 改変はアバターの順に、アバターの中は名前の順
        Assert.Equal(["ほかのアバター", "作り物のアバター", "普段着（ほかのアバター）", "制服", "普段着（作り物のアバター）"], module.Suggestions);
        var rows = SuggestBox.Arrange(module.Suggestions.ToList(), string.Empty, 0, module.InfoSelector, module.Matcher);
        Assert.Equal([false, false, true, false, false], rows.Select(row => row.DividerAbove));
        Assert.Equal(["アバター", "改変"], ModificationModule.Headings);
    });

    [Fact]
    public Task 欄は改変に追加の窓と同じ探し方で_全部の語が名前かアバターかプロジェクトに入る物が当たる() => TestApp.Run(async app =>
    {
        var (_, module, _) = await StartAsync(app);
        List<(string Entry, string? Note)> Find(string query)
            => [.. SuggestBox.Arrange(module.Suggestions.ToList(), query, 0, module.InfoSelector, module.Matcher).Select(row => (row.Entry, row.Hit?.Label))];

        // 名前で当たったときは札を出さない
        Assert.Equal([("制服", (string?)null)], Find("制服"));

        // 語を空白で区切り、全部の語がどこかに入る物だけ。アバターに当たったことを札で言う
        Assert.Equal([("普段着（作り物のアバター）", (string?)"アバター：作り物のアバター")], Find("作り物 普段"));

        // プロジェクトの名前（場所の最後の部分）でも当たる
        Assert.Equal([("制服", (string?)"プロジェクト：学園の撮影")], Find("学園"));

        // アバターの呼び方でも当たる。アバターの行は名前以外で当たっても札を出さない（アバターの名前は行に出ている）
        Assert.Contains(Find("mzh"), row => row.Entry == "作り物のアバター");
        Assert.Contains(Find("mzh"), row => row is ("普段着（作り物のアバター）", "アバター：作り物のアバター"));
    });

    [Fact]
    public Task 改変の候補を選ぶと_そのアバターの枠を立てて_その改変を選んだ状態にする() => TestApp.Run(async app =>
    {
        var (search, module, ids) = await StartAsync(app);

        module.AddCommand.Execute("制服");

        var row = Assert.Single(module.Rows);
        Assert.Equal("作り物のアバター", row.AvatarName);
        Assert.Equal([ids["a-uniform"]], row.Chips.Select(chip => chip.Key));
        Assert.Equal(["9900112", "9900113"], Shown(search));

        // 2段目にはまだ選んでいない改変だけ。1段目からは足したアバターと選んだ改変が消える
        Assert.Equal(["普段着"], row.Suggestions);
        Assert.DoesNotContain("作り物のアバター", module.Suggestions);
        Assert.DoesNotContain("制服", module.Suggestions);
        Assert.True(row.Chips.Single().HasIconSource);
    });

    [Fact]
    public Task アバターだけなら_そのアバターの改変のどれかに使った商品で_改変どうしとアバターどうしはANDにできる() => TestApp.Run(async app =>
    {
        var (search, module, _) = await StartAsync(app);

        module.AddCommand.Execute("作り物のアバター");
        var row = Assert.Single(module.Rows);
        Assert.True(row.HasNoModification);
        Assert.Equal(["9900111", "9900112", "9900113"], Shown(search));

        row.AddCommand.Execute("普段着");
        row.AddCommand.Execute("制服");
        Assert.Equal(["9900111", "9900112", "9900113"], Shown(search));
        Assert.True(row.ShowsMatchMode);

        row.MatchAll = true;
        Assert.Equal(["9900112"], Shown(search));
        Assert.Equal("改変：作り物のアバター（普段着・制服 のすべて）", module.SummaryText);

        row.MatchAll = false;
        module.AddCommand.Execute("ほかのアバター");
        Assert.Equal(["9900111", "9900112", "9900113", "9900114"], Shown(search));

        module.MatchAll = true;
        Assert.Empty(Shown(search));
        Assert.Equal("すべてのアバターを満たす商品のみ（AND）", module.AvatarMatchAllText);
    });

    [Fact]
    public Task 条件は保存した形で戻る() => TestApp.Run(async app =>
    {
        var (search, module, ids) = await StartAsync(app);
        module.AddCommand.Execute("制服");
        module.Rows[0].AddCommand.Execute("普段着");
        module.Rows[0].MatchAll = true;

        var state = module.Save();
        var condition = Assert.Single(state.Modifications);
        Assert.Equal("9900101", condition.Avatar);
        Assert.Equal([ids["a-uniform"], ids["a-casual"]], condition.Modifications);
        Assert.True(condition.MatchAll);
        Assert.Empty(state.Items);

        var back = (ModificationModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Modification);
        back.Load(state);
        Assert.Equal(["制服", "普段着"], back.Rows.Single().Chips.Select(chip => chip.Text));
        Assert.True(back.Rows.Single().MatchAll);
    });

    [Fact]
    public void 画面から静的に引く見出しは公開している()
    {
        // XAML の x:Static は public の物しか引けず、引けないと条件を足した途端に画面の処理で落ちる（撮影の場面で見つかった）
        Assert.True(typeof(ModificationModule).GetProperty(nameof(ModificationModule.Headings))!.GetMethod!.IsPublic);
    }
}
