using System.Text.Json.Nodes;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 購入記録の額の空欄を 0 にする道具（tools/NullPriceFix）の中身。ほかの人の保存先を書き換える物なので、
/// 「額の空欄だけを変え、ほかは1文字も変えない」「控えを取ってから書く」「何度走らせても同じ」を止める
/// </summary>
public sealed class NullPriceFixerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"chmonos-nullprice-{Guid.NewGuid():N}");

    private string ItemsDir => new AppPaths(_root).ItemsDir;

    private string BackupDir => Path.Combine(_root, "backup-null-price");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static ItemRecord Item(string id, params Purchase[] purchases) => new()
    {
        Id = id,
        Local = new LocalBlock { Purchases = purchases },
    };

    private string Save(ItemRecord item)
    {
        // 今の版の保存と同じ書き方（額が null なら price の欄ごと省かれる）
        var path = Path.Combine(ItemsDir, $"{item.Id}.json");
        JsonStore.Write(path, item);
        return path;
    }

    [Fact]
    public void 額の空いた購入記録だけを0にし_ほかの行は1文字も変えない()
    {
        var path = Save(Item("9900001",
            new Purchase { VariationId = 1, NameSnapshot = "作り物の版", Price = null },
            new Purchase { VariationId = 2, NameSnapshot = "作り物の別の版", Price = 500 }));
        var before = File.ReadAllText(path);
        Assert.DoesNotContain("\"price\": 0", before);

        var scan = NullPriceFixer.Scan(_root);
        Assert.Equal(1, scan.ItemsScanned);
        Assert.Equal(1, scan.PurchaseCount);

        var applied = NullPriceFixer.Apply(_root, BackupDir);

        Assert.Equal(["9900001.json"], applied.Changed);
        var after = File.ReadAllText(path);
        var record = JsonStore.Read<ItemRecord>(path)!;
        Assert.Equal([0, 500], record.Local.Purchases.Select(purchase => purchase.Price));

        // 違うのは足した price の1行だけ（並びは今の版の書き方：nameSnapshot の次）
        var added = after.Split('\n').Except(before.Split('\n')).ToList();
        var removed = before.Split('\n').Except(after.Split('\n')).ToList();
        Assert.Single(added);
        Assert.Contains("\"price\": 0", added[0]);
        Assert.Empty(removed);
        Assert.Equal(before, string.Join('\n', after.Split('\n').Where(line => line != added[0])));

        // 控えは書き換える前の中身そのまま
        Assert.Equal(before, File.ReadAllText(Path.Combine(BackupDir, "9900001.json")));
    }

    [Fact]
    public void 手で書いたprice_nullも0にする()
    {
        Directory.CreateDirectory(ItemsDir);
        var path = Path.Combine(ItemsDir, "9900002.json");
        File.WriteAllText(path, """{ "id": "9900002", "local": { "purchases": [ { "price": null, "kind": "自分用" } ] } }""");

        NullPriceFixer.Apply(_root, BackupDir);

        var purchase = (JsonNode.Parse(File.ReadAllText(path))!["local"]!["purchases"]!.AsArray())[0]!;
        Assert.Equal(0, purchase["price"]!.GetValue<int>());
    }

    [Fact]
    public void 今の型に無い欄も残す()
    {
        Directory.CreateDirectory(ItemsDir);
        var path = Path.Combine(ItemsDir, "9900003.json");
        File.WriteAllText(path, """
            { "id": "9900003", "futureField": { "a": 1 }, "local": { "purchases": [ { "kind": "自分用", "futureNote": "x" } ], "futureLocal": [1, 2] } }
            """);

        NullPriceFixer.Apply(_root, BackupDir);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal(1, root["futureField"]!["a"]!.GetValue<int>());
        Assert.Equal(2, root["local"]!["futureLocal"]!.AsArray().Count);
        var purchase = root["local"]!["purchases"]![0]!.AsObject();
        Assert.Equal("x", purchase["futureNote"]!.GetValue<string>());

        // 名前の欄が無ければ kind の前に足す
        Assert.Equal(["price", "kind", "futureNote"], purchase.Select(pair => pair.Key));
    }

    [Fact]
    public void 何度走らせても同じで_2回目は何も書かず控えも作らない()
    {
        Save(Item("9900004", new Purchase { Price = null }));
        NullPriceFixer.Apply(_root, BackupDir);
        Directory.Delete(BackupDir, recursive: true);

        var second = NullPriceFixer.Apply(_root, BackupDir);

        Assert.Empty(second.Changed);
        Assert.Null(second.BackupDir);
        Assert.False(Directory.Exists(BackupDir));
        Assert.Equal(0, NullPriceFixer.Scan(_root).PurchaseCount);
    }

    [Fact]
    public void 額の空欄が無い記録は書かない()
    {
        var path = Save(Item("9900005", new Purchase { Price = 0 }, new Purchase { Price = 1200 }));
        var stamp = File.GetLastWriteTimeUtc(path);

        var applied = NullPriceFixer.Apply(_root, BackupDir);

        Assert.Empty(applied.Changed);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void 読めない記録は触らず名前だけ返し_控えの下と説明のファイルは見ない()
    {
        Directory.CreateDirectory(Path.Combine(ItemsDir, ".prev"));
        var broken = Path.Combine(ItemsDir, "9900006.json");
        File.WriteAllText(broken, "{ \"id\": \"9900006\", \"local\": { \"purchases\": [ {");
        File.WriteAllText(Path.Combine(ItemsDir, ".prev", "9900007.json"), """{ "local": { "purchases": [ {} ] } }""");
        File.WriteAllText(Path.Combine(ItemsDir, "9900008.h2.json"), """{ "local": { "purchases": [ {} ] } }""");

        var scan = NullPriceFixer.Scan(_root);
        var applied = NullPriceFixer.Apply(_root, BackupDir);

        Assert.Equal(["9900006.json"], scan.Unreadable);
        Assert.Equal(0, scan.PurchaseCount);
        Assert.Empty(applied.Changed);
        Assert.Equal("{ \"id\": \"9900006\", \"local\": { \"purchases\": [ {", File.ReadAllText(broken));
    }

    [Fact]
    public void 保存先に商品の記録が無くても落ちない()
    {
        var scan = NullPriceFixer.Scan(_root);

        Assert.Equal(0, scan.ItemsScanned);
        Assert.Empty(NullPriceFixer.Apply(_root, BackupDir).Changed);
    }
}
