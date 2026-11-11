using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 2つ以上の商品をまとめた容量（plan 27・2026-11-11）。1つのファイルを2つの商品に付けられるので、商品ごとの値を足すと二重に数える。
/// 合計は同じ中身・同じ場所を1回だけ数える。商品1つの値は今までどおり。
/// </summary>
public sealed class LibrarySizesTests
{
    private static ItemRecord Item(string id, string? category = null, params LocalFileRecord[] files) => new()
    {
        Id = id,
        Booth = new BoothBlock { Name = "作り物" + id, Category = category is null ? null : new BoothCategory { Id = 1, Name = category } },
        Local = new LocalBlock { LocalFiles = files },
    };

    private static LocalFileRecord File(string hash, long size, params string[] paths)
        => new() { Hash = hash, Paths = paths, SizeBytes = size };

    [Fact]
    public void 同じ場所のファイルを2つの商品が持てば合計は1回分()
    {
        var shared = File("H-SET", 100, @"D:\作り物\Set.zip");
        IReadOnlyList<ItemRecord> items = [Item("1", null, shared), Item("2", null, shared, File("H-ONLY", 10, @"D:\作り物\単品.zip"))];

        // 商品ごとの値は、その商品が持つ物の大きさのまま
        Assert.Equal(100, items[0].OwnedSizeBytes);
        Assert.Equal(110, items[1].OwnedSizeBytes);

        Assert.Equal(110, LibrarySizes.Owned(items));
        Assert.Equal(110, LibrarySizes.OnDisk(items));
        Assert.Equal(0, LibrarySizes.DuplicateOnDisk(items));
    }

    /// <summary>同じ中身を別の場所に置いた写しは、商品をまたいでも実占有では2回・重複に1回（重複の一覧と同じ「1つ残せば空く量」）。</summary>
    [Fact]
    public void 同じ中身を別の場所に置けば論理は1回_実占有は場所ごと_重複は2つ目から()
    {
        IReadOnlyList<ItemRecord> items =
        [
            Item("1", null, File("H", 100, @"D:\a\Set.zip")),
            Item("2", null, File("H", 100, @"E:\b\Set.zip")),
        ];

        Assert.Equal(100, LibrarySizes.Owned(items));
        Assert.Equal(200, LibrarySizes.OnDisk(items));
        Assert.Equal(100, LibrarySizes.DuplicateOnDisk(items));
    }

    /// <summary>範囲は商品ごとの値と同じ：外した行・見つからない場所は実占有に数えない。見つからない物も論理には数える。</summary>
    [Fact]
    public void 外した行は数えず_見つからない物は実占有に数えない()
    {
        IReadOnlyList<ItemRecord> items =
        [
            Item("1", null, File("H", 100, @"D:\a.zip") with { Detached = true }),
            Item("2", null, File("G", 50, @"D:\g.zip") with { MissingSince = DateTimeOffset.Now }),
        ];

        Assert.Equal(50, LibrarySizes.Owned(items));
        Assert.Equal(0, LibrarySizes.OnDisk(items));
    }

    [Fact]
    public void 同じ場所の登録フォルダは1回分()
    {
        var folder = new LocalFolderRecord { Path = @"D:\作り物\展開", TotalBytes = 300 };
        IReadOnlyList<ItemRecord> items =
        [
            Item("1") with { Local = new LocalBlock { LocalFolders = [folder] } },
            Item("2") with { Local = new LocalBlock { LocalFolders = [folder with { Path = @"D:\作り物\展開\" }] } },
        ];

        Assert.Equal(300, LibrarySizes.Owned(items));
        Assert.Equal(300, LibrarySizes.OnDisk(items));
    }

    /// <summary>統計の合計とカテゴリの棒も1回分（前は商品ごとの値を足していた）。</summary>
    [Fact]
    public void 統計の合計とカテゴリの棒は共有したファイルを1回だけ数える()
    {
        var shared = File("H-SET", 100, @"D:\作り物\Set.zip");
        var snapshot = StatsService.Build(
            [Item("1", "衣装", shared), Item("2", "衣装", shared), Item("3", "小物", shared)],
            new AvatarRegistry(),
            unresolvedCount: 0);

        Assert.Equal(100, snapshot.LogicalBytes);
        Assert.Equal(100, snapshot.PhysicalBytes);
        Assert.Equal(0, snapshot.DuplicateBytes);

        // カテゴリの中では1回、カテゴリどうしでは重なる（どちらのカテゴリにも入っている物なので）
        var clothes = Assert.Single(snapshot.Categories, bar => bar.Key == "衣装");
        Assert.Equal((100L, 2), (clothes.Bytes, clothes.ItemCount));
        Assert.Equal(100, Assert.Single(snapshot.Categories, bar => bar.Key == "小物").Bytes);

        // 同じ場所を2つの商品が持つだけなら写しではないので、重複の一覧にも出ない
        Assert.Empty(snapshot.Duplicates);
    }
}
