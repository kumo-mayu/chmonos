using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 「変換」のプロファイルの商品の群（ユーザ判断 2026-11-20・④）：決めていない（何も決めていない）→ 途中（1つ以上決めたが、未設定か確認待ちが残る）
/// → 決めた → 扱わない。札（確認待ち・未設定の数）は群を足す前と同じ。「後で」・次の商品へ移る流れは、決めていないと途中の両方を回る。
/// 名前・ID は作り物。
/// </summary>
public class ConversionItemGroupTests
{
    private static readonly ProfileSlug A = new("fakea", Forward: true, Reverse: false);
    private static readonly ProfileSlug B = new("fakeb", Forward: true, Reverse: false);

    internal static ConversionTarget Target(string slug, bool confirmed = false, bool rejected = false)
        => new() { AvatarItemId = "9800401", Forward = true, Slug = slug, Confirmed = confirmed, Rejected = rejected };

    private static ConversionItemRowViewModel RowOf(ConversionSetting? setting, params ProfileSlug[] profiles)
    {
        var item = Make.Item("9900871", "作り物のプロファイル");
        return ConversionItemRowViewModel.Create(item with { Local = item.Local with { Conversion = setting } }, profiles);
    }

    [Fact]
    public void 何も決めていない商品は_決めていない()
    {
        var row = RowOf(null, A, B);

        Assert.Equal("決めていない", row.Group);
        Assert.Equal("未設定 2", row.BadgeText);
        Assert.True(row.NeedsDecision);
    }

    [Fact]
    public void 確認待ちしか無い商品は_決めていない()
    {
        var row = RowOf(new ConversionSetting { Targets = [Target("fakea"), Target("fakeb")] }, A, B);

        Assert.Equal("決めていない", row.Group);
        Assert.Equal("確認待ち 2", row.BadgeText);
    }

    [Fact]
    public void 一つ確認済みで未設定が残る商品は_途中で_札は今のまま()
    {
        var row = RowOf(new ConversionSetting { Targets = [Target("fakea", confirmed: true)] }, A, B);

        Assert.Equal("途中", row.Group);
        Assert.Equal("未設定 1", row.BadgeText);
        Assert.True(row.NeedsDecision);
    }

    [Fact]
    public void 一つ確認済みで確認待ちが残る商品は_途中()
    {
        var row = RowOf(new ConversionSetting { Targets = [Target("fakea", confirmed: true), Target("fakeb")] }, A, B);

        Assert.Equal("途中", row.Group);
        Assert.Equal("確認待ち 1", row.BadgeText);
    }

    /// <summary>外すとその行は選ぶ欄に戻る（つなぎ先はまだ無い）ので、外しただけでは「決めた」に数えない。</summary>
    [Fact]
    public void つなぎ先を外しただけの商品は_決めていない()
    {
        var row = RowOf(new ConversionSetting { Targets = [Target("fakea", confirmed: true, rejected: true)] }, A, B);

        Assert.Equal("決めていない", row.Group);
        Assert.Equal("未設定 2", row.BadgeText);
    }

    [Fact]
    public void 全部確認済みなら決めた_扱わないにした商品は扱わないで_どちらも札を出さない()
    {
        var decided = RowOf(new ConversionSetting { Targets = [Target("fakea", confirmed: true), Target("fakeb", confirmed: true)] }, A, B);
        var notProfile = RowOf(new ConversionSetting { Mode = ConversionMode.NotProfile, Targets = [Target("fakea", confirmed: true)] }, A, B);

        Assert.Equal("決めた", decided.Group);
        Assert.Equal("扱わない", notProfile.Group);
        Assert.False(decided.HasBadge);
        Assert.False(notProfile.HasBadge);
        Assert.False(decided.NeedsDecision);
        Assert.False(notProfile.NeedsDecision);
    }

    // ---- 画面の一覧と流れ ----

    internal static Task SaveProfilesAsync(TestApp app, string id, string name, ConversionSetting? conversion, params string[] slugs)
        => SaveProfilesAsync(app, id, name, conversion, item => item, slugs);

