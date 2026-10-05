using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ファイルが見つからなくなった日時（<see cref="LocalFileRecord.MissingSince"/>・ユーザ判断 2026-10-04）。
/// 前は「無い」が記録に残るのが、取り込みがその商品のファイルを扱って場所を外したときだけで、手で zip を消しても
/// カードの印・検索の条件・統計に出ず、その場でディスクを見る商品ページとだけ食い違っていた。
/// </summary>
public sealed class FileMissingSinceTests : IDisposable
{
    private const string ItemId = "local-1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-file-missing-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly string _elsewhere;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public FileMissingSinceTests()
    {
        _watched = Path.Combine(_root, "watched");
        _elsewhere = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(_watched);
        Directory.CreateDirectory(_elsewhere);
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
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

    private static readonly DateTimeOffset First = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private static LocalFileRecord FileAt(string hash, params string[] paths) => new()
    {
        Hash = hash,
        Paths = paths,
        SizeBytes = 3,
    };

    /// <summary>取り込み元（<see cref="_watched"/>）の外に置いた物。取り込みはこの商品のファイルを扱わない。</summary>
    private string ExistingFile(string name)
    {
        var path = Path.Combine(_elsewhere, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private string GoneFile(string name) => Path.Combine(_elsewhere, name);

    private Task SaveAsync(params LocalFileRecord[] files) => _store.Items.SaveAsync(new ItemRecord
    {
        Id = ItemId,
        Local = new LocalBlock { LocalFiles = files, Memo = "自分で書いたメモ" },
    });

    private async Task<ItemRecord> ItemAsync() => (await _store.Items.LoadAsync(ItemId))!;

    private async Task<LocalFileRecord> FileAsync(string hash)
        => (await ItemAsync()).Local.LocalFiles.Single(file => file.Hash == hash);

    private async Task ImportAsync()
    {
        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
        await pipeline.RunAsync(new ImportWorkSet([_watched]));
        Assert.Equal(0, client.Calls);
    }

    private ItemService NewService()
    {
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        return new ItemService(_store, client, new ImagePipeline(client, _paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験ではBOOTHへ行かないはず");
    }

    private string ItemJson() => File.ReadAllText(_paths.ItemFile(ItemId));

    // ---- 取り込みのたびに記録の場所を全部見る ----

    [Fact]
    public async Task 取り込みは_手で消したファイルに見つからなくなった日時を付け_場所は外さない()
    {
        var gone = GoneFile("消した.zip");
        await SaveAsync(FileAt("AAAA", gone));
        var before = DateTimeOffset.Now;

        await ImportAsync();

        var file = await FileAsync("AAAA");
        Assert.NotNull(file.MissingSince);
        Assert.InRange(file.MissingSince!.Value, before, DateTimeOffset.Now);

        // 覚えている場所は消さない（取り込みはこの商品のファイルを扱っていない）
        Assert.Equal([gone], file.Paths);

        var item = await ItemAsync();
        Assert.True(item.HasMissingFile);
        Assert.Equal("自分で書いたメモ", item.Local.Memo);
        Assert.Contains("\"missingSince\"", ItemJson());
    }

    [Fact]
    public async Task 無い間は_最初に無いと見た日時のまま()
    {
        await SaveAsync(FileAt("AAAA", GoneFile("消した.zip")) with { MissingSince = First });

        await ImportAsync();

        Assert.Equal(First, (await FileAsync("AAAA")).MissingSince);
    }

    [Fact]
    public async Task また見つかったら_取り込みが日時を消し_印から外れる()
    {
        await SaveAsync(FileAt("AAAA", ExistingFile("戻した.zip")) with { MissingSince = First });

        await ImportAsync();

        Assert.Null((await FileAsync("AAAA")).MissingSince);
        Assert.False((await ItemAsync()).HasMissingFile);
        Assert.DoesNotContain("missingSince", ItemJson());
    }

    [Fact]
    public async Task 場所が複数あれば_どれか1つでも在れば見つかっている扱い()
    {
        await SaveAsync(FileAt("AAAA", GoneFile("消した方.zip"), ExistingFile("残した方.zip")));

        await ImportAsync();

        Assert.Null((await FileAsync("AAAA")).MissingSince);
        Assert.False((await ItemAsync()).HasMissingFile);
    }

    /// <summary>外付けを外している間は「無い」と書かない（取り込みが外付けの上の場所を残すのと同じ考え）。</summary>
    [Fact]
    public async Task つながっていないドライブの上にしか無いファイルには_書かない()
    {
        await SaveAsync(FileAt("AAAA", Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "外付けの上.zip")));

        await ImportAsync();

        var file = await FileAsync("AAAA");
        Assert.Null(file.MissingSince);
        Assert.Single(file.Paths);
        Assert.False((await ItemAsync()).HasMissingFile);
    }

    [Fact]
    public async Task 場所の無いファイルにも_取り込みが日時を付ける()
    {
        await SaveAsync(FileAt("AAAA"));

        await ImportAsync();

        Assert.NotNull((await FileAsync("AAAA")).MissingSince);
    }

    // ---- 1つの決まり（FileMissingMarks.Apply） ----

    [Fact]
    public void 見たときと場所が変わったファイルには_当てない()
    {
        // 見ている間に取り込みが新しい場所を足していた：古い答えで「無い」と書くと、見つけた直後に印が戻る
        var current = new[] { FileAt("AAAA", @"D:\old\a.zip", @"D:\new\a.zip") };
        var sightings = new[] { new FileSighting("AAAA", [@"D:\old\a.zip"], FilePresence.Missing) };

        Assert.Null(FileMissingMarks.Apply(current, sightings, First));
    }

    [Fact]
    public void 書くのは日時だけで_ほかの欄は今の値のまま()
    {
        var current = new[]
        {
            FileAt("AAAA", @"D:\a.zip") with { VariationId = 7, Detached = true, ArchiveBroken = true, Contents = ["a.unitypackage"] },
        };

        var applied = FileMissingMarks.Apply(current, [new FileSighting("aaaa", [@"d:\A.ZIP"], FilePresence.Missing)], First)!;

        Assert.Equal(current[0] with { MissingSince = First }, Assert.Single(applied));
    }

    [Fact]
    public void つながっていないドライブと分かっただけなら_付いている日時も消さない()
    {
        var current = new[] { FileAt("AAAA", @"Q:\a.zip") with { MissingSince = First } };

        Assert.Null(FileMissingMarks.Apply(current, [new FileSighting("AAAA", [@"Q:\a.zip"], FilePresence.OnDetachedDrive)], DateTimeOffset.Now));
    }

    // ---- ドライブごとにまとめて見る ----

    [Fact]
    public void ドライブはドライブごとに1回だけ見て_つながっていないドライブの上のファイルは見に行かない()
    {
        var looked = new List<string>();
        var probe = new FilePresenceProbe(
            fileExists: path =>
            {
                looked.Add(path);
                return path.EndsWith("here.zip", StringComparison.Ordinal);
            },
            rootExists: root => root == @"D:\");

        Assert.Equal(FilePresence.Present, probe.Of([@"D:\a\here.zip"]));
        Assert.Equal(FilePresence.Missing, probe.Of([@"D:\b\gone.zip"]));
        Assert.Equal(FilePresence.OnDetachedDrive, probe.Of([@"Q:\c\x.zip"]));
        Assert.Equal(FilePresence.OnDetachedDrive, probe.Of([@"Q:\d\y.zip"]));

        Assert.Equal(2, probe.RootChecks);
        Assert.DoesNotContain(looked, path => path.StartsWith(@"Q:\", StringComparison.Ordinal));
    }

    [Fact]
    public void 答えの来ないドライブは_待ちを打ち切ってつながっていない扱い()
    {
        using var never = new ManualResetEventSlim();
        var probe = new FilePresenceProbe(
            fileExists: _ => throw new InvalidOperationException("つながっていないドライブの上は見に行かないはず"),
            rootExists: _ =>
            {
                never.Wait(TimeSpan.FromSeconds(5));
                return true;
            },
            rootWait: TimeSpan.FromMilliseconds(20));

        Assert.Equal(FilePresence.OnDetachedDrive, probe.Of([@"\\nas\share\a.zip", @"\\nas\share\b.zip"]));
        Assert.Equal(1, probe.RootChecks);
        never.Set();
    }

    // ---- 見つかったら消える（取り込みが場所を足す・中身で探す） ----

    [Fact]
    public void 取り込みが在る場所を足したら_日時を消す()
    {
        var existing = new[] { FileAt("AAAA", @"D:\old\a.zip") with { MissingSince = First } };

        var merged = LocalFileMerger.Merge(existing, [FileAt("AAAA", @"D:\new\a.zip")], pathExists: path => path == @"D:\new\a.zip", onMissingVolume: _ => false);

        var file = Assert.Single(merged);
        Assert.Equal([@"D:\new\a.zip"], file.Paths);
        Assert.Null(file.MissingSince);
    }

    [Fact]
    public void 取り込みが扱っても在る場所が無ければ_日時は残る()
    {
        var existing = new[] { FileAt("AAAA", @"D:\old\a.zip") with { MissingSince = First } };

        var merged = LocalFileMerger.Merge(existing, [], pathExists: _ => false, onMissingVolume: _ => false);

        Assert.Equal(First, Assert.Single(merged).MissingSince);
    }

    [Fact]
    public async Task 見つからないファイルを探して結び直したら_日時を消す()
    {
        var moved = Path.Combine(_watched, "移した.zip");
        await File.WriteAllTextAsync(moved, "なかみ");
        var hash = await FileHasher.ComputeSha256Async(moved);
        await SaveAsync(new LocalFileRecord
        {
            Hash = hash,
            Paths = [GoneFile("前の場所.zip")],
            SizeBytes = new FileInfo(moved).Length,
            MissingSince = First,
        });

        await new MissingFileFinder(_store).FindAsync([_watched]);

        var file = await FileAsync(hash);
        Assert.Equal([moved], file.Paths);
        Assert.Null(file.MissingSince);
    }

    // ---- 使おうとして分かったとき（商品ページ・開く・送る） ----

    [Fact]
    public async Task 使おうとして無いと分かったら日時を付け_在ると分かったら消す_メモは残る()
    {
        var path = GoneFile("消した.zip");
        await SaveAsync(FileAt("AAAA", path));
        var service = NewService();

        Assert.True(await service.NoteFilePresenceAsync(ItemId, [new FileSighting("AAAA", [path], FilePresence.Missing)]));
        Assert.NotNull((await FileAsync("AAAA")).MissingSince);
        Assert.True((await ItemAsync()).HasMissingFile);

        // 同じ状態は書かない
        Assert.False(await service.NoteFilePresenceAsync(ItemId, [new FileSighting("AAAA", [path], FilePresence.Missing)]));

        Assert.True(await service.NoteFilePresenceAsync(ItemId, [new FileSighting("AAAA", [path], FilePresence.Present)]));
        Assert.Null((await FileAsync("AAAA")).MissingSince);
        Assert.Equal("自分で書いたメモ", (await ItemAsync()).Local.Memo);
    }

    /// <summary>
    /// 商品ページが書くのと取り込みが書くのが重なっても、どちらも消えない（商品ごとの錠の中で今の値に当てる）。
    /// </summary>
    [Fact]
    public async Task 取り込みが商品の錠を持っている間に書いても_取り込みが足したファイルは消えない()
    {
        var path = GoneFile("消した.zip");
        await SaveAsync(FileAt("AAAA", path));

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var import = Task.Run(() => _store.Items.ChangeLocalAsync(
            ItemId,
            local =>
            {
                entered.Set();
                release.Wait();
                return local with { LocalFiles = [.. local.LocalFiles, FileAt("BBBB", @"D:\new\b.zip")] };
            },
            LocalOwners.Import));
        entered.Wait();

        var note = NewService().NoteFilePresenceAsync(ItemId, [new FileSighting("AAAA", [path], FilePresence.Missing)]);

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (_store.Items.LockUsers(ItemId) < 2 && !note.IsCompleted && waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(5);
        }

        release.Set();
        await Task.WhenAll(import, note);

        var files = (await ItemAsync()).Local.LocalFiles;
        Assert.Equal(["AAAA", "BBBB"], files.Select(file => file.Hash));
        Assert.NotNull(files.Single(file => file.Hash == "AAAA").MissingSince);
    }

    [Fact]
    public async Task 見ている間に取り込みが場所を足し替えていたら_古い答えで無いと書かない()
    {
        var path = GoneFile("前の場所.zip");
        await SaveAsync(FileAt("AAAA", path));

        // 見た後・書く前に、取り込みが移し先を結び直した
        await _store.Items.ChangeLocalAsync(
            ItemId,
            local => local with { LocalFiles = [FileAt("AAAA", @"D:\new\a.zip")] },
            LocalOwners.Import);

        Assert.False(await NewService().NoteFilePresenceAsync(ItemId, [new FileSighting("AAAA", [path], FilePresence.Missing)]));
        Assert.Null((await FileAsync("AAAA")).MissingSince);
    }

    // ---- 印・条件・統計 ----

    [Fact]
    public void 印と統計は_日時の付いた外していないファイルを数える()
    {
        static ItemRecord With(string id, LocalFileRecord file) => new()
        {
            Id = id,
            Local = new LocalBlock { LocalFiles = [file] },
        };

        var missing = With("1", FileAt("AAAA", @"D:\a.zip") with { MissingSince = First });
        var detached = With("2", FileAt("BBBB", @"D:\b.zip") with { MissingSince = First, Detached = true });
        var present = With("3", FileAt("CCCC", @"D:\c.zip"));

        Assert.True(missing.HasMissingFile);
        Assert.False(detached.HasMissingFile);
        Assert.False(present.HasMissingFile);

        var snapshot = StatsService.Build([missing, detached, present], new AvatarRegistry(), unresolvedCount: 0);
        Assert.Equal(1, snapshot.Backlog.MissingFileCount);
    }
}
