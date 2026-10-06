using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>2026-10-05 の判断：アバターの画面の探す欄に札を出す（作り物の名前）。検索の「改変」の候補の呼び方は SearchModificationModuleTests。</summary>
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
