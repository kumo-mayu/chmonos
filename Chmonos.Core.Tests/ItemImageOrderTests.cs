using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Xunit;

namespace Chmonos.Core.Tests;

public class ItemImageOrderTests
{
    private const string Dir = @"C:\images\1";

    private static BoothImage Image(string url) => new() { OriginalUrl = url };

    private static string PathOf(string url) => Path.Combine(Dir, ImagePipeline.FileNameFor(url));

    /// <summary>
    /// 保存名はURLのハッシュなので、フォルダを名前順に読むと並びが乱数になる。
    /// BOOTHの並びを正にする。
    /// </summary>
    [Fact]
    public void FollowsBoothOrderNotFileNameOrder()
    {
        var urls = new[] { "https://x/a.png", "https://x/b.png", "https://x/c.png" };
        var images = urls.Select(Image).ToList();

        // わざとファイル名順（ハッシュ順）に並べて渡す
        var onDisk = urls.Select(PathOf).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

        var arranged = ItemImageOrder.Paths(Dir, images, onDisk, userImages: null, showRemoved: true);

        Assert.Equal(urls.Select(PathOf), arranged);
    }

    /// <summary>BOOTH側から消えた画像は消さずに末尾へ回し、印を付ける。</summary>
    [Fact]
    public void PutsOrphansLast()
    {
        var images = new[] { Image("https://x/a.png") };
        var orphan = PathOf("https://x/gone.png");
        var onDisk = new[] { orphan, PathOf("https://x/a.png") };

        var arranged = ItemImageOrder.Arrange(Dir, images, onDisk, userImages: null, showRemoved: true);

        Assert.Equal(PathOf("https://x/a.png"), arranged[0].Path);
        Assert.False(arranged[0].IsOrphaned);
        Assert.Equal(orphan, arranged[1].Path);
        Assert.True(arranged[1].IsOrphaned);
    }

    /// <summary>
    /// 消えた画像を見せない設定（既定。ユーザ判断 2026-10-07）では、BOOTH から消えた画像を並びに入れない。
    /// 作者が見せたくなくて外した画像のことがある。自分で足した画像は残す（記録に無くても名前で分かる物も）
    /// </summary>
    [Fact]
    public void LeavesOutRemovedBoothImagesWhenNotShown()
    {
        var images = new[] { Image("https://x/a.png") };
        var orphan = PathOf("https://x/gone.png");
        var mine = Path.Combine(Dir, "user-0a1b2c3d.webp");
        var onDisk = new[] { orphan, mine, PathOf("https://x/a.png") };

        var arranged = ItemImageOrder.Arrange(Dir, images, onDisk, userImages: null, showRemoved: false);

        Assert.Equal([PathOf("https://x/a.png"), mine], arranged.Select(image => image.Path));
        Assert.DoesNotContain(arranged, image => image.IsOrphaned);
    }

    /// <summary>まだ落としていない画像は並びから飛ばす（取得の途中で開いても崩れない）。</summary>
    [Fact]
    public void SkipsImagesNotYetDownloaded()
    {
        var images = new[] { Image("https://x/a.png"), Image("https://x/b.png") };
        var onDisk = new[] { PathOf("https://x/b.png") };

        var arranged = ItemImageOrder.Paths(Dir, images, onDisk, userImages: null, showRemoved: true);

        Assert.Equal([PathOf("https://x/b.png")], arranged);
    }

    /// <summary>同じURLが2回出てきても1枚として扱う。</summary>
    [Fact]
    public void DoesNotRepeatTheSameFile()
    {
        var images = new[] { Image("https://x/a.png"), Image("https://x/a.png") };
        var onDisk = new[] { PathOf("https://x/a.png") };

        Assert.Single(ItemImageOrder.Paths(Dir, images, onDisk, userImages: null, showRemoved: true));
    }

    [Fact]
    public void HandlesAnEmptyFolder()
        => Assert.Empty(ItemImageOrder.Paths(Dir, [Image("https://x/a.png")], [], userImages: null, showRemoved: true));
}
