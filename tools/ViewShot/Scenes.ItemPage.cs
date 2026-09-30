using System.Text;
using System.Windows;
using System.Windows.Controls;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.App.Views;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

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
