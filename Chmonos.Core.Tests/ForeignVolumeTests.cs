using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 控えた文字に別のディスクが来ている間（外付けA を外し、別のディスクB が同じ E: に来た）は、その文字の上の記録を
/// 「つながっていない」と同じに扱う（2026-10-05・見つからない・移動の点検の2）。
/// 前は根（E:\）がつながっているかしか見なかったので、Aの上の全ファイルに見つからなくなった日時が付き、取り込みが場所を外していた。
/// ディスクの通し番号は作り物の読み手（<see cref="IVolumeReader"/>）で差し替える。一時フォルダのドライブ文字を「控えた文字」にする。
/// </summary>
public sealed class ForeignVolumeTests : IDisposable
{
    private const string ItemId = "local-9900001";
    private const string SerialA = "AAAA0001";
    private const string SerialB = "BBBB0002";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-foreign-volume-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly string _elsewhere;
    private readonly DataStore _store;

    public ForeignVolumeTests()
    {
        _watched = Directory.CreateDirectory(Path.Combine(_root, "watched")).FullName;
        _elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;
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

    private sealed class FakeReader(params MountedVolume[] volumes) : IVolumeReader
    {
        public IReadOnlyList<MountedVolume> Mounted() => volumes;
    }

    /// <summary>一時フォルダのある文字（試験の「外付け」の文字）。</summary>
    private string Letter => VolumeTable.LetterOf(_root)!;

    /// <summary>その文字に A を控え、今その文字に <paramref name="serialNow"/> のディスクが見えている表。</summary>
    private async Task<VolumeTable> TableAsync(string? recorded, string serialNow)
    {
        if (recorded is not null)
        {
            await _store.Volumes.SaveAsync([new VolumeRecord { Letter = Letter, Serial = recorded }]);
        }

        return new VolumeTable(_store, new FakeReader(new MountedVolume(Letter, serialNow, null)));
    }

    private string Gone(string name) => Path.Combine(_elsewhere, name);

    private Task SaveAsync(params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Local = new LocalBlock { LocalFiles = files } });

    private async Task<LocalFileRecord> OnlyFileAsync() => Assert.Single((await _store.Items.LoadAsync(ItemId))!.Local.LocalFiles);

    [Fact]
    public async Task 控えた文字に別のディスクが来ている間は_見回りは日時を付けず場所も残す()
    {
        var gone = Gone("衣装.zip");
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [gone], SizeBytes = 3 });

        var written = await new MissingMarksSweep(_store, volumes: await TableAsync(SerialA, SerialB)).SweepAsync();

        Assert.Empty(written);
        var file = await OnlyFileAsync();
        Assert.Null(file.MissingSince);
        Assert.Equal([gone], file.Paths);
    }

    [Fact]
    public async Task 控えた文字に控えたのと同じディスクがあれば_見回りは日時を付ける()
    {
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [Gone("衣装.zip")], SizeBytes = 3 });

        await new MissingMarksSweep(_store, volumes: await TableAsync(SerialA, SerialA)).SweepAsync();

        Assert.NotNull((await OnlyFileAsync()).MissingSince);
    }

    [Fact]
    public async Task 控えの無い文字は_今までどおり根だけで見て日時を付ける()
    {
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [Gone("衣装.zip")], SizeBytes = 3 });

        await new MissingMarksSweep(_store, volumes: await TableAsync(recorded: null, SerialB)).SweepAsync();

        Assert.NotNull((await OnlyFileAsync()).MissingSince);
    }

    [Fact]
    public async Task 控えた文字に別のディスクが来ている間は_見つからないファイルを探しても場所を差し替えない()
    {
        var copy = Path.Combine(_watched, "衣装.zip");
        await File.WriteAllTextAsync(copy, "作り物の中身");
        var hash = await FileHasher.ComputeSha256Async(copy);
        var gone = Gone("衣装.zip");
        await SaveAsync(new LocalFileRecord { Hash = hash, Paths = [gone], SizeBytes = new FileInfo(copy).Length });

        var result = await new MissingFileFinder(_store, await TableAsync(SerialA, SerialB)).FindAsync([_watched]);

        Assert.Equal(0, result.MissingBefore);
        var file = await OnlyFileAsync();
        Assert.Equal([gone], file.Paths);
        Assert.Null(file.MissingSince);
    }

    [Fact]
    public void 読み替えた先は別のディスクと見ず_読み替えない文字に来た別のディスクは見分ける()
    {
        // E: に A、F: に Z を控えた。今は A が F: に、B が E: に来ている（挿す順が変わり、別のディスクも挿した）
        var volumes = new VolumeSnapshot(
            [new VolumeRecord { Letter = "E:", Serial = SerialA }, new VolumeRecord { Letter = "F:", Serial = "CCCC0003" }],
            [new MountedVolume("E:", SerialB, null), new MountedVolume("F:", SerialA, null)]);
        var probe = new FilePresenceProbe(fileExists: _ => true, rootExists: _ => true, volumes: volumes);

        // 商品ページは記録の E: を F: に読み替えて見る。F: に今あるのは控えた A なので在る
        Assert.Equal(FilePresence.Present, probe.Of([@"E:\booth\a.zip"], path => "F:" + path[2..]));

        // 読み替えずに E: を見る（取り込み・見回り）と、そこにあるのは B なので「つながっていない」
        Assert.Equal(FilePresence.OnDetachedDrive, probe.Of([@"E:\booth\a.zip"]));

        // F: に控えたのは別のディスク（Z）。今 F: にある A の上で Z の記録を「在る」とは言わない
        Assert.Equal(FilePresence.OnDetachedDrive, probe.Of([@"F:\old\b.zip"]));
    }
}
