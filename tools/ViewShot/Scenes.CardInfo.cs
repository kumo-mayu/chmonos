using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chmonos.App.ViewModels;
using Chmonos.Core.Images;
using Chmonos.Core.Models;

namespace ViewShot;

/// <summary>
/// 検索のカードに出すユーザータグ・属性・払った額・対応の数（2026-10-04 ユーザ判断：案A の札と1行・案C の乗せたときと列）の場面と、
/// その重さを測る場面。作り物の商品 2000 件（ユーザータグ・属性・対応アバター・払った額・容量つき）の上で描く。
/// </summary>
internal static partial class Scenes
{
    private static IEnumerable<Scene> CardInfo =>
    [
        CardInfoScene("card-info", "カードの札：ユーザータグ・属性の札と、払った額・対応の1行（2000件・幅228）"),
        CardInfoScene("card-info-small", "カードの札：カードを小さくしたとき（幅160・札1枚・短い1行）", width: 160),
        CardInfoScene("card-info-peek", "カードの札：名前の欄に乗せたとき、絵の上に重ねる", peek: true),
        CardInfoScene("card-info-peek-small", "カードの札：小さいカードに乗せたとき（入らない属性は「ほか n 件」）", width: 160, peek: true),
        CardInfoScene("card-info-sorted", "カードの札：属性「質感」で並べたとき、質感を先に出す", sortAttribute: "質感"),
        CardInfoScene("card-info-chosen", "カードの札：設定で「軽さ」「かわいい」を選んだとき", chosen: ["軽さ", "かわいい"]),
        CardInfoScene("card-info-list", "カードの札：リストの列（ユーザータグ・属性・払った額・対応）", list: true),
        CardInfoSettings(),
        CardInfoPerf(),
    ];

