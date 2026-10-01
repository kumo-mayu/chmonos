using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 別名を消したことが、再検出をまたいで残ること（穴②）。
///
/// 自動で覚えた別名はタグから毎回作り直されるので、**行ごと消すと次の検出で復活する。**
/// 対応アバターの <c>Rejected</c> と同じ形で、消したという事実の方を残す。
/// </summary>
public class AvatarAliasTests : IDisposable
{
    private const string AvatarId = "4897493";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarAliasTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-alias-" + Guid.NewGuid().ToString("N"));
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

    private async Task SaveRegistryAsync(params AvatarAlias[] aliases)
        => await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "【くうた-Kuuta-】オリジナル3Dモデル",
                    DisplayName = "くうた",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                    Aliases = aliases,
                },
            ],
        });

    private AvatarRegistryEntry Entry() => _store.Avatars.Load().Entries.Single();

    private static AvatarAlias Learned(string text)
        => new() { Text = text, Count = 3, Source = nameof(AvatarLinkSource.Tag) };

    /// <summary>
    /// **穴②の本体。**自動で覚えた別名を消すと、行は残って印が付く。
    /// 行ごと消すと、次の検出でタグから作り直されて復活してしまう。
    /// </summary>
    [Fact]
    public async Task MarksALearnedAliasAsRejectedInsteadOfDeletingTheRow()
    {
        await SaveRegistryAsync(Learned("くうた"), Learned("3Dモデル"));

        await _service.RemoveAliasAsync(AvatarId, "3Dモデル");

        var aliases = Entry().Aliases;

        Assert.Equal(2, aliases.Count);
        Assert.False(aliases.Single(alias => alias.Text == "くうた").Rejected);
        Assert.True(aliases.Single(alias => alias.Text == "3Dモデル").Rejected);
    }

    /// <summary>手で足した別名は検出が作らないので、行ごと消してよい。</summary>
    [Fact]
    public async Task DeletesAManualAliasOutright()
    {
        await SaveRegistryAsync(Learned("くうた"));
        await _service.AddAliasAsync(AvatarId, "Kuuta");

        await _service.RemoveAliasAsync(AvatarId, "Kuuta");

        Assert.DoesNotContain(Entry().Aliases, alias => alias.Text == "Kuuta");
    }

    /// <summary>足し直したら印を下ろす。もう一度覚えさせられる。</summary>
    [Fact]
    public async Task ClearsTheMarkWhenTheAliasIsAddedBackByHand()
    {
        await SaveRegistryAsync(Learned("3Dモデル"));
        await _service.RemoveAliasAsync(AvatarId, "3Dモデル");
        Assert.True(Entry().Aliases.Single().Rejected);

        await _service.AddAliasAsync(AvatarId, "3Dモデル");

        Assert.False(Entry().Aliases.Single().Rejected);
        Assert.Single(Entry().Aliases);
    }

    /// <summary>
    /// 消した別名は照合に使わない。ここが効かないと、印を付けても意味が無い。
    /// </summary>
    [Fact]
    public void KeepsARejectedAliasOutOfTheMatchingIndex()
    {
        var registry = new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    DisplayName = "くうた",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                    Aliases =
                    [
                        Learned("くうた"),
                        Learned("3Dモデル") with { Rejected = true },
                    ],
                },
            ],
        };

        var index = AvatarNameIndex.Build(registry);

        Assert.Equal([AvatarId], index.FindAvatars("くうた対応の衣装です"));
        Assert.Empty(index.FindAvatars("3Dモデルです"));
    }

    /// <summary>
    /// 再検出で数え直されても、印は落ちない。
    /// ここが落ちると、消しても毎週戻ってくる。
    /// </summary>
    [Fact]
    public async Task KeepsTheMarkWhenDetectionRecountsTheAlias()
    {
        // 汎用語（「3Dモデル」など）は数え直しの対象から外れるので、固有の表記で試す
        await SaveRegistryAsync(Learned("くうた") with { Rejected = true, Count = 3 });

        // 検出は「この表記を含む商品が5件あった」と数え直してくる
        var merged = AvatarService.MergeAliases(
            Entry().Aliases,
            new Dictionary<string, int> { ["くうた"] = 5 },
            nameof(AvatarLinkSource.Tag));

        var alias = merged.Single();

        Assert.Equal(5, alias.Count);
        Assert.True(alias.Rejected);
    }
}
