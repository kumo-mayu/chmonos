using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>中身の更新の控えと、促しが消える条件（<c>docs/spec/used-updates.md</c>）。</summary>
public class UsedUpdatesTests
{
    private static readonly DateTimeOffset Found = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static LocalFileRecord File(string hash, bool detached = false, bool old = false) => new()
    {
        Hash = hash,
        Paths = old ? [] : [$@"D:\fake\{hash}.zip"],
        SizeBytes = 1,
        Detached = detached,
        Replaced = old ? new ReplacedVersion(@"D:\fake\old.zip", Found) : null,
    };

    private static ItemRecord Item(LocalBlock local) => new()
    {
        Id = "1",
        Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.UnixEpoch },
        Local = local,
    };

    private static LocalBlock Updated(params LocalFileRecord[] files)
    {
        var local = new LocalBlock { LocalFiles = files };
        return local with { ContentUpdate = UsedUpdates.Found(local, Found) };
    }

    [Fact]
    public void 控えは持っているファイルだけで_外した物と古い版は入れない()
    {
        var local = Updated(File("A"), File("B", detached: true), File("C", old: true));

        Assert.Equal(["A"], local.ContentUpdate!.FilesBefore);
        Assert.True(local.HasPendingContentUpdate);
    }

    [Fact]
    public void 新しいファイルを取り込むと商品の促しは消える()
    {
        var local = Updated(File("A"));

        var imported = local with { LocalFiles = [File("A"), File("NEW")] };

        Assert.True(imported.HasFilesNewerThanUpdate);
        Assert.False(imported.HasPendingContentUpdate);
    }

    [Fact]
    public void 新しいファイルでも外した物は数えない()
    {
        var local = Updated(File("A"));

        var detached = local with { LocalFiles = [File("A"), File("NEW", detached: true)] };

        Assert.True(detached.HasPendingContentUpdate);
    }

    [Fact]
    public void 見つけた後に登録したフォルダも新しい物に数える()
    {
        var local = Updated(File("A"));

        var before = local with { LocalFolders = [new LocalFolderRecord { Path = @"D:\fake\old", RegisteredAt = Found.AddDays(-1) }] };
        var after = local with { LocalFolders = [new LocalFolderRecord { Path = @"D:\fake\new", RegisteredAt = Found.AddDays(1) }] };

        Assert.True(before.HasPendingContentUpdate);
        Assert.False(after.HasPendingContentUpdate);
    }

    [Fact]
    public void 確認したを押すと商品の促しは消える()
    {
        var local = Updated(File("A"));

        var checkedLocal = local with { ContentUpdate = local.ContentUpdate! with { CheckedAt = Found.AddDays(1) } };

        Assert.False(checkedLocal.HasPendingContentUpdate);
    }

    [Fact]
    public void 改変の行は_使ったファイルが更新の前からの物なら出て_新しいファイルで送り直すと消える()
    {
        var item = Item(Updated(File("A")) with { LocalFiles = [File("A"), File("NEW")] });

        Assert.True(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A" }));
        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "NEW" }));
    }

    [Fact]
    public void 改変の行の確認は_その更新より後に押した物だけ効く()
    {
        var item = Item(Updated(File("A")));

        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A", UpdateCheckedAt = Found.AddDays(1) }));
        Assert.True(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A", UpdateCheckedAt = Found.AddDays(-1) }));
    }

    [Fact]
    public void 使ったファイルが分からない行は商品の印に従い_外した行には出さない()
    {
        var pending = Item(Updated(File("A")));
        var imported = Item(Updated(File("A")) with { LocalFiles = [File("A"), File("NEW")] });
        var unknown = new ModificationMember { ItemId = "1" };

        Assert.True(UsedUpdates.IsMemberBehind(pending, unknown));
        Assert.False(UsedUpdates.IsMemberBehind(imported, unknown));
        Assert.False(UsedUpdates.IsMemberBehind(pending, new ModificationMember { ItemId = "1", FileHash = "A", Detached = true }));
    }

    [Fact]
    public void 更新を見つけていなければ何も出さない()
    {
        var item = Item(new LocalBlock { LocalFiles = [File("A")] });

        Assert.False(item.Local.HasPendingContentUpdate);
        Assert.False(UsedUpdates.IsMemberBehind(item, new ModificationMember { ItemId = "1", FileHash = "A" }));
    }
}
