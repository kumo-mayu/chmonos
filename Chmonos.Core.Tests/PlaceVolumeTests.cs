using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ファイル・登録したフォルダの記録が、場所ごとにディスクの通し番号を持つ（2026-10-05・見つからない・移動の点検の3・ユーザ判断 3-A）。
/// 文字の控え（<c>volumes.json</c>）は1つの文字に1台しか覚えないので、2台の外付けA・Bが日によって同じ文字を使うと、
/// Bを取り込んで控えがBに替わった後は、Aの上の記録をBの上で探して「見つかりません」が付き、取り込みが場所を外していた。
/// ディスクの通し番号は作り物の読み手（<see cref="IVolumeReader"/>）で差し替え、一時フォルダのドライブ文字を「外付けの文字」にする。
/// </summary>
public sealed class PlaceVolumeTests : IDisposable
{
    private const string ItemId = "local-9900011";
    private const string SerialA = "AAAA0011";
    private const string SerialB = "BBBB0012";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-place-volume-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly string _elsewhere;
    private readonly DataStore _store;

    public PlaceVolumeTests()
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

    private string Letter => VolumeTable.LetterOf(_root)!;

    /// <summary>文字の控えにその文字の <paramref name="recorded"/> を書き、今その文字に <paramref name="serialNow"/> が来ている表。</summary>
    private async Task<VolumeTable> TableAsync(string? recorded, string serialNow)
    {
        if (recorded is not null)
        {
            await _store.Volumes.SaveAsync([new VolumeRecord { Letter = Letter, Serial = recorded }]);
        }

        return new VolumeTable(_store, new FakeReader(new MountedVolume(Letter, serialNow, null)));
    }

    private static Dictionary<string, string> On(string path, string serial) => new() { [path] = serial };

    private Task SaveAsync(params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord { Id = ItemId, Local = new LocalBlock { LocalFiles = files } });

    private async Task<LocalBlock> LocalAsync() => (await _store.Items.LoadAsync(ItemId))!.Local;

