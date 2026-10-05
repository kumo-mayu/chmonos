using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 共通素体の側からアバターを外す（メモ46・ユーザ判断 2026-10-05 1-B）。
///
/// 名前から推した仲間は保存しない値なので、外すには「素体に入れない」の印（<see cref="AvatarRegistryEntry.NoBase"/>）を残す。
/// 印があれば推さず、素体を選び直せば印を下ろす。
/// </summary>
public class AvatarBaseRemovalTests : IDisposable
{
    /// <summary>名前の「#MARUBODY」から MARUBODY に推されるアバター。</summary>
    private const string Inferred = "9900101";

    /// <summary>名前に手掛かりが無く、手で素体を決めたアバター。</summary>
    private const string Manual = "9900102";

    /// <summary>名前から MARUBODY に推され、手でも MARUBODY に決めたアバター。</summary>
    private const string Both = "9900103";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarBaseRemovalTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-baseremoval-" + Guid.NewGuid().ToString("N"));
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

    private static AvatarRegistryEntry Avatar(string id, string boothName, string? baseName = null) => new()
    {
        ItemId = id,
        BoothName = boothName,
        Category = "3Dキャラクター",
        BaseName = baseName,
    };

    private Task SeedAsync() => _store.Avatars.SaveAsync(new AvatarRegistry
    {
        Entries =
        [
            Avatar(Inferred, "オリジナル3Dモデル「作り物A」 #MARUBODY"),
            Avatar(Manual, "オリジナル3Dモデル「作り物B」", baseName: "作り物の素体"),
            Avatar(Both, "オリジナル3Dモデル「作り物C」 #MARUBODY", baseName: "MARUBODY"),
        ],
        BaseGroups = [new AvatarBaseGroup { Name = "作り物の素体", IsManual = true }],
    });

    private AvatarRegistryEntry Entry(string id) => _store.Avatars.Load().Entries.Single(entry => entry.ItemId == id);

    private IReadOnlyList<string> Members(string baseName)
        => AvatarCompatibilityIndex.Build(_store.Avatars.Load()).MembersOf(baseName);

    [Fact]
    public async Task 推した仲間を外すと_印が立って素体の一覧から消える()
    {
        await SeedAsync();
        Assert.Contains(Inferred, Members("MARUBODY"));

        await _service.RemoveFromBaseAsync(Inferred, "MARUBODY");

        Assert.True(Entry(Inferred).NoBase);
        Assert.Null(Entry(Inferred).BaseName);
        Assert.DoesNotContain(Inferred, Members("MARUBODY"));
    }

    [Fact]
    public async Task 手で決めた所属を外すと_素体名が空になり印は立てない()
    {
        await SeedAsync();

        await _service.RemoveFromBaseAsync(Manual, "作り物の素体");

        Assert.Null(Entry(Manual).BaseName);
        Assert.False(Entry(Manual).NoBase);
        Assert.DoesNotContain(Manual, Members("作り物の素体"));
    }

    /// <summary>空にしただけでは名前から同じ素体に推され、一覧に残って外れたように見えない。</summary>
    [Fact]
    public async Task 手で決めた所属が名前からも推されるときは_印も立てて外す()
    {
        await SeedAsync();

        await _service.RemoveFromBaseAsync(Both, "MARUBODY");

        Assert.Null(Entry(Both).BaseName);
        Assert.True(Entry(Both).NoBase);
        Assert.DoesNotContain(Both, Members("MARUBODY"));
    }

    /// <summary>画面が読んだ後に所属が変わっていたら、その素体に入っていないので何も書かない。</summary>
    [Fact]
    public async Task 別の素体に入っているアバターは外さない()
    {
        await SeedAsync();

        await _service.RemoveFromBaseAsync(Manual, "MARUBODY");

        Assert.Equal("作り物の素体", Entry(Manual).BaseName);
        Assert.False(Entry(Manual).NoBase);
    }

    [Fact]
    public void 印があると名前から推さない()
    {
        var registry = new AvatarRegistry
        {
            Entries = [Avatar(Inferred, "オリジナル3Dモデル「作り物A」 #MARUBODY") with { NoBase = true }],
        };

        var index = AvatarCompatibilityIndex.Build(registry);

        Assert.Null(index.BaseNameOf(Inferred));
        Assert.Null(AvatarBaseKeys.InferBaseOf(registry.Entries[0], AvatarBaseKeys.Lookup([])));
    }

    [Fact]
    public async Task 素体を選び直すと印を下ろす()
    {
        await SeedAsync();
        await _service.RemoveFromBaseAsync(Inferred, "MARUBODY");

        await _service.SetBaseAsync(Inferred, "作り物の素体");

        Assert.False(Entry(Inferred).NoBase);
        Assert.Equal("作り物の素体", Entry(Inferred).BaseName);
        Assert.Contains(Inferred, Members("作り物の素体"));
    }

    /// <summary>JSON は人が読める形に保つ：印は立っているときだけ書き、名前で読める欄にする。</summary>
    [Fact]
    public async Task 印は立っているときだけ_noBaseとして書き出す()
    {
        await SeedAsync();
        await _service.RemoveFromBaseAsync(Inferred, "MARUBODY");

        var json = File.ReadAllText(Path.Combine(_root, "avatar-registry.json"));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"noBase\""));
        Assert.Contains("\"noBase\": true", json);
    }

    /// <summary>アバターの詳細で薄く出す推した素体は、手で決めた所属が無いときだけ持つ（メモ46-3）。</summary>
    [Fact]
    public async Task 一覧の要約は手で決めた所属が無いときだけ推した素体を持つ()
    {
        await SeedAsync();

        var summaries = await _service.LoadAsync();

        Assert.Equal("MARUBODY", summaries.Single(summary => summary.Entry.ItemId == Inferred).InferredBaseName);
        Assert.Null(summaries.Single(summary => summary.Entry.ItemId == Manual).InferredBaseName);
        Assert.Null(summaries.Single(summary => summary.Entry.ItemId == Both).InferredBaseName);
    }
}
