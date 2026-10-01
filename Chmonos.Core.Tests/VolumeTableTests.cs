using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>ドライブ文字が変わった外付けを同じボリュームとして読み替える（ユーザ判断 2026-09-14）。</summary>
public sealed class VolumeTableTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-volume-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;

    public VolumeTableTests()
    {
        var paths = new AppPaths(_root);
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

    private static VolumeRecord Known(string letter, string serial) => new() { Letter = letter, Serial = serial };

    [Fact]
    public void 挿し直して文字が変わったら読み替える()
    {
        var remap = VolumeTable.Remap([Known("E:", "AAAA0001")], [new MountedVolume("F:", "AAAA0001", "BOOTH")]);

        Assert.Equal("F:", remap["E:"]);
        Assert.Equal(@"F:\BOOTH\a.zip", VolumeTable.Apply(@"E:\BOOTH\a.zip", remap));
    }

    [Fact]
    public void 同じ文字に同じボリュームが見えていれば読み替えない()
        => Assert.Empty(VolumeTable.Remap([Known("E:", "AAAA0001")], [new MountedVolume("E:", "AAAA0001", null)]));

    [Fact]
    public void どこにも見えていなければ読み替えない()
    {
        // 取り外している。元の文字の下に「取り外しているドライブ」として出す
        Assert.Empty(VolumeTable.Remap([Known("E:", "AAAA0001")], [new MountedVolume("E:", "BBBB0002", null)]));
    }

    [Fact]
    public void 二台の文字が入れ替わっても取り違えない()
    {
        var remap = VolumeTable.Remap(
            [Known("E:", "AAAA0001"), Known("F:", "BBBB0002")],
            [new MountedVolume("E:", "BBBB0002", null), new MountedVolume("F:", "AAAA0001", null)]);

        Assert.Equal("F:", remap["E:"]);
        Assert.Equal("E:", remap["F:"]);
    }

    [Fact]
    public void 同じラベルでも通し番号が違えば別のボリューム()
    {
        // 既定の名前（「ボリューム」）のままの外付けが2台。ラベルで見分けると取り違える
        var remap = VolumeTable.Remap(
            [Known("E:", "AAAA0001") with { Label = "ボリューム" }],
            [new MountedVolume("E:", "BBBB0002", "ボリューム")]);

        Assert.Empty(remap);
    }

    [Fact]
    public void 共有のパスは読み替えない()
        => Assert.Equal(@"\\nas\share\a.zip", VolumeTable.Apply(@"\\nas\share\a.zip", new Dictionary<string, string> { ["E:"] = "F:" }));

    [Fact]
    public async Task 取り込みで記録した文字の組を控える()
    {
        var table = new VolumeTable(_store, new FakeReader(new MountedVolume("E:", "AAAA0001", "BOOTH"), new MountedVolume("G:", "CCCC0003", null)));

        await table.RecordAsync([@"E:\BOOTH\a.zip", @"e:\BOOTH\b.zip"]);

        var record = Assert.Single(_store.Volumes.Load());
        Assert.Equal("E:", record.Letter);
        Assert.Equal("AAAA0001", record.Serial);
        Assert.Equal("BOOTH", record.Label);
    }

    [Fact]
    public async Task 開いたときは記録したファイルが在る文字だけ控え直す()
    {
        // 在るファイル（一時フォルダ）の文字は控え、在らない物しか無い文字は控えない（別の外付けが来ているかもしれない）
        Directory.CreateDirectory(_root);
        var present = Path.Combine(_root, "present.zip");
        await File.WriteAllTextAsync(present, "zip");
        var tempLetter = VolumeTable.LetterOf(present)!;
        var otherLetter = tempLetter == "Q:" ? "R:" : "Q:";

        var table = new VolumeTable(_store, new FakeReader(
            new MountedVolume(tempLetter, "AAAA0001", null),
            new MountedVolume(otherLetter, "BBBB0002", null)));

        await table.ObserveAsync([present, $@"{otherLetter}\missing\a.zip"]);

        var record = Assert.Single(_store.Volumes.Load());
        Assert.Equal(tempLetter, record.Letter);
    }

    [Fact]
    public async Task 開いたときに読み替えを返す()
    {
        await _store.Volumes.SaveAsync([Known("E:", "AAAA0001")]);
        var table = new VolumeTable(_store, new FakeReader(new MountedVolume("F:", "AAAA0001", null)));

        var remap = await table.ObserveAsync([@"E:\BOOTH\a.zip"]);

        Assert.Equal("F:", remap["E:"]);
        // 元の文字の控えは消さない（記録のパスはまだ E: のまま）
        Assert.Equal("AAAA0001", Assert.Single(_store.Volumes.Load(), record => record.Letter == "E:").Serial);
    }

    [Fact]
    public async Task 検索の画面は表を書かずに読み替えだけを確かめ直す()
    {
        await _store.Volumes.SaveAsync([Known("E:", "AAAA0001")]);
        var before = File.GetLastWriteTimeUtc(_store.Volumes.Path);
        var table = new VolumeTable(_store, new FakeReader(new MountedVolume("F:", "AAAA0001", null)));

        Assert.Equal(@"E:\a.zip", table.Current(@"E:\a.zip"));
        table.RefreshRemap();

        Assert.Equal(@"F:\a.zip", table.Current(@"E:\a.zip"));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_store.Volumes.Path));
    }

    // 複製したディスクは同じ通し番号を持つ。どちらが記録した方か分からないので読み替えない（点検 2026-09-23）
    [Fact]
    public void 同じ番号が2文字に見えるときは読み替えない()
        => Assert.Empty(VolumeTable.Remap(
            [Known("E:", "AAAA0001")],
            [new MountedVolume("F:", "AAAA0001", null), new MountedVolume("G:", "AAAA0001", null)]));

    [Fact]
    public void 通し番号0は読み替えに使わない()
        => Assert.Empty(VolumeTable.Remap([Known("E:", "00000000")], [new MountedVolume("F:", "00000000", null)]));

    [Fact]
    public void 通し番号0の組は控えず前の組を残す()
    {
        var merged = VolumeTable.Merge(
            [Known("E:", "AAAA0001")],
            [new MountedVolume("E:", "00000000", null)],
            DateTimeOffset.Now);

        Assert.Equal("AAAA0001", Assert.Single(merged).Serial);
    }
}