    internal static async Task SaveProfilesAsync(
        TestApp app, string id, string name, ConversionSetting? conversion, Func<ItemRecord, ItemRecord> shape, params string[] slugs)
    {
        var hash = new string(id[^1], 64);
        new UnityPackagePathStore(app.Services.Paths).Add(hash, "Profile.unitypackage",
        [
            .. slugs.Select((slug, index) => new UnityPackageAsset(
                $"0000000000000000000000000000000{index + 1}", $"Assets/OutfitRetargetingSystem/Editor/config_template2{slug}.json")),
        ]);
        var item = shape(Make.Item(id, name));
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

    /// <summary>決めた（い）・途中（う）・決めていない（あ・え）・扱わない（お）の5つ。名前の順と群の順が食い違うように付けてある。</summary>
    private static async Task<AvatarsViewModel> OpenAsync(TestApp app)
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター" }],
        });
        await SaveProfilesAsync(app, "9900871", "作り物のプロファイルあ", null, "fakea", "fakeb");
        await SaveProfilesAsync(app, "9900872", "作り物のプロファイルい",
            new ConversionSetting { Targets = [Target("fakea", confirmed: true), Target("fakeb", confirmed: true)] }, "fakea", "fakeb");
        await SaveProfilesAsync(app, "9900873", "作り物のプロファイルう",
            new ConversionSetting { Targets = [Target("fakea", confirmed: true)] }, "fakea", "fakeb");
        await SaveProfilesAsync(app, "9900874", "作り物のプロファイルえ", new ConversionSetting { Targets = [Target("fakea")] }, "fakea");
        await SaveProfilesAsync(app, "9900875", "作り物のプロファイルお", new ConversionSetting { Mode = ConversionMode.NotProfile }, "fakea");

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);
        return avatars;
    }

    private static IReadOnlyList<ConversionItemRowViewModel> Rows(AvatarsViewModel avatars)
        => avatars.ConversionItems.Cast<ConversionItemRowViewModel>().ToList();

    [Fact]
    public Task 一覧は_決めていない_途中_決めた_扱わないの順に群で並ぶ() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);

        Assert.Equal(["9900871", "9900874", "9900873", "9900872", "9900875"], Rows(avatars).Select(row => row.ItemId));
        Assert.Equal(["決めていない", "決めていない", "途中", "決めた", "扱わない"], Rows(avatars).Select(row => row.Group));
        Assert.Equal(["決めていない（2）", "途中（1）", "決めた（1）", "扱わない（1）"], avatars.ConversionItems.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(group => group.Name.ToString()));
    });

    [Fact]
    public Task 左の流れは_先頭に追加した組の節_続けて同じ行を同じ群で並べる() => TestApp.Run(async app =>
    {
        // 探す欄より下を1つの流れにする（ユーザ判断 2026-11-20・①）。節も群の1つにして、商品の行と同じ一覧で流す
        var avatars = await OpenAsync(app);

        var items = avatars.ConversionList.Cast<object>().ToList();
        Assert.Same(avatars.PairsSection, items[0]);
        Assert.Same(avatars.ConversionPairs, avatars.PairsSection.Pairs);
        Assert.Equal(Rows(avatars), items.Skip(1));
        Assert.Equal(["追加した組", "決めていない（2）", "途中（1）", "決めた（1）", "扱わない（1）"],
            avatars.ConversionList.Groups.Cast<System.Windows.Data.CollectionViewGroup>().Select(group => group.Name.ToString()));

        // 絞っても節は残る（組の欄と、当たる物が無い時の1行は節の中にある）
        avatars.ConversionPairs.Query = "当たらない語";
        await UiThread.Until(() => avatars.ShowsNoMatchingConversionItems, "絞って0件になる");
        Assert.Equal([avatars.PairsSection], avatars.ConversionList.Cast<object>());
    });

    [Fact]
    public Task 後では_決めていないと途中の両方を回り_決めたと扱わないは飛ばす() => TestApp.Run(async app =>
    {
        var avatars = await OpenAsync(app);
        Assert.Equal("9900871", avatars.SelectedConversion!.ItemId);

        var visited = new List<string>();
        for (var step = 0; step < 4; step++)
        {
            avatars.LaterCommand.Execute(null);
            visited.Add(avatars.SelectedConversion!.ItemId);
        }

        Assert.Equal(["9900874", "9900873", "9900871", "9900874"], visited);
    });

    [Fact]
    public Task 決めていない商品が無く途中だけが残る時は_変換を開くと途中の商品を選ぶ() => TestApp.Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター" }],
        });
        await SaveProfilesAsync(app, "9900872", "作り物のプロファイルい",
            new ConversionSetting { Targets = [Target("fakea", confirmed: true), Target("fakeb", confirmed: true)] }, "fakea", "fakeb");
        await SaveProfilesAsync(app, "9900873", "作り物のプロファイルう",
            new ConversionSetting { Targets = [Target("fakea", confirmed: true)] }, "fakea", "fakeb");

        var main = await app.StartAsync();
        main.ShowAvatarsCommand.Execute(null);
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);

        Assert.Equal("9900873", avatars.SelectedConversion!.ItemId);
    });
}
