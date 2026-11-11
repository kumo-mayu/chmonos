using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>選んだ商品の間でファイルを割り振る画面（plan 30）の材料：選んだ商品のファイルを中身ごとにまとめ、持ち主と重複回数を返す。</summary>
public sealed class FilePoolTests
{
    private static ItemRecord Item(string id, params LocalFileRecord[] files) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物" + id },
        Local = new LocalBlock { LocalFiles = files },
    };

    private static LocalFileRecord File(string hash, long size, params string[] paths)
        => new() { Hash = hash, Paths = paths, SizeBytes = size };

    [Fact]
    public void ファイルの溜まりは中身ごとに持ち主と重複回数を返す()
    {
        var shared = File("H-SET", 100, @"D:\作り物\Set.zip");
        var pool = FilePool.Of(
        [
            Item("1", shared, File("H-A", 10, @"D:\作り物\Alpha.zip")),
            Item("2", shared with { Paths = [@"E:\控え\Set.zip"] }),
            Item("3", File("H-B", 20, @"D:\作り物\Beta.zip") with { Detached = true }, File("H-NONAME", 5)),
        ]);

        Assert.Equal(["Alpha.zip", "Set.zip", null], pool.Select(file => file.FileName));
        var set = pool[1];
        Assert.Equal(["1", "2"], set.HolderIds);
        Assert.Equal(2, set.HolderCount);
        Assert.Equal([@"D:\作り物\Set.zip", @"E:\控え\Set.zip"], set.Paths);
        Assert.Equal(1, pool[0].HolderCount);

        // 外した行は持ち主に数えない。場所を覚えていない物は名前が無いまま後ろへ
        Assert.DoesNotContain(pool, file => file.Hash == "H-B");
        Assert.Equal(["3"], pool[2].HolderIds);
    }

    /// <summary>同じ商品を2回渡しても、持ち主は1回（検索の選択から来るので、重なりは呼ぶ側でなくここで均す）。</summary>
    [Fact]
    public void 同じ商品を2回渡しても持ち主は1回()
    {
        var item = Item("1", File("H", 1, @"D:\a.zip"));

        Assert.Equal(1, Assert.Single(FilePool.Of([item, item])).HolderCount);
    }
}
