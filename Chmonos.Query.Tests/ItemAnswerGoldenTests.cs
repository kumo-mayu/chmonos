using System.Runtime.CompilerServices;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Query.Tests;

/// <summary>
/// 答えの JSON を、期待する答え（<c>Golden/*.json</c>）と比べる（2026-10-12・答えの形は外の道具との約束なので、知らないうちに変えない）。
/// 形を変えたときは、環境変数 <c>CHMONOS_UPDATE_GOLDEN=1</c> で走らせて書き直し、差分を読んでから入れる。
/// 保存先の場所は試験ごとに違うので、答えの中の場所は <c>&lt;store&gt;</c> に置き換えて比べる。
/// </summary>
public sealed class ItemAnswerGoldenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-query-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static DateTimeOffset Jst(int year, int month, int day) => new(year, month, day, 12, 0, 0, TimeSpan.FromHours(9));

    /// <summary>作り物の保存先（名前・ID・場所は作り物。友人のデータの物を書かない）。</summary>
    private async Task<QuerySnapshot> StoreAsync()
    {
        var store = new DataStore(new AppPaths(_root));

        // 衣装：対応アバターの紐付け（確認済み・推定・消した・説明文のリンク）、購入、属性、タグ、ファイル（ある・見つからない・外した・古い版）
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9800001",
            Booth = new BoothBlock
            {
                Name = "作り物の衣装",
                Url = "https://shop-a.booth.pm/items/9800001",
                Shop = new BoothShop { Name = "作り物のショップ", Subdomain = "shop-a", Url = "https://shop-a.booth.pm/" },
                Category = new BoothCategory { Id = 208, Name = "3D衣装", ParentName = "3Dモデル" },
                Tags = ["夏", "水着"],
                PriceText = "¥ 1,500",
                WishListsCount = 321,
                PublishedAt = Jst(2025, 3, 1),
                FetchedAt = Jst(2026, 10, 1),
                Images = [new BoothImage { OriginalUrl = "https://example.invalid/a.png", Caption = "表紙" }],
                Variations =
                [
                    new BoothVariation { Id = 11, Name = "通常版", Price = 1500, Status = "on_sale", Type = "digital" },
                    new BoothVariation { Id = 12, Name = "支援版", Price = 3000, Status = "on_sale", Type = "digital" },
                ],
                Embeds = ["https://www.youtube.com/embed/AAAAAAAAAAA"],
            },
            Local = new LocalBlock
            {
                Purchases = [new Purchase { VariationId = 11, Kind = PurchaseKind.ForSelf, Price = 1500, PurchasedAt = new DateOnly(2025, 4, 1) }],
                Attributes = new Dictionary<string, int> { ["かわいい"] = 4 },
                UserTags = [new UserTagAssignment { Top = "衣装", Subs = ["夏"] }],
                Memo = "作り物のメモ",
                Avatars =
                [
                    new AvatarLink { AvatarItemId = "7000001", Source = AvatarLinkSource.SupportSection, Confirmed = true },
                    new AvatarLink { AvatarItemId = "7000002", Source = AvatarLinkSource.Tag },
                    new AvatarLink { AvatarItemId = "7000003", Source = AvatarLinkSource.Manual, Rejected = true },
                    new AvatarLink { AvatarItemId = "7000004", Source = AvatarLinkSource.H2Link },
                ],
                AvatarsDetectedAt = Jst(2026, 10, 2),
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = new string('A', 64), Paths = [@"D:\assets\costume.zip"], SizeBytes = 1000, VariationId = 11,
                        Contents = ["costume/costume.unitypackage", "costume/readme.txt"],
                        UnityPackages = [new UnityPackageSummary { Entry = "costume/costume.unitypackage", Roots = ["Assets/Costume"] }],
                    },
                    new LocalFileRecord { Hash = new string('B', 64), Paths = [], SizeBytes = 10, MissingSince = Jst(2026, 9, 1) },
                    new LocalFileRecord { Hash = new string('C', 64), Paths = [@"D:\assets\extra.zip"], SizeBytes = 20, Detached = true },
                    new LocalFileRecord
                    {
                        Hash = new string('D', 64), Paths = [], SizeBytes = 900,
                        Replaced = new ReplacedVersion(@"D:\assets\costume.zip", Jst(2026, 8, 1)),
                    },
                ],
                IsFavorite = true,
            },
        });

        // アバター（登録簿にある・持っている）
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "7000001",
            Booth = new BoothBlock { Name = "作り物のアバター", Shop = new BoothShop { Name = "作り物のショップ", Subdomain = "shop-a" } },
            Local = new LocalBlock { LocalFolders = [new LocalFolderRecord { Path = @"D:\avatars\a", FileCount = 3, TotalBytes = 300, UnityPackages = ["a.unitypackage"] }] },
        });

        // BOOTHに無い商品・自分で作ったショップと自分で入れたカテゴリ・ファイルを持たない
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = LocalItemId.For("0123456789abcdef"),
            Local = new LocalBlock
            {
                DisplayName = "作り物の自作素材",
                Shop = new LocalShop { Name = "自分", Subdomain = LocalShopKey.Prefix + "0001" },
                Category = "テクスチャ",
                IsHidden = true,
            },
        });

        await store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "7000001", BoothName = "作り物のアバター", Category = "3Dキャラクター" },
                new AvatarRegistryEntry { ItemId = "7000002", BoothName = "作り物のアバターB", Category = "3Dキャラクター" },
            ],
        });

        // 画像（BOOTH の1枚と、消えた画像の控え）
        var images = Path.Combine(_root, "images", "9800001");
        Directory.CreateDirectory(images);
        await File.WriteAllBytesAsync(Path.Combine(images, Chmonos.Core.Images.ImagePipeline.FileNameFor("https://example.invalid/a.png")), [0]);
        await File.WriteAllBytesAsync(Path.Combine(images, "0000000000.webp"), [0]);

        await store.VideoTitles.SaveAsync([new VideoTitleRecord { VideoId = "AAAAAAAAAAA", Title = "作り物の紹介動画", FetchedAt = Jst(2026, 10, 1) }]);
        return await QuerySnapshot.LoadAsync(store);
    }

    [Fact]
    public async Task 商品1件の答え()
    {
        var snapshot = await StoreAsync();
        foreach (var id in new[] { "9800001", "7000001", LocalItemId.For("0123456789abcdef") })
        {
            var json = QueryJson.Serialize(ItemMapper.Detail(snapshot.ItemsById[id], snapshot), indented: true);
            AssertGolden($"item-{id}.json", json);
        }
    }

    [Fact]
    public async Task 一覧の1行の答え()
    {
        var snapshot = await StoreAsync();
        var json = string.Join(
            Environment.NewLine,
            snapshot.Items.Select(item => QueryJson.Serialize(ItemMapper.Summary(item, snapshot))));
        AssertGolden("summaries.jsonl", json);
    }

    private void AssertGolden(string name, string actual, [CallerFilePath] string source = "")
    {
        // 場所は JSON の中で \\ に書かれるので、同じ書き方にしてから置き換える
        var escapedRoot = JsonSerializer.Serialize(_root)[1..^1];
        actual = actual.Replace(escapedRoot, "<store>").Replace("\r\n", "\n");

        var file = Path.Combine(Path.GetDirectoryName(source)!, "Golden", name);
        if (Environment.GetEnvironmentVariable("CHMONOS_UPDATE_GOLDEN") == "1" || !File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, actual + "\n");
            Assert.Fail($"期待する答えを書いた（{name}）。中身を読んでから、もう一度走らせる。");
        }

        Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n").TrimEnd('\n'), actual);
    }
}
