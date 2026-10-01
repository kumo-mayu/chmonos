using System.Text;
using System.Windows;
using System.Windows.Controls;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace ViewShot;

/// <summary>
/// 商品ページの、説明の長い商品の場面（本文と見出し21個）。主の窓・フォルダビュー・改変の画面の3か所。
///
/// 説明は画面の外の見出しを後から足す（<c>Controls/ProgressiveItems</c>）。台は落ち着くまで待ってから描くので、
/// ここの場面が見るのは**出揃った後の姿**（最後まで流した所・縦に長い窓・畳んでから開いた所）と、
/// <c>item-page-long-first-frame</c> の**最初の配置だけで描いた1コマ**。
/// </summary>
internal static partial class Scenes
{
    // 縦に長い窓。説明の全部と右の列の全部が、流さずに入る高さ（本文と見出し21個で約4,300px）
    private const double TallHeight = 5200;

    private static IEnumerable<Scene> ItemPages =>
    [
        new Scene("item-page-long", "商品ページ：説明の長い商品（本文と見出し21個）。開いた所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        }),

        new Scene("item-page-long-tall", "商品ページ：説明の長い商品。縦に長い窓で、説明と右の列の全部", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        })
        {
            Height = TallHeight,
        },

        new Scene("item-page-long-end", "商品ページ：説明の長い商品。最後まで流した所（最後の見出しと動画の欄）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);
            return new Shot(root);
        }),

        new Scene("item-page-long-first-frame", "商品ページ：説明の長い商品。最初の配置だけで描いた1コマ（後から足す分を待たない）", context =>
        {
            return FirstFrameAsync(context);

            static async Task<Shot> FirstFrameAsync(SceneContext context)
            {
                var (item, _) = await SeedLongItemAsync(context);
                var main = await context.StartAsync();
                main.ShowItem(item);
                var root = context.MainWindow();
                return new Shot(root) { Still = context.PresentFirstFrame(root) };
            }
        }),

