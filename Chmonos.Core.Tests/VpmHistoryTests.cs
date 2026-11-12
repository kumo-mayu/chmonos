using System.Text.Json;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// VPM の履歴（ユーザ判断 2026-11-12）。版が変わる・下がる・消える・また入る、読めなかったときに壊さない、前の控えからの始まり、JSON の形。
/// 名前は作り物。時計は差し替える（時計で結果の変わる試験を書かない）。
/// </summary>
public sealed class VpmHistoryTests : IDisposable
{
    private const string ProjectDir = "projA";

    private readonly string _root = Directory.CreateTempSubdirectory("chmonos-vpmhistory-").FullName;
    private readonly AppPaths _paths;
    private readonly Clock _clock = new();
    private readonly UnityScanStore _store;
    private readonly string _project;

    public VpmHistoryTests()
    {
        _paths = new AppPaths(Path.Combine(_root, "store"));
        _paths.EnsureCreated();
        _store = new UnityScanStore(_paths, _clock);
        _project = Path.Combine(_root, ProjectDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly DateTimeOffset Day1 = new(2026, 10, 1, 21, 3, 12, TimeSpan.FromHours(9));

    private static VpmPackage Package(string name, string? version, string? folder = "f") => new(name, name + "の表示名", version, folder, true);

    private void SaveVpm(params VpmPackage[] packages) => _store.Save(_project, UnityProjectScan.Empty with { Vpm = packages });

    private VpmPackageHistory History(string name)
        => Assert.Single(_store.Load(_project)!.VpmHistory!, history => history.Name == name);

    private static IEnumerable<(string? Version, bool Removed)> States(VpmPackageHistory history)
        => history.Changes.Select(change => (change.Version, change.Removed));

    [Fact]
    public void 版が上がる_下がる_消える_また入ると_それぞれ見つけた時の行を足す()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(10);
        SaveVpm(Package("com.example.shader", "2.3.3"));
        _clock.Now = Day1.AddDays(11);
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(12);
        SaveVpm();
        _clock.Now = Day1.AddDays(13);
        SaveVpm(Package("com.example.shader", "2.3.3"));

        var history = History("com.example.shader");
        Assert.Equal([("2.3.2", false), ("2.3.3", false), ("2.3.2", false), (null, true), ("2.3.3", false)], States(history));
        Assert.Equal([Day1, Day1.AddDays(10), Day1.AddDays(11), Day1.AddDays(12), Day1.AddDays(13)], history.Changes.Select(change => change.From));
    }

    [Fact]
    public void 変わらなければ_何度調べても行を足さない()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);
        SaveVpm(Package("com.example.shader", "2.3.2"));
        SaveVpm(Package("com.example.shader", "2.3.2"));

