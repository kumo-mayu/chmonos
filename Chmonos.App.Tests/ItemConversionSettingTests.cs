using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの「変換扱い」の欄（<c>docs/spec/conversion.md</c>「画面」）。
/// プロファイルの商品は、中に作り物のプロファイル2つ（fakeaoi は順方向、fakebase は逆方向）を持つ。名前・ID は作り物。
/// </summary>
public class ItemConversionSettingTests
{
    private const string ProfileId = "9900871";
    private const string Hash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    private static readonly ConversionTarget SuggestedBase = new() { BaseName = "作り物の素体", Reverse = true, Slug = "fakebase" };

    private static async Task<ItemViewModel> OpenAsync(TestApp app, ConversionSetting? conversion, bool withFiles = true)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター", BaseName = "作り物の素体" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体" }],
        });

        if (withFiles)
        {
            new UnityPackagePathStore(app.Services.Paths).Add(Hash, "Profile.unitypackage",
            [
                new UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2fakeaoi.json"),
                new UnityPackageAsset("00000000000000000000000000000002", "Assets/OutfitRetargetingSystem/Editor/config_fakebase2template.json"),
            ]);
        }

        var item = Make.Item(ProfileId, "作り物のプロファイル");
        item = item with
        {
            Local = item.Local with
            {
                LocalFiles = withFiles
                    ?
                    [
                        new LocalFileRecord
                        {
                            Hash = Hash,
                            Paths = [@"D:\fake\profile.zip"],
                            SizeBytes = 1,
                            Contents = ["Profile.unitypackage"],
                            UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                        },
                    ]
                    : [],
                Conversion = conversion,
            },
        };
        await app.AddItemAsync(item);
        var main = await app.StartAsync();
        return new ItemViewModel(item, app.Services, main, main.Thumbnails);
    }

    private static async Task<ConversionSetting?> SavedAsync(TestApp app)
        => (await app.Store.Items.LoadAsync(ProfileId))!.Local.Conversion;

    [Fact]
    public Task プロファイルを持たない商品には_欄を出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, conversion: null, withFiles: false);

        Assert.False(page.ShowsConversion);
        Assert.Empty(page.ConversionRows);
    });

    [Fact]
    public Task 中のプロファイルごとに行を出し_推した物は確認待ち_決まっていない物は選ぶ欄() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        Assert.True(page.ShowsConversion);
        Assert.Equal("今の扱い：プロファイル（自動の判定）", page.ConversionStateText);
        Assert.True(page.IsConversionAuto);

        var open = Assert.Single(page.ConversionRows, row => row.ProfileText == "fakeaoi");
        Assert.False(open.HasTarget);
        Assert.Equal("順方向", open.DirectionText);

        var suggested = Assert.Single(page.ConversionRows, row => row.ProfileText == "fakebase");
        Assert.Equal("作り物の素体（共通素体）", suggested.TargetText);
        Assert.Equal("逆方向", suggested.DirectionText);
        Assert.True(suggested.IsUnconfirmed);

        Assert.Contains("アオイ", page.ConversionTargetSuggestions);
        Assert.Contains("作り物の素体（共通素体）", page.ConversionTargetSuggestions);
    });

    [Fact]
    public Task 確認済みにすると_確認したつなぎ先として保存する() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        page.ConversionRows.Single(row => row.ProfileText == "fakebase").ConfirmCommand!.Execute(null);

        await app.SettleAsync();

        Assert.True((await SavedAsync(app))!.Targets.Single().Confirmed);
        Assert.False(page.ConversionRows.Single(row => row.ProfileText == "fakebase").IsUnconfirmed);
    });

    [Fact]
    public Task 選ぶ欄でアバターを選ぶと_そのプロファイルのつなぎ先を確認済みで書く() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, conversion: null);

        page.ConversionRows.Single(row => row.ProfileText == "fakeaoi").ChooseCommand!.Execute("アオイ");

        await app.SettleAsync();

        var saved = Assert.Single((await SavedAsync(app))!.Targets);
        Assert.Equal(new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeaoi", Confirmed = true }, saved);
    });

    [Fact]
    public Task 外すと_最後のつなぎ先なら変換扱いを書き出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, new ConversionSetting { Targets = [SuggestedBase] });

        page.ConversionRows.Single(row => row.ProfileText == "fakebase").RemoveCommand!.Execute(null);

        await app.SettleAsync();

        Assert.Null(await SavedAsync(app));
    });

    [Fact]
    public Task 扱わないにしても_つなぎ先は残し_文で言う() => TestApp.Run(async app =>
    {
        var confirmed = SuggestedBase with { Confirmed = true };
        var page = await OpenAsync(app, new ConversionSetting { Targets = [confirmed] });

        page.SetConversionModeCommand.Execute("notProfile");

        await app.SettleAsync();

        Assert.Equal(ConversionMode.NotProfile, (await SavedAsync(app))!.Mode);
        Assert.Equal([confirmed], (await SavedAsync(app))!.Targets);
        Assert.True(page.IsConversionNotProfile);
        Assert.Equal("今の扱い：プロファイルとして扱わない（手動で指定）", page.ConversionStateText);
    });
}
