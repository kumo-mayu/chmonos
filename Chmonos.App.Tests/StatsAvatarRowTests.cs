using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>統計の「アバター別の所持数」（ユーザ判断 2026-11-20）：持っているアバターに印・数は「直接 + 素体経由」・行を押すと検索へ。</summary>
public sealed class StatsAvatarRowTests
{
    private static StatsSnapshot Snapshot() => StatsService.Build([], new AvatarRegistry(), 0) with
    {
        Avatars =
        [
            new StatsCountBar { Key = "a", Label = "持っているA", ItemCount = 5, DirectCount = 3 },
            new StatsCountBar { Key = "b", Label = "持っていないB", ItemCount = 4, DirectCount = 4 },
            new StatsCountBar { Key = string.Empty, Label = "対応アバター未設定", ItemCount = 2, IsResidual = true },
        ],
        Wearables =
        [
            new StatsAvatarWearable { AvatarItemId = "a", Name = "持っているA", DirectCount = 3, ViaBaseCount = 2 },
            new StatsAvatarWearable { AvatarItemId = "c", Name = "持っているC（0件）", DirectCount = 0, ViaBaseCount = 0 },
        ],
    };

    [Fact]
    public void 持っているアバターに印を付け_数は直接と素体経由で出す()
    {
        var rows = StatsViewModel.AvatarRows(Snapshot(), ownedOnly: false, (_, _) => { }, () => { });

        Assert.Equal(["持っているA", "持っていないB", "持っているC（0件）", "対応アバター未設定"], rows.Select(row => row.Label));
        Assert.Equal("3 + 素体経由 2", rows[0].ValueText);
        Assert.Equal("持っている", rows[0].Badge);
        Assert.Equal("4", rows[1].ValueText);
        Assert.Equal(string.Empty, rows[1].Badge);
        Assert.Equal("持っている", rows[2].Badge);
        Assert.True(rows[3].IsResidual);
    }

    [Fact]
    public void 持っているアバターだけに切ると_持っていない行と未設定の行が消える()
    {
        var rows = StatsViewModel.AvatarRows(Snapshot(), ownedOnly: true, (_, _) => { }, () => { });

        Assert.Equal(["持っているA", "持っているC（0件）"], rows.Select(row => row.Label));
    }

    [Fact]
    public void 行を押すと_そのアバターの検索へ_未設定の行は指定の無い商品の検索へ()
    {
        var shown = new List<(string Id, string Name)>();
        var unspecified = 0;
        var rows = StatsViewModel.AvatarRows(Snapshot(), ownedOnly: false, (id, name) => shown.Add((id, name)), () => unspecified++);

        rows[1].OpenCommand!.Execute(null);
        rows[3].OpenCommand!.Execute(null);

        Assert.Equal([("b", "持っていないB")], shown);
        Assert.Equal(1, unspecified);
    }

    [Fact]
    public Task 検索の入口は_対応の指定が無い商品の側だけを入れる() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        var main = await app.StartAsync();
        var search = main.Search;

        search.ShowOnlyUnspecifiedAvatar();

        var module = Assert.Single(search.Modules.OfType<ListModule>(), m => m.Kind == SearchModuleKind.Avatar);
        Assert.True(module.ShowUnspecified);
        Assert.False(module.ShowMatched);
        await app.SettleAsync();
    });
}
