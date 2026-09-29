using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 共通素体の名前の変更が統合になるときの見分けと、確認に出す数（ユーザ判断 2026-09-29：確認なしで統合されていた）。
/// </summary>
public class AvatarBaseRenameTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarBaseRenameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-baserename-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new AvatarService(_store);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AnotherBasesNameIsAMerge()
        => Assert.Equal("素体B", AvatarBaseRename.MergeTarget(["素体A", "素体B"], "素体A", "素体B"));

    /// <summary>統合の判定は Core の改名と同じく、大文字小文字と前後の空白を見ない。統合先は一覧にある方の書き方で返す。</summary>
    [Fact]
    public void IgnoresCaseAndSurroundingSpacesLikeTheRename()
        => Assert.Equal("Base B", AvatarBaseRename.MergeTarget(["Base A", "Base B"], "Base A", "  base b "));

    [Fact]
    public void ANewNameIsNotAMerge()
        => Assert.Null(AvatarBaseRename.MergeTarget(["素体A", "素体B"], "素体A", "素体C"));

    /// <summary>大文字小文字だけを変えるのは自分自身の名前の変更で、統合ではない（確認を出さない）。</summary>
    [Fact]
    public void ChangingOnlyTheCaseIsNotAMerge()
        => Assert.Null(AvatarBaseRename.MergeTarget(["base a", "素体B"], "base a", "Base A"));

    /// <summary>
    /// 確認に出す商品の数（<see cref="AvatarService.CountItemsUsingBaseAsync"/>）が、統合で書き換わる商品の数と一致する。
    /// 所属のアバターも統合先へ移る。
    /// </summary>
    [Fact]
    public async Task TheCountShownBeforeMergingIsWhatTheMergeRewrites()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "1", BoothName = "アバター1", BaseName = "素体A" },
                new AvatarRegistryEntry { ItemId = "2", BoothName = "アバター2", BaseName = "素体B" },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = "素体A" }, new AvatarBaseGroup { Name = "素体B" }],
        });

        await SaveItemAsync("10", "素体A");
        await SaveItemAsync("11", "素体A");
        await SaveItemAsync("12", "素体B");

        var shown = await _service.CountItemsUsingBaseAsync("素体A");
        var rewritten = await _service.RenameBaseAsync("素体A", "素体B");

        Assert.Equal(2, shown);
        Assert.Equal(shown, rewritten);
        Assert.Equal(3, await _service.CountItemsUsingBaseAsync("素体B"));

        var registry = _store.Avatars.Load();
        Assert.All(registry.Entries, entry => Assert.Equal("素体B", entry.BaseName));
        Assert.DoesNotContain(registry.BaseGroups, group => group.Name == "素体A");
    }

    private async Task SaveItemAsync(string id, string baseName)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock(),
            Local = new LocalBlock { AvatarBases = [new AvatarBaseLink { BaseName = baseName, Confirmed = true }] },
        });
}
