using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;

namespace ViewShot;

internal static partial class Scenes
{
    private static IEnumerable<Scene> Import =>
    [
        new Scene("import-result-unreadable", "取り込みの結果：読めなかった文・OneDrive の文・壊れたzipの文が2つ・下のボタン3つ", async context =>
        {
            var main = await context.StartAsync();
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            Backdoor.ShowImportSummary(context.Screen<ImportViewModel>(), new ImportSummary
            {
                FilesScanned = 412,
                FilesHashed = 412,
                ItemsAdded = 38,
                UnresolvedFiles = 9,
                NotFound = 2,
                FilesUnreadable = 3,
                FoldersUnreadable = 1,
                FilesOnlineOnly = 5,
                FilesBrokenArchive = 2,
                FilesBrokenArchiveOnItems = 4,
                BrokenArchiveItemNames = ["作り物の衣装セット", "作り物の髪型", "作り物のアクセサリー"],
            });
            await context.SettleAsync();

            // 「結果」の見出しのすぐ外のカード
            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "結果")) };
        }),

        new Scene("import-result-one-broken", "取り込みの結果：壊れたzipの文が1つだけ・商品名が長い（30字で切る）", async context =>
        {
            var main = await context.StartAsync();
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            Backdoor.ShowImportSummary(context.Screen<ImportViewModel>(), new ImportSummary
            {
                FilesScanned = 12,
                FilesHashed = 12,
                ItemsAdded = 1,
                FilesBrokenArchiveOnItems = 1,
                BrokenArchiveItemNames = ["【12アバター対応】作り物のとても長い名前の衣装セット フルパッケージ版 テクスチャ4K PSD付き"],
            });
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "結果")) };
        })
        {
            // 窓の最小の幅。文が折り返し、ボタンが次の行へ送られるかを見る
            Width = 900,
        },
    ];

    private static IEnumerable<Scene> Item =>
    [
        new Scene("item-files-states", "商品ページの手元のファイル：在る・壊れたzip・見つかりません・取り外しているドライブ・同じ中身が2箇所", async context =>
        {
            var plain = Fake.Zip(@"ライブラリ\costume_full_v1.2.zip", "costume.unitypackage", "readme.txt");
            var item = await context.Fake.ItemAsync("9900101", "作り物の衣装セット（ファイルの行の確かめ）", record => record with
            {
                Local = record.Local with
                {
                    LocalFiles =
                    [
                        Fake.FileRecord(plain),
                        Fake.FileRecord(Fake.BrokenZip(@"ライブラリ\costume_texture_4k.zip"), size: 1_288_000_000, broken: true),
                        Fake.FileRecord(Fake.MissingPath(@"ライブラリ\移したファイル_costume_psd.zip"), size: 310_000_000),
                        // 壊れた印があっても、無い物には「見つかりません」だけを出す
                        Fake.FileRecord(Fake.MissingPath(@"ライブラリ\無くて壊れた印もある.zip"), broken: true),
                        Fake.FileRecord(Fake.DetachedDrivePath(@"BOOTH\costume_backup.zip"), size: 48_300_000),
                        new LocalFileRecord
                        {
                            Hash = Fake.Hex("two-places"),
                            Paths = [Fake.Zip(@"ライブラリ\costume_extra.zip"), Fake.Zip(@"バックアップ\costume_extra.zip")],
                            SizeBytes = 9_400_000,
                            VariationId = 1,
                        },
                    ],
                },
            });

            var main = await context.StartAsync();
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            // 在るかは、行を出した後で画面のスレッドの外で確かめて付く
            var page = context.Screen<ItemViewModel>();
            await SceneContext.UntilAsync(
                () => page.LocalFiles.Count(row => row.IsMissing) == 2 && page.LocalFiles.Any(row => row.IsOnDetachedDrive),
                "「見つかりません」と「取り外しているドライブ」の印が付く");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ItemFilesPanel>(root) };
        })
        {
            // 右の列が札を全部並べられる幅。主の窓の既定の幅（1440）では、右の「この商品から外す」の下に札が隠れる
            // （2026-09-30 にこの台で見つけた。--width 1280,1440 で見られる）
            Width = 1600,
        },

        new Scene("item-page", "商品ページの全体：絵3枚・タグ・手元のファイル1つ", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900102",
                "作り物の衣装セット",
                record => record with
                {
                    Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_full_v1.2.zip"))] },
                },
                images: 3);

            var main = await context.StartAsync();
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root);
        }),
    ];

    private static IEnumerable<Scene> Bands =>
    [
        Unpacking("band-unpacking", "下の帯：一時展開の進み具合（1本・長いzip名・半分まで）", main =>
            Backdoor.ShowUnpacking(
                main, 1, "作り物のとても長い名前の衣装セット_フルパッケージ版_v2.1_対応アバター12体同梱_テクスチャ4K_PSD付き.zip",
                1_288_000_000, 2_470_000_000, stopping: false)),

        Unpacking("band-unpacking-two", "下の帯：一時展開の進み具合（2本を並べて展開・名前は出さない）", main =>
            Backdoor.ShowUnpacking(main, 2, string.Empty, 310_000_000, 3_900_000_000, stopping: false)),
    ];

    private static Scene Unpacking(string name, string title, Action<MainViewModel> show)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);

            show(main);
            await context.SettleAsync();

            // 帯は、進み具合の文を出している部品のすぐ外の枠
            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, main.UnpackText)), FocusMargin = 24 };
        })
        {
            Height = 600,
        };

    // 改変の画面は「改変」の段で開く。既定の「Unityプロジェクト」の段は、この PC の Unity Hub のプロジェクトの一覧を読むので、
    // 作り物にできない（PC ごとに違う絵になり、その PC のプロジェクトの名前が写る）
    private static IEnumerable<Scene> Modifications =>
    [
        new Scene("modification-hub-narrow", "改変の画面：上の切り替え（Unityプロジェクト・アバター・改変）と左の一覧・何も選んでいない右の欄・窓の最小の幅", async context =>
        {
            await SeedModificationsAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(ModificationHubLevel.Modification);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var hub = context.Screen<ModificationHubViewModel>();
            await SceneContext.UntilAsync(() => hub.Lines.Count > 0, "改変の一覧が並ぶ");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Width = 900,
            Height = 640,
        },

        new Scene("modification-selected-narrow", "改変の画面：長い名前の改変を選んだ右の欄（名前・ボタン・使ったものの行）・窓の最小の幅", async context =>
        {
            var selected = await SeedModificationsAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(
                ModificationHubLevel.Modification,
                new ModificationHubSelection(ModificationHubSelectionKind.Modification, selected));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            await SceneContext.UntilAsync(() => Look.View<ModificationView>(root) is not null, "選んだ改変が右の欄に出る");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Width = 900,
        },

        new Scene("modification-selected-status", "改変の画面：右の欄の上の帯に知らせの文が出ている所（日付と知らせが下の段へ送られる）・窓の最小の幅", async context =>
        {
            var selected = await SeedModificationsAsync(context);
            var main = await context.StartAsync();
            main.ShowModifications(
                ModificationHubLevel.Modification,
                new ModificationHubSelection(ModificationHubSelectionKind.Modification, selected));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            await SceneContext.UntilAsync(() => Look.View<ModificationView>(root) is not null, "選んだ改変が右の欄に出る");
            var detail = (ModificationViewModel)Look.View<ModificationView>(root)!.DataContext;
            Backdoor.ShowModificationStatus(detail, "「作り物のとても長い名前の衣装セット フルパッケージ版」を追加しました。");
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
        })
        {
            Width = 900,
            Height = 400,
        },
    ];

    /// <summary>アバター1体・衣装1つ・改変3つ（長い名前・プロジェクト無しを含む）。長い名前の改変の ID を返す。</summary>
    private static async Task<string> SeedModificationsAsync(SceneContext context)
    {
        var avatar = await context.Fake.ItemAsync("9900201", "作り物のアバター「ミナト」", record => record with
        {
            Booth = record.Booth with { Category = new BoothCategory { Id = 208, Name = "3Dキャラクター", ParentName = "3Dモデル" } },
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\minato_v1.0.zip", "minato.unitypackage"))] },
        });
        var costume = await context.Fake.ItemAsync("9900202", "作り物の衣装セット", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_full_v1.2.zip", "costume.unitypackage"))] },
        });
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = avatar.Id, DisplayName = "ミナト", BoothName = avatar.Booth.Name }],
        });

        const string LongName = "とても長い名前の改変：冬のコートと帽子とマフラーと手袋とブーツのセット（色違い3種）";
        var day = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));
        var longId = string.Empty;
        foreach (var (name, project, offset) in new[]
                 {
                     ("夏の普段着", @"ユニティ\MinatoSummer", 0),
                     (LongName, @"ユニティ\MinatoWinter", 1),
                     ("プロジェクト無し", null, 2),
                 })
        {
            var created = day.AddDays(offset);
            var id = ModificationId.For(avatar.Id, name, created);
            await context.Seed.Modifications.SaveAsync(new ModificationRecord
            {
                Id = id,
                AvatarItemId = avatar.Id,
                Name = name,
                CreatedAt = created,
                UpdatedAt = created,
                Memo = name == LongName ? "色違いは3種。帽子はボーンを入れ直した。" : null,
                // 在るフォルダ（Unity のプロジェクトの形はしていない）。無いフォルダにすると「見つかりません」の行になる
                UnityProject = project is null ? null : Fake.Folder(project),
                Members = [new ModificationMember { ItemId = costume.Id, VariationId = 1, AddedAt = created }],
            });

            if (name == LongName)
            {
                longId = id;
            }
        }

        return longId;
    }

    private static IEnumerable<Scene> Search =>
    [
        new Scene("search-cards", "検索：カードの一覧（作り物の絵・長い名前・絵の無い商品）", async context =>
        {
            await SeedLibraryAsync(context, count: 8);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

            // 取り込みの結果の「取り込んだ商品を検索で開く」と同じ入り方（条件を外して、最近取り込んだ順）。
            // 保存された条件が無い保存先では、最初の条件「お気に入り」が「お気に入りのみ」で始まり、0件になる（2026-09-30 にこの台で見つけた）
            main.Search.ShowRecentlyAddedFirst();
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.Count) == 8, "カードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("search-first-open", "検索：保存された条件が無い保存先で、最初に開いたとき（条件は既定の3つ）", async context =>
        {
            await SeedLibraryAsync(context, count: 8);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("search-broken-zip", "検索：条件「壊れたzip」で絞った結果（取り込みの結果のボタンから来た状態）", async context =>
        {
            await SeedLibraryAsync(context, count: 8);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");

            main.Search.ShowOnlyBrokenZip();
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.Count) == 2, "壊れたzipのある商品だけになる");
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>
    /// 作り物の商品を並べる。3件目と6件目は壊れた zip を持ち、2件目は名前が長く、5件目は絵が無い。
    /// <paramref name="change"/> は、何件目か（0 から）と商品を受けて、場面ごとの値（タグ・属性）を足す
    /// </summary>
    private static async Task SeedLibraryAsync(SceneContext context, int count, Func<int, ItemRecord, ItemRecord>? change = null)
    {
        string[] names =
        [
            "作り物の衣装セット",
            "【12アバター対応】作り物のとても長い名前の衣装セット フルパッケージ版 テクスチャ4K PSD付き",
            "作り物の髪型（壊れたzipあり）",
            "作り物のアクセサリー",
            "絵の無い作り物のテクスチャ",
            "作り物の靴（壊れたzipあり）",
            "作り物のアバター",
            "作り物のギミック",
        ];

        for (var index = 0; index < count; index++)
        {
            var id = (9900301 + index).ToString();
            var broken = index is 2 or 5;
            var file = broken ? Fake.BrokenZip($@"ライブラリ\item{index}.zip") : Fake.Zip($@"ライブラリ\item{index}.zip");
            var at = index;
            await context.Fake.ItemAsync(
                id,
                names[index % names.Length],
                record =>
                {
                    var seeded = record with
                    {
                        Local = record.Local with { LocalFiles = [Fake.FileRecord(file, broken: broken)] },
                    };
                    return change?.Invoke(at, seeded) ?? seeded;
                },
                images: index == 4 ? 0 : 1,
                shop: index % 2 == 0 ? "作り物ショップ" : "作り物の別のショップ");
        }
    }
}
