using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Tests;

public sealed class ImageRoleTests
{
    private static OrderedImage Booth(string name) => new(@"D:\images\" + name, ImageOrigin.Booth);

    private static OrderedImage Mine(string name) => new(@"D:\images\" + name, ImageOrigin.UserAdded);

    private static OrderedImage Gone(string name) => new(@"D:\images\" + name, ImageOrigin.Orphaned);

    private static Dictionary<string, ImageRole> Roles(params (string Name, ImageRole Role)[] entries)
        => entries.ToDictionary(entry => entry.Name, entry => entry.Role);

    // ---- 役割の決まり方 ----

    [Fact]
    public void 付けていないBOOTHの画像はBOOTH扱い()
        => Assert.Equal(ImageRole.Booth, ItemImageOrder.RoleOf(Booth("a.webp"), null));

    [Fact]
    public void 付けていない自分の画像はその他扱い()
        => Assert.Equal(ImageRole.Other, ItemImageOrder.RoleOf(Mine("user-1.webp"), null));

    [Fact]
    public void 消えた画像も出どころはBOOTHのまま()
    {
        // 消えたことは並びと札で示している。役割の話ではない
        Assert.Equal(ImageRole.Booth, ItemImageOrder.RoleOf(Gone("b.webp"), null));
    }

    [Fact]
    public void 付けた役割が出どころより優先される()
    {
        var roles = Roles(("a.webp", ImageRole.Modified));

        Assert.Equal(ImageRole.Modified, ItemImageOrder.RoleOf(Booth("a.webp"), roles));
    }

    [Fact]
    public void 改変例は自動では付かない()
    {
        // どれが改変後の姿かは人にしか分からない
        Assert.NotEqual(ImageRole.Modified, ItemImageOrder.RoleOf(Mine("user-1.webp"), null));
    }

    [Fact]
    public void 大文字小文字を無視して役割を引く()
    {
        // JSONの辞書は比較の仕方を持たないので、手で直したファイルからは素の比較になる
        var roles = Roles(("A.WEBP", ImageRole.Modified));

        Assert.Equal(ImageRole.Modified, ItemImageOrder.RoleOf(Booth("a.webp"), roles));
    }

    // ---- サムネイルの選び方 ----

    [Fact]
    public void デフォルトのときは指名が勝つ()
    {
        var ordered = new[] { Booth("a.webp"), Booth("b.webp"), Mine("user-1.webp") };

        var picked = ItemImageOrder.Thumbnail(ordered, "b.webp", ThumbnailRole.Default, null);

        Assert.EndsWith("b.webp", picked);
    }

    [Fact]
    public void デフォルトで指名が無ければ並びの1枚目()
    {
        var ordered = new[] { Booth("a.webp"), Mine("user-1.webp") };

        Assert.EndsWith("a.webp", ItemImageOrder.Thumbnail(ordered, null, ThumbnailRole.Default, null));
    }

    [Fact]
    public void 役割を選んでいるときは指名より役割が勝つ()
    {
        // 「改変例を出す」と決めたのに、★を付けた商品だけ別の絵になるのは筋が通らない
        var ordered = new[] { Booth("a.webp"), Mine("user-1.webp") };
        var roles = Roles(("user-1.webp", ImageRole.Modified));

        var picked = ItemImageOrder.Thumbnail(ordered, "a.webp", ThumbnailRole.Modified, roles);

        Assert.EndsWith("user-1.webp", picked);
    }

    [Fact]
    public void 役割の中では並びの先頭を採る()
    {
        var ordered = new[] { Booth("a.webp"), Booth("b.webp") };

        var picked = ItemImageOrder.Thumbnail(ordered, null, ThumbnailRole.Booth, null);

        Assert.EndsWith("a.webp", picked);
    }

    [Fact]
    public void その役割が無ければ普通のサムネイルに戻す()
    {
        // カードが空欄になると、絵が無いのか役割が付いていないのか読めない
        var ordered = new[] { Booth("a.webp"), Booth("b.webp") };

        var picked = ItemImageOrder.Thumbnail(ordered, "b.webp", ThumbnailRole.Modified, null);

        Assert.EndsWith("b.webp", picked);
    }

    [Fact]
    public void 役割が無く指名も無ければ1枚目に戻す()
    {
        var ordered = new[] { Booth("a.webp") };

        Assert.EndsWith("a.webp", ItemImageOrder.Thumbnail(ordered, null, ThumbnailRole.Modified, null));
    }

    [Fact]
    public void 画像が無ければnull()
        => Assert.Null(ItemImageOrder.Thumbnail([], "a.webp", ThumbnailRole.Modified, null));

    [Fact]
    public void その他を選ぶと自分で足した画像が出る()
    {
        var ordered = new[] { Booth("a.webp"), Mine("user-1.webp") };

        var picked = ItemImageOrder.Thumbnail(ordered, null, ThumbnailRole.Other, null);

        Assert.EndsWith("user-1.webp", picked);
    }

    [Fact]
    public void BOOTHを選ぶと自分の画像は選ばれない()
    {
        var ordered = new[] { Mine("user-1.webp"), Booth("a.webp") };

        var picked = ItemImageOrder.Thumbnail(ordered, null, ThumbnailRole.Booth, null);

        Assert.EndsWith("a.webp", picked);
    }

    [Theory]
    [InlineData(ThumbnailRole.Default, null)]
    [InlineData(ThumbnailRole.Booth, ImageRole.Booth)]
    [InlineData(ThumbnailRole.Modified, ImageRole.Modified)]
    [InlineData(ThumbnailRole.Other, ImageRole.Other)]
    public void 設定を役割に読み替える(ThumbnailRole setting, ImageRole? expected)
        => Assert.Equal(expected, Models.ImageRoles.AsImageRole(setting));
}
