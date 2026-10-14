using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Query.Tests;

/// <summary>写しの読み直し（変わった部分だけ。docs/spec/local-api.md「同期」）。名前・ID は作り物。</summary>
public sealed class SnapshotUpdateTests : IDisposable
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

    private static ItemRecord Item(string id, string name) => new() { Id = id, Booth = new BoothBlock { Name = name, Description = "作り物の本文" } };

    private static StoreChangeSet Changed(params (StorePart Part, string? Id)[] parts)
    {
        var changes = new StoreChangeSet();
        foreach (var (part, id) in parts)
        {
            changes.Add(part, id);
        }

        return changes;
    }

    /// <summary>
    /// アプリと同じプロセスで動くときは、商品の記録をアプリの控えと共有する（docs/history/headless-core-2026-10-13.md 段2）。
    /// 画面が説明文ごと控えに持っているので、説明文を落とした写しを別に作らない。変わらない商品は読み直しでも同じ実体のまま
    /// </summary>
    [Fact]
    public async Task アプリと同じプロセスでは_商品の記録をアプリの控えと共有し_説明文も落とさない()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9500001", "作り物の一"));
        var appCopy = await store.Items.LoadAsync("9500001");

        var shared = await QuerySnapshot.LoadAsync(store, sharesItemCache: true);
        Assert.Same(appCopy, shared.ItemsById["9500001"]);
        Assert.Equal("作り物の本文", shared.ItemsById["9500001"].Booth.Description);

        var again = await shared.UpdateAsync(Changed((StorePart.Items, null)));
        Assert.Same(appCopy, again.ItemsById["9500001"]);

        // 別のプロセスとして読む写し（共有しない）は、今までどおり説明文を落とす
        var separate = await QuerySnapshot.LoadAsync(new DataStore(new AppPaths(_root)));
        Assert.Null(separate.ItemsById["9500001"].Booth.Description);
    }

    /// <summary>
    /// アプリと記録を共有しているときは、画面が同じ記録の物に作った検索用の文字列を借り、二重に持たない（2026-10-14：実物に近い2万件で約185MB）。
    /// 画面が覚えていない記録と、共有しない写しは自分で作る
    /// </summary>
    [Fact]
    public async Task アプリと共有しているときは_画面が覚えた検索用の文字列を借りる()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9500001", "作り物の一"));
        await store.Items.SaveAsync(Item("9500002", "作り物の二"));
        var appCopy = await store.Items.LoadAsync("9500001");
        var screens = Core.Services.SearchText.Build(appCopy!);
        Core.Services.SearchText.Remember(appCopy!, screens);

        var shared = await QuerySnapshot.LoadAsync(store, sharesItemCache: true);
        lock (shared.SearchGate)
        {
            Assert.Same(screens, shared.HaystackOf(shared.ItemsById["9500001"]));
            Assert.NotNull(shared.HaystackOf(shared.ItemsById["9500002"]));
        }

        var separate = await QuerySnapshot.LoadAsync(new DataStore(new AppPaths(_root)));
        lock (separate.SearchGate)
        {
            Assert.NotSame(screens, separate.HaystackOf(separate.ItemsById["9500001"]));
        }
    }

    [Fact]
    public async Task 変わったファイルだけを読み直し_変わらない商品は同じ記録を使い回す()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9500001", "作り物の一"));
        await store.Items.SaveAsync(Item("9500002", "作り物の二"));
        var first = await QuerySnapshot.LoadAsync(store);

        // 日時の刻みに収まって同じ日時になるのを避ける
        await Task.Delay(30);
        await store.Items.SaveAsync(Item("9500002", "作り物の二・直した"));
        await store.Items.SaveAsync(Item("9500003", "作り物の三"));
        var second = await first.UpdateAsync(Changed((StorePart.Items, "9500002")));

        Assert.Same(first.ItemsById["9500001"], second.ItemsById["9500001"]);
        Assert.Equal("作り物の二・直した", second.ItemsById["9500002"].DisplayName);

        // 知らせに載っていなくても、一覧を見直して増えた商品を拾う
        Assert.True(second.ItemsById.ContainsKey("9500003"));
        Assert.Equal(["9500002", "9500003"], second.ChangedItemsSince(first));

        // 読み直しても説明文は落としたまま
        Assert.All(second.Items, item => Assert.Null(item.Booth.Description));

        File.Delete(store.Paths.ItemFile("9500001"));
        var third = await second.UpdateAsync(Changed((StorePart.Items, null)));
        Assert.False(third.ItemsById.ContainsKey("9500001"));
        Assert.Equal(["9500001"], third.ChangedItemsSince(second));
    }

    [Fact]
    public async Task 知らされない部分は読み直さず_知らされた部分は読み直す()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9500001", "作り物の一"));
        var first = await QuerySnapshot.LoadAsync(store);

        await store.Avatars.SaveAsync(new AvatarRegistry { Entries = [new AvatarRegistryEntry { ItemId = "7000001", BoothName = "作り物のアバター" }] });
        await Task.Delay(30);
        await store.Items.SaveAsync(Item("9500001", "作り物の一・直した"));

        var registryOnly = await first.UpdateAsync(Changed((StorePart.Registry, null)));
        Assert.Single(registryOnly.Registry.Entries);
        Assert.Same(first.ItemsById["9500001"], registryOnly.ItemsById["9500001"]);

        var everything = await first.UpdateAsync(StoreChangeSet.Everything());
        Assert.Equal("作り物の一・直した", everything.ItemsById["9500001"].DisplayName);
    }

    [Fact]
    public async Task 壊れたJSONは_読めなかった商品として知らせる()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(Item("9500001", "作り物の一"));
        Directory.CreateDirectory(store.Paths.ItemsDir);
        await File.WriteAllTextAsync(store.Paths.ItemFile("9500002"), "{ 壊れた");

        var snapshot = await QuerySnapshot.LoadAsync(store);

        Assert.Equal(["9500001"], snapshot.Items.Select(item => item.Id));
        Assert.Equal(["9500002"], snapshot.FailedItemIds);
    }

    [Theory]
    [InlineData(@"items\9500001.json", StorePart.Items, "9500001")]
    [InlineData(@"images\9500001\abc.webp", StorePart.Images, "9500001")]
    [InlineData(@"images\9500001", StorePart.Images, "9500001")]
    [InlineData(@"images\_avatars\7000001\a.webp", null, null)]
    [InlineData(@"images\_avatars\7000001", StorePart.Registry, null)]
    [InlineData(@"modifications\mod-0001.json", StorePart.Modifications, null)]
    [InlineData(@"avatar-registry.json", StorePart.Registry, null)]
    [InlineData(@"userTags.json", StorePart.UserTags, null)]
    [InlineData(@"api-settings.json", StorePart.ApiSettings, null)]
    [InlineData(@"format.json", StorePart.Format, null)]
    [InlineData(@"items\9500001.json.tmp", null, null)]
    [InlineData(@"items\.prev\9500001.json", null, null)]
    [InlineData(@"logs\app.log", null, null)]
    [InlineData(@"search-bridge.cache", null, null)]
    public void ファイルの場所から変わった部分を見分ける(string relative, StorePart? part, string? itemId)
    {
        var paths = new AppPaths(_root);
        var found = StoreChanges.Classify(paths, Path.Combine(_root, relative));

        Assert.Equal(part, found?.Part);
        Assert.Equal(itemId, found?.ItemId);
    }
}
