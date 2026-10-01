using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 名前が取れないアバターの手掛かり。
///
/// BOOTHが404を返す項目は商品IDしか出せず、**IDと件数だけでは
/// 数字で判断させることになる。**何の商品が対応先として挙げているかを並べれば読める。
/// </summary>
public class AvatarReferenceTests : IDisposable
{
    private const string AvatarId = "4897493";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarReferenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-avref-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>名前を引けなかった項目（404）。category は null のまま。</summary>
    private async Task SaveNamelessAvatarAsync()
        => await _store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry
                {
                    ItemId = AvatarId,
                    Category = null,
                    CheckedAt = DateTimeOffset.Now,
                    AvatarOverride = true,
                    Aliases = [new AvatarAlias { Text = "くうた", Count = 3 }],
                },
            ],
        });

    private async Task SaveWearableAsync(string itemId, string name)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = name },
            Local = new LocalBlock
            {
                Avatars = [new AvatarLink { AvatarItemId = AvatarId, Confirmed = true }],
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = itemId + "hash",
                        Paths = [$@"C:\dl\{itemId}.zip"],
                        SizeBytes = 1,
                    },
                ],
            },
        });

    [Fact]
    public async Task NamesTheItemsThatPointAtTheAvatar()
    {
        await SaveNamelessAvatarAsync();
        await SaveWearableAsync("111", "【くうた対応】School sweater");

        var summary = Assert.Single(await _service.LoadAsync());

        Assert.Equal("【くうた対応】School sweater", Assert.Single(summary.ReferencedBy));
        Assert.Equal(1, summary.DirectCount);
    }

    /// <summary>並べすぎても読めないので数件で止める。件数は DirectCount が持っている。</summary>
    [Fact]
    public async Task StopsCollectingNamesButKeepsCounting()
    {
        await SaveNamelessAvatarAsync();
        foreach (var index in Enumerable.Range(0, 7))
        {
            await SaveWearableAsync($"20{index}", $"衣装 {index}");
        }

        var summary = Assert.Single(await _service.LoadAsync());

        Assert.Equal(7, summary.DirectCount);
        Assert.True(summary.ReferencedBy.Count is > 0 and < 7);
    }

    /// <summary>誰も挙げていなければ空。無い手掛かりを作らない。</summary>
    [Fact]
    public async Task StaysEmptyWhenNothingPointsAtIt()
    {
        await SaveNamelessAvatarAsync();

        Assert.Empty(Assert.Single(await _service.LoadAsync()).ReferencedBy);
    }

    /// <summary>
    /// ユーザが付けた名前の方を並べる。BOOTHに無い商品として登録した衣装でも
    /// 「何が挙げているか」は読めるべき。
    /// </summary>
    [Fact]
    public async Task UsesTheNameTheUserGaveForTheReferringItem()
    {
        await SaveNamelessAvatarAsync();
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "local-abcd1234",
            Booth = new BoothBlock(),
            Local = new LocalBlock
            {
                DisplayName = "謎のくうた用衣装",
                Avatars = [new AvatarLink { AvatarItemId = AvatarId, Confirmed = true }],
                LocalFiles =
                [
                    new LocalFileRecord { Hash = "abcd1234", Paths = [@"C:\dl\x.zip"], SizeBytes = 1 },
                ],
            },
        });

        var summary = Assert.Single(await _service.LoadAsync());

        Assert.Equal("謎のくうた用衣装", Assert.Single(summary.ReferencedBy));
    }
}
