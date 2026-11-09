using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace Chmonos.App.Tests;

/// <summary>
/// 商品ページの対応アバターの「変換経由」の段（<c>docs/spec/conversion.md</c>）。
/// 衣装はアオイに対応。アオイ用の逆方向と、モモ用の順方向のプロファイルを持っているので、モモが変換経由で並ぶ。名前・ID は作り物。
/// </summary>
public class ItemAvatarsViaConversionTests
{
    private const string ClothingId = "9900891";

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"作り物のアバター「{name}」",
        DisplayName = name,
        Category = "3Dキャラクター",
    };

    private static ItemRecord Profile(string id, ConversionTarget target)
    {
        var item = Make.Item(id, "作り物のプロファイル");
        return item with { Local = item.Local with { Conversion = new ConversionSetting { Targets = [target] } } };
    }

    private static async Task<ItemViewModel> OpenAsync(TestApp app, bool confirmed, Func<AppSettings, AppSettings>? settings = null)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar("9800401", "アオイ"), Avatar("9800402", "モモ"), Avatar("9800403", "キキ")],
        });
        if (settings is not null)
        {
            await app.ChangeSettingsAsync(settings);
        }

        var clothing = Make.Item(ClothingId, "作り物の衣装");
        clothing = clothing with
        {
            Local = clothing.Local with
            {
                Avatars = [new AvatarLink { AvatarItemId = "9800401", Name = "アオイ", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                AvatarsDetectedAt = DateTimeOffset.Now,
            },
        };
        await app.AddItemAsync(clothing);
        await app.AddItemAsync(Profile("9900892", new ConversionTarget { AvatarItemId = "9800401", Reverse = true, Confirmed = confirmed }));
        await app.AddItemAsync(Profile("9900893", new ConversionTarget { AvatarItemId = "9800402", Forward = true, Confirmed = true }));
        var main = await app.StartAsync();
        return new ItemViewModel(clothing, app.Services, main, main.Thumbnails);
    }

    [Fact]
    public Task 確認したプロファイルで変換できるアバターを_変換経由の段に並べ_元のアバターを吹き出しで言う() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, confirmed: true);

        var row = Assert.Single(page.ViaConversionAvatars);
        Assert.Equal("モモ", row.Name);
        Assert.Contains("「アオイ」用", row.ViaTooltip, StringComparison.Ordinal);
        Assert.True(page.HasViaConversionAvatars);
    });

    [Fact]
    public Task 見出しの件数は_商品の対応の数に変換経由で増える数を足した形() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, confirmed: true);

        Assert.Equal("（1+1 体）", page.AvatarsCountText);
    });

    [Fact]
    public Task 変換経由を表示しない設定では_件数も前の形() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, confirmed: true, settings => settings with { ShowAvatarsViaConversion = false });

        Assert.Equal("（1 体）", page.AvatarsCountText);
    });

    [Fact]
    public Task 確認していないつなぎ先は_既定では使わない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, confirmed: false);

        Assert.Empty(page.ViaConversionAvatars);
        Assert.False(page.HasViaConversionAvatars);
    });

    [Fact]
    public Task プロファイルの商品ページで確認すると_開き直した衣装に変換経由が出る() => TestApp.Run(async app =>
    {
        var before = await OpenAsync(app, confirmed: false);
        Assert.Empty(before.ViaConversionAvatars);

        var profile = (await app.Store.Items.LoadAsync("9900892"))!;
        var profilePage = new ItemViewModel(profile, app.Services, app.Main, app.Main.Thumbnails);
        Assert.True(profilePage.Conversion.ShowsConversion);
        Assert.Single(profilePage.Conversion.Rows).ConfirmCommand!.Execute(null);
        await app.SettleAsync();

        // 検索の写しに知らせてあるので、全件を読み直さなくても次に開いたときに出る
        var clothing = (await app.Store.Items.LoadAsync(ClothingId))!;
        var after = new ItemViewModel(clothing, app.Services, app.Main, app.Main.Thumbnails);
        Assert.Equal("モモ", Assert.Single(after.ViaConversionAvatars).Name);
    });

    [Fact]
    public Task 設定で切ると_変換経由の段を出さない() => TestApp.Run(async app =>
    {
        var page = await OpenAsync(app, confirmed: true, settings => settings with { ShowAvatarsViaConversion = false });

        Assert.Empty(page.ViaConversionAvatars);
    });
}
