using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.Core.Tests;

/// <summary>
/// 商品ページ・改変の画面で、item に書いてある入る先を使い、zip と控えを読み直さないこと（2026-09-29）。
/// ディスクは見ない（在るか・zip の一覧・控え・zip の中身をすべて差し替える）。zip の場所は在らない場所にして、メモリの表（在る zip だけ覚える）にも当たらないようにする。
/// </summary>
public sealed class UnityPackageReadsTests
{
    private const string Zip = @"Q:\読み取り試験\在らない\商品.zip";

    private static LocalFileRecord Record(string hash, IReadOnlyList<string> contents, IReadOnlyList<UnityPackageSummary>? summaries) => new()
    {
        Hash = hash,
        Paths = [Zip],
        SizeBytes = 1,
        Contents = contents,
        UnityPackages = summaries,
    };

    private static UnityPackageSummary Summary(string entry, params string[] roots) => new() { Entry = entry, Roots = roots };

    private static IReadOnlyList<UnityPackageEntry> FailFind(string zip) => throw new InvalidOperationException("zip を開いた");

    [Fact]
    public void 要約がそろっていればzipを開かずzipの中の順に返す()
    {
        var file = Record(
            "AAA",
            ["読んでね.txt", "Dep/dep.unitypackage", "Main/main.unitypackage"],
            // 控えの並び（辞書の順）が zip の順と違っても、zip の中の順（中身の一覧）に並べ直す
            [Summary("Main/main.unitypackage", "Assets/FUKA"), Summary("Dep/dep.unitypackage", "Packages/com.dep")]);

        var places = UnityHandoff.PlacesOf(file, _ => true, FailFind);

        Assert.Equal(["Dep/dep.unitypackage", "Main/main.unitypackage"], places.Select(place => place.Entry.EntryPath));
        Assert.Equal(["Packages/com.dep"], places[0].Roots!);
        Assert.Equal(["Assets/FUKA"], places[1].Roots!);
        Assert.All(places, place => Assert.Equal("AAA", place.Entry.ZipHash));
        Assert.All(places, place => Assert.Equal(Zip, place.Entry.ZipPath));
    }

    [Fact]
    public void 要約が無ければ今までどおりzipを開く()
    {
        var opened = 0;
        var file = Record("BBB", ["a.unitypackage"], summaries: null);

        var places = UnityHandoff.PlacesOf(file, _ => true, zip =>
        {
            opened++;
            return [new UnityPackageEntry(zip, "a.unitypackage", 10)];
        });

        Assert.Equal(1, opened);
        var place = Assert.Single(places);
        Assert.Null(place.Roots);
        Assert.Equal("BBB", place.Entry.ZipHash);
    }

    [Fact]
    public void 要約が一部の包みしか載せていなければzipを開く()
    {
        // 控えが商品ページで1つずつ足されて作られると、取り込みの裏はそれを「ある」と見て、一部だけの要約を書くことがある
        var opened = 0;
        var file = Record("CCC", ["a.unitypackage", "b.unitypackage"], [Summary("a.unitypackage", "Assets/A")]);

        var places = UnityHandoff.PlacesOf(file, _ => true, zip =>
        {
            opened++;
            return [new UnityPackageEntry(zip, "a.unitypackage", 1), new UnityPackageEntry(zip, "b.unitypackage", 1)];
        });

        Assert.Equal(1, opened);
        Assert.Equal(2, places.Count);
        Assert.All(places, place => Assert.Null(place.Roots));
    }

    [Fact]
    public void zipが無ければ要約があっても行を出さない()
    {
        var file = Record("DDD", ["a.unitypackage"], [Summary("a.unitypackage", "Assets/A")]);

        Assert.Empty(UnityHandoff.PlacesOf(file, _ => false, FailFind));
    }

    [Fact]
    public void unitypackageの無いzipの空の要約はzipを開かず空()
    {
        var file = Record("EEE", ["readme.txt"], []);

        Assert.Empty(UnityHandoff.PlacesOf(file, _ => true, FailFind));
    }

    private sealed class FakeDisk
    {
        public int StoreLoads { get; private set; }
        public int ZipReads { get; private set; }
        public List<string> Added { get; } = [];
        public Dictionary<string, Dictionary<string, IReadOnlyList<UnityPackageAsset>>> Stores { get; } = new(StringComparer.OrdinalIgnoreCase);

        public UnityPackageReads Reads() => new(
            hash =>
            {
                StoreLoads++;
                return Stores.TryGetValue(hash, out var store) ? store : null;
            },
            package =>
            {
                ZipReads++;
                return [new UnityPackageAsset("f0", $"Assets/FromZip/{package.EntryPath}")];
            },
            (hash, entry, _) => Added.Add($"{hash}:{entry}"));
    }

    private static UnityPackageEntry Package(string hash, string entry) => new(Zip, entry, 0) { ZipHash = hash };

    private static Dictionary<string, IReadOnlyList<UnityPackageAsset>> Store(params string[] entries)
        => entries.ToDictionary(
            entry => entry,
            entry => (IReadOnlyList<UnityPackageAsset>)[new UnityPackageAsset("a0", $"Assets/{entry}/x.prefab")],
            StringComparer.Ordinal);

