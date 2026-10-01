using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 購入記録（purchases）の読み書き。
///
/// 以前はここに旧形式（orderedVariations）の読み替えの試験もあったが、読み替えそのものを外した
/// （2026-09-12・ユーザ判断：公開前は今の形に合わないデータの側を問題にする）。
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
