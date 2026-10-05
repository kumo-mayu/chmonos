using System.Globalization;
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

        // 取り込んでいる最中（担当から上がった11件の ⑪ 2026-10-05）。進み具合は走らせないと出ないので、知らせてくる値を直に入れる
        new Scene("import-running", "取り込んでいる最中：段・件数・棒・中断・残りの見込み・今の商品・ボタンが「今の取り込みに追加」", async context =>
        {
            var main = await context.StartAsync();
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            Backdoor.ShowImportRunning(
                context.Screen<ImportViewModel>(), "5. 商品ページとタグから対応アバターを検出", "見つかった商品を確かめています", 37, 120,
                "作り物の衣装セット【12アバター対応】",
                ("この段の残り 約 3 分", "編集できるまで 約 3 分", "画像を取り終わるまで 約 9 分"));
            main.BoothActivity.ReportWork(WorkSource.Import, "取り込み：対応アバターを検出中", 37, 120);
            await context.SettleAsync();

            return new Shot(root);
        })
        {
            // 上のボタン（今の取り込みに追加・中断）・進み具合の欄の終わり・下の1行を1枚に入れる
            Height = 1000,
        },

        new Scene("import-running-scanning", "取り込んでいる最中：ファイルをスキャン（総数が無いので棒は出ず件数だけ）", async context =>
        {
            var main = await context.StartAsync();
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            Backdoor.ShowImportRunning(
                context.Screen<ImportViewModel>(), "1. ファイルをスキャン", string.Empty, 1834, 0,
                @"D:\作り物\ダウンロード\作り物の髪型_v2.zip");
            main.BoothActivity.ReportWork(WorkSource.Import, "取り込み：ファイルをスキャン中", 1834, 0);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "1. ファイルをスキャン")) };
        }),

        new Scene("import-running-throttled", "取り込んでいる最中：商品ページを取得・BOOTHの指示で間隔を広げている（黄色の帯）", async context =>
        {
            // 帯の秒数は今の間隔から出る。広げた後の値に見えるよう、間隔の設定を長くしておく（減速そのものは作れない）
            var main = await context.StartAsync(settings => settings with { FetchIntervalMs = 6000 });
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            Backdoor.ShowImportRunning(
                context.Screen<ImportViewModel>(), "4. 商品ページを取得", string.Empty, 212, 480,
                "作り物のアクセサリー",
                ("この段の残り 約 7 分", "編集できるまでの時間は、あと 約 7 分 で分かります", "画像の残りは、商品ページを取り終わると分かります"),
                throttled: true);
            main.BoothActivity.ReportWork(WorkSource.Import, "取り込み：商品ページを取得中", 212, 480);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "4. 商品ページを取得")) };
        }),

        // 見つからない登録フォルダの候補（見つからない・移動の点検 10-A）。候補が3つの行と候補が無い行
        new Scene("import-missing-folders", "見つからないファイルを探した結果：見つからない登録フォルダと候補（候補3つ・候補無し）", async context =>
        {
            var main = await context.StartAsync();
            main.ShowImportCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var import = context.Screen<ImportViewModel>();
            Backdoor.ShowMissingSearchText(import, "見つからないファイルはありませんでした。");
            import.ShowMissingFolders(
            [
                new MissingFolder
                {
                    ItemId = "9900701",
                    ItemName = "作り物の衣装セット",
                    Path = @"D:\Booth\extracted\costume_v1.2",
                    FileCount = 42,
                    TotalBytes = 182_400_000,
                    Candidates =
                    [
                        new FolderCandidate(@"E:\Assets\costume\costume_v1.2", 42, 182_400_000, FolderMatchKind.NameAndContents),
                        new FolderCandidate(@"E:\Assets\VeryLongFolderNameWithoutAnySpaces\AnotherVeryLongFolderName\renamed", 42, 182_400_000, FolderMatchKind.Contents),
                        new FolderCandidate(@"F:\old-backup\costume_v1.2", 40, 175_000_000, FolderMatchKind.Name),
                    ],
                },
                new MissingFolder
                {
                    ItemId = "9900702",
                    ItemName = "作り物の髪型",
                    Path = @"D:\Booth\extracted\hair",
                    FileCount = 8,
                    TotalBytes = 12_300_000,
                },
            ]);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, "監視対象")) };
        }),
    ];

    private static IEnumerable<Scene> Item =>
    [
        new Scene("item-files-states", "商品ページの手元のファイル：在る・壊れたzip・見つかりません・取り外しているドライブ・同じ中身が2箇所・確かめられない・古い版", async context =>
        {
            var plain = Fake.Zip(@"ライブラリ\costume_full_v1.2.zip", "costume.unitypackage", "readme.txt");
            // 展開してあるフォルダ。中の unitypackage は行に並び、zip の行と同じ「Unity ▾」が出る（2026-10-05）。もう1つは見つからないフォルダ
            var expanded = Fake.Folder(@"展開物\costume_expanded");
            Fake.PlainFile(@"展開物\costume_expanded\Costume_Base.unitypackage");
            Fake.PlainFile(@"展開物\costume_expanded\Costume_Option.unitypackage");
            var item = await context.Fake.ItemAsync("9900101", "作り物の衣装セット（ファイルの行の確かめ）", record => record with
            {
                Local = record.Local with
                {
                    LocalFolders =
                    [
                        new LocalFolderRecord
                        {
                            Path = expanded, FileCount = 2, TotalBytes = 96_000_000,
                            UnityPackages = ["Costume_Base.unitypackage", "Costume_Option.unitypackage"],
                        },
                        new LocalFolderRecord { Path = Fake.MissingPath(@"展開物\移したフォルダ"), FileCount = 14, TotalBytes = 210_000_000 },
                    ],
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
                        Fake.FileRecord(Fake.Zip(@"鍵のかかったフォルダ\costume_locked.zip"), size: 12_000_000),

                        // 同じ名前の新しい版で上書きされた古い版（点検の8）。1行目と同じ名前で並ぶ
                        new LocalFileRecord
                        {
                            Hash = Fake.Hex("old-version"),
                            Paths = [],
                            SizeBytes = 41_000_000,
                            Replaced = new ReplacedVersion(plain, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(9))),
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
                () => page.LocalFiles.Count(row => row.IsMissing) == 2 && page.LocalFiles.Any(row => row.IsOnDetachedDrive)
                    && page.LocalFolders.Any(row => row.HasUnityPackages) && page.LocalFolders.Any(row => row.IsMissing),
                "「見つかりません」と「取り外しているドライブ」の印が付く");

            // 確かめられない（権限が無い）は、台の上で本物の拒否を作ると後片付けで消せなくなるので、見た答えだけを差し替える（MB-B）。
            // 答えから札を決める所は試験（ItemFileRowTests）が確かめている
            page.LocalFiles.Single(row => row.FileName == "costume_locked.zip").Presence = FilePresence.Unverifiable;
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

        // 対応アバターの多い商品（29体・共通素体2つ・ファイル2つ）。Tab と矢印の確かめ（ViewShot tabs）と、札の並びのフォーカスの枠を見る。
        // 2026-10-01 の点検で、29体の商品は欄を抜けるのに Tab を62回押した
        new Scene("item-page-avatars", "商品ページ：対応アバター29体・共通素体2つ・手元のファイル2つ（Tab の通しの確かめ）", async context =>
        {
            var item = await context.Fake.ItemAsync(
                "9900103",
                "作り物の衣装（対応アバターの多い商品）",
                record => record with
                {
                    Local = record.Local with
                    {
                        LocalFiles =
                        [
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_many_avatars_v1.0.zip", "costume.unitypackage")),
                            Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_many_avatars_texture.zip")),
                        ],
                        Avatars = Enumerable.Range(1, 29)
                            .Select(index => new AvatarLink
                            {
                                AvatarItemId = (9800000 + index).ToString(CultureInfo.InvariantCulture),
                                Name = $"作り物のアバター{index:00}",
                                Source = AvatarLinkSource.SupportSection,
                                Confirmed = true,
                            })
                            .ToList(),
                        AvatarBases =
                        [
                            new AvatarBaseLink { BaseName = "作り物の素体A", Source = AvatarLinkSource.SupportSection, Confirmed = true },
                            new AvatarBaseLink { BaseName = "作り物の素体B", Source = AvatarLinkSource.SupportSection, Confirmed = true },
                        ],
                    },
                },
                images: 2);

            var main = await context.StartAsync();
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
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

        // 公開前の点検 2026-10-01：書き出しの間は保存が止まるのに帯が無く、終わった知らせも設定の画面の外には出なかった
        StoreJobBand("band-backup-running", "下の帯：バックアップの書き出し中（長い作業の帯・進み具合の棒と中止）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.BeginLongJob("バックアップを書き出しています", "書き出しが終わるまで、保存は待たされます。見ることはできます。", new CancellationTokenSource());
            main.ReportLongJob("バックアップを書き出しています… 1,234/5,678", 1_234, 5_678);
            return main.LongJobText;
        }),

        // 件数が分かるまでは流れる棒（一時展開の帯と同じ。2026-10-02）
        StoreJobBand("band-backup-counting", "下の帯：バックアップの書き出し中・件数がまだ分からない（流れる棒）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.BeginLongJob("バックアップを書き出しています", "書き出しが終わるまで、保存は待たされます。見ることはできます。", new CancellationTokenSource());
            main.ReportLongJob("バックアップを書き出しています… 0/0");
            return main.LongJobText;
        }),

        // 書き出せたときだけ「エクスプローラで開く」が出る（ユーザ判断 2026-10-01）
        StoreJobBand("band-backup-done", "下の帯：設定の画面を離れている間に書き出しが終わった知らせ（エクスプローラで開く・設定を開く・×）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.EndStoreJob(new StoreJobOutcome("バックアップに 5,678 ファイル（1.2 GB）を書き出しました。", @"D:\作り物\Chmonos-backup-20261001-1200.zip"));
            return main.StoreJobNoticeText;
        }),

        StoreJobBand("band-backup-failed", "下の帯：設定の画面を離れている間に書き出しが失敗した知らせ（エクスプローラで開くは出ない）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");
            main.EndStoreJob(
                $"バックアップを書き出せませんでした。{Chmonos.Core.Services.FailureText.Cause(new UnauthorizedAccessException())}");
            return main.StoreJobNoticeText;
        }),

        // 戻すにも「中止」を出す（止めると Core が戻す先の展開物を消す。ユーザ判断 2026-10-02）
        StoreJobBand("band-restore-running", "下の帯：バックアップから戻している間（進み具合の棒と中止）", main =>
        {
            main.BeginStoreJob(StoreJobKind.Restore, "バックアップから戻しています…");
            main.BeginLongJob("バックアップから戻しています", "戻し終えるまで、保存は待たされます。終わったら開き直します。", new CancellationTokenSource());
            main.ReportLongJob("バックアップから戻しています… 1,234/5,678", 1_234, 5_678);
            return main.LongJobText;
        }),
    ];

    /// <param name="show">帯を出し、帯の中の文を返す（その文の外の枠で切り出す）。</param>
    private static Scene StoreJobBand(string name, string title, Func<MainViewModel, string> show)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);

            var text = show(main);
            await context.SettleAsync();

            return new Shot(root) { Focus = () => Look.Ancestor<Border>(Look.Text(root, text)), FocusMargin = 24 };
        })
        {
            Height = 600,
        };

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

        // 知らせの置き場の前後（2026-10-04）。同じ幅で2枚を比べ、知らせが出ても下の欄が動かないことを見る（ViewShot diff）
        new Scene("modification-detail-notice-before", "改変の詳細：知らせが出る前（欄の下の知らせの1行は、出ていなくても場所を取っている）", context => ModificationDetailNoticeAsync(context, show: false))
        {
            Height = 1500,
        },
        new Scene("modification-detail-notice-after", "改変の詳細：欄・ボタンの下に知らせが出た後", context => ModificationDetailNoticeAsync(context, show: true))
        {
            Height = 1500,
        },
        new Scene("modification-hub-notice-before", "改変の画面（アバターの見方）：右の「新しい改変」の下に知らせが出る前", context => ModificationHubNoticeAsync(context, show: false)),
        new Scene("modification-hub-notice-after", "改変の画面（アバターの見方）：右の「新しい改変」の下に知らせが出た後", context => ModificationHubNoticeAsync(context, show: true)),

        // 右の欄の上から下までを1枚で見る（blueprint ID の欄の並び・大きさのスライダーの位置）。窓を縦に長くして描く
        new Scene("modification-selected-tall", "改変の画面：改変を選んだ右の欄の全部（縦に長い窓。blueprint ID の欄・大きさのスライダー）", async context =>
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

            return new Shot(root);
        })
        {
            Width = 1280,
            Height = 2000,
        },

        new Scene("modification-selected-status","改変の画面：右の欄の上の帯に知らせの文が出ている所（日付と知らせが下の段へ送られる）・窓の最小の幅", async context =>
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

        // 結果の行は、絵と頭文字と大きな絵の吹き出しを持つ（メモ15-③）。調べる処理は走らせず（zip の中身が要る）、行を直に足す
        new Scene("modification-project-found", "改変の詳細：プロジェクトの中を調べた結果の一覧（絵のある行・長い名前・絵の無い行）", async context =>
        {
            var selected = await SeedModificationsAsync(context);
            var rows = new List<(ItemRecord Item, int Present, int Total)>
            {
                (await context.Fake.ItemAsync("9900301", "作り物の靴セット", images: 1), 12, 12),
                (await context.Fake.ItemAsync("9900302", "作り物の長い名前の商品：冬のコートと帽子とマフラーと手袋とブーツのセット（色違い3種・差分つき）", images: 1), 3, 40),
                (await context.Fake.ItemAsync("9900303", "絵の無い作り物の小物", images: 0), 5, 5),
            };
            var main = await context.StartAsync();
            main.ShowModifications(
                ModificationHubLevel.Modification,
                new ModificationHubSelection(ModificationHubSelectionKind.Modification, selected));
            var root = context.MainWindow();
            await context.PresentAsync(root);

            await SceneContext.UntilAsync(() => Look.View<ModificationView>(root) is not null, "選んだ改変が右の欄に出る");
            var detail = (ModificationViewModel)Look.View<ModificationView>(root)!.DataContext;
            foreach (var (item, present, total) in rows)
            {
                var image = item.Booth.Images.Count > 0
                    ? System.IO.Path.Combine(context.Seed.Paths.ItemImagesDir(item.Id), Chmonos.Core.Images.ImagePipeline.FileNameFor(item.Booth.Images[0].OriginalUrl))
                    : null;
                detail.FoundInProject.Add(new ProjectCandidateRowViewModel
                {
                    ItemId = item.Id, Name = item.DisplayName, Present = present, Total = total,
                    ThumbnailPath = image, Thumbnails = main.Thumbnails,
                });
            }

            Backdoor.ShowProjectFindText(detail, $"このプロジェクトに入っている手元の商品が {rows.Count} 件ありました。この改変に使ったものなら「追加」を押してください。");
            await context.SettleAsync();
            Look.Named<ScrollViewer>(Look.View<ModificationView>(root)!, "Body")!.ScrollToEnd();
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ModificationView>(root), FocusMargin = 0 };
        })
        {
            Width = 900,
            Height = 900,
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

    private static async Task<Shot> ModificationDetailNoticeAsync(SceneContext context, bool show)
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

        if (show)
        {
            var detail = (ModificationViewModel)context.Screen<ModificationHubViewModel>().Detail!;
            detail.NameNotice.Set("同じ名前の改変が既にあります。", true);
            detail.BlueprintNotice.Set("VRChatに着替えを送りました。着替わらないときは、VRChatでOSCが有効かを確かめてください。", false);
            detail.ProjectNotice.Set("「MinatoWinter」をUnityで開いています。少し時間がかかります。", false);
            detail.AddNotice.Set("「作り物の衣装セット」を追加しました。", false);
            detail.MembersNotice.Set("「作り物の衣装セット」を外しました。「戻す」で元に戻せます。", false);
            detail.GalleryNotice.Set("写真を 2 枚貼りました。", false);
            await context.SettleAsync();
        }

        return new Shot(root) { Focus = () => Look.View<ModificationView>(root), FocusMargin = 0 };
    }

    private static async Task<Shot> ModificationHubNoticeAsync(SceneContext context, bool show)
    {
        await SeedModificationsAsync(context);
        var main = await context.StartAsync();
        main.ShowModifications(
            ModificationHubLevel.Avatar,
            new ModificationHubSelection(ModificationHubSelectionKind.Avatar, "9900201"));
        var root = context.MainWindow();
        await context.PresentAsync(root);

        var hub = context.Screen<ModificationHubViewModel>();
        await SceneContext.UntilAsync(() => hub.Detail is HubAvatarDetail, "アバターが右の欄に出る");
        await context.SettleAsync();

        if (show)
        {
            hub.StartCreateCommand.Execute("9900201");
            await context.SettleAsync();
        }

        return new Shot(root) { Focus = () => Look.View<ModificationHubView>(root), FocusMargin = 0 };
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
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == 8, "カードが並ぶ");
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
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == 2, "壊れたzipのある商品だけになる");
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
