using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// アバターの管理の「変換」（<c>docs/spec/conversion.md</c>「変換扱いの画面」・ユーザ判断 2026-11-09）。
/// プロファイルの商品4つ：推した（確認待ち）／決めた／扱わない／つなぎ先が無い。名前・ID は作り物。
/// </summary>
public class AvatarsConversionTests
{
    private static readonly ConversionTarget SuggestedA = new() { AvatarItemId = "9800401", Forward = true, Slug = "fakea" };

    private static async Task SaveProfileAsync(TestApp app, string id, string name, string slug, ConversionSetting? conversion)
    {
        var hash = new string(id[^1], 64);
        new UnityPackagePathStore(app.Services.Paths).Add(hash, "Profile.unitypackage",
        [
            new UnityPackageAsset("00000000000000000000000000000001", $"Assets/OutfitRetargetingSystem/Editor/config_template2{slug}.json"),
        ]);
        var item = Make.Item(id, name);
        await app.AddItemAsync(item with
        {
            Local = item.Local with
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        Paths = [$@"D:\fake\{id}.zip"],
                        SizeBytes = 1,
                        Contents = ["Profile.unitypackage"],
                        UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                    },
                ],
                Conversion = conversion,
            },
        });
    }

    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター" }],
        });
        await SaveProfileAsync(app, "9900861", "作り物のプロファイルA", "fakea", new ConversionSetting { Targets = [SuggestedA] });
        await SaveProfileAsync(app, "9900862", "作り物のプロファイルB", "fakeb",
            new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeb", Confirmed = true }] });
        await SaveProfileAsync(app, "9900863", "作り物のプロファイルC", "fakec", new ConversionSetting { Mode = ConversionMode.NotProfile });
        await SaveProfileAsync(app, "9900864", "作り物のプロファイルD", "faked", conversion: null);

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        return avatars;
    }

    private static IReadOnlyList<ConversionItemRowViewModel> Rows(AvatarsViewModel avatars)
        => avatars.ConversionItems.Cast<ConversionItemRowViewModel>().ToList();

    private static ConversionItemRowViewModel Row(AvatarsViewModel avatars, string id)
        => Rows(avatars).Single(row => row.ItemId == id);

    [Fact]
    public Task 決めていない物を上に群で分け_決めていない数を添える() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        Assert.Equal("変換（4）", avatars.ConversionModeText);
        Assert.Equal(["9900861", "9900864", "9900862", "9900863"], Rows(avatars).Select(row => row.ItemId));
        Assert.Equal(["決めていない", "決めていない", "決めた", "扱わない"], Rows(avatars).Select(row => row.Group));
        Assert.Equal("確認待ち 1", Row(avatars, "9900861").BadgeText);
        Assert.Equal("未設定 1", Row(avatars, "9900864").BadgeText);
        Assert.False(Row(avatars, "9900862").HasBadge);
    });

    [Fact]
    public Task 変換を見ると_決めていない最初の商品を選び_アバターの詳細は出さない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        avatars.ShowConversionModeCommand.Execute(null);

        Assert.True(avatars.IsConversionMode);
        Assert.False(avatars.IsAvatarMode);
        Assert.False(avatars.ShowsAvatarDetail);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
        Assert.True(avatars.ShowsConversionDetail);
        Assert.True(Assert.Single(avatars.ConversionEditor.Rows).IsUnconfirmed);

        avatars.ShowAvatarModeCommand.Execute(null);
        Assert.False(avatars.IsConversionMode);
        Assert.True(avatars.IsAvatarMode);
    });

    [Fact]
    public Task 推した通りに決めると_確認済みで保存し_決めた群へ移って_次の決めていない商品へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        avatars.DecideSuggestedCommand.Execute(null);
        await app.SettleAsync();

        var saved = (await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!;
        Assert.True(Assert.Single(saved.Targets).Confirmed);
        Assert.Equal("決めた", Row(avatars, "9900861").Group);
        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);

        // 検索の写しにも入る（衣装の商品ページの変換経由に使われる）
        Assert.True(app.Main.Search.FindItem("9900861")!.Local.Conversion!.Targets.Single().Confirmed);
    });

    [Fact]
    public Task プロファイルではないにすると_扱わないで保存して次へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        avatars.SelectedConversion = Row(avatars, "9900864");

        avatars.MarkNotProfileCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal(ConversionMode.NotProfile, (await app.Store.Items.LoadAsync("9900864"))!.Local.Conversion!.Mode);
        Assert.Equal("扱わない", Row(avatars, "9900864").Group);
        Assert.Equal("9900861", avatars.SelectedConversion!.ItemId);
    });

    [Fact]
    public Task まとめて決めると_窓でチェックを残した物だけを確認済みにする() => TestApp.Run(async app =>
    {
        // 推したつなぎ先が2つの商品を足す（片方のチェックを外す）
        await SaveProfileAsync(app, "9900865", "作り物のプロファイルE", "fakee", new ConversionSetting
        {
            Targets =
            [
                new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakee" },
                new ConversionTarget { AvatarItemId = "9800401", Reverse = true, Slug = "fakee2" },
            ],
        });
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);
        Assert.Equal("推した通りにまとめて決める（3 件）", avatars.BulkText);

        ConfirmConversionsDialogViewModel? shown = null;
        ConfirmConversionsDialogViewModel.Intercept = model =>
        {
            shown = model;
            model.Sections.Single(section => section.ItemId == "9900865").Rows.Single(row => row.Target.Slug == "fakee2").IsChecked = false;
            return true;
        };
        try
        {
            avatars.BulkConfirmCommand.Execute(null);
            await app.SettleAsync();
        }
        finally
        {
            ConfirmConversionsDialogViewModel.Intercept = null;
        }

        // 窓には扱わない商品を出さず、最初は全部にチェックが付いている
        Assert.Equal(["9900861", "9900865"], shown!.Sections.Select(section => section.ItemId).Order());
        Assert.True((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
        var e = (await app.Store.Items.LoadAsync("9900865"))!.Local.Conversion!.Targets;
        Assert.Equal([true, false], e.Select(target => target.Confirmed));
        Assert.Equal("推した通りにまとめて決める（1 件）", avatars.BulkText);
    });

    [Fact]
    public Task まとめて決める窓でキャンセルすると_何も書かない() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        ConfirmConversionsDialogViewModel.Intercept = _ => false;
        try
        {
            avatars.BulkConfirmCommand.Execute(null);
            await app.SettleAsync();
        }
        finally
        {
            ConfirmConversionsDialogViewModel.Intercept = null;
        }

        Assert.False((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
    });

    [Fact]
    public void まとめて決める窓は_チェックが無いと押せず理由を言う()
    {
        var row = new ConfirmConversionsRow { Target = SuggestedA, Text = "fakea（順方向）→ アオイ" };
        var model = new ConfirmConversionsDialogViewModel([new ConfirmConversionsSection { ItemId = "9900861", Name = "作り物", Rows = [row] }]);
        Assert.Equal("確認済みにする（1 件）", model.CommitText);

        row.IsChecked = false;

        Assert.False(model.CanCommit);
        Assert.Equal("チェックが1つもありません。", model.CommitHint);
    }

    [Fact]
    public Task 後では_何も書かずに次の決めていない商品へ進む() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        avatars.ShowConversionModeCommand.Execute(null);

        avatars.LaterCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("9900864", avatars.SelectedConversion!.ItemId);
        Assert.False((await app.Store.Items.LoadAsync("9900861"))!.Local.Conversion!.Targets.Single().Confirmed);
    });
}
