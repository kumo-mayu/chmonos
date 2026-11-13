using System.IO.Compression;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests.Robustness;

public sealed class BackupPersistenceAdversarialTests : IDisposable
{
    private readonly RobustnessDirectory _directory = new();
    private readonly AppPaths _paths;
    private readonly string _project;
    private readonly string _zip;
    private readonly string _destination;

    public BackupPersistenceAdversarialTests()
    {
        _paths = new AppPaths(Path.Combine(_directory.Root, "source"));
        _paths.EnsureCreated();
        _project = Path.Combine(_directory.Root, "project");
        _zip = Path.Combine(_directory.Root, "backup.zip");
        _destination = Path.Combine(_directory.Root, "restored");
    }
    public void Dispose() => _directory.Dispose();

    private async Task Seed()
    {
        JsonStore.Write(Path.Combine(_paths.Root, "settings.json"), new AppSettings());
        new UnityScanStore(_paths, new FixedClock()).Save(_project, UnityProjectScan.Empty with
        {
            Vpm = [new VpmPackage("com.example.tool", "作り物", "1", null, true)],
        });
        JsonStore.Write(_paths.ItemFile("9900001"), ItemService.EmptyItem("9900001") with
        {
            Local = new LocalBlock
            {
                Memo = "人が入れたメモ",
                Conversion = new ConversionSetting { Targets = [new ConversionTarget { BaseName = "Base A", Confirmed = true, Forward = true }] },
                VariationTargets = [new VariationTarget { VariationId = 1, Bases = ["Base A"] }],
                ContentUpdate = new ContentUpdate { FoundAt = DateTimeOffset.UnixEpoch, FilesBefore = ["before"], ForBases = ["Base A"] },
            },
        });
        var modificationDirectory = Directory.CreateDirectory(Path.Combine(_paths.Root, "modifications")).FullName;
        JsonStore.Write(Path.Combine(modificationDirectory, "mod-01234567.json"), new ModificationRecord
        {
            Id = "mod-01234567", AvatarItemId = "9900001", Name = "作り物", UnityProject = _project,
            VpmSnapshots = [new ModificationVpmSnapshot { At = DateTimeOffset.UnixEpoch, Packages = [new ModificationVpmPackage("com.example.tool", null, "1")] }],
        });
        await new NotificationArchive(Path.Combine(_paths.Root, NotificationArchive.DirName)).AppendAsync([
            new NotificationRecord { Id = "notice-1", Kind = NotificationKind.ItemUpdated, Title = "作り物", Detail = "残す", CreatedAt = DateTimeOffset.UnixEpoch, ItemId = "9900001" },
        ]);
    }

    private Dictionary<string, byte[]> SourceBytes() => Directory.GetFiles(_paths.Root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(_paths.Root, path), File.ReadAllBytes);

    private static void AssertBytes(string root, Dictionary<string, byte[]> expected)
    {
        foreach (var (relative, bytes) in expected)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, relative)));
        }
    }

    [Fact]
    public async Task 新しい全保存欄と通知保管庫を往復すると_バイト列も読んだ意味も一致する()
    {
        await Seed();
        var before = SourceBytes();
        var exported = BackupArchive.Export(_paths.Root, _zip, includeImages: false);
        Assert.Equal(0, exported.SkippedLocked);
        BackupArchive.Restore(_zip, _destination);
        AssertBytes(_destination, before);
        AssertBytes(_paths.Root, before);
        var restored = new AppPaths(_destination);
        Assert.Equal("1", Assert.Single(new UnityScanStore(restored).Load(_project)!.VpmHistory!).Current.Version);
        var item = (await new ItemRepository(restored).LoadAsync("9900001"))!;
        Assert.Equal("人が入れたメモ", item.Local.Memo);
        Assert.True(Assert.Single(item.Local.Conversion!.Targets).Confirmed);
        Assert.Equal("Base A", Assert.Single(item.Local.VariationTargets).Bases.Single());
        Assert.Equal("before", Assert.Single(item.Local.ContentUpdate!.FilesBefore));
        var modification = (await new ModificationRepository(restored).LoadAsync("mod-01234567"))!;
        Assert.Equal("1", Assert.Single(Assert.Single(modification.RecordedVpm).Items).Version);
        Assert.Equal("notice-1", Assert.Single((await new NotificationArchive(Path.Combine(_destination, NotificationArchive.DirName)).ReadAsync("1970-01")).Records).Id);
    }

    [Fact]
    public async Task unityscansを戻した直後に中止しても_元の履歴は無傷で_同じ場所へやり直せる()
    {
        await Seed();
        var before = SourceBytes();
        BackupArchive.Export(_paths.Root, _zip, includeImages: false);
        using var stop = new CancellationTokenSource();
        using (var archive = ZipFile.OpenRead(_zip))
        {
            var scanName = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("unityscans/", StringComparison.Ordinal)).Name;
            Assert.ThrowsAny<OperationCanceledException>(() => BackupArchive.Restore(_zip, _destination,
                new Watch(value => { if (value.CurrentName == scanName) stop.Cancel(); }), stop.Token));
        }
        AssertBytes(_paths.Root, before);
        Assert.True(!Directory.Exists(_destination) || !Directory.EnumerateFileSystemEntries(_destination).Any());
        BackupArchive.Restore(_zip, _destination);
        AssertBytes(_destination, before);
    }

    [Fact]
    public async Task 展開後の確定に失敗しても_後から置いた物を残し_元の新欄を変えない()
    {
        await Seed();
        var before = SourceBytes();
        BackupArchive.Export(_paths.Root, _zip, includeImages: false);
        var sentinel = Path.Combine(_destination, "後から置いた.txt");
        Assert.Throws<IOException>(() => BackupArchive.Restore(_zip, _destination, commit: () =>
        {
            File.WriteAllText(sentinel, "残す");
            // 実際の保存先の記録には触れず、確定が失敗した結果だけを返す。
            throw new IOException("作り物の確定失敗");
        }));
        Assert.Equal("残す", File.ReadAllText(Assert.Single(Directory.GetFiles(_destination, "*", SearchOption.AllDirectories))));
        AssertBytes(_paths.Root, before);
    }

    [Fact]
    public async Task unityscansの同名項目で復元が失敗しても_半端な保存先を残さない()
    {
        await Seed();
        var before = SourceBytes();
        BackupArchive.Export(_paths.Root, _zip, includeImages: false);
        using (var archive = ZipFile.Open(_zip, ZipArchiveMode.Update))
        {
            var scan = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("unityscans/", StringComparison.Ordinal));
            using var writer = new StreamWriter(archive.CreateEntry(scan.FullName).Open());
            writer.Write("{}");
        }
        Assert.Throws<IOException>(() => BackupArchive.Restore(_zip, _destination));
        Assert.True(!Directory.Exists(_destination) || !Directory.EnumerateFileSystemEntries(_destination).Any());
        AssertBytes(_paths.Root, before);
    }

    private sealed class Watch(Action<BackupProgress> report) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => report(value);
    }
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
