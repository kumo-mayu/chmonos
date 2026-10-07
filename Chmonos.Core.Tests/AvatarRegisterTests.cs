using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品を、人がアバターと指定した物として登録簿に載せる（ユーザ判断 2026-10-07）。
/// 作者がカテゴリを3Dキャラクターにしていないアバターは、登録簿に載らず、アバターの管理から扱いを変える場所が無かった
/// </summary>
public sealed class AvatarRegisterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-avatar-register-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly DataStore _store;
    private readonly AvatarService _service;

    public AvatarRegisterTests()
    {
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
    }

    private Task SaveItemAsync(string id, string category) => _store.Items.SaveAsync(new ItemRecord
    {
        Id = id,
        Booth = new BoothBlock
        {
            Name = "作り物のアバター【オリジナル3Dモデル】",
            Category = new BoothCategory { Id = 208, Name = category },
            Shop = new BoothShop { Name = "作り物のショップ", Subdomain = "example-shop" },
            FetchedAt = DateTimeOffset.Now,
        },
        Local = new LocalBlock(),
    });

    [Fact]
    public async Task 登録簿に無い商品は_人の指定付きで作られ_アバターになる()
    {
        await SaveItemAsync("9901301", "3Dモデル（その他）");

        await _service.RegisterAvatarAsync("9901301");

        var entry = Assert.Single(_store.Avatars.Load().Entries);
        Assert.Equal("9901301", entry.ItemId);
        Assert.True(entry.AvatarOverride);
        Assert.Equal("作り物のショップ", entry.ShopName);
        Assert.Equal("3Dモデル（その他）", entry.Category);
        Assert.True(AvatarService.IsAvatar(entry));
    }

    [Fact]
    public async Task 既にある項目は_人の指定だけが付き_ほかは変わらない()
    {
        await SaveItemAsync("9901302", "3Dモデル（その他）");
        await _store.Avatars.UpdateAsync(registry => new AvatarRegistry
        {
            Entries = [new AvatarRegistryEntry { ItemId = "9901302", Category = "3Dモデル（その他）", BaseName = "作り物の素体", CheckedAt = DateTimeOffset.Now }],
        });

        await _service.RegisterAvatarAsync("9901302");

        var entry = Assert.Single(_store.Avatars.Load().Entries);
        Assert.True(entry.AvatarOverride);
        Assert.Equal("作り物の素体", entry.BaseName);
    }

    [Fact]
    public async Task 商品が無ければ何もしない()
    {
        await _service.RegisterAvatarAsync("9901399");

        Assert.Empty(_store.Avatars.Load().Entries);
    }
}
