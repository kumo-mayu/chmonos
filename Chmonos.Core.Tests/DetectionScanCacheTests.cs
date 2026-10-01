using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 検出の走査の控え（<c>AvatarService</c>）。変わっていない商品は走査し直さないが、
/// **走査に渡す物（説明・booth・登録簿・設定）のどれかが変われば、同じサービスの次の検出で読み直す**ことを確かめる。
/// 画面は1つの AvatarService を起動から持ち回るので、控えが古いまま残ると検出が変化を拾わなくなる。
/// </summary>
public sealed class DetectionScanCacheTests : IDisposable
{
    private const string AvatarId = "1111";
    private const string OtherAvatarId = "2222";
    private const string ItemId = "999";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-scan-cache-" + Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public DetectionScanCacheTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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
    }

    private static AvatarRegistryEntry Avatar(string id, string name) => new()
    {
        ItemId = id,
        BoothName = $"オリジナル3Dモデル「{name}」",
        Category = "3Dキャラクター",
        CheckedAt = DateTimeOffset.Now,
    };

    private async Task SetUpAsync(string html, IReadOnlyList<string>? tags = null)
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar(AvatarId, "マヌカ")] });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "ネイルチップ", Tags = tags ?? [] },
            Local = new LocalBlock(),
        });
        WriteHtml(html);
    }

    /// <summary>手で直したように、更新日時をはっきり動かして書く。</summary>
    private void WriteHtml(string html)
    {
        var path = _paths.ItemHtmlFile(ItemId);
        var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
        File.WriteAllText(path, html);
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    private async Task<IReadOnlyList<string>> AvatarsOfItemAsync()
        => (await _store.Items.LoadAsync(ItemId))!.Local.Avatars.Select(link => link.AvatarItemId).ToList();

    [Fact]
    public async Task ReadsTheDescriptionAgainWhenItChanges()
    {
        await SetUpAsync("<h2>説明</h2><p>ネイルです</p>");
        var service = new AvatarService(_store, new AppSettings());

        await service.DetectAsync();
        Assert.Empty(await AvatarsOfItemAsync());

        WriteHtml($"<h2>対応アバター</h2><p>マヌカ https://booth.pm/ja/items/{AvatarId}</p>");
        await service.DetectAsync();

        Assert.Equal([AvatarId], await AvatarsOfItemAsync());
    }

    /// <summary>booth を取り直した（タグが増えた）商品は、同じ説明のままでも走査し直す。</summary>
    [Fact]
    public async Task ReadsTheItemAgainWhenBoothChanges()
    {
        await SetUpAsync("<h2>説明</h2><p>ネイルです</p>");
        var service = new AvatarService(_store, new AppSettings());

        await service.DetectAsync();
        Assert.Empty(await AvatarsOfItemAsync());

        await _store.Items.ChangeBoothAsync(ItemId, booth => booth with { Tags = ["マヌカ"] });
        await service.DetectAsync();

        Assert.Equal([AvatarId], await AvatarsOfItemAsync());
    }

    /// <summary>登録簿にアバターが増えたら（索引が変わったら）、変わっていない商品も照合し直す。</summary>
    [Fact]
    public async Task MatchesAgainWhenTheRegistryGainsAnAvatar()
    {
        await SetUpAsync("<h2>説明</h2><p>ネイルです</p>", tags: ["セレスティア"]);
        var service = new AvatarService(_store, new AppSettings());

        await service.DetectAsync();
        Assert.Empty(await AvatarsOfItemAsync());

        await _store.Avatars.UpdateAsync(registry => new AvatarRegistry
        {
            Entries = [.. registry.Entries, Avatar(OtherAvatarId, "セレスティア")],
            BaseGroups = registry.BaseGroups,
        });
        await service.DetectAsync();

        Assert.Equal([OtherAvatarId], await AvatarsOfItemAsync());
    }

    /// <summary>読まない見出しの設定を外したら、その見出しの下を読み直す（設定は使うたびに今の値を読む）。</summary>
    [Fact]
    public async Task ReadsAgainWhenTheHeadingSettingsChange()
    {
        await SetUpAsync($"<h2>クレジット</h2><p>https://booth.pm/ja/items/{AvatarId}</p>");
        var settings = new AppSettings { AvatarIgnoredHeadings = ["クレジット"] };
        var service = new AvatarService(_store, () => settings);

        await service.DetectAsync();
        Assert.Empty(await AvatarsOfItemAsync());

        settings = settings with { AvatarIgnoredHeadings = [] };
        await service.DetectAsync();

        Assert.Equal([AvatarId], await AvatarsOfItemAsync());
    }

    /// <summary>同じサービスで何度走らせても、1回目と同じ結果になる（控えから返した結果で変わらない）。</summary>
    [Fact]
    public async Task RepeatedDetectionGivesTheSameResult()
    {
        await SetUpAsync($"<h2>対応アバター</h2><p>マヌカ https://booth.pm/ja/items/{AvatarId}</p>");
        var service = new AvatarService(_store, new AppSettings());

        var first = await service.DetectAsync();
        var afterFirst = await _store.Items.LoadAsync(ItemId);
        var second = await service.DetectAsync();

        Assert.Equal(first.AvatarsFound, second.AvatarsFound);
        Assert.Equal(0, second.ItemsUpdated);
        Assert.Same(afterFirst, await _store.Items.LoadAsync(ItemId));
    }
}