    [Fact]
    public async Task 記録のディスクが来ていない間は_文字の控えが今のディスクでも_見回りは日時を付けない()
    {
        // Aの上で記録した物。後でBを取り込んで、文字の控えはBに替わった。今その文字に来ているのはB
        var gone = Path.Combine(_elsewhere, "衣装.zip");
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [gone], SizeBytes = 3, Volumes = On(gone, SerialA) });

        await new MissingMarksSweep(_store, volumes: await TableAsync(SerialB, SerialB)).SweepAsync();

        var file = Assert.Single((await LocalAsync()).LocalFiles);
        Assert.Null(file.MissingSince);
        Assert.Equal([gone], file.Paths);
    }

    [Fact]
    public async Task 記録のディスクが来ていれば_文字の控えが別のディスクでも_見回りは無いと見て日時を付ける()
    {
        var gone = Path.Combine(_elsewhere, "衣装.zip");
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [gone], SizeBytes = 3, Volumes = On(gone, SerialB) });

        await new MissingMarksSweep(_store, volumes: await TableAsync(SerialA, SerialB)).SweepAsync();

        Assert.NotNull(Assert.Single((await LocalAsync()).LocalFiles).MissingSince);
    }

    [Fact]
    public async Task 番号の無い記録は_見回りが在ると見たときに今のディスクの番号を書き足す()
    {
        var here = Path.Combine(_watched, "here.zip");
        await File.WriteAllTextAsync(here, "作り物");
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [here], SizeBytes = 3 });

        await new MissingMarksSweep(_store, volumes: await TableAsync(recorded: null, SerialA)).SweepAsync();

        var file = Assert.Single((await LocalAsync()).LocalFiles);
        Assert.Equal(SerialA, PlaceVolumes.Of(file, here));
    }

    [Fact]
    public async Task 番号のある記録の場所は_見回りが在ると見ても番号を書き換えない()
    {
        var here = Path.Combine(_watched, "here.zip");
        await File.WriteAllTextAsync(here, "作り物");
        await SaveAsync(new LocalFileRecord { Hash = "h1", Paths = [here], SizeBytes = 3, Volumes = On(here, SerialA) });

        var written = await new MissingMarksSweep(_store, volumes: await TableAsync(recorded: null, SerialA)).SweepAsync();

        Assert.Empty(written);
    }

    private async Task ImportAsync(VolumeTable volumes)
    {
        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        await new ImportPipeline(
                _store, client, new ImagePipeline(client, _store.Paths, settings), () => settings, volumes: volumes)
            .RunAsync(new ImportWorkSet([_watched]));
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task 取り込みは_記録のディスクが来ていない場所を外さず_見つけた場所に今のディスクの番号を書く()
    {
        var copy = Path.Combine(_watched, "説明書.pdf");
        await File.WriteAllTextAsync(copy, "作り物の説明書");
        var gone = Path.Combine(_elsewhere, "説明書.pdf");
        await SaveAsync(new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(copy),
            Paths = [gone],
            SizeBytes = new FileInfo(copy).Length,
            Volumes = On(gone, SerialA),
        });

        // 控えはもうBのもの（前にBを取り込んだ）。今来ているのもB
        await ImportAsync(await TableAsync(SerialB, SerialB));

        var file = Assert.Single((await LocalAsync()).LocalFiles);
        Assert.Equal([gone, copy], file.Paths);
        Assert.Equal(SerialA, PlaceVolumes.Of(file, gone));
        Assert.Equal(SerialB, PlaceVolumes.Of(file, copy));
    }

    [Fact]
    public async Task 取り込みは_別のディスクの同じ場所に別の中身があっても_記録から場所を外さない()
    {
        // Aの上の「衣装.zip」の記録。今来ているBの同じ場所には、同じ名前の別の中身がある
        var same = Path.Combine(_watched, "衣装.zip");
        await File.WriteAllTextAsync(same, "Bの上の別の中身");
        await SaveAsync(new LocalFileRecord { Hash = "A-NO-NAKAMI", Paths = [same], SizeBytes = 99, Volumes = On(same, SerialA) });

        await ImportAsync(await TableAsync(SerialB, SerialB));

        var file = Assert.Single((await LocalAsync()).LocalFiles, file => file.Hash == "A-NO-NAKAMI");
        Assert.Equal([same], file.Paths);
    }

    /// <summary>
    /// 走査の控えは、控えたのと別のディスクの上なら場所・大きさ・日時が同じでも使い回さない（点検の16・ユーザ判断 16-A）。
    /// 取り込みが今のディスクを周回の頭の写しで見て、控えに渡しているかを確かめる。
    /// </summary>
    [Fact]
    public async Task 取り込みは_別のディスクで取った走査の控えのハッシュを使い回さない()
    {
        var path = Path.Combine(_watched, "衣装.zip");
        await File.WriteAllTextAsync(path, "Bの上の中身");
        var info = new FileInfo(path);
        await _store.ScanCache.SaveAsync(
        [
            new ScanCacheEntry
            {
                Path = path,
                SizeBytes = info.Length,
                ModifiedAtUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                Hash = "A-NO-HASH",
                Volume = SerialA,
            },
        ]);

        await ImportAsync(await TableAsync(SerialB, SerialB));

        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.Equal(await FileHasher.ComputeSha256Async(path), unresolved.Hash);
        Assert.Equal(SerialB, Assert.Single(_store.ScanCache.Load()).Volume);
    }

    [Fact]
    public async Task 登録したフォルダも_記録のディスクが来ていない間は見回りが日時を付けず_在ると見たら番号を書き足す()
    {
        var gone = Path.Combine(_elsewhere, "展開済み");
        var here = Directory.CreateDirectory(Path.Combine(_watched, "展開済み")).FullName;
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Local = new LocalBlock
            {
                LocalFolders =
                [
                    new LocalFolderRecord { Path = gone, Volume = SerialA },
                    new LocalFolderRecord { Path = here },
                ],
            },
        });

        await new MissingMarksSweep(_store, volumes: await TableAsync(SerialB, SerialB)).SweepAsync();

        var folders = (await LocalAsync()).LocalFolders;
        Assert.Null(Assert.Single(folders, folder => folder.Path == gone).MissingSince);
        Assert.Equal(SerialB, Assert.Single(folders, folder => folder.Path == here).Volume);
    }

    [Fact]
    public void 読み替えは記録の番号のディスクが見えている文字で_見えていなければ記録のまま()
    {
        // E: に控えたのは Z。今 Z は F: に、記録のディスク A は G: に来ている
        var snapshot = new VolumeSnapshot(
            [new VolumeRecord { Letter = "E:", Serial = "CCCC0013" }],
            [new MountedVolume("F:", "CCCC0013", null), new MountedVolume("G:", SerialA, null)]);
        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["E:"] = "F:" };

        Assert.Equal(@"G:\booth\a.zip", VolumeTable.Apply(@"E:\booth\a.zip", SerialA, remap, snapshot));

        // 記録のディスク（B）がどこにも見えていなければ、文字の控えの読み替え（E: → F:）もしない（F: にあるのは別のディスク）
        Assert.Equal(@"E:\booth\b.zip", VolumeTable.Apply(@"E:\booth\b.zip", SerialB, remap, snapshot));

        // 番号の無い記録は今までどおり文字の控えで読み替える
        Assert.Equal(@"F:\booth\c.zip", VolumeTable.Apply(@"E:\booth\c.zip", null, remap, snapshot));
    }

    [Fact]
    public async Task 番号はJSONに場所ごとに書き_無ければ書かない()
    {
        var here = Path.Combine(_watched, "here.zip");
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord { Hash = "h1", Paths = [here], SizeBytes = 3, Volumes = On(here, SerialA) },
                    new LocalFileRecord { Hash = "h2", Paths = [here + "2"], SizeBytes = 3 },
                ],
                LocalFolders = [new LocalFolderRecord { Path = _watched, Volume = SerialB }, new LocalFolderRecord { Path = _elsewhere }],
            },
        });

        var json = await File.ReadAllTextAsync(Directory.GetFiles(_store.Paths.ItemsDir, "*.json").Single());

        Assert.Equal(1, CountOf(json, "\"volumes\""));
        Assert.Contains($"\"{SerialA}\"", json, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(json, "\"volume\""));
        Assert.Equal(SerialA, PlaceVolumes.Of((await LocalAsync()).LocalFiles[0], here.ToUpperInvariant()));
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var index = text.IndexOf(part, StringComparison.Ordinal); index >= 0; index = text.IndexOf(part, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
