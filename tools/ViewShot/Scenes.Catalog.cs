using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 網羅のために足した場面（2026-10-05。表は <c>docs/dev/ui-shots.md</c>）。
/// 今までの場面は「直す所を見る」ために足してきたので、直したことの無い部品と状態が抜けていた：
/// 検索の条件の種類の大半・小窓の大半・BOOTHに無い商品などの商品ページ・何も当たらない検索。
/// まとめて撮る口（<c>ViewShot catalog</c>）が全部を撮るので、ここに足した物も、足した後は毎回撮られる。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> CatalogScenes =>
    [
        .. CatalogModules,
        .. CatalogDialogs,
        .. CatalogScreens,
        .. CatalogMoreScreens,
        .. CatalogNotices,
    ];

    // ---- 検索の条件（モジュール）：種類ごとに、足した直後の姿 ----

    /// <summary>
    /// 種類の一覧から作るので、種類を足すと場面も増える（足した種類を撮り忘れない）。
    /// 足した直後の姿は種類ごとに部品の型（選ぶ・一覧・範囲・日付・属性・タグ・最近・編集状況）が違い、
    /// 一覧の型でも候補の出方が種類ごとに違う（ショップ・アバター・改変・パス）ので、全部を1枚ずつ撮る
    /// </summary>
    private static IEnumerable<Scene> CatalogModules =>
        Enum.GetValues<SearchModuleKind>().SelectMany(kind => (NoSetState.Contains(kind) ? new[] { false } : [false, true]).Select(set => new Scene(
            $"catalog-module-{Kebab(kind.ToString())}{(set ? "-set" : string.Empty)}",
            set
                ? $"検索の条件「{kind}」：値を入れた姿（一覧は候補の先頭2つ・範囲は下と上・日付は始めと終わり・選ぶ物は2つ目）"
                : $"検索の条件「{kind}」：足した直後の姿（作り物の8件。タグ・ユーザータグ・属性・対応アバター・お気に入り・贈り物・無料を散らす）",
            async context =>
            {
                var (search, root) = await StartCatalogFiltersAsync(context);
                var module = AddModule(search, kind);
                if (set)
                {
                    await context.SettleAsync();
                    SetSomeValue(module);
                }

                await context.SettleAsync();
                return FiltersShot(root);
            })));

    /// <summary>
    /// 値を入れた姿を撮らない種類。改変・Unityプロジェクトは作り物の保存先に選べる物が無く、足した直後と同じ絵になる
    /// （値を入れた姿は改変の画面の場面で見る）。編集状況は値を入れた姿の場面が別にある（search-edit-status）
    /// </summary>
    private static readonly HashSet<SearchModuleKind> NoSetState =
        [SearchModuleKind.Modification, SearchModuleKind.UnityProject, SearchModuleKind.Unedited];

    /// <summary>
    /// 部品の型ごとに、見た目が変わる値を1つ入れる（札が積まれる・範囲が埋まる・件数が変わる）。
    /// 値は人が選ぶのと同じ口で入れる（候補の文字・選ぶ物の2つ目）
    /// </summary>
    private static void SetSomeValue(SearchModule module)
    {
        switch (module)
        {
            case ListModule { Kind: SearchModuleKind.Avatar } avatar:
                // アバターの候補は群に分けた別の一覧で出す（Suggestions は空のまま）ので、鍵（商品ID）で積む
                avatar.AddKey("9900901");
                break;
            case ListModule list:
                foreach (var text in list.Suggestions.Take(2).ToList())
                {
                    list.Add(text);
                }

                break;
            case UserTagModule tags:
                tags.AddTop("衣装");
                break;
            case AttributeModule attributes:
                attributes.AddRow("かわいい", 30, 80);
                break;
            case RangeModule range:
                range.MinText = "40";
                range.MaxText = "200";
                break;
            case DateModule date:
                date.SinceDate = new DateTime(2026, 6, 1);
                date.TillDate = new DateTime(2026, 9, 1);
                break;
            case ChoiceModule choice when choice.Options.Count > 1:
                choice.Selected = choice.Options[1];
                break;
            case RecentModule recent when recent.Options.Count > 1:
                recent.Selected = recent.Options[1];
                break;
            case UneditedModule unedited:
                unedited.Fields.First().IsOn = true;
                break;
        }
    }

    /// <summary>条件の部品が候補を出せるように、どの種類にも当たる値を散らした8件で検索を開き、既定の条件を外す。</summary>
    private static async Task<(SearchViewModel Search, FrameworkElement Root)> StartCatalogFiltersAsync(SceneContext context)
    {
        await context.Seed.UserTags.SaveAsync(new UserTagMaster
        {
            Tops =
            [
                new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "夏" }, new UserTagSub { Name = "制服" }] },
                new UserTagTop { Name = "髪" },
                new UserTagTop { Name = "改変待ち" },
            ],
        });
        await context.Seed.Attributes.SaveAsync(new AttributeMaster
        {
            Attributes = [new AttributeDefinition { Name = "かわいい" }, new AttributeDefinition { Name = "軽さ" }],
        });

        // 対応アバターの条件の候補は、アバターの一覧に載ったアバターから出る
        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9900901", DisplayName = "作り物アバター甲", BoothName = "作り物アバター甲" },
                new AvatarRegistryEntry { ItemId = "9900902", DisplayName = "作り物アバター乙", BoothName = "作り物アバター乙" },
            ],
        });

        await SeedLibraryAsync(context, count: 8, (index, item) => item with
        {
            Booth = item.Booth with
            {
                Tags = index % 2 == 0 ? ["衣装", "夏"] : ["小物"],
                WishListsCount = index * 40,
                IsAdult = index == 6,
                IsEndOfSale = index == 7,
                Variations = [new BoothVariation { Id = 1, Name = "フルセット", Price = index == 3 ? 0 : 1000 + (index * 250), Type = "downloadable" }],
            },
            Local = item.Local with
            {
                IsFavorite = index % 3 == 0,
                UserTags = index % 2 == 0
                    ? [new UserTagAssignment { Top = "衣装", Subs = ["夏"] }]
                    : [new UserTagAssignment { Top = "髪" }],
                Attributes = index % 2 == 0
                    ? new Dictionary<string, int> { ["かわいい"] = 20 + (index * 10), ["軽さ"] = 70 }
                    : new Dictionary<string, int>(),
                Avatars = index < 4
                    ? [new AvatarLink { AvatarItemId = "9900901", Name = "作り物アバター甲", Source = AvatarLinkSource.SupportSection }]
                    : [],
                Purchases = [new Purchase { VariationId = 1, Price = index == 3 ? 0 : 1000, Kind = index == 1 ? PurchaseKind.Received : default }],
                AcquiredAt = new DateOnly(2026, 9, 1).AddDays(-index * 20),
            },
        });

        var main = await context.StartAsync();
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");
        foreach (var module in main.Search.Modules.ToList())
        {
            module.RemoveCommand!.Execute(null);
        }

        return (main.Search, root);
    }

    private static string Kebab(string pascal)
    {
        var text = new StringBuilder();
        foreach (var letter in pascal)
        {
            if (char.IsUpper(letter) && text.Length > 0)
            {
                text.Append('-');
            }

            text.Append(char.ToLowerInvariant(letter));
        }

        return text.ToString();
    }

    // ---- 小窓 ----

    /// <summary>
    /// 小窓は作るときに持ち主を主の窓にする（<c>Owner = Application.Current.MainWindow</c>）。台には主の窓が無く、
    /// 最初に作った窓（＝その小窓）が主の窓になるので「自分を自分の持ち主にする」で落ちていた（属性の統合・小分類の移動が描けなかった理由）。
    /// 出さない窓を先に1つ作って主の窓にしておく。持ち主にできるのは窓口（HWND）を持つ窓だけなので、出さずに窓口だけ作る
    /// </summary>
    private static void PrepareDialogOwner()
    {
        if (Application.Current.MainWindow is { } current && new WindowInteropHelper(current).Handle != IntPtr.Zero)
        {
            return;
        }

        var owner = new Window { ShowInTaskbar = false, WindowStyle = WindowStyle.None, Width = 1, Height = 1 };
        new WindowInteropHelper(owner).EnsureHandle();
        Application.Current.MainWindow = owner;
    }

    private static Scene DialogScene(string name, string title, Func<SceneContext, Task<Window>> build)
        => new(name, title, async context =>
        {
            PrepareDialogOwner();
            var window = await build(context);
            return new Shot(SceneContext.Unwrap(window));
        })
        {
            Width = null,
            Height = null,
        };

    private static Window NewChoice(string title, string question, string detail, string first, string second, string? third)
    {
        // 窓を出す口（Ask）は ShowDialog まで進むので、作るところだけを呼ぶ
        var constructor = typeof(ChoiceDialog).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string)])
            ?? throw new InvalidOperationException("ChoiceDialog の作り方が変わりました。");
        return (Window)constructor.Invoke([title, question, detail, first, second, third]);
    }

    private static IEnumerable<Scene> CatalogDialogs =>
    [
        DialogScene("catalog-dialog-choice-two", "2択の窓：2つの選択肢とキャンセル・説明の文あり", _ => Task.FromResult(NewChoice(
            "取り込み方を選ぶ",
            "このフォルダの中身をどう取り込みますか？",
            "zip を展開したフォルダが 3 つあります。元の zip も手元にあります。",
            "元の zip だけ取り込む",
            "フォルダも取り込む",
            third: null))),

        DialogScene("catalog-dialog-choice-three", "3択の窓：3つの選択肢とキャンセル（幅の広い形）・説明の文なし", _ => Task.FromResult(NewChoice(
            "どちらを残しますか",
            "同じ中身のファイルが 2 か所にあります。",
            string.Empty,
            "新しい方を残す",
            "古い方を残す",
            "両方とも残す"))),

        DialogScene("catalog-dialog-rename-tag", "分類の名前を変える窓：まだ入力していない（統合先の候補3つ）", _ => Task.FromResult<Window>(
            new RenameTagDialog(new RenameTagDialogViewModel("大分類", "かわいい", 128, ["可愛い", "カワイイ", "きれい"])))),

        DialogScene("catalog-dialog-rename-tag-merge", "分類の名前を変える窓：今ある名前を選んで統合になる所", _ =>
        {
            var model = new RenameTagDialogViewModel("小分類", "ふわふわ", 12, ["もこもこ", "ゆるい"]);
            model.PickCommand.Execute("もこもこ");
            return Task.FromResult<Window>(new RenameTagDialog(model));
        }),

        DialogScene("catalog-dialog-merge-attribute", "属性の統合の窓：両方に値が入った商品があり、どちらを残すかを聞く", _ => Task.FromResult<Window>(
            new MergeAttributeDialog(new MergeAttributeDialogViewModel("かわいい", "可愛い", new AttributeMergePreview { ItemCount = 24, Conflicts = 3 })))),

        DialogScene("catalog-dialog-merge-attribute-plain", "属性の統合の窓：値がぶつからない（聞く欄が出ない）", _ => Task.FromResult<Window>(
            new MergeAttributeDialog(new MergeAttributeDialogViewModel("軽さ", "軽量", new AttributeMergePreview { ItemCount = 5, Conflicts = 0 })))),

        DialogScene("catalog-dialog-move-sub", "小分類の移動の窓：移動先を選んだ所（下見の文と、元の大分類をどうするか）", async context =>
        {
            await context.Seed.UserTags.SaveAsync(new UserTagMaster
            {
                Tops =
                [
                    new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "夏" }] },
                    new UserTagTop { Name = "季節" },
                    new UserTagTop { Name = "髪" },
                ],
            });
            await context.StartAsync();
            var model = new MoveSubDialogViewModel(context.Services.UserTags, "衣装", "夏", ["季節", "髪"]);
            model.PickTargetCommand.Execute("季節");
            await SceneContext.UntilAsync(() => model.HasTarget, "移動先が決まる");
            return new MoveSubDialog(model);
        }),

        DialogScene("catalog-dialog-move-sub-none", "小分類の移動の窓：移動先をまだ選んでいない（押せないボタンと理由の1行）", async context =>
        {
            await context.Seed.UserTags.SaveAsync(new UserTagMaster
            {
                Tops = [new UserTagTop { Name = "衣装", Subs = [new UserTagSub { Name = "夏" }] }, new UserTagTop { Name = "季節" }],
            });
            await context.StartAsync();
            return new MoveSubDialog(new MoveSubDialogViewModel(context.Services.UserTags, "衣装", "夏", ["季節"]));
        }),

        DialogScene("catalog-dialog-saved-search-save", "検索の保存の窓：今の検索を保存（名前の初めの値あり）", _ => Task.FromResult<Window>(
            new SavedSearchNameDialog(new SavedSearchNameDialogViewModel(SavedSearchNameDialogViewModel.Purpose.Save, "夏の衣装", ["お気に入りの髪"])))),

        DialogScene("catalog-dialog-saved-search-taken", "検索の保存の窓：今ある名前と同じ（欄の下の「既にあります」と押せない保存）", _ => Task.FromResult<Window>(
            new SavedSearchNameDialog(new SavedSearchNameDialogViewModel(SavedSearchNameDialogViewModel.Purpose.Save, "お気に入りの髪", ["お気に入りの髪"])))),

        DialogScene("catalog-dialog-saved-search-rename", "検索の名前を変える窓（戻せることの1行）", _ => Task.FromResult<Window>(
            new SavedSearchNameDialog(new SavedSearchNameDialogViewModel(SavedSearchNameDialogViewModel.Purpose.Rename, "夏の衣装", [])))),

        DialogScene("catalog-dialog-change-item-id", "商品IDを変える窓：移し先のIDを打った所（確かめる前。BOOTHへは問い合わせない）", async context =>
        {
            await context.StartAsync();
            var model = new ChangeItemIdDialogViewModel(context.Services, "9900501", "作り物の衣装セット") { IdInput = "9900502" };
            return new ChangeItemIdDialog(model);
        }),

        DialogScene("catalog-dialog-pick-packages", "送る unitypackage を選ぶ窓：2つある商品1件（記録する文あり）", _ =>
        {
            // 選ぶ欄は、在る zip の中を数えて作る（記録の上の中身だけでは作らない）ので、本当に zip を置く
            var item = FileItem("9900401", "作り物の衣装セット", Fake.FileRecord(
                Fake.Zip(@"ライブラリ\pick_packages.zip", "Costume/Costume_Body.unitypackage", "Costume/Costume_Option.unitypackage")));
            var section = PackageChoiceSection.Build(item)
                ?? throw new InvalidOperationException("作り物の商品から選ぶ欄が作れませんでした（unitypackage が2つ無い）。");
            var model = new PickPackagesDialogViewModel("Unityへ送る", "Unityの「作り物のプロジェクト」へ送ります。", [section], othersCount: 2, records: true);
            return Task.FromResult<Window>(new PickPackagesDialog(model));
        }),
    ];

    // ---- 画面の状態 ----

    private static IEnumerable<Scene> CatalogScreens =>
    [
        new Scene("catalog-search-no-hits", "検索：探す語に何も当たらない（0件の表示）", async context =>
        {
            await SeedLibraryAsync(context, count: 8);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 8, "商品を読み終える");
            main.Search.ShowRecentlyAddedFirst();
            main.Search.QueryText = "当たらない語ぞぞぞ";
            await SceneContext.UntilAsync(() => !main.Search.Rows.Any(row => row.Cards.OfType<ItemCardViewModel>().Any()), "カードが消える");
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("catalog-search-one", "検索：1件だけの保存先（カード1枚と件数の表示）", async context =>
        {
            await SeedLibraryAsync(context, count: 1);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 1, "商品を読み終える");
            main.Search.ShowRecentlyAddedFirst();
            await SceneContext.UntilAsync(() => main.Search.Rows.Sum(row => row.Cards.OfType<ItemCardViewModel>().Count()) == 1, "カードが並ぶ");
            await context.SettleAsync();
            return new Shot(root);
        }),

        CatalogItemScene("catalog-item-local-only", "商品ページ：BOOTHに無い商品（仮のIDで登録・絵は添えた1枚）", async context =>
            await context.Fake.ItemAsync(LocalItemId.For("0123456789abcdef"), "作り物の手元だけの衣装", record => record with
            {
                Booth = record.Booth with { Url = string.Empty, Tags = [], Shop = null },
                Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\local_only.zip"))] },
            })),

        CatalogItemScene("catalog-item-no-images", "商品ページ：絵が1枚も無い商品", async context =>
            await context.Fake.ItemAsync("9900511", "絵の無い作り物の衣装", record => record with
            {
                Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\no_images.zip"))] },
            }, images: 0)),

        CatalogItemScene("catalog-item-delisted", "商品ページ：BOOTHから消えた商品（取り直しで見つからなかった）", async context =>
            await context.Fake.ItemAsync("9900512", "作り物の消えた衣装", record => record with
            {
                Local = record.Local with
                {
                    IsDelisted = true,
                    ConsecutiveNotFoundCount = 3,
                    LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\delisted.zip"))],
                },
            })),

        CatalogItemScene("catalog-item-hidden", "商品ページ：隠した商品（お気に入り・メモあり）", async context =>
            await context.Fake.ItemAsync("9900513", "作り物の隠した衣装", record => record with
            {
                Local = record.Local with
                {
                    IsHidden = true,
                    IsFavorite = true,
                    Memo = "作り物のメモ。改行を含む\n2行目のメモ。",
                    LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\hidden.zip"))],
                },
            })),

        CatalogItemScene("catalog-item-not-owned", "商品ページ：手元のファイルが無い商品（持っていない）", async context =>
            await context.Fake.ItemAsync("9900514", "作り物の持っていない衣装", images: 2)),

        new Scene("catalog-nav-badges", "ナビ：未確定・未編集・通知の数の札が全部出た所", async context =>
        {
            await context.Seed.Unresolved.SaveAsync(
            [
                Fake.Unresolved(Fake.Zip(@"ダウンロード\catalog_a.zip", "a.unitypackage"), contents: ["a.unitypackage"]),
                Fake.Unresolved(Fake.Zip(@"ダウンロード\catalog_b.zip", "b.unitypackage"), contents: ["b.unitypackage"]),
            ]);
            await SeedLibraryAsync(context, count: 3);
            await context.Seed.Notifications.SaveAsync(
            [
                new NotificationRecord
                {
                    Id = "c1",
                    Kind = NotificationKind.ItemUpdated,
                    Title = "作り物の更新",
                    Detail = string.Empty,
                    ItemId = "9900301",
                    CreatedAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9)),
                },
            ]);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 3, "商品を読み終える");
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.Named<FrameworkElement>(root, "NavRail"), FocusMargin = 0 };
        }) { Height = 700 },
    ];

    private static IEnumerable<Scene> CatalogMoreScreens =>
    [
        new Scene("catalog-search-first-frame", "検索：最初の配置だけで描いた1コマ（読み込み中。カードが並ぶ前）", context =>
        {
            return FirstFrameAsync(context);

            static async Task<Shot> FirstFrameAsync(SceneContext context)
            {
                await SeedLibraryAsync(context, count: 8);
                await context.StartAsync();
                var root = context.MainWindow();
                return new Shot(root) { Still = context.PresentFirstFrame(root) };
            }
        }),

        new Scene("catalog-edit-empty", "編集：編集する商品が無い保存先", async context =>
        {
            var main = await context.StartAsync();
            main.ShowEditCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("catalog-edit-start", "編集：未編集の商品が3件ある保存先で開いた所", async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            var main = await context.StartAsync();
            main.ShowEditCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();
            return new Shot(root);
        }),

        new Scene("catalog-stats-many", "統計：60件・12店（よく買っているショップの行が多い・内訳の行が多い）", async context =>
        {
            await SeedLibraryAsync(context, count: 60, (index, item) => item with
            {
                Booth = item.Booth with
                {
                    Shop = new BoothShop { Name = $"作り物の店 その{(index % 12) + 1}", Subdomain = $"viewshot-many-{index % 12}" },
                    Variations = [new BoothVariation { Id = 1, Name = "フルセット", Price = 500 + (index * 37 % 4000), Type = "downloadable" }],
                },
                Local = item.Local with
                {
                    Purchases = [new Purchase { VariationId = 1, Price = 500 + (index * 37 % 4000) }],
                    AcquiredAt = new DateOnly(2026, 9, 1).AddDays(-index * 9),
                },
            });
            var main = await context.StartAsync();
            main.ShowStats();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await context.SettleAsync();
            return new Shot(root);
        }),
    ];

    /// <summary>主の窓の知らせの帯（ほかの画面で済んだことを知らせる）。検索の画面の上に出す。</summary>
    private static IEnumerable<Scene> CatalogNotices =>
    [
        CatalogNoticeScene("catalog-notice-hidden", "知らせの帯：商品を隠した（元に戻す）", (main, item) => main.NoteHidden(item)),
        CatalogNoticeScene("catalog-notice-changed-away", "知らせの帯：離れた商品ページの操作が済んだ（開く）",
            (main, item) => main.NoteItemChangedAway(item, $"「{item.DisplayName}」の商品情報を取り直しました。")),
        CatalogNoticeScene("catalog-notice-import-not-started", "知らせの帯：取り込みを始められなかった",
            (main, _) => main.NoteImportNotStarted("ほかの長い作業（対応アバターの検出）が終わってから取り込みます。")),
        CatalogNoticeScene("catalog-notice-folder-removed", "知らせの帯：監視をやめた（元に戻す）",
            (main, _) => main.NoteFolderRemoved(@"「D:\作り物\ダウンロード」の監視をやめました。", "元に戻す", () => Task.CompletedTask)),
    ];

    private static Scene CatalogNoticeScene(string name, string title, Action<MainViewModel, ItemRecord> note)
        => new(name, title, async context =>
        {
            await SeedLibraryAsync(context, count: 3);
            var main = await context.StartAsync();
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 3, "商品を読み終える");
            var item = await context.Seed.Items.LoadAsync("9900301")
                ?? throw new InvalidOperationException("作り物の商品が読めません。");
            note(main, item);
            await context.SettleAsync();
            return new Shot(root);
        });

    private static Scene CatalogItemScene(string name, string title, Func<SceneContext, Task<ItemRecord>> seed)
        => new(name, title, async context =>
        {
            var item = await seed(context);
            var main = await context.StartAsync();
            main.ShowItem(item);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root);
        });
}