    [Fact]
    public void 同じzipの包みを続けて読むと控えは1回だけ読む()
    {
        var disk = new FakeDisk();
        disk.Stores["H1"] = Store("a", "b", "c");
        var reads = disk.Reads();

        var roots = new[] { "a", "b", "c" }.Select(entry => reads.ReadDestinations(Package("H1", entry))).ToList();

        Assert.Equal(1, disk.StoreLoads);
        Assert.Equal(0, disk.ZipReads);
        Assert.Equal(["Assets/a", "Assets/b", "Assets/c"], roots.Select(root => Assert.Single(root)));
    }

    [Fact]
    public void zipが変わるときだけ控えを読み直す()
    {
        var disk = new FakeDisk();
        disk.Stores["H1"] = Store("a", "b");
        disk.Stores["H2"] = Store("c", "d");
        var reads = disk.Reads();

        foreach (var package in new[] { Package("H1", "a"), Package("H1", "b"), Package("H2", "c"), Package("H2", "d") })
        {
            Assert.NotEmpty(reads.ReadAssets(package));
        }

        Assert.Equal(2, disk.StoreLoads);
        Assert.Equal(0, disk.ZipReads);
    }

    [Fact]
    public void 控えに無い包みはzipから読んで控えに足しそのあとは読み直さない()
    {
        var disk = new FakeDisk();
        disk.Stores["H1"] = Store("a");
        var reads = disk.Reads();

        Assert.Equal(["Assets/a/x.prefab"], reads.ReadAssets(Package("H1", "a")).Select(asset => asset.Path));
        Assert.Equal(["Assets/FromZip/b"], reads.ReadAssets(Package("H1", "b")).Select(asset => asset.Path));
        Assert.Equal(["Assets/FromZip/b"], reads.ReadAssets(Package("H1", "b")).Select(asset => asset.Path));

        Assert.Equal(1, disk.StoreLoads);
        Assert.Equal(1, disk.ZipReads);
        Assert.Equal(["H1:b"], disk.Added);
    }

    [Fact]
    public void 控えが無いか読めないzipは包みごとにzipを解くが控えは1回だけ見る()
    {
        // 前の形（パスだけ）の控えは読めない物として null が返る。そのときは今までどおり zip を解いて書き直す
        var disk = new FakeDisk();
        var reads = disk.Reads();

        reads.ReadAssets(Package("OLD", "a"));
        reads.ReadAssets(Package("OLD", "b"));

        Assert.Equal(1, disk.StoreLoads);
        Assert.Equal(2, disk.ZipReads);
        Assert.Equal(["OLD:a", "OLD:b"], disk.Added);
    }

    private static UnityPackagePlace Place(string hash, string entry, params string[] roots)
        => new(Package(hash, entry), roots);

    [Fact]
    public void 入る先が書いてある包み1つなら中身を読まない()
    {
        var disk = new FakeDisk();

        var roots = UnityHandoff.DestinationRoots([Place("H1", "a", "Assets/A", "Assets/B")], disk.Reads());

        Assert.Equal(["Assets/A", "Assets/B"], roots);
        Assert.Equal(0, disk.StoreLoads);
    }

    [Fact]
    public void 包みが複数でも入る先が1つにまとまるなら中身を読まない()
    {
        var disk = new FakeDisk();

        var roots = UnityHandoff.DestinationRoots([Place("H1", "a", "Assets/A"), Place("H1", "b", "Assets/A"), Place("H1", "c")], disk.Reads());

        Assert.Equal(["Assets/A"], roots);
        Assert.Equal(0, disk.StoreLoads);
    }

    [Fact]
    public void 包みごとに入る先が分かれるなら並びを決めるため控えを1回読む()
    {
        var disk = new FakeDisk();
        disk.Stores["H1"] = new(StringComparer.Ordinal)
        {
            ["a"] = [new UnityPackageAsset("1", "Packages/com.dep/p.cs")],
            ["b"] = [new UnityPackageAsset("2", "Assets/FUKA/x.prefab"), new UnityPackageAsset("3", "Assets/FUKA/y.prefab")],
        };

        var roots = UnityHandoff.DestinationRoots(
            [Place("H1", "a", "Packages/com.dep"), Place("H1", "b", "Assets/FUKA")], disk.Reads());

        // パスの多い方が先（書いてある入る先を包みの順につなぐと逆になる）
        Assert.Equal(["Assets/FUKA", "Packages/com.dep"], roots);
        Assert.Equal(1, disk.StoreLoads);
        Assert.Equal(0, disk.ZipReads);
    }

    [Fact]
    public void 入る先が書いていない包みがあれば読む()
    {
        var disk = new FakeDisk();
        disk.Stores["H1"] = Store("a", "b");

        var roots = UnityHandoff.DestinationRoots([Place("H1", "a", "Assets/a"), new UnityPackagePlace(Package("H1", "b"), null)], disk.Reads());

        Assert.Equal(["Assets/a", "Assets/b"], roots);
        Assert.Equal(1, disk.StoreLoads);
    }
}
