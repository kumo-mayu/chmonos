using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests.Robustness;

public sealed class NewLocalFieldsAdversarialTests : IDisposable
{
    private readonly RobustnessDirectory _directory = new();
    private readonly DataStore _store;
    private const string FromId = "9900001";
    private const string ToId = "9900002";
    private const string ItemId = "9900003";

    public NewLocalFieldsAdversarialTests()
    {
        var paths = new AppPaths(Path.Combine(_directory.Root, "store"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }
    public void Dispose() => _directory.Dispose();

    private static LocalBlock Local() => new()
    {
        Memo = "人が入れたメモ", UserTags = [new UserTagAssignment { Top = "人が入れたタグ" }],
        Purchases = [new Purchase { Price = 1200 }],
        VariationTargets = [new VariationTarget { VariationId = 1, Avatars = [FromId], Bases = ["Base A"], RejectedAvatars = [FromId], RejectedBases = ["Base A"] }],
        DetectedVariationTargets = [new VariationTarget { VariationId = 2, Avatars = [FromId], Bases = ["Base A"], RejectedAvatars = [FromId], RejectedBases = ["Base A"] }],
        Conversion = new ConversionSetting { Targets = [new ConversionTarget { AvatarItemId = FromId, Slug = "avatar", Confirmed = true, Forward = true }, new ConversionTarget { BaseName = "Base A", Slug = "base", Rejected = true }] },
        ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.UnixEpoch, FilesBefore = ["before"], ForAvatars = [FromId], ForBases = ["Base A"] },
    };

    private Task Save(string id, LocalBlock? local = null) => _store.Items.SaveAsync(ItemService.EmptyItem(id) with { Local = local ?? new LocalBlock() });
    private async Task<LocalBlock> Reload(string id)
        => (await new ItemRepository(_store.Paths).LoadAsync(id))!.Local;

    [Fact]
    public async Task D2_旧来の対応素体欄が空でも_改名は新しい全参照をディスクへ書く()
    {
        await Save(ItemId, Local());
        await _store.Avatars.SaveAsync(new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "Base A" }] });
        var service = new AvatarService(_store);
        Assert.Equal(1, await service.CountItemsUsingBaseAsync("base a"));
        Assert.Equal(1, await service.RenameBaseAsync("base a", "Base B"));
        var local = await Reload(ItemId);
        foreach (var field in LocalReferences.Fields.Where(field => field.Kind == LocalReferenceKind.BaseName))
        {
            Assert.DoesNotContain(field.Values(local), value => value.Equals("Base A", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal("Base B", Assert.Single(local.VariationTargets).Bases.Single());
        Assert.Equal("Base B", local.Conversion!.Targets.Single(row => row.BaseName is not null).BaseName);
        Assert.True(local.Conversion.Targets.Single(row => row.BaseName is not null).Rejected);
        Assert.Equal("Base B", Assert.Single(local.ContentUpdate!.ForBases!));
        Assert.Equal(Local().Memo, local.Memo);
        Assert.Equal(1200, Assert.Single(local.Purchases).Price);
    }

    [Fact]
    public async Task D2_素体削除で新欄だけの参照も消し_限定更新を全体向けに変えない()
    {
        await Save(ItemId, Local());
        var service = new AvatarService(_store);
        Assert.Equal(1, await service.DeleteBaseAsync("Base A"));
        var local = await Reload(ItemId);
        Assert.Empty(Assert.Single(local.VariationTargets).Bases);
        Assert.Null(Assert.Single(local.VariationTargets).RejectedBases);
        Assert.Empty(Assert.Single(local.DetectedVariationTargets).Bases);
        Assert.DoesNotContain(local.Conversion!.Targets, row => row.BaseName is not null);
        Assert.Equal(new[] { "Base A" }, local.ContentUpdate!.ForBases);
        Assert.Equal(new[] { FromId }, local.ContentUpdate.ForAvatars);
    }

    [Fact]
    public async Task D3_旧来の対応アバター欄が空でも_ID付替えは新しい全参照をディスクへ書く()
    {
        await Save(FromId);
        await Save(ToId);
        await Save(ItemId, Local());
        // 移す先を先に作っておき、通信部品も画像部品も渡さない。通信へ進めば試験が失敗する。
        var service = new ItemService(_store, null!, null!, new AppSettings());
        Assert.Equal(ItemIdChangeOutcome.Moved, await service.ChangeItemIdAsync(FromId, ToId));
        var local = await Reload(ItemId);
        foreach (var field in LocalReferences.Fields.Where(field => field.Kind == LocalReferenceKind.ItemId))
        {
            Assert.DoesNotContain(FromId, field.Values(local));
        }
        Assert.Equal(new[] { ToId }, Assert.Single(local.VariationTargets).Avatars);
        Assert.Equal(new[] { ToId }, Assert.Single(local.DetectedVariationTargets).RejectedAvatars);
        Assert.Equal(ToId, local.Conversion!.Targets.Single(row => row.AvatarItemId is not null).AvatarItemId);
        Assert.Equal(new[] { ToId }, local.ContentUpdate!.ForAvatars);
        Assert.False(_store.Items.Exists(FromId));
        Assert.Equal(Local().Memo, local.Memo);
    }

    [Fact]
    public async Task D3_まとめると自分自身への変換参照を付け替え_移せない新欄を下見に出す()
    {
        await Save(FromId, Local());
        var targetUpdate = new ContentUpdate { FoundAt = DateTimeOffset.UnixEpoch.AddDays(1), ForAvatars = [ToId] };
        await Save(ToId, new LocalBlock { ContentUpdate = targetUpdate });
        var plan = ItemIdChange.Plan(ItemService.EmptyItem(FromId) with { Local = Local() }, ItemService.EmptyItem(ToId), ToId, ItemIdTargetStatus.Found);
        Assert.Contains(plan.Dropped, drop => drop.Reason == DroppedReason.VariationLink && drop.Text.Contains("対応", StringComparison.Ordinal));
        Assert.Contains(plan.Dropped, drop => drop.Reason == DroppedReason.FetchState && drop.Text.Contains("新しい版", StringComparison.Ordinal));
        var service = new ItemService(_store, null!, null!, new AppSettings());
        Assert.Equal(ItemIdChangeOutcome.Moved, await service.ChangeItemIdAsync(FromId, ToId));
        var local = await Reload(ToId);
        Assert.Equal(ToId, local.Conversion!.Targets.Single(row => row.AvatarItemId is not null).AvatarItemId);
        Assert.True(local.Conversion.Targets.Single(row => row.AvatarItemId is not null).Confirmed);
        Assert.Empty(local.VariationTargets);
        Assert.Equal(targetUpdate.FoundAt, local.ContentUpdate!.FoundAt);
    }

    [Fact]
    public async Task 新欄とメモを同時に更新しても_古い写しの保存で互いの入力を消さない()
    {
        await Save(ItemId, Local());
        var old = await Reload(ItemId);
        await Task.WhenAll(
            _store.Items.SaveLocalAsync(ItemId, old with { Memo = "新しいメモ" }, [LocalField.Memo]),
            _store.Items.SaveLocalAsync(ItemId, old with { Conversion = old.Conversion! with { Mode = ConversionMode.Profile } }, [LocalField.Conversion]),
            _store.Items.SaveLocalAsync(ItemId, old with { ContentUpdate = old.ContentUpdate! with { CheckedAt = DateTimeOffset.UnixEpoch.AddDays(1) } }, [LocalField.ContentUpdate]),
            _store.Items.ChangeLocalAsync(ItemId, current => current with { VariationTargets = [.. current.VariationTargets, new VariationTarget { VariationId = 3, All = true }] }, [LocalField.VariationTargets]));
        var local = await Reload(ItemId);
        Assert.Equal("新しいメモ", local.Memo);
        Assert.Equal(ConversionMode.Profile, local.Conversion!.Mode);
        Assert.NotNull(local.ContentUpdate!.CheckedAt);
        Assert.Equal(2, local.VariationTargets.Count);
        Assert.Equal(JsonSerializer.Serialize(old.UserTags, JsonStore.Options), JsonSerializer.Serialize(local.UserTags, JsonStore.Options));
        Assert.Equal(1200, Assert.Single(local.Purchases).Price);
        Assert.True(_store.Items.HasUsableCopy(ItemId));
        Assert.Equal(JsonSerializer.Serialize(local, JsonStore.Options), JsonSerializer.Serialize(_store.Items.ReadCopy(ItemId)!.Local, JsonStore.Options));
    }

    [Fact]
    public async Task 新欄のnull配列を読んで無関係な欄を保存しても_入力を失わない()
    {
        var local = JsonSerializer.Deserialize<LocalBlock>("""
            {"memo":"残す","variationTargets":null,"detectedVariationTargets":null,
             "conversion":{"mode":"profile","targets":null},
             "contentUpdate":{"foundAt":"2026-11-12T00:00:00Z","filesBefore":null,"forAvatars":null,"forBases":null}}
            """, JsonStore.Options)!;
        await Save(ItemId, local);
        await _store.Items.ChangeLocalAsync(ItemId, current => current with { IsFavorite = true }, [LocalField.IsFavorite]);
        var loaded = await Reload(ItemId);
        Assert.Equal("残す", loaded.Memo);
        Assert.Empty(loaded.VariationTargets);
        Assert.Empty(loaded.DetectedVariationTargets);
        Assert.Empty(loaded.Conversion!.Targets);
        Assert.Empty(loaded.ContentUpdate!.FilesBefore);
    }

    [Fact]
    public async Task 新欄の置換が失敗しても_本体とprevを壊さず_再試行できる()
    {
        await Save(ItemId, Local());
        var path = _store.Paths.ItemFile(ItemId);
        var copy = _store.Paths.ItemCopyFile(ItemId);
        var before = File.ReadAllBytes(path);
        var beforeCopy = File.ReadAllBytes(copy);
        var changed = Local() with { Conversion = new ConversionSetting { Mode = ConversionMode.NotProfile } };
        // 削除の共有を許さない読み手で、最後の置き換えだけを確実に失敗させる。
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _store.Items.SaveLocalAsync(ItemId, changed, [LocalField.Conversion]));
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(beforeCopy, File.ReadAllBytes(copy));
        }
        Assert.Empty(Directory.GetFiles(_store.Paths.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.True(await _store.Items.SaveLocalAsync(ItemId, changed, [LocalField.Conversion]));
        var local = await Reload(ItemId);
        Assert.Equal(ConversionMode.NotProfile, local.Conversion!.Mode);
        Assert.Equal("人が入れたメモ", local.Memo);
        Assert.Equal("Base A", Assert.Single(local.ContentUpdate!.ForBases!));
        Assert.Equal(JsonSerializer.Serialize(local, JsonStore.Options), JsonSerializer.Serialize(_store.Items.ReadCopy(ItemId)!.Local, JsonStore.Options));
    }
}