        Assert.Equal(Day1, Assert.Single(History("com.example.shader").Changes).From);
    }

    [Fact]
    public void 読めなかったとき_作り物の調べのときは_前のVPMと履歴を残す()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);

        // 全部を読めなかった（Vpm が null）。商品の結果は今の物に書き直す
        _store.Save(_project, new UnityProjectScan([new UnityProjectMatch("9900001", 1, 1)], new Dictionary<string, IReadOnlyList<UnityImageMatch>>()));

        var loaded = _store.Load(_project)!;
        Assert.Equal("2.3.2", Assert.Single(loaded.Vpm!).Version);
        Assert.Single(History("com.example.shader").Changes);
        Assert.Single(loaded.Packages);
    }

    [Fact]
    public void プロジェクトのフォルダやPackagesが無いときは_読めなかったとして_外したと記録しない()
    {
        Assert.Null(VpmPackages.TryRead(_project));
        Directory.CreateDirectory(Path.Combine(_project, "Assets"));
        Assert.Null(VpmPackages.TryRead(_project));

        // 送る前の照らしは今までどおり空で受ける
        Assert.Empty(VpmPackages.Read(_project));

        Directory.CreateDirectory(Path.Combine(_project, "Packages", "com.example.shader"));
        File.WriteAllText(Path.Combine(_project, "Packages", "com.example.shader", "package.json"), """{ "name": "com.example.shader", "version": "1.0.0" }""");
        Assert.Equal("1.0.0", Assert.Single(VpmPackages.TryRead(_project)!).Version);
    }

    [Fact]
    public void 入っている物の版が読めないときは_前の版を変えない()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);
        SaveVpm(Package("com.example.shader", null));

        Assert.Equal([("2.3.2", false)], States(History("com.example.shader")));
    }

    [Fact]
    public void manifestにだけある物は_入っているに数える()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);
        SaveVpm(Package("com.example.shader", "2.3.2", folder: null));

        Assert.Single(History("com.example.shader").Changes);
    }

    [Fact]
    public void 履歴の欄の無い前の控えは_次に調べた時の姿を最初の行にする()
    {
        // 前の控え（vpm はあるが vpmHistory が無い）
        Directory.CreateDirectory(Path.Combine(_paths.Root, AppPaths.UnityScansDirName));
        _store.Save(_project, UnityProjectScan.Empty);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, AppPaths.UnityScansDirName)));
        File.WriteAllText(file, $$"""{"project":{{JsonSerializer.Serialize(_project)}},"vpm":[{"name":"com.example.old","version":"1.0.0","folder":"com.example.old","inManifest":true}]}""");
        Assert.Null(_store.Load(_project)!.VpmHistory);

        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));

        // 前の vpm にあった物を「外した」とは書かない（いつ入っていたかの行が無い）
        var history = Assert.Single(_store.Load(_project)!.VpmHistory!);
        Assert.Equal("com.example.shader", history.Name);
        Assert.Equal([("2.3.2", false)], States(history));
    }

    [Fact]
    public void 前の控えが別のプロジェクトの物なら_その履歴を引き継がない()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, AppPaths.UnityScansDirName)));
        File.WriteAllText(file, File.ReadAllText(file).Replace(JsonSerializer.Serialize(_project)[1..^1], @"D:\\作り物\\projB", StringComparison.Ordinal));

        _clock.Now = Day1.AddDays(1);
        SaveVpm(Package("com.example.tool", "0.1.0"));

        Assert.Equal("com.example.tool", Assert.Single(_store.Load(_project)!.VpmHistory!).Name);
    }

    [Fact]
    public void 場所を付け替えると_履歴もそのまま写り_行は足さない()
    {
        _clock.Now = Day1;
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);
        SaveVpm(Package("com.example.shader", "2.3.3"));

        _clock.Now = Day1.AddDays(5);
        var moved = Path.Combine(_root, "projMoved");
        Assert.True(_store.CopyTo(_project, moved));

        var history = Assert.Single(_store.Load(moved)!.VpmHistory!);
        Assert.Equal([Day1, Day1.AddDays(1)], history.Changes.Select(change => change.From));
    }

    [Fact]
    public void 控えのJSONは_人が読める形で_終わりの時や計算で出せる値を書かない()
    {
        _clock.Now = Day1.AddTicks(1234567);
        SaveVpm(Package("com.example.shader", "2.3.2"));
        _clock.Now = Day1.AddDays(1);
        SaveVpm();

        var json = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, AppPaths.UnityScansDirName))));
        using var document = JsonDocument.Parse(json);
        var history = Assert.Single(document.RootElement.GetProperty("vpmHistory").EnumerateArray());
        Assert.Equal(["name", "displayName", "changes"], history.EnumerateObject().Select(property => property.Name));
        var changes = history.GetProperty("changes").EnumerateArray().ToList();

        // 時刻は秒まで・手元の時刻帯。版の無い外した行は version を書かない
        Assert.Equal("2026-10-01T21:03:12+09:00", changes[0].GetProperty("from").GetString());
        Assert.Equal(["from", "version", "removed"], changes[0].EnumerateObject().Select(property => property.Name));
        Assert.False(changes[0].GetProperty("removed").GetBoolean());
        Assert.Equal(["from", "removed"], changes[1].EnumerateObject().Select(property => property.Name));
        Assert.True(changes[1].GetProperty("removed").GetBoolean());
        Assert.DoesNotContain("\"shown\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"current\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"to\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 手で直した履歴の欠けた行は_読まない()
    {
        _store.Save(_project, UnityProjectScan.Empty);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_paths.Root, AppPaths.UnityScansDirName)));
        File.WriteAllText(file, $$"""
            {"project":{{JsonSerializer.Serialize(_project)}},"vpmHistory":[null,{"name":""},{"name":"com.example.a"},
              {"name":"com.example.b","changes":[null,{"from":"2026-10-02T00:00:00+09:00","version":"2"},{"from":"2026-10-01T00:00:00+09:00","version":"1"}]}]}
            """);

        var history = Assert.Single(_store.Load(_project)!.VpmHistory!);
        Assert.Equal(["1", "2"], history.Changes.Select(change => change.Version));
    }

    [Fact]
    public void ある時に入っていた版を引ける()
    {
        var history = new VpmPackageHistory("com.example.shader", null,
        [
            new VpmHistoryEntry(Day1, "2.3.2", false),
            new VpmHistoryEntry(Day1.AddDays(10), null, true),
            new VpmHistoryEntry(Day1.AddDays(20), "2.3.3", false),
        ]);

        Assert.Null(VpmHistories.StateAt(history, Day1.AddDays(-1)));
        Assert.Equal("2.3.2", VpmHistories.VersionsAt([history], Day1.AddDays(5))["com.example.shader"]);
        Assert.Empty(VpmHistories.VersionsAt([history], Day1.AddDays(15)));
        Assert.Equal("2.3.3", VpmHistories.VersionsAt([history], Day1.AddDays(20))["COM.example.shader"]);
    }

    /// <summary>時刻を決めて返す時計。手元の時刻帯も +9 に決める（試験の結果を PC の設定に左右させない）。</summary>
    private sealed class Clock : TimeProvider
    {
        private static readonly TimeZoneInfo s_zone = TimeZoneInfo.CreateCustomTimeZone("試験の時刻帯", TimeSpan.FromHours(9), "試験の時刻帯", "試験の時刻帯");

        public DateTimeOffset Now { get; set; } = Day1;

        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => s_zone;
    }
}
