using Chmonos.Core.Storage;
using Chmonos.Core.Images;
using Xunit;

namespace Chmonos.Core.Tests;

public class ImagePipelineTests
{
    /// <summary>
    /// 商品JSONに入っているのは48x48のURLだけ。BOOTHのCDNは決まったサイズしか
    /// 返さない（実測で 48 / 128 / 150 と原寸のみが200）ので、150へ差し替える。
    /// </summary>
    [Fact]
    public void RewritesTheIconUrlToTheLargerSize()
    {
        const string Small = "https://booth.pximg.net/c/48x48/users/8465772/icon_image/abc_base_resized.jpg";

        Assert.Equal(
            "https://booth.pximg.net/c/150x150/users/8465772/icon_image/abc_base_resized.jpg",
            ImagePipeline.LargerIconUrl(Small));
    }

    /// <summary>知らない形のURLは触らない。勝手に壊すより、そのまま取りに行く方がまし。</summary>
    [Fact]
    public void LeavesUnknownUrlShapesAlone()
    {
        const string Plain = "https://booth.pximg.net/users/8465772/icon_image/abc_base_resized.jpg";

        Assert.Equal(Plain, ImagePipeline.LargerIconUrl(Plain));
        Assert.Equal("https://example.com/icon.png", ImagePipeline.LargerIconUrl("https://example.com/icon.png"));
    }

    /// <summary>
    /// アイコンの保存名は元URLで決まる。
    /// ショップが差し替えるとURLが変わるので、別ファイルになって落とし直される
    /// （時間で確かめ直さなくても、次のitem取得で自動的に追いつく）。
    /// </summary>
    [Fact]
    public void NamesTheIconAfterItsSourceUrl()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "bam-icon-name"));

        var before = paths.ShopIconFile("shop", "https://booth.pximg.net/c/48x48/users/1/icon_image/aaa.jpg");
        var after = paths.ShopIconFile("shop", "https://booth.pximg.net/c/48x48/users/1/icon_image/bbb.jpg");

        Assert.NotEqual(before, after);
        Assert.StartsWith("shop_", Path.GetFileName(before));

        // 同じURLなら何度呼んでも同じ名前
        Assert.Equal(before, paths.ShopIconFile("shop", "https://booth.pximg.net/c/48x48/users/1/icon_image/aaa.jpg"));
    }

    /// <summary>既に別のサイズが入っていても150へ寄せる。</summary>
    [Fact]
    public void NormalizesAnyExistingSize()
    {
        Assert.Equal(
            "https://booth.pximg.net/c/150x150/users/1/icon_image/x.jpg",
            ImagePipeline.LargerIconUrl("https://booth.pximg.net/c/620x620/users/1/icon_image/x.jpg"));
    }
}
