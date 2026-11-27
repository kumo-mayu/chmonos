using System.Text.Json;
using Chmonos.App.Controls;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 設定「アバターの商品IDを表示する」（ユーザ判断 2026-11-27・既定は切）。切では候補と札の字は名前だけで、
/// IDは打てば当たる。名前が重なる物だけIDを添える。前の字「名前（ID）」も読める。名前・IDは作り物
/// </summary>
public sealed class AvatarItemIdSettingTests
{
    private const string OwnedId = "9900001";
    private const string OtherId = "9900002";

    private static AvatarRegistry Registry() => new()
    {
        Entries =
        [
            new AvatarRegistryEntry { ItemId = OwnedId, BoothName = "作り物の持っているアバター", AvatarOverride = true, IsOwnedManually = true },
            new AvatarRegistryEntry { ItemId = OtherId, BoothName = "作り物の持っていないアバター", AvatarOverride = true },
        ],
    };

    private static async Task<ListModule> AvatarModuleAsync(TestApp app, AvatarRegistry registry, bool? showIds = null)
    {
        await app.AddItemAsync(Make.Item("1000001", "作り物の衣装"));
        await app.Store.Avatars.SaveAsync(registry);
        if (showIds is { } value)
        {
            await app.ChangeSettingsAsync(settings => settings with { ShowAvatarItemIds = value });
        }

        var search = (await app.StartAsync()).Search;
        return (ListModule)SearchModules.Add(search, SearchModuleKind.Avatar);
    }

    [Fact]
    public void 前の版の設定には項目が無く_切で読む()
        => Assert.False(JsonSerializer.Deserialize<AppSettings>("""{ "cardWidth": 228 }""", JsonStore.Options)!.ShowAvatarItemIds);

    [Fact]
    public Task 既定では検索の候補は名前だけで_商品IDを打っても当たる() => TestApp.Run(async app =>
    {
        var module = await AvatarModuleAsync(app, Registry());

        Assert.Contains("作り物の持っているアバター", module.Suggestions);
        Assert.DoesNotContain(module.Suggestions, text => text.Contains(OwnedId, StringComparison.Ordinal) || text.Contains(OtherId, StringComparison.Ordinal));

        var rows = SuggestBox.Arrange(module.Suggestions.ToList(), "9900002", 0, module.InfoSelector);
        Assert.Equal(["作り物の持っていないアバター"], rows.Select(row => row.Entry));
        Assert.Equal($"商品ID {OtherId}", rows[0].Hit!.Label);

        module.AddCommand.Execute("作り物の持っていないアバター");
        Assert.Equal(SearchModuleConditions.AvatarKey + OtherId, Assert.Single(module.Chips).Key);
    });

    [Fact]
    public Task 設定を入れると_候補は名前と商品IDになる() => TestApp.Run(async app =>
    {
        var module = await AvatarModuleAsync(app, Registry(), showIds: true);

        Assert.Contains(AvatarSuggestionText.Format("作り物の持っているアバター", OwnedId), module.Suggestions);
        Assert.Contains(AvatarSuggestionText.Format("作り物の持っていないアバター", OtherId), module.Suggestions);
    });

    /// <summary>
    /// ショップ名の分からない同じ名前の2体。名前だけでは欄が2体目を捨てる（字から選ぶため）ので、その2体だけIDを添え、
    /// 別々に選べる。重ならないアバターは名前だけのまま
    /// </summary>
    [Fact]
    public Task 同じ名前でショップの分からない2体は_IDを添えて両方残り_別々に選べる() => TestApp.Run(async app =>
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                .. Registry().Entries,
                new AvatarRegistryEntry { ItemId = "9900011", BoothName = "作り物の同じ名前", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "9900012", BoothName = "作り物の同じ名前", AvatarOverride = true, IsOwnedManually = true },
            ],
        };
        var module = await AvatarModuleAsync(app, registry);

        var first = AvatarSuggestionText.Format("作り物の同じ名前", "9900011");
        var second = AvatarSuggestionText.Format("作り物の同じ名前", "9900012");
        Assert.Contains(first, module.Suggestions);
        Assert.Contains(second, module.Suggestions);
        Assert.Contains("作り物の持っているアバター", module.Suggestions);

        module.AddCommand.Execute(first);
        module.AddCommand.Execute(second);
        Assert.Equal(
            [SearchModuleConditions.AvatarKey + "9900011", SearchModuleConditions.AvatarKey + "9900012"],
            module.Chips.Select(chip => chip.Key));
    });

    /// <summary>保存するのは札のキー（商品ID）だけで、字は候補から付け直す。設定を変えて読み直すと、入れてあった札の字も変わる</summary>
    [Fact]
    public Task 設定を切って読み直すと_入れてあった札の字も名前だけになる() => TestApp.Run(async app =>
    {
        var module = await AvatarModuleAsync(app, Registry(), showIds: true);
        module.AddCommand.Execute(AvatarSuggestionText.Format("作り物の持っているアバター", OwnedId));
        Assert.Equal(AvatarSuggestionText.Format("作り物の持っているアバター", OwnedId), Assert.Single(module.Chips).Text);

        await app.ChangeSettingsAsync(settings => settings with { ShowAvatarItemIds = false });
        await app.Main.Search.ReloadAsync();

        Assert.Equal("作り物の持っているアバター", Assert.Single(module.Chips).Text);
    });

    /// <summary>
    /// 素体の側からアバターを足す欄。候補は名前だけで、前の字「名前（ID）」（設定を入れていた時の字・道具が渡す字）でも足せる
    /// </summary>
    [Fact]
    public Task 素体に足す欄は名前だけで_前の名前とIDの字でも足せる() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = OwnedId, BoothName = "作り物アバターA", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = OtherId, BoothName = "作り物アバターB", AvatarOverride = true },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体", IsManual = true }],
        });
        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        avatars.ShowBaseModeCommand.Execute(null);
        avatars.SelectedBase = avatars.Bases.Single(row => row.Name == "作り物の素体");

        Assert.Equal(["作り物アバターA", "作り物アバターB"], avatars.MemberCandidates.Order(StringComparer.Ordinal));

        await avatars.AddMemberAsync(AvatarSuggestionText.Format("作り物アバターB", OtherId));
        await app.SettleAsync();

        Assert.Equal("作り物の素体", app.Store.Avatars.Load().Entries.Single(entry => entry.ItemId == OtherId).BaseName);
    });

    /// <summary>元から名前だけの欄（商品ページの対応アバター）も、IDを打てば当たる</summary>
    [Fact]
    public Task 商品ページの対応アバターの欄も_商品IDを打てば当たる() => TestApp.Run(async app =>
    {
        var self = Make.Item("1000001", "作り物の衣装");
        await app.AddItemAsync(self);
        await app.Store.Avatars.SaveAsync(Registry());
        var main = await app.StartAsync();
        var page = new ItemViewModel(self, app.Services, main, main.Thumbnails);

        var rows = SuggestBox.Arrange(page.SupportSuggestions.ToList(), "9900002", 0, page.SupportSuggestInfoSelector);

        Assert.Equal(["作り物の持っていないアバター"], rows.Select(row => row.Entry));
    });
}
