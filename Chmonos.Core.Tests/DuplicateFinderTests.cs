using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

public sealed class DuplicateFinderTests
{
    private static ItemRecord Item(string id, LocalBlock local, string? name = null)
        => new()
        {
            Id = id,
            Booth = new BoothBlock
            {
                Name = name ?? ("item " + id),
                FetchedAt = DateTimeOffset.Now,
            },
            Local = local,
        };

    private static LocalFileRecord File(string hash, long size, params string[] paths)
        => new() { Hash = hash, SizeBytes = size, Paths = paths };

    private static LocalFolderRecord Folder(string path, long bytes)
        => new() { Path = path, TotalBytes = bytes, FileCount = 1 };

    [Fact]
    public void 重複が無ければ何も出さない()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 100, @"D:\a\one.zip")],
        });

        Assert.Empty(DuplicateFinder.Find([item]));
    }

    [Fact]
    public void 同じ商品の中で2箇所にあるものを出す()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 500, @"D:\a\one.zip", @"E:\backup\one.zip")],
        });

        var group = Assert.Single(DuplicateFinder.Find([item]));

        Assert.Equal(DuplicateKind.SameFile, group.Kind);
        Assert.Equal("one.zip", group.Label);
        Assert.Equal(2, group.Places.Count);
        Assert.Equal(500, group.ReclaimableBytes);
        Assert.False(group.CrossesItems);
    }

    [Fact]
    public void 商品をまたいだ同じハッシュを束ねる()
    {
        // これが数え漏れていた。StatsService は1商品の中でしか DistinctBy(Hash) していない
        var a = Item("1", new LocalBlock { LocalFiles = [File("AAA", 800, @"D:\a\shared.zip")] });
        var b = Item("2", new LocalBlock { LocalFiles = [File("AAA", 800, @"D:\b\shared.zip")] });

        var group = Assert.Single(DuplicateFinder.Find([a, b]));

        Assert.Equal(DuplicateKind.SameFile, group.Kind);
        Assert.True(group.CrossesItems);
        Assert.Equal(800, group.ReclaimableBytes);
        Assert.Equal(["1", "2"], group.Places.Select(place => place.ItemId));
    }

    [Fact]
    public void 場所が3つあれば2つ分が空く()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 100, @"D:\1\x.zip", @"D:\2\x.zip", @"D:\3\x.zip")],
        });

        Assert.Equal(200, DuplicateFinder.Find([item])[0].ReclaimableBytes);
    }

    [Fact]
    public void 同じパスを2回数えない()
    {
        // 商品をまたいで同じ実体を指していることがある。場所は1つ
        var a = Item("1", new LocalBlock { LocalFiles = [File("AAA", 100, @"D:\a\x.zip")] });
        var b = Item("2", new LocalBlock { LocalFiles = [File("AAA", 100, @"D:\A\X.ZIP")] });

        Assert.Empty(DuplicateFinder.Find([a, b]));
    }

    [Fact]
    public void zipと展開済フォルダの両方持ちを出す()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 200, @"D:\dl\Wendy_ver1.01.zip")],
            LocalFolders = [Folder(@"D:\dl\Wendy_ver1.01", 900)],
        });

        var group = Assert.Single(DuplicateFinder.Find([item]));

        Assert.Equal(DuplicateKind.ArchiveAndUnpacked, group.Kind);
        Assert.Equal("Wendy_ver1.01", group.Label);
        // 展開した方を消す前提。zipは配布物そのもので作り直せない
        Assert.Equal(900, group.ReclaimableBytes);
    }

    [Fact]
    public void 名前が対応しないフォルダは両方持ちにしない()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 200, @"D:\dl\Wendy_ver1.01.zip")],
            LocalFolders = [Folder(@"D:\dl\別のもの", 900)],
        });

        Assert.Empty(DuplicateFinder.Find([item]));
    }

    [Fact]
    public void 末尾の区切りがあってもフォルダ名を読める()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 200, @"D:\dl\Kipfel_1.2.0.zip")],
            LocalFolders = [Folder(@"D:\dl\Kipfel_1.2.0\", 700)],
        });

        Assert.Equal("Kipfel_1.2.0", Assert.Single(DuplicateFinder.Find([item])).Label);
    }

    [Fact]
    public void 別商品のzipと名前が一致しても両方持ちにしない()
    {
        // 同じ名前でも別の配布物。同じ商品の中だけで見る
        var a = Item("1", new LocalBlock { LocalFiles = [File("AAA", 200, @"D:\a\common.zip")] });
        var b = Item("2", new LocalBlock { LocalFolders = [Folder(@"D:\b\common", 900)] });

        Assert.Empty(DuplicateFinder.Find([a, b]));
    }

    [Fact]
    public void 空く量の大きい順に並べる()
    {
        var small = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 100, @"D:\1\a.zip", @"D:\2\a.zip")],
        });
        var big = Item("2", new LocalBlock
        {
            LocalFiles = [File("BBB", 5000, @"D:\1\b.zip", @"D:\2\b.zip")],
        });

        var found = DuplicateFinder.Find([small, big]);

        Assert.Equal(["b.zip", "a.zip"], found.Select(group => group.Label));
    }

    [Fact]
    public void 商品名を持ってくる()
    {
        var item = Item("1", new LocalBlock
        {
            LocalFiles = [File("AAA", 100, @"D:\1\x.zip", @"D:\2\x.zip")],
        }, name: "【オリジナル3Dモデル】Wendy");

        Assert.All(
            DuplicateFinder.Find([item])[0].Places,
            place => Assert.Equal("【オリジナル3Dモデル】Wendy", place.ItemName));
    }
}