        new Scene("item-page-long-folded", "商品ページ：説明の長い商品。説明の欄を畳んで開いた所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            SectionFolds.DescriptionExpanded = false;
            var root = await OpenItemAsync(context, item);
            return new Shot(root);
        }),

        new Scene("item-page-long-unfolded", "商品ページ：説明の長い商品。説明の欄を畳んで開き、後から欄を開いた所（縦に長い窓）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            SectionFolds.DescriptionExpanded = false;
            var root = await OpenItemAsync(context, item);
            context.Screen<ItemViewModel>().IsDescriptionExpanded = true;
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = TallHeight,
        },

        new Scene("item-page-long-sections-folded", "商品ページ：説明の長い商品。「すべて折りたたむ」を押した所（縦に長い窓）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            context.Screen<ItemViewModel>().ToggleAllSectionsCommand.Execute(null);
            await context.SettleAsync();
            return new Shot(root);
        })
        {
            Height = TallHeight,
        },

        // ---- 主の窓の商品ページの、流した位置（進む＝先頭から・戻る／進む＝離れたときの位置・同じ商品の開き直し＝保つ） ----
        // 下の「戻る」「進む」「開き直す」の4枚は、この item-page-long-scrolled と同じ位置で出るはず。diff で比べる。
        // 画面が使い回される物（back-after-scroll・reopen-after-scroll）も、見本と画素まで同じになる。
        // 前は右の列の下端が1行ぶん（28px）短かった：後から読んで付く「Unity の送り先の行」の出し分けが、付いたことを知らせていなかった（同日に直した）

        new Scene("item-page-long-scrolled", "商品ページ：説明の長い商品を途中まで流した所（位置の場面の見本）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, ScrolledOffset);
            return BodyShot(root);
        }),

        // 説明の長い2つ目の商品を、先頭から開いた所と同じ絵になるはず（item-page-second）
        new Scene("item-page-next-after-scroll", "商品ページ：長い商品を途中まで流してから、別の長い商品を開いた所（先頭から出る）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var second = await SeedSecondLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, ScrolledOffset);

            context.Main.ShowItem(second);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("item-page-second", "商品ページ：説明の長い2つ目の商品を開いた所（item-page-next-after-scroll の見本）", async context =>
        {
            await SeedLongItemAsync(context);
            var second = await SeedSecondLongItemAsync(context);
            var root = await OpenItemAsync(context, second);
            return BodyShot(root);
        }),

        new Scene("item-page-back-after-scroll", "商品ページ：長い商品を流す → 別の商品を開いて流す → 戻った所（離れたときの位置）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var second = await SeedSecondLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, ScrolledOffset);

            context.Main.ShowItem(second);
            await context.SettleAsync();
            await ScrollBodyAsync(context, root, OtherOffset);

            context.Main.GoBack();
            await UntilItemAsync(context, item.Id);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("item-page-back-from-other-screen", "商品ページ：長い商品を流す → 統計の画面へ移る → 戻った所（画面は作り直される）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, ScrolledOffset);

            context.Main.ShowStatsCommand.Execute(null);
            await context.SettleAsync();

            context.Main.GoBack();
            await UntilItemAsync(context, item.Id);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("item-page-forward-after-back", "商品ページ：別の商品 → 長い商品を流す → 戻って流す → 進んだ所（離れたときの位置）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var second = await SeedSecondLongItemAsync(context);
            var root = await OpenItemAsync(context, second);
            context.Main.ShowItem(item);
            await context.SettleAsync();
            await ScrollBodyAsync(context, root, ScrolledOffset);

            context.Main.GoBack();
            await UntilItemAsync(context, second.Id);
            await context.SettleAsync();
            await ScrollBodyAsync(context, root, OtherOffset);

            context.Main.GoForward();
            await UntilItemAsync(context, item.Id);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("item-page-reopen-after-scroll", "商品ページ：長い商品を流してから、同じ商品を開き直した所（位置を保つ）", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, ScrolledOffset);

            // 取り直した後・ファイルを外した後と同じ道
            context.Main.ReplaceItem(item);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        // 説明が短く、右の列が長い商品。右の列の行（zip の中の Unity へ送れる物）は裏で読んで後から届くので、
        // 戻った直後は流せる長さが足りない。届いてから位置が合うことを見る（下の2枚は、どちらも最後のカードの下端まで流れているはず）。
        new Scene("item-page-files-end", "商品ページ：説明が短くファイルの多い商品を、最後まで流した所（item-page-files-back の見本）", async context =>
        {
            var item = await SeedManyFilesItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);
            return BodyShot(root);
        }),

        new Scene("item-page-files-back", "商品ページ：説明が短くファイルの多い商品を最後まで流す → 統計の画面へ移る → 戻った所", async context =>
        {
            var item = await SeedManyFilesItemAsync(context);
            var root = await OpenItemAsync(context, item);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);

            context.Main.ShowStatsCommand.Execute(null);
            await context.SettleAsync();

            context.Main.GoBack();
            await UntilItemAsync(context, item.Id);
            await context.SettleAsync();
            return BodyShot(root);
        }),

        new Scene("folder-item-long-end", "フォルダビューに組み込んだ商品ページ：説明の長い商品を選び、最後まで流した所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenFolderItemAsync(context, item.Id);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),

        new Scene("folder-item-reselect", "フォルダビューに組み込んだ商品ページ：長い商品を途中まで流してから、別の商品を選び直した所", async context =>
        {
            var (item, other) = await SeedLongItemAsync(context);
            var root = await OpenFolderItemAsync(context, item.Id);
            await ScrollBodyAsync(context, root, 600);

            SelectFolderItem(context.Screen<FolderViewModel>(), other.Id);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),

        new Scene("folder-item-long", "フォルダビューに組み込んだ商品ページ：説明の長い商品を選んだ所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenFolderItemAsync(context, item.Id);
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),

        // 上の folder-item-long と同じ絵になるはず（選び直したら先頭へ戻り、説明は最後まで出揃う）。2枚を diff で比べる
        new Scene("folder-item-reselect-long", "フォルダビューに組み込んだ商品ページ：短い商品を最後まで流してから、説明の長い商品を選び直した所", async context =>
        {
            var (item, other) = await SeedLongItemAsync(context);
            var root = await OpenFolderItemAsync(context, other.Id);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);

            SelectFolderItem(context.Screen<FolderViewModel>(), item.Id);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),

        new Scene("modification-item-long-end", "改変の画面に組み込んだ商品ページ：説明の長い商品を選び、最後まで流した所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenHubMemberAsync(context, member: 0);
            await ScrollBodyAsync(context, root, double.PositiveInfinity);
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),

        new Scene("modification-item-reselect", "改変の画面に組み込んだ商品ページ：長い商品を途中まで流してから、使ったものを選び直した所", async context =>
        {
            var (item, _) = await SeedLongItemAsync(context);
            var root = await OpenHubMemberAsync(context, member: 0);
            await ScrollBodyAsync(context, root, 600);

            // 左の一覧の「使ったもの」の行を押したときと同じ命令
            var hub = context.Screen<ModificationHubViewModel>();
            var next = hub.Lines.OfType<HubMemberLine>().Select(line => line.Row).First(row => row.ItemId != item.Id);
            hub.ShowMemberCommand.Execute(next);
            await context.SettleAsync();
            return new Shot(root) { Focus = () => Look.View<ItemView>(root), FocusMargin = 0 };
        }),
    ];

    private static async Task<FrameworkElement> OpenItemAsync(SceneContext context, ItemRecord item)
    {
        var main = await context.StartAsync();
        main.ShowItem(item);
        var root = context.MainWindow();
        await context.PresentAsync(root);
        return root;
    }

    // 位置の場面で流す量。1500 は見出しの途中（本文と最初の数個の見出しが画面の外へ出る）。
    // 600 は別の商品の側で流す量で、1500 と取り違えたら絵で分かるように離してある
    private const double ScrolledOffset = 1500;
    private const double OtherOffset = 600;

    /// <summary>
    /// ページを流す部品（Body）だけを切り出す。上の帯の「← 〇〇に戻る」は来た道で文言が変わるので、
    /// 道の違う場面どうしを比べるときは入れない
    /// </summary>
    private static Shot BodyShot(FrameworkElement root)
        => new(root)
        {
            Focus = () => Look.View<ItemView>(root) is { } page ? Look.Named<ScrollViewer>(page, "Body") : null,
            FocusMargin = 0,
        };

    /// <summary>戻る・進むの開き直しは保存先を読むので待ちが入る。その商品のページに替わるまで待つ。</summary>
    private static Task UntilItemAsync(SceneContext context, string itemId)
        => SceneContext.UntilAsync(
            () => context.Main.CurrentViewModel is ItemViewModel page && page.Item.Id == itemId,
            "戻る・進むで商品ページが開く");

    /// <summary>説明の長い2つ目の商品（見出しは同じ形で、名前と本文の1行目が違う）。主画面を組む前に呼ぶ。</summary>
    private static Task<ItemRecord> SeedSecondLongItemAsync(SceneContext context)
        => context.Fake.ItemAsync(
            "9900404",
            "作り物のコート（説明の長い2つ目の商品）",
            record => record with
            {
                Booth = record.Booth with
                {
                    Description = "2つ目の作り物の商品です。\n" + LongBody("9900403"),
                    H2Sections = LongSections(),
                },
                Local = record.Local with
                {
                    LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\coat_long_v1.0.zip", "coat.unitypackage"))],
                },
            });

    /// <summary>説明が短く、zip を12個持つ商品（zip ごとに Unity へ送れる物が2つ）。右の列が左より長い。主画面を組む前に呼ぶ。</summary>
    private static Task<ItemRecord> SeedManyFilesItemAsync(SceneContext context)
        => context.Fake.ItemAsync(
            "9900405",
            "作り物の小物セット（ファイルの多い商品）",
            record => record with
            {
                Local = record.Local with
                {
                    LocalFiles = Enumerable.Range(1, 12)
                        .Select(index => Fake.FileRecord(Fake.Zip(
                            $@"ライブラリ\props_part{index:00}.zip",
                            $"props_{index:00}_a.unitypackage",
                            $"props_{index:00}_b.unitypackage")))
                        .ToList(),
                },
            });

    /// <summary>フォルダビューを開き、その商品のファイルの行を選ぶ（右に商品ページが組み込まれる）。</summary>
    private static async Task<FrameworkElement> OpenFolderItemAsync(SceneContext context, string itemId)
    {
        var main = await context.StartAsync();
        main.ShowFolders();
        var root = context.MainWindow();
        await context.PresentAsync(root);

        var folders = context.Screen<FolderViewModel>();

        // 畳んだフォルダの中の行は作られない。絞り込むと、当てはまる行まで開いて並ぶ
        folders.Filter = "作り物";
        await SceneContext.UntilAsync(() => folders.Rows.Any(row => row.Entry?.Item?.Id == itemId), "商品のファイルの行が並ぶ");
        SelectFolderItem(folders, itemId);
        await SceneContext.UntilAsync(() => Look.View<ItemView>(root) is not null, "右に商品ページが出る");
        await context.SettleAsync();
        return root;
    }

    private static void SelectFolderItem(FolderViewModel folders, string itemId)
        => folders.Selected = folders.Rows.First(row => row.Entry?.Item?.Id == itemId);

    /// <summary>改変の画面を開き、その改変の「使ったもの」の1件を選ぶ（右に商品ページが組み込まれる）。</summary>
    private static async Task<FrameworkElement> OpenHubMemberAsync(SceneContext context, int member)
    {
        var main = await context.StartAsync();
        main.ShowModifications(
            ModificationHubLevel.Modification,
            new ModificationHubSelection(ModificationHubSelectionKind.Member, LongModificationId, member));
        var root = context.MainWindow();
        await context.PresentAsync(root);
        await SceneContext.UntilAsync(() => Look.View<ItemView>(root) is not null, "右に商品ページが出る");
        await context.SettleAsync();
        return root;
    }

    /// <summary>商品ページを流す（ページを流す部品の名前は Body）。端を越える量を渡すと最後まで。</summary>
    private static async Task ScrollBodyAsync(SceneContext context, FrameworkElement root, double offset)
    {
        var page = Look.View<ItemView>(root) ?? throw new InvalidOperationException("商品ページが出ていません。");
        var body = Look.Named<ScrollViewer>(page, "Body") ?? throw new InvalidOperationException("商品ページに Body がありません。");
        if (double.IsPositiveInfinity(offset))
        {
            // 後から足す分が出揃ってから、端を決める
            await context.SettleAsync();
            body.ScrollToEnd();
        }
        else
        {
            body.ScrollToVerticalOffset(offset);
        }

        await context.SettleAsync();
    }

    /// <summary>
    /// 説明の長い商品（本文9行＋見出し21個）と、短い商品と、その2つを使った改変を1つ。
    /// 見出しの数と長さは、開くのに時間のかかっていた実際の商品の形（見出し21個・約1万字）に寄せた。文は全部作り物
    /// </summary>
    private static async Task<(ItemRecord Long, ItemRecord Short)> SeedLongItemAsync(SceneContext context)
    {
        var avatar = await context.Fake.ItemAsync(LongAvatarId, "作り物のアバター「ミナト」", record => record with
        {
            Booth = record.Booth with { Category = new BoothCategory { Id = 208, Name = "3Dキャラクター", ParentName = "3Dモデル" } },
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\minato_v1.0.zip", "minato.unitypackage"))] },
        });

        var other = await context.Fake.ItemAsync("9900403", "作り物の髪型", record => record with
        {
            Local = record.Local with { LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\hair_v1.0.zip", "hair.unitypackage"))] },
        });

        var item = await context.Fake.ItemAsync(
            "9900402",
            "作り物の衣装セット（説明の長い商品）",
            record => record with
            {
                Booth = record.Booth with
                {
                    Description = LongBody(other.Id),
                    H2Sections = LongSections(),
                    Tags = ["VRChat", "3Dモデル", "衣装", "作り物のタグ", "冬服"],
                    Variations =
                    [
                        new BoothVariation { Id = 1, Name = "フルセット", Price = 1500, Type = "downloadable" },
                        new BoothVariation { Id = 2, Name = "ミナト用", Price = 800, Type = "downloadable" },
                        new BoothVariation { Id = 3, Name = "テクスチャのみ", Price = 300, Type = "downloadable" },
                    ],
                },
                Local = record.Local with
                {
                    LocalFiles = [Fake.FileRecord(Fake.Zip(@"ライブラリ\costume_long_v1.2.zip", "costume.unitypackage"))],
                    UserTags =
                    [
                        new UserTagAssignment { Top = "冬服" },
                        new UserTagAssignment { Top = "お気に入りの作者", Subs = ["コート"] },
                    ],
                    Attributes = new Dictionary<string, int> { ["かわいい"] = 80, ["かっこいい"] = 35 },
                    Memo = "作り物のメモ。袖のボーンは入れ直した。",
                },
            },
            images: 4);

        await context.Seed.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = avatar.Id, DisplayName = "ミナト", BoothName = avatar.Booth.Name }],
        });

        var created = LongModificationCreated;
        await context.Seed.Modifications.SaveAsync(new ModificationRecord
        {
            Id = LongModificationId,
            AvatarItemId = avatar.Id,
            Name = LongModificationName,
            CreatedAt = created,
            UpdatedAt = created,
            Members =
            [
                new ModificationMember { ItemId = item.Id, VariationId = 1, AddedAt = created },
                new ModificationMember { ItemId = other.Id, VariationId = 1, AddedAt = created },
            ],
        });

        return (item, other);
    }

    private const string LongAvatarId = "9900401";
    private const string LongModificationName = "冬の普段着";
    private static readonly DateTimeOffset LongModificationCreated = new(2026, 9, 20, 21, 0, 0, TimeSpan.FromHours(9));

    private static string LongModificationId => ModificationId.For(LongAvatarId, LongModificationName, LongModificationCreated);

    private static string LongBody(string ownedItemId)
    {
        var text = new StringBuilder();
        text.AppendLine("場面を描くための作り物の衣装セットです。本文は9行で、下に見出しが21個続きます。");
        text.AppendLine("冬のコート・帽子・マフラー・手袋・ブーツの5点が入っています。");
        text.AppendLine();
        text.AppendLine("対応アバターは作り物のアバター「ミナト」です。ほかのアバターへは、各自で合わせてください。");

        // 手元にある商品へのリンク（色が変わり、押すとアプリの中で開く）と、外へ出るリンク
        text.AppendLine($"合わせて使える髪型：https://viewshot.booth.pm/items/{ownedItemId}");
        text.AppendLine("導入の手引き：https://viewshot.invalid/guide/costume-long");
        text.AppendLine();
        text.AppendLine("テクスチャは 4K です。PSD は別のバリエーションに入っています。");
        text.Append("不具合を見つけたら、ショップのメッセージから知らせてください。");
        return text.ToString();
    }

    private static List<H2Section> LongSections()
    {
        string[] headings =
        [
            "◆ 内容物", "◆ 対応アバター", "◆ 導入の手順", "◆ 必要なもの", "◆ シェイプキー", "◆ マテリアルとテクスチャ",
            "◆ ポリゴン数", "◆ PhysBone の設定", "◆ 色違い", "◆ 着せ替えの例", "◆ よくある質問", "◆ 既知の不具合",
            "◆ 利用規約", "◆ 禁止していること", "◆ 改変について", "◆ クレジットの書き方", "◆ 連絡先", "◆ 同梱のツール",
            "◆ 動作を確かめた環境", "◆ 更新履歴", "◆ とても長い見出し：冬のコートと帽子とマフラーと手袋とブーツのセットについての補足と注意",
        ];

        var sections = new List<H2Section>();
        for (var index = 0; index < headings.Length; index++)
        {
            var text = new StringBuilder();

            // 行の数を見出しごとに変える（2〜9行）。高さの揃わない行が並ぶ形にする
            var lines = 2 + (index * 5 % 8);
            for (var line = 0; line < lines; line++)
            {
                if (line > 0)
                {
                    text.AppendLine();
                }

                text.Append($"・{headings[index][2..]}の作り物の説明 {line + 1} 行目。");
                if ((index + line) % 3 == 0)
                {
                    text.Append("幅に収まらず折り返す長さの文にして、右の端での折り返しと行の高さが変わっていないことを見られるようにしてあります。");
                }

                if (index % 6 == 1 && line == 1)
                {
                    text.Append($" https://viewshot.invalid/notes/{index}");
                }
            }

            sections.Add(new H2Section { Heading = headings[index], Text = text.ToString() });
        }

        return sections;
    }
}
