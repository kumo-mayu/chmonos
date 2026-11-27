using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>
/// 外のソフトの情報の置き場（extensions/）が、書き出しと戻す・形式を上げる前の控え・引越しに付いていくか
/// （docs/spec/local-api-extensions.md「置き場」の [要確認]）。名前空間・中身は作り物
/// </summary>
public sealed class ExtensionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chmonos-ext-tests", Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_dir, "store");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task 置き場は引越し_書き出しと戻す_形式を上げる前の控えに付いていく()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(Root, "settings.json"), "{}");
        var store = new DataStore(paths);
        await store.Extensions.ChangeSchemaAsync("outfit-checker", _ => new ExtensionSchemaFile { Label = "作り物", Software = "作り物の道具" });

        // 付け先ごとの入れ子のフォルダ（値は次の段で書く形。ここでは置き方だけを見る）
        var nested = Path.Combine("extensions", "outfit-checker", "items", "9600001.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, nested))!);
        File.WriteAllText(Path.Combine(Root, nested), """{ "version": 1, "values": { "polyCount": 70000 } }""");
        var schema = Path.Combine("extensions", "outfit-checker", ExtensionRepository.SchemaFileName);

        var zip = Path.Combine(_dir, "backup.zip");
        BackupArchive.Export(Root, zip, includeImages: false);
        var restored = Path.Combine(_dir, "restored");
        BackupArchive.Restore(zip, restored);
        Assert.True(File.Exists(Path.Combine(restored, schema)));
        Assert.True(File.Exists(Path.Combine(restored, nested)));

        var backup = StoreFormat.Backup(Root, fromVersion: 1, DateTimeOffset.Now);
        Assert.True(File.Exists(Path.Combine(backup, schema)));
        Assert.True(File.Exists(Path.Combine(backup, nested)));

        var moved = Path.Combine(_dir, "moved");
        Assert.True(StoreMover.Move(Root, moved).Succeeded);
        Assert.True(File.Exists(Path.Combine(moved, schema)));
        Assert.True(File.Exists(Path.Combine(moved, nested)));
        Assert.Equal("作り物", new ExtensionRepository(new AppPaths(moved)).LoadSchema("outfit-checker")!.Label);
    }

    /// <summary>
    /// 商品・改変を外したら、全部の名前空間からその値とメモを消す（spec「付け先の変化」）。残すと、同じ ID で登録し直した物に古い値が付く。
    /// ほかの商品の値には触らない
    /// </summary>
    [Fact]
    public async Task 商品と改変を外すと付いた値とメモも消える()
    {
        var paths = new AppPaths(Root);
        paths.EnsureCreated();
        var store = new DataStore(paths);
        await store.Items.SaveAsync(new ItemRecord { Id = "9600001", Booth = new BoothBlock { Name = "作り物" } });
        await store.Items.SaveAsync(new ItemRecord { Id = "9600002", Booth = new BoothBlock { Name = "作り物" } });
        await store.Modifications.SaveAsync(new ModificationRecord { Id = "mod-0001", AvatarItemId = "9600001", Name = "作り物" });

        var one = ExtensionTarget.Item("9600001")!;
        var two = ExtensionTarget.Item("9600002")!;
        var modification = ExtensionTarget.Modification("mod-0001")!;
        foreach (var ns in new[] { "outfit-checker", "other-tool" })
        {
            foreach (var target in new[] { one, two, modification })
            {
                await store.Extensions.ChangeValuesAsync(ns, target, (_, _) => new ExtensionValueFile { Values = { ["a"] = JsonSerializer.SerializeToElement(1) } });
                await store.Extensions.ChangeMemoAsync(ns, target, _ => new ExtensionMemoFile { Memo = JsonSerializer.SerializeToElement("印") });
            }
        }

        await store.Items.DeleteAsync("9600001");
        await store.Modifications.DeleteAsync("mod-0001");

        foreach (var ns in new[] { "outfit-checker", "other-tool" })
        {
            Assert.Null(store.Extensions.LoadValues(ns, one));
            Assert.Null(store.Extensions.LoadMemo(ns, one));
            Assert.Null(store.Extensions.LoadValues(ns, modification));
            Assert.Null(store.Extensions.LoadMemo(ns, modification));
            Assert.NotNull(store.Extensions.LoadValues(ns, two));
            Assert.NotNull(store.Extensions.LoadMemo(ns, two));
        }
    }
}
