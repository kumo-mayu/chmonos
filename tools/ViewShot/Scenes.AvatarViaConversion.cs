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
        new Scene("search-profile", "検索の条件「プロファイル」：変換元にアオイ、変換先にモモとキキ（キキに着せるプロファイルが無いと1行で言う。チェック4つ）", async context =>
        {
            static AvatarRegistryEntry Avatar(string id, string name) => new()
            {
                ItemId = id,
                BoothName = $"作り物のアバター「{name}」",
                DisplayName = name,
                Category = "3Dキャラクター",
            };

            await context.Seed.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar("9800401", "アオイ"), Avatar("9800402", "モモ"), Avatar("9800403", "キキ")] });
            Task<ItemRecord> ProfileAsync(string id, string name, ConversionTarget target)
                => context.Fake.ItemAsync(id, name, record => record with
                {
                    Local = record.Local with { Conversion = new ConversionSetting { Targets = [target] } },
                });

            await ProfileAsync("9900241", "作り物のプロファイル（アオイ・逆方向）", new ConversionTarget { AvatarItemId = "9800401", Reverse = true, Confirmed = true });
            await ProfileAsync("9900242", "作り物のプロファイル（モモ・順方向）", new ConversionTarget { AvatarItemId = "9800402", Forward = true, Confirmed = true });

            var main = await context.StartAsync();
            await SceneContext.UntilAsync(() => !main.Search.IsLoading && main.Search.TotalCount == 2, "商品を読み終える");
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var module = (ProfileModule)AddModule(main.Search, SearchModuleKind.Profile);
            module.Source.AddKey("avatar:9800401", text: "アオイ（9800401）");
            module.Destination.AddKey("avatar:9800402", text: "モモ（9800402）");
            module.Destination.AddKey("avatar:9800403", text: "キキ（9800403）");
            await context.SettleAsync();

            // 条件を足すと最初の入力欄にフォーカスが入り、候補の一覧（小窓）が開く。開いたまま終わると、窓を閉じるときにマウスの掴みを放す所で
            // WPF の後始末が固まる（道具が終わらなかった）。撮りたいのは条件の欄なので、フォーカスを外して一覧を閉じてから撮る
            System.Windows.Input.Keyboard.ClearFocus();
            await context.SettleAsync();
            return FiltersShot(root);
        }),
        new Scene("avatars-conversion","アバターの管理：変換（プロファイルの商品を決めていない／途中／決めた／扱わないの群で並べ、行の頭に商品の絵。確認待ちのある「途中」の見出しの右に「まとめて確認済みにする」。右に変換扱いの欄と次へ進むボタン）", context => AvatarsConversionAsync(context)),
        new Scene("avatars-conversion-search", "アバターの管理：変換の組を探す欄の語（「アオイ」）で、プロファイルの商品の一覧も絞った所（群の見出しは当たった数/全部）", context => AvatarsConversionAsync(context, query: "アオイ")),
        new Scene("avatars-conversion-pair-detail", "アバターの管理：「変換」で追加した組を選んだ右の欄（元・先の絵とリンク・ツールの名前・メモ・この組を外す）と、プロファイルの商品の行に並ぶ組の行", context => AvatarsConversionAsync(context, selectPair: true)),
        new Scene("avatars-conversion-many", "アバターの管理：変換で、プロファイルが400個の商品を選んだ右の欄（開く・流す・1つ確認済みにする重さを測って出す）", context => AvatarsConversionAsync(context, manyProfiles: 400)),
    ];

    /// <summary>
    /// アバターの管理の「変換」。プロファイルの商品4つ（推した・つなぎ先が無い・決めた・扱わない）。最初の決めていない商品を選んだ所。
    /// <paramref name="selectPair"/> のときは、追加した組2つ（メモ付き・ツールの名前付き）を足して、ツールの名前付きの組を選んだ所（組の行とプロファイルの商品の行を並べて見る）
    /// </summary>
    private static async Task<Shot> AvatarsConversionAsync(SceneContext context, bool selectPair = false, string? query = null, int manyProfiles = 0)
    {
        if (selectPair)
        {
            await context.Seed.ConversionPairs.SaveAsync(new ConversionPairFile
            {
                Pairs =
                [
                    new ConversionPair { From = ConversionEnd.Avatar("9800401"), To = ConversionEnd.Base("作り物の素体"), Memo = "袖の長さを直す\n2行目は行に出さない" },
                    new ConversionPair { From = ConversionEnd.Base("作り物の素体"), To = ConversionEnd.Avatar("9800401"), Tool = "作り物のツール", Memo = "裾を詰める" },
                ],
            });
        }

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

        // プロファイルの多い商品（ユーザ判断 2026-11-20・③「大量のプロファイルを含む商品」）。3つに1つずつ、確認済み・確認待ち・未設定
        var manySlugs = Enumerable.Range(0, manyProfiles).Select(index => $"fakemany{index:D3}").ToArray();
        await ProfileAsync("9900221", "作り物のプロファイル（まとめ）", 'D', manyProfiles > 0 ? manySlugs : ["fakeaoi", "fakebase", "fakemomo"], new ConversionSetting
        {
            Targets = manyProfiles > 0
                ? [.. manySlugs.Select((slug, index) => index % 3 == 0
                    ? new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = slug, Confirmed = true }
                    : index % 3 == 1 ? new ConversionTarget { BaseName = "作り物の素体", Forward = true, Slug = slug } : null)
                    .OfType<ConversionTarget>()]
                :
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

        if (selectPair)
        {
            await context.SettleAsync();
            avatars.ConversionPairs.SelectedRow = avatars.ConversionPairs.Rows[1];
            await context.SettleAsync();
            if (!avatars.ShowsConversionPairDetail || avatars.SelectedConversion is not null)
            {
                throw new InvalidOperationException("右に組の中身が出ない");
            }

            return new Shot(root);
        }

        // 推した理由と絵が出る商品を選んでおく
        if (manyProfiles > 0)
        {
            // 初めの1回は型の読み込みなどが乗るので、別の商品を先に1回開いてから測る
            avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().First(row => row.ItemId == "9900222");
            await context.SettleAsync();
            await MeasureManyProfilesAsync(context, root, avatars, manyProfiles);
            return new Shot(root);
        }

        avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().First(row => row.ItemId == "9900221");
        await context.SettleAsync();

        if (query is not null)
        {
            // 「アオイ」につなぐ商品は、途中の（まとめ）と決めた商品の2つ。つなぎ先の無い商品と扱わない商品は隠れる
            avatars.ConversionPairs.Query = query;
            await context.SettleAsync();
            if (avatars.ConversionItems.Cast<ConversionItemRowViewModel>().Count() != 2)
            {
                throw new InvalidOperationException($"「{query}」でプロファイルの商品が2つに絞られない");
            }
        }

        return new Shot(root);
    }

    /// <summary>
    /// プロファイルの多い商品の右の欄の重さを測って出す（ユーザ判断 2026-11-20・③）。開く（選んでから配置が済むまで）・作った行の数・
    /// 流す（右の欄を端まで 120px ずつ送り、1歩ごとの配置）・1つ確認済みにした後（書いて読み直し、配置が済むまで）。
    /// 窓を出さずに描く台なので、画面に出す手間（合成）は入らない。前後の版で同じ場面を回して比べる
    /// </summary>
    private static async Task MeasureManyProfilesAsync(SceneContext context, System.Windows.FrameworkElement root, AvatarsViewModel avatars, int count)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        avatars.SelectedConversion = avatars.ConversionItems.Cast<ConversionItemRowViewModel>().First(row => row.ItemId == "9900221");
        root.UpdateLayout();
        var open = watch.Elapsed.TotalMilliseconds;
        await context.SettleAsync();
        if (avatars.ConversionEditor.Rows.Count != count)
        {
            throw new InvalidOperationException($"右の欄の行が {avatars.ConversionEditor.Rows.Count} 行（{count} 行のはず）");
        }

        var panel = Descendants(root).OfType<ItemConversionPanel>().First();
        int Realized() => Descendants(panel).OfType<System.Windows.FrameworkElement>()
            .Count(element => System.Windows.Automation.AutomationProperties.GetAutomationId(element) == "ItemConversionRow");
        var realized = Realized();

        // 流す相手：欄の中で流せる一覧があればそれ（直した後）、無ければ右の欄の外の ScrollViewer（直す前）
        var scroller = Descendants(panel).OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault(viewer => viewer.ScrollableHeight > 0)
            ?? Ancestors(panel).OfType<System.Windows.Controls.ScrollViewer>().First(viewer => viewer.ScrollableHeight > 0);
        var steps = new List<double>();
        for (var offset = 0.0; offset < scroller.ScrollableHeight && steps.Count < 2000; offset += 120)
        {
            watch.Restart();
            scroller.ScrollToVerticalOffset(offset + 120);
            root.UpdateLayout();
            steps.Add(watch.Elapsed.TotalMilliseconds);
        }

        var target = avatars.ConversionEditor.Rows.First(row => row.IsUnconfirmed);
        var before = avatars.ConversionEditor.Rows.ToList();
        watch.Restart();
        target.ConfirmCommand!.Execute(null);
        await SceneContext.UntilAsync(() => avatars.ConversionEditor.UnconfirmedCount < before.Count(row => row.IsUnconfirmed), "確認済みが右の欄に入る");
        root.UpdateLayout();
        var confirm = watch.Elapsed.TotalMilliseconds;
        var kept = avatars.ConversionEditor.Rows.Count(row => before.Contains(row));

        Console.WriteLine($"  プロファイル {count} 個：開く {open:F0}ms・作った行 {realized}・流す {steps.Count} 歩 合計 {steps.Sum():F0}ms 最長 {(steps.Count > 0 ? steps.Max() : 0):F0}ms・"
            + $"1つ確認済みにして配置まで {confirm:F0}ms・同じ物のまま残った行 {kept}/{count}");
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    private static IEnumerable<System.Windows.DependencyObject> Ancestors(System.Windows.DependencyObject child)
    {
        for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(child); parent is not null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
        {
            yield return parent;
        }
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
                            // 外したつなぎ先（畳んだ一覧に出る）
                            new ConversionTarget { AvatarItemId = "9800401", Forward = true, Slug = "fakemomo", Confirmed = true, Rejected = true },
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
