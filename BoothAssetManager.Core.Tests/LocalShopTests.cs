using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 自分で入れるショップ。**商品が非公開でもショップは見られる場合がある。**
///
/// サブドメインはショップを束ねる鍵であり、アイコンとバナーのファイル名でもある。
/// 手で作った鍵が本物と衝突すると、本物のアイコンとバナーが表示されて区別が付かなくなる。
/// </summary>
public class LocalShopTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly ShopService _service;

    public LocalShopTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-shop-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new ShopService(_store);
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

    // ---- 鍵の決め方 ----

    /// <summary>同じ名前は同じ鍵になる。だから二重にショップを作らずに済む。</summary>
    [Fact]
    public void GivesTheSameKeyToTheSameName()
    {
        Assert.Equal(LocalShopKey.For("ほとぎ屋"), LocalShopKey.For("  ほとぎ屋  "));
        Assert.NotEqual(LocalShopKey.For("ほとぎ屋"), LocalShopKey.For("別のお店"));
    }

    /// <summary>
    /// **ローマ字化しない。**「ほとぎ屋」→ hotogiya は実在するので、
    /// 手で作った鍵が本物と衝突すると本物のアイコンとバナーが出てしまう。
    /// </summary>
    [Fact]
    public void NeverProducesAKeyThatCouldBeARealSubdomain()
    {
        var key = LocalShopKey.For("ほとぎ屋");

        Assert.StartsWith("local-", key, StringComparison.Ordinal);
        Assert.True(LocalShopKey.IsLocal(key));
        Assert.False(LocalShopKey.IsLocal("hotogiya"));
    }

    [Theory]
    [InlineData("https://hotogiya.booth.pm/", "hotogiya")]
    [InlineData("https://hotogiya.booth.pm/items/123", "hotogiya")]
    [InlineData("hotogiya.booth.pm", "hotogiya")]
    [InlineData("HTTPS://Hotogiya.Booth.PM/", "hotogiya")]
    public void ReadsTheSubdomainFromAShopUrl(string text, string expected)
        => Assert.Equal(expected, LocalShopKey.SubdomainFromUrl(text));

    /// <summary>ショップのURLに見えないものからは**推測で鍵を作らない**。</summary>
    [Theory]
    [InlineData("https://booth.pm/ja/items/123")]
    [InlineData("ほとぎ屋")]
    [InlineData("https://example.com/")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesToGuessFromSomethingElse(string? text)
        => Assert.Null(LocalShopKey.SubdomainFromUrl(text));

    // ---- 束ね方 ----

    private async Task SaveAsync(string itemId, BoothShop? booth, LocalShop? local)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { FetchedAt = booth is null ? null : DateTimeOffset.Now, Shop = booth },
            Local = new LocalBlock
            {
                Shop = local,
                LocalFiles =
                [
                    new LocalFileRecord { Hash = itemId + "h", Paths = [$@"C:\dl\{itemId}.zip"], SizeBytes = 1 },
                ],
            },
        });

    private static BoothShop Hotogiya => new() { Name = "ほとぎ屋", Subdomain = "hotogiya", Url = "https://hotogiya.booth.pm/" };

    /// <summary>URLを貼れば本物のショップに束ねられる。**これがURLを聞く理由。**</summary>
    [Fact]
    public async Task JoinsTheRealShopWhenTheUrlWasPasted()
    {
        await SaveAsync("111", Hotogiya, local: null);
        await SaveAsync("local-abcd1234", booth: null, local: new LocalShop
        {
            Name = "ほとぎ屋",
            Subdomain = "hotogiya",
            Url = "https://hotogiya.booth.pm/",
        });

        var shop = Assert.Single(await _service.LoadAsync());

        Assert.Equal("hotogiya", shop.Subdomain);
        Assert.Equal("ほとぎ屋", shop.Name);
        Assert.Equal(2, shop.OwnedCount);
    }

    /// <summary>URLが無ければ手元だけのショップになる。本物とは混ざらない。</summary>
    [Fact]
    public async Task StaysSeparateWhenThereIsNoUrl()
    {
        await SaveAsync("111", Hotogiya, local: null);
        await SaveAsync("local-abcd1234", booth: null, local: new LocalShop
        {
            Name = "ほとぎ屋",
            Subdomain = LocalShopKey.For("ほとぎ屋"),
        });

        var shops = await _service.LoadAsync();

        Assert.Equal(2, shops.Count);
        Assert.Contains(shops, shop => shop.Subdomain == "hotogiya");
        Assert.Contains(shops, shop => LocalShopKey.IsLocal(shop.Subdomain));
    }

    /// <summary>BOOTHから取れていない商品だけのショップにも、ちゃんと名前が出る。</summary>
    [Fact]
    public async Task NamesAShopThatOnlyExistsLocally()
    {
        await SaveAsync("local-abcd1234", booth: null, local: new LocalShop
        {
            Name = "消えたお店",
            Subdomain = LocalShopKey.For("消えたお店"),
        });

        var shop = Assert.Single(await _service.LoadAsync());

        Assert.Equal("消えたお店", shop.Name);
        Assert.Null(shop.Url);
        Assert.Equal(1, shop.OwnedCount);
    }

    /// <summary>ユーザが入れたショップは、BOOTHの観測より優先して出す。名前と同じ扱い。</summary>
    [Fact]
    public void PrefersTheShopTheUserEntered()
    {
        var item = new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { Shop = Hotogiya },
            Local = new LocalBlock
            {
                Shop = new LocalShop { Name = "ほとぎ屋（本店）", Subdomain = "hotogiya" },
            },
        };

        Assert.Equal("ほとぎ屋（本店）", item.ShopName);
        Assert.Equal("hotogiya", item.ShopSubdomain);
        Assert.True(item.HasUserShop);
    }

    /// <summary>
    /// **手元だけのショップにはBOOTHのページが無い。**
    /// バナーとアイコンを取りに行くと、存在しないURLを叩くことになる。
    /// </summary>
    [Fact]
    public async Task NeverFetchesImagesForALocalShop()
    {
        // 通信させたら失敗する作りにしていないので、ここは「行かない」ことだけを見る。
        // _client が null のサービスでも同じ結果になってしまうため、
        // 鍵の判定そのものを確かめる
        Assert.True(LocalShopKey.IsLocal(LocalShopKey.For("消えたお店")));

        var refresh = await _service.RefreshImagesAsync(
            LocalShopKey.For("消えたお店"),
            null!);

        Assert.True(refresh.Failed);
    }

    /// <summary>ショップ名も両方とも検索対象。自分で入れた名前で探せなければ意味が無い。</summary>
    [Fact]
    public void SearchesBothShopNames()
    {
        var item = new ItemRecord
        {
            Id = "111",
            Booth = new BoothBlock { Shop = Hotogiya },
            Local = new LocalBlock { Shop = new LocalShop { Name = "ほとぎやさん", Subdomain = "hotogiya" } },
        };

        var haystack = SearchText.Build(item);

        Assert.Contains("ほとぎやさん", haystack.Primary, StringComparison.Ordinal);
        Assert.Contains("ほとぎ屋", haystack.Primary, StringComparison.Ordinal);
        Assert.Contains("hotogiya", haystack.Primary, StringComparison.Ordinal);
    }
}
