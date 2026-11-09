using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件「プロファイル」（ユーザ判断 2026-11-09・docs/spec/conversion.md「検索」）と、アバターの管理・アバターの商品ページから開く口。
/// 名前・ID は作り物。
/// </summary>
public class SearchProfileModuleTests
{
    private const string AvatarId = "9800401";

    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = AvatarId, BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター" }],
        });

        await app.AddItemAsync(Make.Item(AvatarId, "作り物のアバター「アオイ」"));
        var toAvatar = Make.Item("9900881", "作り物のプロファイル（アオイ）");
        await app.AddItemAsync(toAvatar with
        {
            Local = toAvatar.Local with
            {
                Conversion = new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = AvatarId, Forward = true }] },
            },
        });
        var other = Make.Item("9900882", "作り物のプロファイル（ほか）");
        await app.AddItemAsync(other with
        {
            Local = other.Local with
            {
                Conversion = new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = "9800499", Forward = true, Confirmed = true }] },
            },
        });
        await app.AddItemAsync(Make.Item("9900883", "作り物の衣装"));
        return await app.StartAsync();
    }

    private static IReadOnlyList<string> Shown(MainViewModel main)
        => main.Search.ListItems.Select(card => card.Item.Id).Order().ToList();

    [Fact]
    public Task 足しただけで_プロファイルの商品すべてに絞る() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);

        var module = (ListModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Profile);
        await app.SettleAsync();

        Assert.Equal(["9900881", "9900882"], Shown(main));
        Assert.Contains("プロファイルの商品すべて", module.SummaryText, StringComparison.Ordinal);
        Assert.True(module.Flag);
        Assert.True(module.IncludeUnconfirmed);
    });

    [Fact]
    public Task アバターの管理のプロファイルを検索で開くは_そのアバターのプロファイルに絞った検索を開く() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == AvatarId);

        avatars.ShowProfilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Same(main.Search, main.CurrentViewModel);
        Assert.Equal(["9900881"], Shown(main));

        // 確認待ちを切ると、推しただけのつなぎ先では当たらない
        var module = (ListModule)main.Search.Modules.Single(entry => entry.Kind == SearchModuleKind.Profile);
        module.IncludeUnconfirmed = false;
        await app.SettleAsync();
        Assert.Empty(Shown(main));
    });

    [Fact]
    public Task アバターの商品ページのプロファイルを検索で開くも_同じ検索を開く() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var avatarItem = (await app.Store.Items.LoadAsync(AvatarId))!;
        var page = new ItemViewModel(avatarItem, app.Services, main, main.Thumbnails);

        Assert.True(page.AvatarRegistration.ShowProfilesCommand.CanExecute(null));
        page.AvatarRegistration.ShowProfilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Same(main.Search, main.CurrentViewModel);
        Assert.Equal(["9900881"], Shown(main));
    });
}
