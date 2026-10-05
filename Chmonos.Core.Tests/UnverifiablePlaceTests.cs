using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 読み取り権限の無いフォルダの上のファイルは「無い」ではなく「確かめられない」（2026-10-05・見つからない・移動の点検の13）。
/// 前は <see cref="DiskCheck.FileExists"/> が拒まれても false（無い）と答え、見回りが日時を付け、取り込みが場所を外し得た。
/// 拒まれる場所は、見方の口（<c>fileState</c>）と属性を読む口で作る（実マシンの権限に頼らない）。
/// </summary>
public sealed class UnverifiablePlaceTests : IDisposable
{
    private const string ItemId = "local-9900002";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-unverifiable-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly DataStore _store;

    public UnverifiablePlaceTests()
    {
        _watched = Directory.CreateDirectory(Path.Combine(_root, "watched")).FullName;
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>権限の無いフォルダ（この下は拒まれる）。</summary>
    private string Locked(string name) => Path.Combine(_root, "locked", name);

    private DiskAnswer Answer(string path)
        => path.StartsWith(Path.Combine(_root, "locked"), StringComparison.OrdinalIgnoreCase)
            ? DiskAnswer.Unknown
            : DiskCheck.FileState(path);

    private FilePresenceProbe Probe() => new(fileState: Answer);

    private Task SaveAsync(params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Local = new LocalBlock { LocalFiles = files } });

    private async Task<LocalFileRecord> OnlyFileAsync() => Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles);

    [Fact]
    public void 拒まれたら確かめられない_無いと分かったときだけ無い()
    {
        Assert.Equal(DiskAnswer.Unknown, DiskCheck.StateOf("C:\\x", isFolder: false, _ => throw new UnauthorizedAccessException()));
        Assert.Equal(DiskAnswer.Unknown, DiskCheck.StateOf("C:\\x", isFolder: false, _ => throw new IOException("デバイスの準備ができていません")));
        Assert.Equal(DiskAnswer.Missing, DiskCheck.StateOf("C:\\x", isFolder: false, _ => throw new FileNotFoundException()));
        Assert.Equal(DiskAnswer.Missing, DiskCheck.StateOf("C:\\x", isFolder: false, _ => throw new DirectoryNotFoundException()));
        Assert.Equal(DiskAnswer.Missing, DiskCheck.StateOf("C:\\x", isFolder: false, _ => FileAttributes.Directory));
        Assert.Equal(DiskAnswer.Present, DiskCheck.StateOf("C:\\x", isFolder: true, _ => FileAttributes.Directory));

        var file = Path.Combine(_root, "在る.zip");
        File.WriteAllBytes(file, [1]);
        Assert.Equal(DiskAnswer.Present, DiskCheck.FileState(file));
        Assert.Equal(DiskAnswer.Missing, DiskCheck.FileState(Path.Combine(_root, "無い", "a.zip")));
        Assert.Equal(DiskAnswer.Missing, DiskCheck.FileState(_root));
        Assert.Equal(DiskAnswer.Present, DiskCheck.FolderState(_root));
    }

    [Fact]
    public async Task 見回りは_権限の無いフォルダの上のファイルに日時を付けない()
    {
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [Locked("衣装.zip")], SizeBytes = 3 });

        var written = await new MissingMarksSweep(_store, Probe).SweepAsync();

        Assert.Empty(written);
        Assert.Null((await OnlyFileAsync()).MissingSince);
    }

    [Fact]
    public void 取り込みの突き合わせは_権限の無いフォルダの上の場所を外さない()
    {
        var copy = Path.Combine(_watched, "衣装.zip");
        File.WriteAllBytes(copy, [1, 2, 3]);
        var locked = Locked("衣装.zip");

        var merged = LocalFileMerger.Merge(
            [new LocalFileRecord { Hash = "h1", Paths = [locked], SizeBytes = 3 }],
            [new LocalFileRecord { Hash = "h1", Paths = [copy], SizeBytes = 3 }],
            Probe());

        Assert.Equal([locked, copy], Assert.Single(merged).Paths);
    }

    [Fact]
    public async Task 見つからないファイルを探して読めなかったファイルは_数に入れて結果に出す()
    {
        // 探す物と同じ大きさのファイルが監視フォルダにあるが、ほかのアプリが閉じずに開いている
        var busy = Path.Combine(_watched, "開いている.zip");
        await File.WriteAllBytesAsync(busy, [9, 9, 9]);
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [Path.Combine(_root, "消した.zip")], SizeBytes = 3 });

        MissingFileSearchResult result;
        await using (new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await new MissingFileFinder(_store).FindAsync([_watched]);
        }

        Assert.Equal(1, result.UnreadableFiles);
        Assert.Equal(1, result.StillMissing);
    }
}
