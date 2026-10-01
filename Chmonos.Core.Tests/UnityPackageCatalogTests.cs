using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Tests;

/// <summary>unitypackage の中身を1度だけ読んで残す（2026-09-13 ユーザ判断）。</summary>
public sealed class UnityPackageCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly string _files;
    private readonly DataStore _store;
    private readonly UnityPackagePathStore _paths;
    private readonly UnityPackageCatalog _catalog;

    public UnityPackageCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-unitypkg-" + Guid.NewGuid().ToString("N"));
        _files = Path.Combine(_root, "files");
        Directory.CreateDirectory(_files);
        var appPaths = new AppPaths(Path.Combine(_root, "data"));
        appPaths.EnsureCreated();
        _store = new DataStore(appPaths);
        _appPaths = appPaths;
        _paths = new UnityPackagePathStore(appPaths);
        _catalog = new UnityPackageCatalog(_store, _paths);
    }

    private readonly AppPaths _appPaths;

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

    /// <summary>本物と同じ形の unitypackage（tar.gz、アセットごとに guid/pathname）。</summary>
    private static byte[] MakeUnityPackage(params string[] assetPaths)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            for (var index = 0; index < assetPaths.Length; index++)
            {
                var guid = index.ToString("x32");
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/asset")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes("本体")),
                });
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{guid}/pathname")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(assetPaths[index] + "\n00")),
                });
            }
        }

        return memory.ToArray();
    }

    /// <summary>zip を作り、それを持つ手元のファイルの記録を返す（Contents は取り込みが作るのと同じ中身の一覧）。</summary>
    private LocalFileRecord MakeZip(string name, string hash, params (string Entry, byte[] Data)[] entries)
    {
        var path = Path.Combine(_files, name);
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
        {
            foreach (var (entry, data) in entries)
            {
                using var writer = archive.CreateEntry(entry).Open();
                writer.Write(data);
            }
        }

        return new LocalFileRecord
        {
            Hash = hash,
            Paths = [path],
            SizeBytes = new FileInfo(path).Length,
            Contents = entries.Select(entry => entry.Entry).ToList(),
        };
    }

    private Task SaveItemAsync(string id, params LocalFileRecord[] files)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    private async Task<LocalFileRecord> LoadFileAsync(string id)
        => Assert.Single((await _store.Items.LoadAsync(id))!.Local.LocalFiles);

    [Fact]
    public async Task 読んで入り先を書く()
    {
        var file = MakeZip("piyo.zip", "AAA",
            ("Bracelet/Bracelet.v1.01.unitypackage",
                MakeUnityPackage("Assets/Piyo_crafts/Bracelet/a.prefab", "Assets/Piyo_crafts/Bracelet/Textures/b.png")),
            ("readme.txt", Encoding.UTF8.GetBytes("読んでね")));
        await SaveItemAsync("1", file);

        Assert.Equal(1, await _catalog.ReadAsync([file]));
        Assert.Equal(1, await _catalog.ApplyAsync(["1"]));

        var summary = Assert.Single((await LoadFileAsync("1")).UnityPackages!);
        Assert.Equal("Bracelet/Bracelet.v1.01.unitypackage", summary.Entry);
        Assert.Equal(["Assets/Piyo_crafts"], summary.Roots);

        // 全部のパスは item ではなく控えに置く
        Assert.Equal(2, _paths.Load("AAA")!["Bracelet/Bracelet.v1.01.unitypackage"].Count);
    }

    /// <summary>
    /// 入り先は書く直前の今の一覧に当てる。全件を順に読む間に取り込みが同じ商品へ足したファイルを、
    /// 読んだ写しの一覧で書いて消していた。
    /// </summary>
    [Fact]
    public async Task 読んでいる間に足されたファイルを消さない()
    {
        var file = MakeZip("piyo.zip", "AAA", ("P.unitypackage", MakeUnityPackage("Assets/Piyo/a.prefab")));
        await SaveItemAsync("1", file);
        await _catalog.ReadAsync([file]);

        // 取り込みが同じ商品を書いている最中（錠を持ったまま）に写しに来る
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var import = Task.Run(() => _store.Items.ChangeLocalAsync(
            "1",
            current =>
            {
                entered.Set();
                release.Wait();
                return current with { LocalFiles = [.. current.LocalFiles, new LocalFileRecord { Hash = "NEW", Paths = [@"C:\dl\new.zip"], SizeBytes = 1 }] };
            },
            LocalOwners.Import));
        entered.Wait();

        var apply = _catalog.ApplyAsync(["1"]);
        await Task.WhenAny(apply, Task.Delay(500));
        release.Set();
        await import;

        Assert.Equal(1, await apply);

        var files = (await _store.Items.LoadAsync("1"))!.Local.LocalFiles;
        Assert.Contains(files, record => record.Hash == "NEW");
        Assert.NotNull(files.Single(record => record.Hash == "AAA").UnityPackages);
    }

    [Fact]
    public async Task 控えがあれば解き直さない()
    {
        // 鍵はハッシュ。中身が同じなら zip が消えても、別の商品の同じファイルでも引ける
        var file = MakeZip("fuka.zip", "BBB", ("F.unitypackage", MakeUnityPackage("Assets/FUKA/x.prefab")));
        await SaveItemAsync("1", file);
        await _catalog.ReadAsync([file]);
        File.Delete(file.Paths[0]);

        Assert.Equal(0, await _catalog.ReadAsync([file]));

        await SaveItemAsync("2", file);
        await _catalog.ApplyAsync(["2"]);
        Assert.Equal(["Assets/FUKA"], Assert.Single((await LoadFileAsync("2")).UnityPackages!).Roots);
    }

    [Fact]
    public async Task unitypackageの無いzipは読まない()
    {
        var file = MakeZip("model.zip", "CCC", ("model.fbx", Encoding.UTF8.GetBytes("fbx")));
        await SaveItemAsync("1", file);

        Assert.Equal(0, await _catalog.ReadAsync([file]));
        Assert.Equal(0, await _catalog.ApplyAsync(["1"]));
        Assert.Null((await LoadFileAsync("1")).UnityPackages);
        Assert.False(_paths.Has("CCC"));
    }

    [Fact]
    public async Task 書いてある物は書き直さない()
    {
        var file = MakeZip("piyo.zip", "DDD", ("P.unitypackage", MakeUnityPackage("Assets/Piyo/a.prefab")))
            with { UnityPackages = [new UnityPackageSummary { Entry = "P.unitypackage", Roots = ["Assets/前の値"] }] };
        await SaveItemAsync("1", file);
        await _catalog.ReadAsync([file]);

        Assert.Equal(0, await _catalog.ApplyAsync(["1"]));
        Assert.Equal(["Assets/前の値"], Assert.Single((await LoadFileAsync("1")).UnityPackages!).Roots);
    }

    [Fact]
    public async Task 読めないunitypackageは入り先を空で記録する()
    {
        // 空で記録すれば、取り込みのたびに同じ物を読み直さない
        var file = MakeZip("broken.zip", "EEE", ("broken.unitypackage", Encoding.UTF8.GetBytes("tar.gzではない")));
        await SaveItemAsync("1", file);

        Assert.Equal(1, await _catalog.ReadAsync([file]));
        await _catalog.ApplyAsync(["1"]);

        Assert.Empty(Assert.Single((await LoadFileAsync("1")).UnityPackages!).Roots);
        Assert.Equal(0, await _catalog.ReadAsync([file]));
    }

    [Fact]
    public async Task まだ書いていない物を探す()
    {
        var pending = MakeZip("a.zip", "FFF", ("A.unitypackage", MakeUnityPackage("Assets/A/a.prefab")));
        var done = MakeZip("b.zip", "GGG", ("B.unitypackage", MakeUnityPackage("Assets/B/b.prefab")))
            with { UnityPackages = [new UnityPackageSummary { Entry = "B.unitypackage", Roots = ["Assets/B"] }] };
        var none = MakeZip("c.zip", "HHH", ("c.fbx", Encoding.UTF8.GetBytes("fbx")));
        await SaveItemAsync("1", pending, none);
        await SaveItemAsync("2", done);

        var found = await _catalog.FindPendingAsync();

        Assert.Equal(["FFF"], found.Files.Select(file => file.Hash));
        Assert.Equal(["1"], found.ItemIds);
    }

    [Fact]
    public async Task 手でファイルを付けた商品をまとめて埋める()
    {
        var file = MakeZip("k.zip", "III", ("K/Kuuta.unitypackage", MakeUnityPackage("Assets/Kuuta_ShapekeyAddon/a.asset")));
        await SaveItemAsync("1", file);

        await _catalog.FillItemAsync("1");

        Assert.Equal(["Assets/Kuuta_ShapekeyAddon"], Assert.Single((await LoadFileAsync("1")).UnityPackages!).Roots);
    }

    [Fact]
    public void 取り込みで突き合わせても読んだ結果を失わない()
    {
        // 走査で見つけた方はまだ読んでいない（null）。既にある記録の値を残す
        var current = new LocalFileRecord
        {
            Hash = "JJJ",
            Paths = ["C:/a.zip"],
            SizeBytes = 1,
            UnityPackages = [new UnityPackageSummary { Entry = "A.unitypackage", Roots = ["Assets/A"] }],
        };
        var discovered = new LocalFileRecord { Hash = "JJJ", Paths = ["C:/b.zip"], SizeBytes = 1 };

        var merged = Assert.Single(LocalFileMerger.Merge([current], [discovered], _ => true));

        Assert.Equal(["Assets/A"], Assert.Single(merged.UnityPackages!).Roots);
    }

    [Fact]
    public void zipのハッシュがあれば控えから中身のパスを引く()
    {
        var file = MakeZip("h.zip", "KKK", ("H.unitypackage", MakeUnityPackage("Assets/H/a.prefab", "Assets/H/b.prefab")));
        var package = UnityHandoff.FindPackages(file.Paths[0]).Single() with { ZipHash = "KKK" };

        UnityHandoff.UsePathStore(_paths);
        try
        {
            Assert.Equal(2, UnityHandoff.ReadAssetPaths(package).Count);
            Assert.True(_paths.Has("KKK"));

            // zip が消えても（手元から消した後でも）控えから引ける
            File.Delete(file.Paths[0]);
            Assert.Equal(["Assets/H/a.prefab", "Assets/H/b.prefab"], UnityHandoff.ReadAssetPaths(package));
        }
        finally
        {
            UnityHandoff.UsePathStore(null);
        }
    }

    [Fact]
    public async Task 控えにパスごとのGUIDも書く()
    {
        // フォルダを移された物を GUID で探す（UnityProjectGuids）ため、パスと一緒に残す
        var file = MakeZip("g.zip", "GGG", ("G.unitypackage", MakeUnityPackage("Assets/G", "Assets/G/a.prefab")));

        await _catalog.ReadAsync([file]);

        var assets = _paths.Load("GGG")!["G.unitypackage"];
        Assert.Equal(
            [new UnityPackageAsset(0.ToString("x32"), "Assets/G"), new UnityPackageAsset(1.ToString("x32"), "Assets/G/a.prefab")],
            assets);

        // 人が開いて読める形：1行が「GUID: パス」
        var text = await File.ReadAllTextAsync(_appPaths.UnityPackageFile("GGG"));
        Assert.Contains($"\"{1.ToString("x32")}\": \"Assets/G/a.prefab\"", text);
    }

    [Fact]
    public async Task 読めない控えは無いのと同じで読み直す()
    {
        // 読めない控え（ここでは GUID を持たない形）は使えない。控えは作り直せる写しなので、zip を解き直して書き直す
        var file = MakeZip("o.zip", "OOO", ("O.unitypackage", MakeUnityPackage("Assets/O/a.prefab")));
        Directory.CreateDirectory(Path.GetDirectoryName(_appPaths.UnityPackageFile("OOO"))!);
        await File.WriteAllTextAsync(
            _appPaths.UnityPackageFile("OOO"),
            """{ "packages": { "O.unitypackage": [ "Assets/O/a.prefab" ] } }""");

        Assert.False(_paths.Has("OOO"));
        Assert.Null(_paths.Load("OOO"));

        Assert.Equal(1, await _catalog.ReadAsync([file]));
        Assert.Equal([new UnityPackageAsset(0.ToString("x32"), "Assets/O/a.prefab")], _paths.Load("OOO")!["O.unitypackage"]);
    }

    // ---- 一部の包みしか載っていない要約（2026-09-29） ----
    // 控えは商品ページなどで包みを1つずつ足しても作られる（UnityPackagePathStore.Add）。前の取り込みの裏はそれを「ある」と見て読み直さず、
    // 一部の包みしか載っていない要約を書き得た

    /// <summary>包みが2つの zip。商品ページで A だけ読んで控えに足し、要約にも A だけが載った形。</summary>
    private async Task<LocalFileRecord> PartialAsync(string id, string hash, IReadOnlyList<UnityPackageAsset>? storedA = null)
    {
        var file = MakeZip($"{hash}.zip", hash,
            ("A.unitypackage", MakeUnityPackage("Assets/A/a.prefab")),
            ("B/B.unitypackage", MakeUnityPackage("Assets/B/b.prefab")));
        _paths.Add(hash, "A.unitypackage", storedA ?? [new UnityPackageAsset(0.ToString("x32"), "Assets/A/a.prefab")]);
        file = file with { UnityPackages = [new UnityPackageSummary { Entry = "A.unitypackage", Roots = ["Assets/A"] }] };
        await SaveItemAsync(id, file);
        return file;
    }

    [Fact]
    public async Task 一部しか書いていない要約も探す()
    {
        var partial = await PartialAsync("1", "PPP");

        var found = await _catalog.FindPendingAsync();

        Assert.Equal([partial.Hash], found.Files.Select(file => file.Hash));
        Assert.Equal(["1"], found.ItemIds);
    }

    [Fact]
    public async Task 一部しか載っていない控えを読み直して要約を埋める()
    {
        var partial = await PartialAsync("1", "QQQ");

        Assert.Equal(1, await _catalog.ReadAsync([partial]));
        Assert.Equal(1, await _catalog.ApplyAsync(["1"]));

        var summaries = (await LoadFileAsync("1")).UnityPackages!;
        Assert.Equal(["A.unitypackage", "B/B.unitypackage"], summaries.Select(summary => summary.Entry).Order(StringComparer.Ordinal));
        Assert.Equal(["Assets/B"], summaries.Single(summary => summary.Entry == "B/B.unitypackage").Roots);
        Assert.Equal(["A.unitypackage", "B/B.unitypackage"], _paths.Load("QQQ")!.Keys.Order(StringComparer.Ordinal));

        // そろった後は、表示の側が item の要約だけで足りる（zip を開かない）
        Assert.NotNull(UnityHandoff.KnownPackages(await LoadFileAsync("1")));
        Assert.Empty((await _catalog.FindPendingAsync()).Files);
        Assert.Equal(0, await _catalog.ReadAsync([await LoadFileAsync("1")]));
    }

    [Fact]
    public async Task 控えにある包みは読み直すときも解き直さない()
    {
        // 控えの A は zip の中身と違う印の値にしておく。解き直していれば zip の中身に戻る
        var marker = new UnityPackageAsset("ffffffffffffffffffffffffffffffff", "Assets/控えの値/a.prefab");
        var partial = await PartialAsync("1", "RRR", [marker]);

        await _catalog.ReadAsync([partial]);

        Assert.Equal([marker], _paths.Load("RRR")!["A.unitypackage"]);
    }

    [Fact]
    public async Task 要約がそろっていれば控えが欠けていても要約は書き直さない()
    {
        // 要約は2つともある。控えだけ A しか無い（控えを消した・古い）。控えは読み直して埋めるが、要約はそのまま
        var file = MakeZip("s.zip", "SSS",
            ("A.unitypackage", MakeUnityPackage("Assets/A/a.prefab")),
            ("B.unitypackage", MakeUnityPackage("Assets/B/b.prefab")))
            with
            {
                UnityPackages =
                [
                    new UnityPackageSummary { Entry = "A.unitypackage", Roots = ["Assets/前の値A"] },
                    new UnityPackageSummary { Entry = "B.unitypackage", Roots = ["Assets/前の値B"] },
                ],
            };
        _paths.Add("SSS", "A.unitypackage", [new UnityPackageAsset(0.ToString("x32"), "Assets/A/a.prefab")]);
        await SaveItemAsync("1", file);

        Assert.Equal(1, await _catalog.ReadAsync([file]));
        Assert.Equal(0, await _catalog.ApplyAsync(["1"]));

        Assert.Equal(2, _paths.Load("SSS")!.Count);
        Assert.Equal(["Assets/前の値B"], (await LoadFileAsync("1")).UnityPackages!.Single(summary => summary.Entry == "B.unitypackage").Roots);
    }

    [Fact]
    public async Task 開けないzipは控えずに次の取り込みでまた開く()
    {
        // 空の控えを書くと、入り先の無い要約が書かれる。控えなければ、開けるようになった取り込みで読める
        var path = Path.Combine(_files, "notzip.zip");
        await File.WriteAllTextAsync(path, "zip ではない");
        var file = new LocalFileRecord { Hash = "TTT", Paths = [path], SizeBytes = 1, Contents = ["T.unitypackage"] };
        await SaveItemAsync("1", file);

        Assert.Equal(0, await _catalog.ReadAsync([file]));
        Assert.Equal(0, await _catalog.ApplyAsync(["1"]));

        Assert.False(_paths.Has("TTT"));
        Assert.Null((await LoadFileAsync("1")).UnityPackages);
    }

    [Fact]
    public void 使うときに読んだ物も読めない控えを書き直す()
    {
        var file = MakeZip("u.zip", "UUU", ("U.unitypackage", MakeUnityPackage("Assets/U/a.prefab")));
        Directory.CreateDirectory(Path.GetDirectoryName(_appPaths.UnityPackageFile("UUU"))!);
        File.WriteAllText(
            _appPaths.UnityPackageFile("UUU"),
            """{ "packages": { "U.unitypackage": [ "Assets/U/a.prefab" ] } }""");
        var package = UnityHandoff.FindPackages(file.Paths[0]).Single() with { ZipHash = "UUU" };

        UnityHandoff.UsePathStore(_paths);
        try
        {
            Assert.Equal([new UnityPackageAsset(0.ToString("x32"), "Assets/U/a.prefab")], UnityHandoff.ReadAssets(package));
            Assert.True(_paths.Has("UUU"));
        }
        finally
        {
            UnityHandoff.UsePathStore(null);
        }
    }
}
