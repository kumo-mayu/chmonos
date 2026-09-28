using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 共通素体を手で足す（ユーザ判断 2026-09-28）。
///
/// 手で足した素体は印を持ち、**対応アバターの検出し直しでも消えない。**消えるのは人が消したときだけ。
/// </summary>
public class AvatarBaseManualTests : IDisposable
{
    private const string AvatarId = "1111";
    private const string ItemId = "999";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarBaseManualTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-basemanual-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    /// <summary>アバター1体と、それに対応する商品1件（検出を丸ごと回すため）。</summary>
    private async Task SeedLibraryAsync()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "オリジナル3Dモデル「テスト」",
                    Category = "3Dキャラクター",
                    CheckedAt = DateTimeOffset.Now,
                },
            ],
        });

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ" },
            Local = new LocalBlock(),
        });

        File.WriteAllText(
            _paths.ItemHtmlFile(ItemId),
            $"<h2>対応アバター</h2><p>https://booth.pm/ja/items/{AvatarId}</p>");
    }

    private AvatarBaseGroup? Group(string name)
        => _store.Avatars.Load().BaseGroups.FirstOrDefault(group => group.Name == name);

    [Fact]
    public async Task AddsABaseWithTheManualMark()
    {
        var outcome = await _service.AddBaseAsync("  手の素体  ");

        Assert.Equal(AvatarBaseAddOutcome.Added, outcome);
        var group = Assert.IsType<AvatarBaseGroup>(Group("手の素体"));
        Assert.True(group.IsManual);
        Assert.False(group.Rejected);

        // どのアバターにも結ばれていなくても、一覧に出る
        Assert.Contains(await _service.LoadBasesAsync(), summary => summary.Group.Name == "手の素体");
    }

    /// <summary>**本体。**手で足した素体は、検出し直しを何度回しても消えず、印も落ちない。</summary>
    [Fact]
    public async Task KeepsAManualBaseThroughRedetection()
    {
        await SeedLibraryAsync();
        await _service.AddBaseAsync("手の素体");

        await _service.DetectAsync();
        await _service.DetectAsync();

        var group = Assert.IsType<AvatarBaseGroup>(Group("手の素体"));
        Assert.True(group.IsManual);
        Assert.False(group.Rejected);
        Assert.Contains(await _service.LoadBasesAsync(), summary => summary.Group.Name == "手の素体");
    }

    /// <summary>検出の間に手で足した素体も、検出の書き込みで消えない（検出の写しには無い素体）。</summary>
    [Fact]
    public void KeepsAManualBaseAddedWhileDetecting()
    {
        var snapshot = new AvatarRegistry();
        var latest = new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "手の素体", IsManual = true }] };

        var merged = AvatarService.MergeDetected(
            latest,
            snapshot,
            new Dictionary<string, AvatarRegistryEntry>(),
            new Dictionary<string, AvatarBaseGroup>());

        Assert.Contains(merged.BaseGroups, group => group.Name == "手の素体" && group.IsManual);
    }

    /// <summary>人が消したときは消える（消した印）。検出し直しでも戻らない。</summary>
    [Fact]
    public async Task RemovesAManualBaseOnlyWhenDeletedAndKeepsItGone()
    {
        await SeedLibraryAsync();
        await _service.AddBaseAsync("手の素体");

        await _service.DeleteBaseAsync("手の素体");
        await _service.DetectAsync();

        Assert.True(Group("手の素体")?.Rejected);
        Assert.DoesNotContain(await _service.LoadBasesAsync(), summary => summary.Group.Name == "手の素体");
    }

    /// <summary>消した素体を足し直すと、印を下ろして手で足した物にする。</summary>
    [Fact]
    public async Task RestoresADeletedBase()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            BaseGroups = [new AvatarBaseGroup { Name = "消した素体", Rejected = true }],
        });

        var outcome = await _service.AddBaseAsync("消した素体");

        Assert.Equal(AvatarBaseAddOutcome.Restored, outcome);
        var group = Assert.IsType<AvatarBaseGroup>(Group("消した素体"));
        Assert.False(group.Rejected);
        Assert.True(group.IsManual);
    }

    /// <summary>もう一覧にある素体は書き換えない（検出が作った物に、手で足した印を後から立てない）。</summary>
    [Fact]
    public async Task LeavesAnExistingBaseAlone()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            BaseGroups = [new AvatarBaseGroup { Name = "検出の素体" }],
        });

        var outcome = await _service.AddBaseAsync("検出の素体");

        Assert.Equal(AvatarBaseAddOutcome.AlreadyThere, outcome);
        Assert.False(Group("検出の素体")?.IsManual);
    }

    /// <summary>アバターの詳細で新しい名前を打って作った素体も、手で足した物。</summary>
    [Fact]
    public async Task MarksABaseCreatedFromAnAvatarAsManual()
    {
        await SeedLibraryAsync();

        await _service.SetBaseAsync(AvatarId, "アバターから作った素体");

        Assert.True(Group("アバターから作った素体")?.IsManual);
    }

    /// <summary>統合する改名で、手で足した印を落とさない。</summary>
    [Fact]
    public async Task KeepsTheMarkWhenRenamedIntoAnotherBase()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            BaseGroups =
            [
                new AvatarBaseGroup { Name = "手の素体", IsManual = true },
                new AvatarBaseGroup { Name = "検出の素体" },
            ],
        });

        await _service.RenameBaseAsync("手の素体", "検出の素体");

        Assert.Null(Group("手の素体"));
        Assert.True(Group("検出の素体")?.IsManual);
    }

    /// <summary>欄の無い古い書き方・手で直した JSON は「手で足していない」と読む。</summary>
    [Fact]
    public void ReadsAMissingMarkAsNotManual()
    {
        File.WriteAllText(
            _paths.AvatarRegistryFile,
            """{ "entries": [], "baseGroups": [ { "name": "書いていない素体" } ] }""");

        var group = Assert.Single(_store.Avatars.Load().BaseGroups);
        Assert.False(group.IsManual);
    }

    /// <summary>
    /// 足す欄の候補：一覧にある素体・消した素体・初期辞書の素体。別名は出さない（同じ素体が2つに割れる）。
    /// </summary>
    [Fact]
    public void SuggestsExistingDeletedAndSeedNamesButNotAliases()
    {
        var registry = new AvatarRegistry
        {
            BaseGroups =
            [
                new AvatarBaseGroup
                {
                    Name = "ある素体",
                    Aliases = [new AvatarAlias { Text = "ある素体の別名" }],
                },
                new AvatarBaseGroup { Name = "消した素体", Rejected = true },
            ],
        };

        var names = AvatarService.BaseNameCandidates(registry);

        Assert.Contains("ある素体", names);
        Assert.Contains("消した素体", names);
        Assert.Contains(AvatarBaseSeed.Groups[0].Name, names);
        Assert.DoesNotContain("ある素体の別名", names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count());
    }

    /// <summary>
    /// 商品ページの「＋ 追加」に混ぜる素体の候補（ユーザ判断 2026-09-28）：登録簿の素体のうち削除していない物で、
    /// この商品にまだ付いていない物。外した素体は選び直せるように残す。初期辞書だけの素体は出さない。
    /// </summary>
    [Fact]
    public void ItemBaseCandidatesSkipDeletedAndAttachedButKeepRejectedLinks()
    {
        var registry = new AvatarRegistry
        {
            BaseGroups =
            [
                new AvatarBaseGroup { Name = "付いている素体" },
                new AvatarBaseGroup { Name = "外した素体" },
                new AvatarBaseGroup { Name = "まだの素体" },
                new AvatarBaseGroup { Name = "削除した素体", Rejected = true },
            ],
        };
        AvatarBaseLink[] links =
        [
            new() { BaseName = "付いている素体", Source = AvatarLinkSource.Tag, Confirmed = true },
            new() { BaseName = "外した素体", Source = AvatarLinkSource.Manual, Confirmed = true, Rejected = true },
        ];

        var names = AvatarService.ItemBaseCandidates(registry, links);

        Assert.Equal(2, names.Count);
        Assert.Contains("まだの素体", names);
        Assert.Contains("外した素体", names);
        Assert.DoesNotContain(AvatarBaseSeed.Groups[0].Name, names);
    }

    /// <summary>選んだ素体は手で付けた印（Manual・確定）で足す。次の検出で作り直されないように。</summary>
    [Fact]
    public void WithManualBaseLinkAddsManualConfirmedLink()
    {
        AvatarBaseLink[] links = [new() { BaseName = "元からの素体", Source = AvatarLinkSource.Tag, Confirmed = true }];

        var result = AvatarService.WithManualBaseLink(links, "足す素体");

        Assert.Equal(2, result.Count);
        Assert.Equal(links[0], result[0]);
        var added = result[1];
        Assert.Equal("足す素体", added.BaseName);
        Assert.Equal(AvatarLinkSource.Manual, added.Source);
        Assert.True(added.Confirmed);
        Assert.False(added.Rejected);
    }

    /// <summary>外した素体を選び直すと、行を増やさずに消した印を下ろす（「消したもの」の［戻す］と同じ）。</summary>
    [Fact]
    public void WithManualBaseLinkRestoresRejectedLinkWithoutDuplicating()
    {
        AvatarBaseLink[] links =
        [
            new() { BaseName = "外した素体", Source = AvatarLinkSource.Manual, Confirmed = true, Rejected = true },
        ];

        var result = AvatarService.WithManualBaseLink(links, "外した素体");

        var link = Assert.Single(result);
        Assert.False(link.Rejected);
        Assert.Equal(AvatarLinkSource.Manual, link.Source);
        Assert.True(link.Confirmed);
    }
}
