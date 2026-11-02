using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>プロジェクトを調べた結果の控え。作り物の商品と場所だけを使う。</summary>
public sealed class UnityScanStoreTests : IDisposable
{
    private const string Project = @"D:\作り物\projA";

    private readonly string _root = Directory.CreateTempSubdirectory("chmonos-scans-").FullName;
    private readonly AppPaths _paths;
    private readonly UnityScanStore _store;

    public UnityScanStoreTests()
    {
        _paths = new AppPaths(_root);
        _paths.EnsureCreated();
        _store = new UnityScanStore(_paths);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static UnityProjectScan Scan() => new(
        [new UnityProjectMatch("9900001", 3, 4)],
        new Dictionary<string, IReadOnlyList<UnityImageMatch>>
        {
            ["9900002"] = [new UnityImageMatch("Assets/a.png", "元.png", UnityImageMatchKind.SameImage)],
            ["9900003"] = [new UnityImageMatch("Assets/b.png", "b.png", UnityImageMatchKind.SameName)],
        });

    private string OnlyFile() => Assert.Single(Directory.GetFiles(Path.Combine(_root, AppPaths.UnityScansDirName)));

    [Fact]
    public void 控えた結果を読むと_同じ商品が同じように当たる()
    {
        _store.Save(Project, Scan());

        var loaded = _store.Load(Project)!;

        Assert.True(loaded.FindsSameAs(Scan()));
        Assert.Equal(new UnityProjectMatch("9900001", 3, 4), Assert.Single(loaded.Packages));
        Assert.True(loaded.Contains("9900002", sameName: false));
        Assert.False(loaded.Contains("9900003", sameName: false));
        Assert.True(loaded.Contains("9900003", sameName: true));
    }

    [Fact]
    public void 場所の大文字小文字が違っても_同じプロジェクトとして読む()
    {
        _store.Save(Project, Scan());

        Assert.NotNull(_store.Load(Project.ToUpperInvariant()));
    }

    [Fact]
    public void 控えが無い_壊れている_別の場所の物は_読まない()
    {
        Assert.Null(_store.Load(Project));

        _store.Save(Project, Scan());
        var file = OnlyFile();
        File.WriteAllText(file, "{壊れた");
        Assert.Null(_store.Load(Project));

        File.WriteAllText(file, """{"project":"D:\\作り物\\projB","packages":[{"itemId":"9900001","present":1,"total":1}]}""");
        Assert.Null(_store.Load(Project));
    }

    [Fact]
    public void 手で直した控えの欠けた行と配列は_空として読む()
    {
        _store.Save(Project, Scan());
        var project = System.Text.Json.JsonSerializer.Serialize(Project);
        File.WriteAllText(OnlyFile(), $$$"""{"project":{{{project}}},"packages":[null,{"itemId":"9900001","present":1,"total":1}],"images":{"9900002":null,"9900003":[null]}}""");

        var loaded = _store.Load(Project)!;

        Assert.True(loaded.Contains("9900001", sameName: false));
        Assert.False(loaded.Contains("9900002", sameName: true));
        Assert.False(loaded.Contains("9900003", sameName: true));
    }

    [Fact]
    public void 見つかった物が同じかを比べる()
    {
        var empty = UnityProjectScan.Empty;
        var moved = new UnityProjectScan(
            Scan().Packages,
            new Dictionary<string, IReadOnlyList<UnityImageMatch>>
            {
                ["9900002"] = [new UnityImageMatch("Assets/別の場所/a.png", "元.png", UnityImageMatchKind.SameImage)],
                ["9900003"] = Scan().Images["9900003"],
            });

        Assert.True(Scan().FindsSameAs(Scan()));
        Assert.False(Scan().FindsSameAs(empty));
        Assert.False(empty.FindsSameAs(Scan()));
        Assert.False(Scan().FindsSameAs(moved));
    }
}
