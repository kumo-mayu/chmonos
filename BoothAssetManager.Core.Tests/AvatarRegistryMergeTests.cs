using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 検出の途中に人が保存したものが、検出の終わりに消えないこと（U15）。
///
/// 検出は始めに登録簿を読み、数十分かけて結果を組み立ててから書く（友人データの初回で約37分）。
/// 以前は始めに読んだ写しで丸ごと上書きしていたので、その間にアバター画面で保存した
/// 名前・メモ・所有・素体が黙って元に戻っていた。
/// </summary>
public class AvatarRegistryMergeTests : IDisposable
{
    private const string AvatarId = "1111";
    private const string ItemId = "999";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public AvatarRegistryMergeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-merge-" + Guid.NewGuid().ToString("N"));
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

        GC.SuppressFinalize(this);
    }

    /// <summary>検出が読み終えた後、最初の進み具合の知らせで1回だけ人の操作を差し込む。</summary>
    private sealed class EditOnce(Action edit) : IProgress<AvatarDetectProgress>
    {
        private bool _done;

        public void Report(AvatarDetectProgress value)
        {
            if (_done)
            {
                return;
            }

            _done = true;
            edit();
        }
    }

    private static AvatarRegistryEntry Avatar(string id = AvatarId) => new()
    {
        ItemId = id,
        BoothName = "オリジナル3Dモデル「マヌカ」",
        Category = "3Dキャラクター",
        CheckedAt = DateTimeOffset.Now,
    };

    private async Task SeedAsync()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry { Entries = [Avatar()] });

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

    /// <summary>**U15 の本体。**検出の途中に保存した名前・メモ・所有・素体が、検出の後も残る。</summary>
    [Fact]
    public async Task KeepsWhatWasSavedWhileDetecting()
    {
        await SeedAsync();
        var service = new AvatarService(_store);

        await service.DetectAsync(new EditOnce(() =>
        {
            service.SetDisplayNameAsync(AvatarId, "手で付けた名前").GetAwaiter().GetResult();
            service.SetMemoAsync(AvatarId, "体型が違う").GetAwaiter().GetResult();
            service.SetOwnedManuallyAsync(AvatarId, true).GetAwaiter().GetResult();
            service.SetBaseAsync(AvatarId, "テスト素体").GetAwaiter().GetResult();
        }));

        var entry = _store.Avatars.Load().Entries.Single(entry => entry.ItemId == AvatarId);
        Assert.Equal("手で付けた名前", entry.DisplayName);
        Assert.Equal("体型が違う", entry.Memo);
        Assert.True(entry.IsOwnedManually);
        Assert.Equal("テスト素体", entry.BaseName);
        Assert.Contains(_store.Avatars.Load().BaseGroups, group => group.Name == "テスト素体");

        // 検出の結果も入っている（重ねただけで、捨ててはいない）
        Assert.True(entry.SeenAs.ContainsKey(nameof(AvatarLinkSource.SupportSection)));
    }

    /// <summary>
    /// 呼び名そのものを名前に付けても、保存した名前で出る。
    /// 以前は以前の版の自動の名前と見分けられず、保存しても元の名前に戻って見えた（U15 の後も未所持のアバターで報告）。
    /// </summary>
    [Fact]
    public async Task KeepsANameThatLooksLikeTheOldAutomaticOne()
    {
        await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    BoothName = "【VRChat対応3Dモデル】ミモザ -V2.0",
                    Category = "3Dキャラクター",
                    Aliases = [new AvatarAlias { Text = "ミモ" }],
                },
            ],
        });
        var service = new AvatarService(_store);

        await service.SetDisplayNameAsync(AvatarId, "ミモ");

        var named = _store.Avatars.Load().Entries.Single(entry => entry.ItemId == AvatarId);
        Assert.Equal("ミモ", AvatarNames.ShownName(named));

        // 空で保存すると自動の名前に戻る
        await service.SetDisplayNameAsync(AvatarId, "");

        var cleared = _store.Avatars.Load().Entries.Single(entry => entry.ItemId == AvatarId);
        Assert.Null(cleared.DisplayName);
        Assert.Equal("ミモザ", AvatarNames.ShownName(cleared));
    }

    /// <summary>検出が変えなかった観測値は、その間に「BOOTHに確認し直す」で入った最新の値を残す。</summary>
    [Fact]
    public void KeepsObservedValuesTheDetectionDidNotChange()
    {
        var snapshot = new AvatarRegistry { Entries = [Avatar() with { Category = null }] };
        var latest = new AvatarRegistry { Entries = [Avatar() with { Category = "3Dキャラクター" }] };
        var detected = new Dictionary<string, AvatarRegistryEntry> { [AvatarId] = Avatar() with { Category = null } };

        var merged = AvatarService.MergeDetected(latest, snapshot, detected, new Dictionary<string, AvatarBaseGroup>());

        Assert.Equal("3Dキャラクター", merged.Entries.Single().Category);
    }

    /// <summary>検出が新しく観測した値は重ねる。</summary>
    [Fact]
    public void TakesObservedValuesTheDetectionChanged()
    {
        var snapshot = new AvatarRegistry { Entries = [Avatar() with { ShopName = null }] };
        var detected = new Dictionary<string, AvatarRegistryEntry> { [AvatarId] = Avatar() with { ShopName = "ショップ" } };

        var merged = AvatarService.MergeDetected(snapshot, snapshot, detected, new Dictionary<string, AvatarBaseGroup>());

        Assert.Equal("ショップ", merged.Entries.Single().ShopName);
    }

    /// <summary>
    /// 別名：検出の間に手で消した手動の別名は復活させない。消した印（Rejected）は最新の方を使う。
    /// 検出の間に手で足した別名も残す。検出が数え直した数は重ねる。
    /// </summary>
    [Fact]
    public void KeepsAliasDecisionsMadeWhileDetecting()
    {
        var manual = new AvatarAlias { Text = "手動", Source = nameof(AvatarLinkSource.Manual) };
        var learned = new AvatarAlias { Text = "まぬか", Count = 2, Source = nameof(AvatarLinkSource.Tag) };
        var added = new AvatarAlias { Text = "足した", Source = nameof(AvatarLinkSource.Manual) };

        var snapshot = new AvatarRegistry { Entries = [Avatar() with { Aliases = [manual, learned] }] };
        var latest = new AvatarRegistry { Entries = [Avatar() with { Aliases = [learned with { Rejected = true }, added] }] };
        var detected = new Dictionary<string, AvatarRegistryEntry>
        {
            [AvatarId] = Avatar() with { Aliases = [manual, learned with { Count = 5 }] },
        };

        var aliases = AvatarService.MergeDetected(latest, snapshot, detected, new Dictionary<string, AvatarBaseGroup>())
            .Entries.Single().Aliases;

        Assert.DoesNotContain(aliases, alias => alias.Text == "手動");
        var merged = Assert.Single(aliases, alias => alias.Text == "まぬか");
        Assert.True(merged.Rejected);
        Assert.Equal(5, merged.Count);
        Assert.Contains(aliases, alias => alias.Text == "足した");
    }

    /// <summary>
    /// 素体のグループ：検出が新しく作ったものだけを足す。
    /// 写しにあって最新に無いグループは、検出の間に人が消したか改名したものなので戻さない。
    /// </summary>
    [Fact]
    public void AddsOnlyGroupsTheDetectionCreated()
    {
        var snapshot = new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "旧い名前" }] };
        var latest = new AvatarRegistry { BaseGroups = [new AvatarBaseGroup { Name = "新しい名前" }] };
        var detectedGroups = new Dictionary<string, AvatarBaseGroup>
        {
            ["旧い名前"] = new() { Name = "旧い名前" },
            ["検出が見つけた素体"] = new() { Name = "検出が見つけた素体" },
        };

        var names = AvatarService.MergeDetected(latest, snapshot, new Dictionary<string, AvatarRegistryEntry>(), detectedGroups)
            .BaseGroups.Select(group => group.Name).ToList();

        Assert.Contains("新しい名前", names);
        Assert.Contains("検出が見つけた素体", names);
        Assert.DoesNotContain("旧い名前", names);
    }

    /// <summary>検出の間に別の道（改変を作る）で登録簿に入ったアバターも残す。</summary>
    [Fact]
    public void KeepsEntriesAddedWhileDetecting()
    {
        var latest = new AvatarRegistry { Entries = [Avatar(), Avatar("2222")] };
        var detected = new Dictionary<string, AvatarRegistryEntry> { [AvatarId] = Avatar() };

        var merged = AvatarService.MergeDetected(latest, new AvatarRegistry { Entries = [Avatar()] }, detected, new Dictionary<string, AvatarBaseGroup>());

        Assert.Contains(merged.Entries, entry => entry.ItemId == "2222");
    }

    /// <summary>同時に書いても、錠で1本ずつになるので取りこぼさない。</summary>
    [Fact]
    public async Task UpdateAsyncDoesNotLoseConcurrentWrites()
    {
        var writes = Enumerable.Range(0, 20).Select(index => Task.Run(() => _store.Avatars.UpdateAsync(registry =>
            new AvatarRegistry { Entries = [.. registry.Entries, Avatar(index.ToString())] })));

        await Task.WhenAll(writes);

        Assert.Equal(20, _store.Avatars.Load().Entries.Count);
    }
}
