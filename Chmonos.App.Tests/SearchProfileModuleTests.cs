using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の条件「プロファイル」（ユーザ判断 2026-11-09・docs/spec/conversion.md「検索」の案B）と、アバターの管理・アバターの商品ページから開く口。
/// アオイ用の逆方向（アオイの衣装を持っていく）と、モモ用の順方向（モモに着せる）のプロファイルを持つ。名前・ID は作り物。
/// </summary>
public class SearchProfileModuleTests
{
    private const string Aoi = "9800401";
    private const string Momo = "9800402";
    private const string Kiki = "9800403";

    private static async Task<MainViewModel> StartAsync(TestApp app)
    {
        static AvatarRegistryEntry Avatar(string id, string name) => new()
        {
            ItemId = id,
            BoothName = $"作り物のアバター「{name}」",
            DisplayName = name,
            Category = "3Dキャラクター",
        };

        await app.Store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar(Aoi, "アオイ"), Avatar(Momo, "モモ"), Avatar(Kiki, "キキ")] });
        await app.AddItemAsync(Make.Item(Aoi, "作り物のアバター「アオイ」"));
        await AddProfileAsync(app, "9900881", new ConversionTarget { AvatarItemId = Aoi, Reverse = true });
        await AddProfileAsync(app, "9900882", new ConversionTarget { AvatarItemId = Momo, Forward = true, Confirmed = true });
        await app.AddItemAsync(Make.Item("9900883", "作り物の衣装"));
        return await app.StartAsync();
    }

    private static Task AddProfileAsync(TestApp app, string id, ConversionTarget target)
    {
        var item = Make.Item(id, "作り物のプロファイル");
        return app.AddItemAsync(item with { Local = item.Local with { Conversion = new ConversionSetting { Targets = [target] } } });
    }

    private static IReadOnlyList<string> Shown(MainViewModel main)
        => main.Search.ListItems.Select(card => card.Item.Id).Order().ToList();

    private static string Text(string id, string name) => AvatarSuggestionText.Format(name, id);

    [Fact]
    public Task 足しただけで_プロファイルの商品すべてに絞る() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);

        var module = (ProfileModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Profile);
        await app.SettleAsync();

        Assert.Equal(["9900881", "9900882"], Shown(main));
        Assert.Contains("プロファイルの商品すべて", module.SummaryText, StringComparison.Ordinal);
        Assert.True(module.ViaBase);
        Assert.True(module.IncludeUnconfirmed);
        Assert.False(module.ShowCounterpart);
        Assert.False(module.CounterpartApplies);
    });

    [Fact]
    public Task 変換元は逆方向_変換先は順方向で当て_両方入れるとそろっているかを言う() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ProfileModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Profile);

        module.Destination.AddCommand.Execute(Text(Momo, "モモ"));
        await app.SettleAsync();
        Assert.Equal(["9900882"], Shown(main));
        Assert.True(module.CounterpartApplies);
        Assert.False(module.HasStatus);

        // 「もう一方の候補も出す」：変換先だけのとき、元として使える（逆方向の）物も出す
        module.ShowCounterpart = true;
        await app.SettleAsync();
        Assert.Equal(["9900881", "9900882"], Shown(main));
        module.ShowCounterpart = false;

        module.Source.AddCommand.Execute(Text(Aoi, "アオイ"));
        await app.SettleAsync();
        Assert.Equal(["9900881", "9900882"], Shown(main));
        Assert.False(module.CounterpartApplies);
        Assert.Equal("変換に要るプロファイルはそろっています。", module.StatusText);

        // キキの順方向は無い
        module.Destination.AddCommand.Execute(Text(Kiki, "キキ"));
        await app.SettleAsync();
        Assert.Equal("「キキ（9800403）」に着せるプロファイルがありません。", module.StatusText);

        // 単体で変換できるものだけ：1つの商品で、アオイの衣装の変換とキキ・モモへの着せ替えの両方をまかなえる物は無い
        Assert.False(module.RequireBoth);
        Assert.True(module.BothApplies);
        module.RequireBoth = true;
        await app.SettleAsync();
        Assert.Empty(Shown(main));
    });

    [Fact]
    public Task 確認待ちを切ると_推しただけのつなぎ先では当てない() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ProfileModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Profile);
        module.Source.AddCommand.Execute(Text(Aoi, "アオイ"));
        await app.SettleAsync();
        Assert.Equal(["9900881"], Shown(main));

        module.IncludeUnconfirmed = false;
        await app.SettleAsync();

        Assert.Empty(Shown(main));
    });

    [Fact]
    public Task アバターの管理のプロファイルを検索で開くは_変換元と変換先の両方にそのアバターを入れる() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.Selected = avatars.Rows.Single(row => row.ItemId == Aoi);

        avatars.ShowProfilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Same(main.Search, main.CurrentViewModel);
        var module = (ProfileModule)main.Search.Modules.Single(entry => entry.Kind == SearchModuleKind.Profile);
        Assert.Equal(["avatar:" + Aoi], module.Source.Chips.Select(chip => chip.Key));
        Assert.Equal(["avatar:" + Aoi], module.Destination.Chips.Select(chip => chip.Key));
        Assert.Equal(["9900881"], Shown(main));

        // 同じアバターを両方に入れたときは、そろっているかを言わない（変換の話ではない）
        Assert.False(module.HasStatus);
    });

    [Fact]
    public Task アバターの商品ページのプロファイルを検索で開くも_同じ検索を開く() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var avatarItem = (await app.Store.Items.LoadAsync(Aoi))!;
        var page = new ItemViewModel(avatarItem, app.Services, main, main.Thumbnails);

        page.AvatarRegistration.ShowProfilesCommand.Execute(null);
        await app.SettleAsync();

        Assert.Same(main.Search, main.CurrentViewModel);
        Assert.Equal(["9900881"], Shown(main));
    });

    [Fact]
    public Task 保存した状態から読み直すと_欄と切り替えが戻る() => TestApp.Run(async app =>
    {
        var main = await StartAsync(app);
        var module = (ProfileModule)SearchModuleMenuTests.Add(main.Search, SearchModuleKind.Profile);
        module.Source.AddCommand.Execute(Text(Aoi, "アオイ"));
        module.ViaBase = false;
        module.IncludeUnconfirmed = false;
        var saved = module.Save();

        module.Clear();
        module.Load(saved);

        Assert.Equal(["avatar:" + Aoi], module.Source.Chips.Select(chip => chip.Key));
        Assert.Empty(module.Destination.Chips);
        Assert.False(module.ViaBase);
        Assert.False(module.IncludeUnconfirmed);
        Assert.Equal(["from:avatar:" + Aoi], saved.Items);
    });
}
