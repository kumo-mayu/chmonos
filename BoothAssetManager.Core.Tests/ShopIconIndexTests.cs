using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// ショップ一覧の集計で、アイコンの置き場を1回だけ列挙して引く表。
/// 店ごとに探していた <see cref="AppPaths.FindShopIcon"/> と同じ答えになることを確かめる。
/// 日時はファイルに直接書き込むので、時計には左右されない。
/// </summary>
public class ShopIconIndexTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;

    public ShopIconIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-shopicon-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        Directory.CreateDirectory(_paths.ShopIconsDir);
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

    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private void Put(string name, int minutes)
    {
        var path = Path.Combine(_paths.ShopIconsDir, name);
        File.WriteAllBytes(path, [1]);
        File.SetLastWriteTimeUtc(path, Base.AddMinutes(minutes));
    }

    [Fact]
    public void AnswersTheSameAsLookingUpEachShop()
    {
        Put("alpha_11111111.webp", 1);
        Put("alpha_22222222.webp", 5);
        Put("alpha_33333333.webp", 3);
        Put("alpha_banner.webp", 9);
        Put("beta_44444444.webp", 2);
        Put("gamma_banner.webp", 1);
        Put("delta.webp", 1);
        Put("notes.txt", 1);

        var index = _paths.ReadShopIconIndex();

        foreach (var shop in new[] { "alpha", "beta", "gamma", "delta", "missing" })
        {
            Assert.Equal(_paths.FindShopIcon(shop), index.FindIcon(shop));
            Assert.Equal(File.Exists(_paths.ShopBannerFile(shop)), index.HasBanner(shop));
        }
    }

    /// <summary>名前にURLのハッシュが入るので、差し替えがあると複数残る。出すのは一番新しく取った物。</summary>
    [Fact]
    public void PicksTheNewestIcon()
    {
        Put("shop_aaaaaaaa.webp", 10);
        Put("shop_bbbbbbbb.webp", 30);
        Put("shop_cccccccc.webp", 20);

        Assert.Equal(
            Path.Combine(_paths.ShopIconsDir, "shop_bbbbbbbb.webp"),
            _paths.ReadShopIconIndex().FindIcon("shop"));
    }

    /// <summary>バナーはアイコンとして出さない。バナーだけの店はアイコン無し。</summary>
    [Fact]
    public void TellsBannersApartFromIcons()
    {
        Put("shop_banner.webp", 50);
        Put("shop_aaaaaaaa.webp", 10);
        Put("onlybanner_banner.webp", 10);

        var index = _paths.ReadShopIconIndex();

        Assert.Equal(Path.Combine(_paths.ShopIconsDir, "shop_aaaaaaaa.webp"), index.FindIcon("shop"));
        Assert.True(index.HasBanner("shop"));
        Assert.Null(index.FindIcon("onlybanner"));
        Assert.True(index.HasBanner("onlybanner"));
        Assert.False(index.HasBanner("other"));
    }

    /// <summary>
    /// 名前の最後の _ より前で店を分ける。前の探し方（{sub}_*.webp）は「sub_x」という店のアイコンも
    /// 拾う作りだったが、表では取り違えない。
    /// </summary>
    [Fact]
    public void DoesNotTakeAnotherShopsIconWhoseNameStartsTheSame()
    {
        Put("sub_x_aaaaaaaa.webp", 99);
        Put("sub_bbbbbbbb.webp", 1);
        Put("sub_x_banner.webp", 1);

        var index = _paths.ReadShopIconIndex();

        Assert.Equal(Path.Combine(_paths.ShopIconsDir, "sub_bbbbbbbb.webp"), index.FindIcon("sub"));
        Assert.False(index.HasBanner("sub"));
        Assert.Equal(Path.Combine(_paths.ShopIconsDir, "sub_x_aaaaaaaa.webp"), index.FindIcon("sub_x"));
    }

    /// <summary>ショップは大文字小文字を区別せずに束ねているので、引くときも区別しない。</summary>
    [Fact]
    public void IgnoresCase()
    {
        Put("Shop_aaaaaaaa.webp", 1);
        Put("SHOP_BANNER.webp", 1);

        var index = _paths.ReadShopIconIndex();

        Assert.Equal(Path.Combine(_paths.ShopIconsDir, "Shop_aaaaaaaa.webp"), index.FindIcon("shop"));
        Assert.True(index.HasBanner("shop"));
    }

    [Fact]
    public void IsEmptyWhenTheFolderIsMissing()
    {
        Directory.Delete(_paths.ShopIconsDir);

        var index = _paths.ReadShopIconIndex();

        Assert.Null(index.FindIcon("shop"));
        Assert.False(index.HasBanner("shop"));
    }
}
