using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>手元の物が全部、つながっていないドライブの上の商品（カードの帯「取り外し中」・ユーザ判断 2026-11-20）。</summary>
public class DetachedItemsTests
{
    private const string Gone = @"Q:\shop\a.zip";
    private const string Here = @"C:\shop\a.zip";

    // Q: だけがつながっていない
    private static FilePresenceProbe Probe() => new(rootExists: root => !root.StartsWith('Q'));

    private static LocalFileRecord File(params string[] paths) => new() { Hash = "h" + string.Concat(paths), Paths = [.. paths], SizeBytes = 1 };

    private static ItemRecord Item(LocalBlock local) => new()
    {
        Id = "1000001",
        Booth = new BoothBlock { Name = "item", FetchedAt = DateTimeOffset.Now },
        Local = local,
    };

    private static bool Detached(ItemRecord item)
        => DetachedItems.IsAllOnDetachedDrives(item, Probe(), (_, path) => path, folder => folder.Path);

    [Fact]
    public void 手元の物が全部つながっていないドライブの上なら取り外し中()
        => Assert.True(Detached(Item(new LocalBlock { LocalFiles = [File(Gone), File(@"Q:\shop\b.zip")] })));

    [Fact]
    public void どれか1つでもつながっているドライブの上にあれば取り外し中ではない()
    {
        Assert.False(Detached(Item(new LocalBlock { LocalFiles = [File(Gone), File(Here)] })));
        Assert.False(Detached(Item(new LocalBlock { LocalFiles = [File(Gone, Here)] })));
    }

    [Fact]
    public void 何も持っていない商品は取り外し中ではない_未所持の帯と重ならない()
        => Assert.False(Detached(Item(new LocalBlock())));

    [Fact]
    public void 登録したフォルダも数える()
    {
        var folder = new LocalFolderRecord { Path = @"Q:\folder" };
        Assert.True(Detached(Item(new LocalBlock { LocalFiles = [File(Gone)], LocalFolders = [folder] })));
        Assert.True(Detached(Item(new LocalBlock { LocalFolders = [folder] })));
        Assert.False(Detached(Item(new LocalBlock { LocalFiles = [File(Gone)], LocalFolders = [folder with { Path = @"C:\folder" }] })));
    }

    [Fact]
    public void 場所が空のファイルは取り外し中ではなく_無い物として扱う()
        => Assert.False(Detached(Item(new LocalBlock { LocalFiles = [File(Gone), File()] })));

    [Fact]
    public void 在るドライブの上の物は1つも見に行かない_根の答えだけで決まる()
    {
        var probe = new FilePresenceProbe(
            rootExists: root => !root.StartsWith('Q'),
            fileExists: _ => throw new InvalidOperationException("ファイルは見に行かない"));
        var items = new[]
        {
            Item(new LocalBlock { LocalFiles = [File(Here)] }),
            Item(new LocalBlock { LocalFiles = [File(Gone)] }) with { Id = "1000002" },
        };

        var found = DetachedItems.Find(items, probe, (_, path) => path, folder => folder.Path);

        Assert.Equal(["1000002"], found);
    }
}
