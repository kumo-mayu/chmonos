using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests.Robustness;

public sealed class VpmPersistenceAdversarialTests : IDisposable
{
    private readonly RobustnessDirectory _directory = new();
    private readonly AppPaths _paths;
    private readonly Clock _clock = new();
    private readonly string _project;
    private UnityScanStore Store => new(_paths, _clock);
    private static readonly DateTimeOffset Later = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    public VpmPersistenceAdversarialTests()
    {
        _paths = new AppPaths(Path.Combine(_directory.Root, "store"));
        _paths.EnsureCreated();
        _project = Path.Combine(_directory.Root, "project");
        Directory.CreateDirectory(Path.Combine(_project, "Packages"));
    }

    public void Dispose() => _directory.Dispose();

    private static VpmPackage Package(string version) => new("com.example.tool", "作り物", version, null, true);
    private void Save(string version) => Store.Save(_project, UnityProjectScan.Empty with { Vpm = [Package(version)] });
    private string ScanFile => Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, AppPaths.UnityScansDirName), "*.json"));

    [Theory]
    [InlineData("{\"locked\":{\"com.example.tool\":")]
    [InlineData("[]")]
    public void 壊れたmanifestで再走査しても_外した履歴を作らない(string broken)
    {
        _clock.Now = Later;
        Save("1");
        File.WriteAllText(Path.Combine(_project, "Packages", "vpm-manifest.json"), broken);
        Assert.Null(VpmPackages.TryRead(_project));

        Store.Save(_project, UnityProjectScan.Empty with { Vpm = VpmPackages.TryRead(_project) });

        var loaded = Store.Load(_project)!;
        var history = Assert.Single(loaded.VpmHistory!);
        Assert.False(history.Current.Removed, "読めない manifest を、パッケージを全部外した履歴にしてはいけない");
        Assert.Equal("1", Assert.Single(loaded.Vpm!).Version);
        Assert.Single(history.Changes);
    }

    [Fact]
    public void 上限を超えたmanifestで再走査しても_前の履歴を保つ()
    {
        Save("1");
        // 読み取り上限の1MiBを1バイト以上超える、有効なJSONを作る。
        File.WriteAllText(Path.Combine(_project, "Packages", "vpm-manifest.json"), "{\"padding\":\"" + new string('x', 1024 * 1024) + "\"}");
        Assert.Null(VpmPackages.TryRead(_project));
        Store.Save(_project, UnityProjectScan.Empty with { Vpm = VpmPackages.TryRead(_project) });
        Assert.Equal("1", Assert.Single(Store.Load(_project)!.VpmHistory!).Current.Version);
    }

    [Fact]
    public async Task manifestが途中で切れても_改変のVPM控えは前の走査結果を使う()
    {
        Save("1");
        File.WriteAllText(Path.Combine(_project, "Packages", "vpm-manifest.json"), "{\"locked\":");
        var service = new ModificationService(new DataStore(_paths));
        var modification = (await service.CreateAsync("9900001", "作り物"))!;
        Assert.True(await service.SetProjectAsync(modification.Id, _project));
        Assert.True(await service.AddMemberAsync(modification.Id, new ModificationMember { ItemId = "9900002" }));

        var loaded = (await new ModificationRepository(_paths).LoadAsync(modification.Id))!;
        Assert.Equal("1", Assert.Single(Assert.Single(loaded.RecordedVpm).Items).Version);
    }

    // 時計が戻ったときに履歴が増える件（2026-11-13 の点検 件6）は、ユーザ判断で記録だけにしたので、その試験はここに置かない

    [Fact]
    public async Task 別の保存部品から同時に同じ版を控えても_履歴を増やさない()
    {
        Save("1");
        _clock.Now = Later.AddDays(1);
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => Save("2"))));
        Assert.Equal(new[] { "1", "2" }, Assert.Single(Store.Load(_project)!.VpmHistory!).Changes.Select(entry => entry.Version));
    }

    [Fact]
    public void 手編集したnull配列とnull行でも_有効な履歴を残して次を足せる()
    {
        Save("1");
        File.WriteAllText(ScanFile, $$"""
            {"project":{{JsonSerializer.Serialize(_project)}},"packages":null,"images":null,"vpm":null,
             "vpmHistory":[null,{"name":"com.example.empty","changes":null},
               {"name":"com.example.tool","changes":[null,{"from":"2026-11-11T00:00:00Z","version":"1","removed":false}]}]}
            """);
        Save("2");
        var history = Assert.Single(Store.Load(_project)!.VpmHistory!);
        Assert.Equal(new[] { "1", "2" }, history.Changes.Select(entry => entry.Version));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = Later;
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