    private static Scene CardInfoScene(
        string name, string title, double? width = null, bool peek = false, bool list = false,
        string? sortAttribute = null, IReadOnlyList<string>? chosen = null)
        => new(name, title, async context =>
        {
            await SeedCardInfoLibraryAsync(context, 2000);
            var main = await context.StartAsync(settings => settings with
            {
                CardWidth = width is { } w ? (int)w : settings.CardWidth,
                CardAttributes = chosen ?? [],
            });
            var root = context.MainWindow();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 2000, "商品を読み終える");

            if (sortAttribute is not null)
            {
                main.Search.SortField = main.Search.SortFields.First(field => field.AttributeName == sortAttribute);
            }

            main.Search.IsListMode = list;
            await context.SettleAsync();

            if (peek)
            {
                // 2枚目（ユーザータグ・属性・額・対応がそろう商品）の名前の欄に乗せた姿
                var card = main.Search.Rows.SelectMany(row => row.Cards.OfType<ItemCardViewModel>())
                    .Skip(1).First(candidate => candidate.Item.Local.Attributes.Count >= 3 && candidate.Item.Local.UserTags.Count > 0);
                card.SetPointerOnText(true);
                await context.SettleAsync();
            }

            return new Shot(root);
        });

    /// <summary>設定の「一覧と検索」の「カードに表示する属性」（2つ選んだ姿）。</summary>
    private static Scene CardInfoSettings()
        => new("card-info-settings", "カードの札：設定の「カードに表示する属性」", async context =>
        {
            await SeedCardInfoLibraryAsync(context, 20);
            var main = await context.StartAsync(settings => settings with { CardAttributes = ["質感", "かわいい"] });
            main.ShowSettingsCommand.Execute(null);
            var root = context.MainWindow();
            await context.PresentAsync(root);
            return new Shot(root)
            {
                Focus = () => Look.Text(root, "カードに表示する属性") is { } label
                    ? (FrameworkElement?)Look.Ancestor<StackPanel>(System.Windows.Media.VisualTreeHelper.GetParent(label)) ?? label
                    : null,
                FocusMargin = 60,
            };
        });

    /// <summary>
    /// 重さを測る（描いた絵は見ない）。開く・流す・リストとの切り替えの時間と、メモリを標準出力に書く。
    /// 直す前の版にもこのファイルだけを入れて同じ物を測る（場面は新しい部品に触らない）
    /// </summary>
    private static Scene CardInfoPerf()
        => new("card-info-perf", "カードの札：重さを測る（2000件・数字を書くだけ）", async context =>
        {
            await SeedCardInfoLibraryAsync(context, 2000);
            var main = await context.StartAsync();
            var root = context.MainWindow();

            var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
            var open = Stopwatch.StartNew();
            await context.PresentAsync(root);
            await SceneContext.UntilAsync(() => main.Search.TotalCount == 2000, "商品を読み終える");
            await IdleAsync();
            open.Stop();

            // 2回目からの開き直し（カードとリストの切り替え。カードの部品だけを作り直す）
            var reopen = new List<double>();
            for (var round = 0; round < 3; round++)
            {
                main.Search.IsListMode = true;
                await IdleAsync();
                var watch = Stopwatch.StartNew();
                main.Search.IsListMode = false;
                await IdleAsync();
                reopen.Add(watch.Elapsed.TotalMilliseconds);
            }

            var scroller = Look.All<ScrollViewer>(root).Where(viewer => viewer.IsVisible).MaxBy(viewer => viewer.ScrollableHeight)
                           ?? throw new InvalidOperationException("カードの一覧の送り所が見つかりません。");

            // 同じ歩みで流す（perf-measure スキル：毎回同じ回数・同じ間隔）。1歩ごとに画面のスレッドが空くまでの時間を測る
            var steps = new List<double>();
            for (var i = 0; i < 60; i++)
            {
                var watch = Stopwatch.StartNew();
                scroller.ScrollToVerticalOffset(scroller.VerticalOffset + 350);
                await IdleAsync();
                steps.Add(watch.Elapsed.TotalMilliseconds);
            }

            var managed = GC.GetTotalMemory(forceFullCollection: true);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            Console.WriteLine(
                $"PERF open={open.Elapsed.TotalMilliseconds:F0}ms reopen={string.Join("/", reopen.Select(ms => ms.ToString("F0")))}ms "
                + $"scrollTotal={steps.Sum():F0}ms scrollMax={steps.Max():F0}ms scrollOver33={steps.Count(ms => ms > 33)} "
                + $"managed={(managed - memoryBefore) / 1048576.0:F1}MB private={process.PrivateMemorySize64 / 1048576.0:F0}MB "
                + $"workingSet={process.WorkingSet64 / 1048576.0:F0}MB");

            return new Shot(root);
        }) { Height = 800 };

    /// <summary>画面のスレッドに溜まった仕事（並べ・結び付け・カードの中身を4枚ずつ作る仕事）が全部済むまで待つ。</summary>
    private static async Task IdleAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>作り物の商品を並べる（名前・ショップ・タグ・属性は全部作り物）。</summary>
    private static async Task SeedCardInfoLibraryAsync(SceneContext context, int count)
    {
        string[] adjectives = ["ふわふわ", "シンプル", "ゴシック", "夏色", "ストリート", "和風", "メカ", "レース", "冬の", "パステル"];
        string[] kinds = ["ワンピース", "パーカー", "髪型", "ブーツ", "リボン", "シェーダー", "表情セット", "ネイル", "水着", "制服"];
        string[] shops = ["作り物工房 甲", "作り物工房 乙", "作り物工房 丙", "作り物工房 丁", "作り物の店 戊", "作り物の店 己", "作り物の店 庚"];
        (string Top, string[] Subs)[] tags =
        [
            ("衣装", ["夏", "冬", "制服", "ドレス"]), ("髪", ["ロング", "ショート"]), ("アクセサリ", ["頭", "首", "手"]),
            ("靴", []), ("小物", ["配信", "撮影"]), ("ギミック", []), ("シェーダー", []), ("改変待ち", []),
        ];
        string[] attributes = ["かわいい", "かっこいい", "質感", "よく使う", "軽さ", "改変のしやすさ"];

        await context.Seed.UserTags.SaveAsync(new UserTagMaster
        {
            Tops = tags.Select(tag => new UserTagTop { Name = tag.Top, Subs = tag.Subs.Select(sub => new UserTagSub { Name = sub }).ToList() }).ToList(),
        });
        await context.Seed.Attributes.SaveAsync(new AttributeMaster { Attributes = attributes.Select(name => new AttributeDefinition { Name = name }).ToList() });

        // 絵は 40 枚を作って写す（2000 枚を1枚ずつ圧縮すると遅い）
        var imageCache = Path.Combine(Isolation.FilesRoot, "card-info-images");
        for (var k = 0; k < 40; k++)
        {
            if (!File.Exists(Path.Combine(imageCache, $"{k}.webp")))
            {
                Fake.Image(imageCache, $"{k}.webp", seed: "cardinfo" + k);
            }
        }

        var file = Fake.PlainFile(@"ライブラリ\card-info.zip");
        var rng = new Random(20261004);
        var day = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));
        for (var i = 0; i < count; i++)
        {
            var id = (99500001 + i).ToString();
            var name = $"{adjectives[rng.Next(adjectives.Length)]}{kinds[rng.Next(kinds.Length)]} #{i + 1:D4}";
            var owned = rng.Next(100) >= 8;
            var edited = rng.Next(100) >= 12;
            var assigned = !edited
                ? new List<UserTagAssignment>()
                : tags.OrderBy(_ => rng.Next()).Take(rng.Next(1, 5))
                    .Select(tag => new UserTagAssignment
                    {
                        Top = tag.Top,
                        Subs = tag.Subs.Length == 0 ? [] : tag.Subs.OrderBy(_ => rng.Next()).Take(rng.Next(0, 2)).ToList(),
                    }).ToList();
            var rated = !edited
                ? new Dictionary<string, int>()
                : attributes.Where(_ => rng.Next(100) < 70).ToDictionary(attribute => attribute, _ => rng.Next(0, 101));
            var avatarCount = rng.Next(100) < 20 ? 0 : rng.Next(1, 40);
            var price = rng.Next(100) < 15 ? 0 : 500 * rng.Next(1, 10);
            var size = (long)rng.Next(2, 900) * 1_000_000;

            var url = $"https://viewshot.invalid/{id}/1.png";
            await context.Fake.ItemAsync(
                id,
                name,
                record => record with
                {
                    Booth = record.Booth with { Images = [new BoothImage { OriginalUrl = url }] },
                    Local = record.Local with
                    {
                        AcquiredAt = DateOnly.FromDateTime(day.AddDays(-i).Date),
                        LocalFiles = owned ? [Fake.FileRecord(file, size: size)] : [],
                        UserTags = assigned,
                        Attributes = rated,
                        IsFavorite = rng.Next(100) < 10,
                        Avatars = Enumerable.Range(0, avatarCount)
                            .Select(n => new AvatarLink { AvatarItemId = (99400001 + n).ToString(), Name = $"作り物アバター{n + 1}", Source = AvatarLinkSource.SupportSection })
                            .ToList(),
                        Purchases = rng.Next(100) < 5
                            ? [new Purchase { VariationId = 1, Price = price, Kind = PurchaseKind.Received }]
                            : [new Purchase { VariationId = 1, Price = price }],
                    },
                },
                images: 0,
                shop: shops[i % shops.Length]);

            // 絵は作り物と同じ場所・名前に写す（images: 0 で Fake には描かせない）
            var directory = context.Seed.Paths.ItemImagesDir(id);
            Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(imageCache, $"{i % 40}.webp"), Path.Combine(directory, ImagePipeline.FileNameFor(url)), overwrite: true);
        }
    }
}
