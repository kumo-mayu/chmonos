using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>2026-10-05 の判断：検索の「改変」の候補を呼び方で当てる・アバターの画面の探す欄に札を出す（作り物の名前）。</summary>
public sealed class AvatarAliasSearchTests
{
    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            new AvatarRegistryEntry
            {
                ItemId = "9900001", BoothName = "作り物のアバター", AvatarOverride = true, IsOwnedManually = true,
                Aliases = [new AvatarAlias { Text = "Mzh" }],
            },
            new AvatarRegistryEntry { ItemId = "9900002", BoothName = "ほかのアバター", AvatarOverride = true },
        ],
    };

    [Fact]
    public Task 検索の改変の候補は_アバター名の呼び方でも当たり_札が付く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.Avatars.SaveAsync(Registry());
        await app.Services.Modifications.CreateAsync("9900001", "普段着");
        await app.Services.Modifications.CreateAsync("9900002", "制服");
        var search = (await app.StartAsync()).Search;
        var module = (ListModule)SearchModuleMenuTests.Add(search, SearchModuleKind.Modification);
        await UiThread.Until(() => module.Suggestions.Count > 0, "改変の候補が読まれる");

        var rows = SuggestBox.Arrange(module.Suggestions.ToList(), "mzh", 0, module.InfoSelector);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("呼び方「Mzh」", row.Hit!.Label));
        Assert.Contains(rows, row => row.Entry == "作り物のアバター：普段着");

        // 名前で当たる語には札を付けない
        var byName = SuggestBox.Arrange(module.Suggestions.ToList(), "普段着", 0, module.InfoSelector);
        Assert.Null(Assert.Single(byName).Hit);
    });

    [Fact]
    public Task アバターの画面の探す欄は_呼び方と正式名で当たった行にだけ札を出す() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(Registry());
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");

        avatars.Query = "mzh";
        var row = Assert.Single(avatars.Rows);
        Assert.True(row.HasMatchNote);
        Assert.Equal("呼び方「Mzh」", row.MatchNote);

        // 名前で当たったときと、探す語が空のときは札なし
        avatars.Query = "作り物";
        Assert.False(Assert.Single(avatars.Rows).HasMatchNote);
        avatars.Query = string.Empty;
        Assert.All(avatars.Rows, r => Assert.False(r.HasMatchNote));
    });
}
