using Chmonos.Core.Scanning;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定の件数を登録する回数で数える（ユーザ指示 2026-09-29）。
/// 画面の登録の単位（zip・展開物の根・1ファイル）と同じ数え方になっているかを見る。
/// </summary>
public class UnresolvedUnitsTests
{
    private static readonly ArchiveOrigin Outfit = new("Outfit_v1.zip", @"D:\DL\Outfit_v1.zip");

    [Fact]
    public void CountsAZipAndItsContentsAsOne()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("a1", Outfit, null),
            UnresolvedUnits.KeyOf("b2", Outfit, null),
            UnresolvedUnits.KeyOf("c3", Outfit, null),
        };

        Assert.Equal(1, UnresolvedUnits.Count(keys));
    }

    /// <summary>別々の場所にある同じ名前のzipの中身は、画面の束（zipの名前）と同じく1つにまとまる。</summary>
    [Fact]
    public void GroupsByTheZipNameIgnoringCase()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("a1", Outfit, null),
            UnresolvedUnits.KeyOf("b2", new ArchiveOrigin("OUTFIT_V1.ZIP", @"E:\old\OUTFIT_V1.ZIP"), null),
        };

        Assert.Equal(1, UnresolvedUnits.Count(keys));
    }

    [Fact]
    public void CountsEachZipSeparately()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("a1", Outfit, null),
            UnresolvedUnits.KeyOf("b2", new ArchiveOrigin("Hair.zip", @"D:\DL\Hair.zip"), null),
        };

        Assert.Equal(2, UnresolvedUnits.Count(keys));
    }

    [Fact]
    public void CountsAnUnpackedFolderWithoutItsZipAsOne()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("a1", null, @"D:\Assets\Outfit"),
            UnresolvedUnits.KeyOf("b2", null, @"D:\Assets\Outfit"),
            UnresolvedUnits.KeyOf("c3", null, @"d:\assets\outfit"),
        };

        Assert.Equal(1, UnresolvedUnits.Count(keys));
    }

    /// <summary>まとまる先の無いファイルは、同じフォルダにあっても別々の商品であり得るので1件ずつ。</summary>
    [Fact]
    public void CountsLooseFilesOneByOne()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("a1", null, null),
            UnresolvedUnits.KeyOf("b2", null, null),
            UnresolvedUnits.KeyOf("c3", null, string.Empty),
        };

        Assert.Equal(3, UnresolvedUnits.Count(keys));
    }

    /// <summary>種類の違う鍵がたまたま同じ文字にならない（zipの名前とフォルダ・ハッシュ）。</summary>
    [Fact]
    public void KeepsKindsApart()
    {
        var keys = new[]
        {
            UnresolvedUnits.KeyOf("x", new ArchiveOrigin("same", @"D:\same"), null),
            UnresolvedUnits.KeyOf("y", null, "same"),
            UnresolvedUnits.KeyOf("same", null, null),
        };

        Assert.Equal(3, UnresolvedUnits.Count(keys));
    }

    [Fact]
    public void CountsNothingAsZero()
        => Assert.Equal(0, UnresolvedUnits.Count([]));
}
