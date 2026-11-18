using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 人が消した呼び方（<see cref="AvatarAlias.Rejected"/>）は、検出と検索の記法のほかに、探す欄・未確定の候補・変換の照合・更新の見分けでも使わない
/// （ユーザ判断 2026-11-18。spec <c>avatars.md</c>「照合にも一覧にも出さない」）。
/// どの試験も、消していない呼び方なら当たる事を同じ形で確かめ、印だけで結果が変わる事を見る。名前・ID は作り物。
/// </summary>
public sealed class RejectedAliasUseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-rejalias-" + Guid.NewGuid().ToString("N"));

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

    private static AvatarRegistryEntry Entry(bool rejected) => new()
    {
        ItemId = "9908001",
        DisplayName = "サクヤ",
        Category = "3Dキャラクター",
        Aliases = [new AvatarAlias { Text = "Zelkova", Source = "Tag", Rejected = rejected }],
    };

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 探す欄は消した呼び方で当てない(bool rejected, bool expected)
    {
        Assert.Equal(expected, AvatarSearch.Matches(Entry(rejected), "サクヤ", "zelk", out _));
        Assert.Equal(expected, AvatarSearch.Hints(Entry(rejected), "サクヤ").Any(hint => hint.Text == "Zelkova"));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void 未確定の登録簿の候補は消した呼び方で当てない(bool rejected, int expected)
        => Assert.Equal(expected, RegistryCandidates.For(@"C:\dl\Zelkova_outfit_v1.zip", [Entry(rejected)]).Count);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 変換はアバターの消した呼び方でプロファイルに当てない(bool rejected, bool expected)
    {
        // 対応アバターが2体あると、slug と名前の合う物だけを手掛かりにする
        var registry = new AvatarRegistry
        {
            Entries = [Entry(rejected), new AvatarRegistryEntry { ItemId = "9908002", DisplayName = "ミナモ", Category = "3Dキャラクター" }],
        };
        var item = Item(avatars: ["9908001", "9908002"], bases: []);

        var suggestion = ConversionInference.Suggest(item, [new ProfileSlug("zelkova", true, false)], registry, AvatarCompatibilityIndex.Build(registry)).Single();

        Assert.Equal(expected, suggestion.Clues.Any(clue => clue.Kind == ConversionClueKind.DetectedAvatar && clue.AvatarItemId == "9908001"));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 変換は素体の消した呼び方でプロファイルに当てない(bool rejected, bool expected)
    {
        var registry = new AvatarRegistry
        {
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体", Aliases = [new AvatarAlias { Text = "FakeBody", Rejected = rejected }] }],
        };

        var suggestion = ConversionInference.Suggest(Item(avatars: [], bases: []), [new ProfileSlug("FakeBody", true, false)], registry, AvatarCompatibilityIndex.Build(registry)).Single();

        Assert.Equal(expected, suggestion.Clues.Any(clue => clue.Kind == ConversionClueKind.SlugIsBaseName));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task 更新の見分けは消した呼び方を名前に数えない(bool rejected, bool expected)
    {
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        var store = new DataStore(paths);
        await store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries = [Entry(rejected)],
            BaseGroups = [new AvatarBaseGroup { Name = "作り物素体", Aliases = [new AvatarAlias { Text = "FakeBody", Rejected = rejected }] }],
        });

        var names = await new UsedUpdateRecorder(store, _ => Task.CompletedTask)
            .AvatarNamesAsync(Item(avatars: ["9908001"], bases: ["作り物素体"]), CancellationToken.None);

        Assert.Equal(expected, names.Contains("Zelkova"));
        Assert.Equal(expected, names.Contains("FakeBody"));
    }

    private static ItemRecord Item(IReadOnlyList<string> avatars, IReadOnlyList<string> bases) => new()
    {
        Id = "9908100",
        Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "作り物の衣装" },
        Local = new LocalBlock
        {
            Avatars = [.. avatars.Select(avatar => new AvatarLink { AvatarItemId = avatar, Source = AvatarLinkSource.SupportSection })],
            AvatarBases = [.. bases.Select(baseName => new AvatarBaseLink { BaseName = baseName })],
        },
    };
}
