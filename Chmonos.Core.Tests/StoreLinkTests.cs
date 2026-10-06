using System.IO.Compression;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 保存先の中のジャンクションの先（保存先の外）を、引越し・書き出し・<c>.tmp</c> の片付けが辿らないこと（外部の点検 2026-10-06・ユーザ判断「A」）。
/// <see cref="SearchOption.AllDirectories"/> は先まで降り、引越しは外のファイルを写したうえで消し、書き出しは zip に入れ、片付けは外の <c>.tmp</c> を消していた。
/// 外には番兵のファイルを置き、どの操作の後も同じ中身で残ることを見る。
/// </summary>
public sealed class StoreLinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-link-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _links = [];

    private string Source => Path.Combine(_root, "src");

    private string Destination => Path.Combine(_root, "dst");

    private string Outside => Path.Combine(_root, "outside");

    private string Sentinel => Path.Combine(Outside, "sentinel.json");

    public StoreLinkTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "items"));
        File.WriteAllText(Path.Combine(Source, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(Source, "items", "9900001.json"), "{ \"id\": \"9900001\" }");
        Directory.CreateDirectory(Outside);
        File.WriteAllText(Sentinel, "外の番兵");
    }

    public void Dispose()
    {
        // 先にリンクだけを外す（中身を辿って消さない）
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Link(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Assert.True(TestJunction.TryCreate(link, target), "ジャンクションを作れませんでした");
        _links.Add(link);
    }

    private void AssertSentinelKept()
        => Assert.Equal("外の番兵", File.ReadAllText(Sentinel));

    [Fact]
    public void FilesDoesNotDescendIntoJunction()
    {
        Link(Path.Combine(Source, "images"), Outside);

        var files = StoreTree.Files(Source).Select(path => Path.GetRelativePath(Source, path)).ToList();

        Assert.DoesNotContain(files, path => path.EndsWith("sentinel.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Path.Combine("items", "9900001.json"), files);
        Assert.DoesNotContain(StoreTree.Directories(Source), path => path.StartsWith(Path.Combine(Source, "images") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("images", StoreTree.FindLink(Source));
    }

    [Fact]
    public void FindLinkIsNullWithoutLinks()
        => Assert.Null(StoreTree.FindLink(Source));

    [Fact]
    public void MoveRefusesWhenSourceHasLink()
    {
        Link(Path.Combine(Source, "images"), Outside);

        var result = StoreMover.Move(Source, Destination);

        Assert.False(result.Succeeded);
        Assert.Equal(StoreTree.LinkRefusal("images"), result.Error);
        Assert.False(Directory.Exists(Destination));
        AssertSentinelKept();
        Assert.True(File.Exists(Path.Combine(Source, "items", "9900001.json")));
    }

    [Fact]
    public void MoveRefusesWhenDestinationHasLink()
    {
        Directory.CreateDirectory(Destination);
        Link(Path.Combine(Destination, "items"), Outside);

        var result = StoreMover.Move(Source, Destination);

        Assert.False(result.Succeeded);
        Assert.Contains("選んだ場所の中の「items」", result.Error);
        Assert.Equal(["sentinel.json"], Directory.EnumerateFileSystemEntries(Outside).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(Source, "items", "9900001.json")));
    }

    [Fact]
    public void ReplaceRefusesBeforeParkingDestination()
    {
        Link(Path.Combine(Source, "images"), Outside);
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "settings.json"), "{ \"there\": true }");

        var result = StoreMover.Replace(Source, Destination);

        Assert.False(result.Succeeded);
        Assert.Equal(StoreTree.LinkRefusal("images"), result.Error);
        // 選んだ先の中身は退けずにそのまま
        Assert.Equal(["settings.json"], Directory.EnumerateFileSystemEntries(Destination).Select(Path.GetFileName));
        AssertSentinelKept();
    }

    [Fact]
    public void ExportRefusesWhenStoreHasLink()
    {
        Link(Path.Combine(Source, "images"), Outside);
        var zip = Path.Combine(_root, "backup.zip");

        var refused = Assert.Throws<StoreLinkException>(() => BackupArchive.Export(Source, zip, includeImages: true));

        Assert.Equal("images", refused.RelativePath);
        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".tmp"));
        AssertSentinelKept();
    }

    [Fact]
    public void ExportWithoutLinksStillWorks()
    {
        var zip = Path.Combine(_root, "backup.zip");

        var result = BackupArchive.Export(Source, zip, includeImages: true);

        Assert.Equal(2, result.Files);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, entry => entry.FullName == "items/9900001.json");
    }

    [Fact]
    public void StaleTemporaryCleanupDoesNotFollowJunction()
    {
        var outsideTmp = Path.Combine(Outside, "old.tmp");
        var insideTmp = Path.Combine(Source, "items", "old.tmp");
        File.WriteAllText(outsideTmp, "外の書きかけ");
        File.WriteAllText(insideTmp, "中の書きかけ");
        var old = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(outsideTmp, old);
        File.SetLastWriteTimeUtc(insideTmp, old);
        Link(Path.Combine(Source, "images"), Outside);

        var deleted = JsonStore.DeleteStaleTemporaryFiles(Source, includeSubdirectories: true);

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(insideTmp));
        Assert.True(File.Exists(outsideTmp));
        AssertSentinelKept();
    }
}
