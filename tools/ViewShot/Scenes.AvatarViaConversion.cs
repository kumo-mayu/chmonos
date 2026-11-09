using Chmonos.App.Views;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 商品ページの対応アバター：変換経由の段（docs/spec/conversion.md）。衣装はアオイに対応。アオイ用の逆方向と、モモ・キキ用の順方向のプロファイルを
/// 持っているので、モモとキキが変換経由で並ぶ（キキは持っている）。名前・ID は作り物
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> AvatarViaConversionScenes =>
    [
        new Scene("item-page-avatars-via-conversion", "商品ページ：変換経由の段（持っているプロファイルで変換できる見込みのアバター）", ViaConversionAsync),
        new Scene("item-page-conversion-setting", "商品ページ：変換扱いの欄（中のプロファイルごとのつなぎ先・確認待ち・選ぶ欄）", ConversionSettingAsync),
        DialogScene("catalog-dialog-confirm-conversions", "推した通りにまとめて決める窓：商品2つ・つなぎ先3つ（1つはチェックを外した所）", _ =>
        {
            ConfirmConversionsRow Row(string text, bool isChecked = true) => new()
            {
                Target = new ConversionTarget { AvatarItemId = "9800401", Forward = true },
                Text = text,
                IsChecked = isChecked,
            };
            var model = new ConfirmConversionsDialogViewModel(
            [
                new ConfirmConversionsSection
                {
                    ItemId = "9900231",
                    Name = "作り物のプロファイル（まとめ）",
                    Rows = [Row("fakeaoi（順方向）→ アオイ"), Row("fakebase（逆方向）→ 作り物の素体（共通素体）", isChecked: false)],
                },
                new ConfirmConversionsSection { ItemId = "9900232", Name = "作り物のプロファイル（モモ）", Rows = [Row("fakemomo（両方向）→ モモ")] },
            ]);
            return Task.FromResult<System.Windows.Window>(new ConfirmConversionsDialog(model));
        }),
        new Scene("avatars-conversion","アバターの管理：変換（プロファイルの商品を決めていない／決めた／扱わないの群で並べ、右に変換扱いの欄と次へ進むボタン）", AvatarsConversionAsync),
    ];

    /// <summary>アバターの管理の「変換」。プロファイルの商品4つ（推した・つなぎ先が無い・決めた・扱わない）。最初の決めていない商品を選んだ所。</summary>
    private static async Task<Shot> AvatarsConversionAsync(SceneContext context)
    {
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター", BaseName = "作り物の素体" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体" }],
        });

        async Task ProfileAsync(string id, string name, char hashChar, string[] slugs, ConversionSetting? conversion)
        {
            var hash = new string(hashChar, 64);
            new Chmonos.Core.Storage.UnityPackagePathStore(Chmonos.Core.Storage.AppPaths.Default).Add(hash, "Profile.unitypackage",
                slugs.Select((slug, index) => new Chmonos.Core.Services.UnityPackageAsset(
                    $"{index + 1:D32}", $"Assets/OutfitRetargetingSystem/Editor/config_template2{slug}.json")).ToList());
            await context.Fake.ItemAsync(id, name, record => record with
            {
                Local = record.Local with
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
                    // 推した理由の例：商品の対応素体から（見回りと同じ推し方で出す）
                    AvatarBases = id == "9900221" ? [new AvatarBaseLink { BaseName = "作り物の素体", Confirmed = true }] : [],
                },
            }, images: 1);
        }

        await ProfileAsync("9900221", "作り物のプロファイル（まとめ）", 'D', ["fakeaoi", "fakebase", "fakemomo"], new ConversionSetting
        {
            Targets =
            [
                new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeaoi", Confirmed = true },
                new ConversionTarget { BaseName = "作り物の素体", Forward = true, Slug = "fakebase" },
            ],
        });
        await ProfileAsync("9900222", "作り物のプロファイル（つなぎ先なし）", 'E', ["fakekiki"], null);
        await ProfileAsync("9900223", "作り物のプロファイル（決めた）", 'F', ["fakeaoi"], new ConversionSetting
        {
            Targets = [new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakeaoi", Confirmed = true }],
        });
        await ProfileAsync("9900224", "作り物の変換ツール", 'A', ["fakesample"], new ConversionSetting { Mode = ConversionMode.NotProfile });

        var main = await context.StartAsync();
        await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 4, "商品を読み終える");
        main.ShowAvatarsCommand.Execute(null);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        var avatars = context.Screen<Chmonos.App.ViewModels.AvatarsViewModel>();
        await SceneContext.UntilAsync(() => !avatars.IsLoading && avatars.HasConversionItems, "変換の一覧ができる");
        avatars.ShowConversionModeCommand.Execute(null);

        // 推した理由と絵が出る商品を選んでおく
        avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().First(row => row.ItemId == "9900221");
        await context.SettleAsync();
        return new Shot(root);
    }

    /// <summary>
    /// プロファイルの商品ページの「変換扱い」の欄。中に作り物のプロファイルが3つ：確認したつなぎ先・推した（確認待ちの）つなぎ先・決まっていない物（選ぶ欄）。
    /// </summary>
    private static async Task<Shot> ConversionSettingAsync(SceneContext context)
    {
        const string hash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9800401", BoothName = "作り物のアバター「アオイ」", DisplayName = "アオイ", Category = "3Dキャラクター", BaseName = "作り物の素体" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体" }],
        });
        new Chmonos.Core.Storage.UnityPackagePathStore(Chmonos.Core.Storage.AppPaths.Default).Add(hash, "Profile.unitypackage",
        [
            new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000001", "Assets/OutfitRetargetingSystem/Editor/config_template2fakeaoi.json"),
            new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000002", "Assets/OutfitRetargetingSystem/Editor/config_fakeaoi2template.json"),
            new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000003", "Assets/OutfitRetargetingSystem/Editor/config_fakebase2template.json"),
            new Chmonos.Core.Services.UnityPackageAsset("00000000000000000000000000000004", "Assets/OutfitRetargetingSystem/Editor/config_template2fakemomo.json"),
        ]);

        var item = await context.Fake.ItemAsync(
            "9900211",
            "作り物のプロファイル",
            record => record with
            {
                Local = record.Local with
                {
                    LocalFiles =
                    [
                        new LocalFileRecord
                        {
                            Hash = hash,
                            Paths = [@"D:\fake\profile.zip"],
                            SizeBytes = 1,
                            Contents = ["Profile.unitypackage"],
                            UnityPackages = [new UnityPackageSummary { Entry = "Profile.unitypackage", Roots = ["Assets/OutfitRetargetingSystem"] }],
                        },
                    ],
                    Conversion = new ConversionSetting
                    {
                        Targets =
                        [
                            new ConversionTarget { AvatarItemId = "9800401", Forward = true, Reverse = true, Slug = "fakeaoi", Confirmed = true },
                            new ConversionTarget { BaseName = "作り物の素体", Reverse = true, Slug = "fakebase" },
                        ],
                    },
                },
            },
            images: 1);

        var main = await context.StartAsync();
        await SceneContext.UntilAsync(() => !main.Search.IsLoading, "商品を読み終える");
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);

        // 欄はページの末尾の「記録していること」の中にあるので、末尾まで送ってから撮る
        if (Look.View<ItemView>(root) is { } page && Look.Named<System.Windows.Controls.ScrollViewer>(page, "Body") is { } body)
        {
            body.ScrollToEnd();
            await context.PresentAsync(root);
        }

        return new Shot(root)
        {
            Focus = () => Look.All<System.Windows.Controls.TextBlock>(root)
                .FirstOrDefault(text => System.Windows.Automation.AutomationProperties.GetAutomationId(text) == "ItemConversionState") is { } state
                    ? Look.Ancestor<System.Windows.Controls.Border>(state)
                    : null,
            FocusMargin = 8,
        };
    }

    private static async Task<Shot> ViaConversionAsync(SceneContext context)
    {
        static AvatarRegistryEntry Avatar(string id, string name, bool owned = false) => new()
        {
            ItemId = id,
            BoothName = $"作り物のアバター「{name}」",
            DisplayName = name,
            Category = "3Dキャラクター",
            IsOwnedManually = owned,
        };

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Avatar("9800401", "アオイ"), Avatar("9800402", "モモ"), Avatar("9800403", "キキ", owned: true)],
        });

        Task<ItemRecord> ProfileAsync(string id, string name, params ConversionTarget[] targets)
            => context.Fake.ItemAsync(id, name, record => record with
            {
                Local = record.Local with { Conversion = new ConversionSetting { Mode = ConversionMode.Profile, Targets = targets } },
            });

        await ProfileAsync("9900202", "作り物のプロファイル（アオイ）", new ConversionTarget { AvatarItemId = "9800401", Forward = true, Reverse = true, Confirmed = true });
        await ProfileAsync("9900203", "作り物のプロファイル（モモ・キキ）",
            new ConversionTarget { AvatarItemId = "9800402", Forward = true, Confirmed = true },
            new ConversionTarget { AvatarItemId = "9800403", Forward = true, Confirmed = true });

        var item = await context.Fake.ItemAsync(
            "9900201",
            "作り物の衣装（アオイに対応）",
            record => record with
            {
                Local = record.Local with
                {
                    Avatars = [new AvatarLink { AvatarItemId = "9800401", Name = "アオイ", Source = AvatarLinkSource.SupportSection, Confirmed = true }],
                    AvatarsDetectedAt = DateTimeOffset.Now,
                },
            },
            images: 1);

        var main = await context.StartAsync();

        // 変換経由は、検索が読んだ手元の全商品からプロファイルを拾う。読み終える前に開くと、まだ空で段が出ない（アプリでは検索を読んだ後に開く）
        await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 3, "商品を読み終える");
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        return new Shot(root) { Focus = () => Look.View<ItemAvatarsPanel>(root), FocusMargin = 8 };
    }
}
