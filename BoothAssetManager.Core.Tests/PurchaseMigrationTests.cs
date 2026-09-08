using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 旧形式（orderedVariations）から新形式（purchases）への読み替え。
///
/// 一括変換は走らせず、読んだときに読み替えて、次に保存したときに書き換わる形にしてある。
/// 全件を一度に書き換えると、途中で失敗したときにどこまで進んだか分からなくなるため。
/// </summary>
public class PurchaseMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "booth-migration-" + Guid.NewGuid().ToString("N"));

    private ItemRepository Repository() => new(new AppPaths(_root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task WriteRawAsync(string itemId, string localJson)
    {
        var dir = Path.Combine(_root, "items");
        Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(
            Path.Combine(dir, itemId + ".json"),
            $$"""
            {
              "id": "{{itemId}}",
              "booth": { "name": "テスト商品", "fetchedAt": "2026-01-01T00:00:00+09:00" },
              "local": {{localJson}}
            }
            """);
    }

    [Fact]
    public async Task ReadsLegacyRecordsAsPurchases()
    {
        await WriteRawAsync("1", """
            {
              "orderedVariations": [
                { "variationId": 100, "price": 1500, "nameSnapshot": "本体" },
                { "variationId": 200, "price": 800, "isGifted": true }
              ]
            }
            """);

        var item = await Repository().LoadAsync("1");

        Assert.NotNull(item);
        Assert.Equal(2, item!.Local.Purchases.Count);
        Assert.Equal(PurchaseKind.ForSelf, item.Local.Purchases[0].Kind);
        Assert.Equal(1500, item.Local.Purchases[0].Price);
        Assert.Equal("本体", item.Local.Purchases[0].NameSnapshot);

        // isGifted は「貰った」の意味で使われていた
        Assert.Equal(PurchaseKind.Received, item.Local.Purchases[1].Kind);
    }

    /// <summary>読み替えたら旧形式は落とす。保存し直したときに古い形が残らないように。</summary>
    [Fact]
    public async Task DropsLegacyFieldAfterReading()
    {
        await WriteRawAsync("1", """
            { "orderedVariations": [ { "variationId": 100, "price": 1500 } ] }
            """);

        var repository = Repository();
        var item = await repository.LoadAsync("1");
        Assert.Null(item!.Local.LegacyOrderedVariations);

        await repository.SaveAsync(item);

        var json = await File.ReadAllTextAsync(Path.Combine(_root, "items", "1.json"));
        Assert.DoesNotContain("orderedVariations", json, StringComparison.Ordinal);
        Assert.Contains("purchases", json, StringComparison.Ordinal);
    }

    /// <summary>新しい形が既にあるなら、そちらが正。古い方は捨てるだけ。</summary>
    [Fact]
    public async Task PrefersNewFormatWhenBothArePresent()
    {
        await WriteRawAsync("1", """
            {
              "purchases": [ { "variationId": 100, "price": 9999, "kind": "贈った" } ],
              "orderedVariations": [ { "variationId": 100, "price": 1500 } ]
            }
            """);

        var item = await Repository().LoadAsync("1");

        var purchase = Assert.Single(item!.Local.Purchases);
        Assert.Equal(9999, purchase.Price);
        Assert.Equal(PurchaseKind.Given, purchase.Kind);
    }

    /// <summary>
    /// 同じvariationを複数回買った記録が持てること。
    /// 旧形式は variationId をキーにしていたので、3回目が1回目を上書きして支出が1/3になっていた。
    /// </summary>
    [Fact]
    public async Task KeepsEveryPurchaseOfTheSameVariation()
    {
        await WriteRawAsync("1", """
            {
              "purchases": [
                { "variationId": 100, "price": 1500 },
                { "variationId": 100, "price": 1500, "kind": "贈った", "note": "誕生日" },
                { "variationId": 100, "price": 1500, "kind": "贈った" }
              ]
            }
            """);

        var item = await Repository().LoadAsync("1");

        Assert.Equal(3, item!.Local.Purchases.Count);
        Assert.Equal(1500, Purchases.SelfSpendOf(item));
        Assert.Equal(3000, Purchases.GivenSpendOf(item));
        Assert.Equal(2, Purchases.GivenCountOf(item));
        Assert.Equal("誕生日", item.Local.Purchases[1].Note);
    }

    [Fact]
    public async Task LeavesRecordsWithoutPurchasesAlone()
    {
        await WriteRawAsync("1", """{ "memo": "何も買っていない" }""");

        var item = await Repository().LoadAsync("1");

        Assert.Empty(item!.Local.Purchases);
        Assert.Null(item.Local.LegacyOrderedVariations);
    }

    /// <summary>書き出した形が人に読めること。JSONは直接編集する前提なので。</summary>
    [Fact]
    public async Task WritesKindAsReadableText()
    {
        var repository = Repository();
        await repository.SaveAsync(new ItemRecord
        {
            Id = "1",
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                Purchases = [new Purchase { VariationId = 100, Price = 1500, Kind = PurchaseKind.Given }],
            },
        });

        var json = await File.ReadAllTextAsync(Path.Combine(_root, "items", "1.json"));

        Assert.Contains("\"kind\": \"贈った\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kind\": 2", json, StringComparison.Ordinal);

        // 計算で出る値は書かない。書くと、手で直せる値だと誤解される
        Assert.DoesNotContain("isOwnSpending", json, StringComparison.Ordinal);
        using var _ = JsonDocument.Parse(json);
    }
}
