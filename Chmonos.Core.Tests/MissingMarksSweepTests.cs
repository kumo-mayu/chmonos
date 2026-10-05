using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 起動したときの裏の見回り（<see cref="MissingMarksSweep"/>・ユーザ判断 2026-10-05）と、
/// 「見つからないファイルを探す」で見つからなかった物への日時（<see cref="MissingFileFinder"/>）。
/// 前は取り込みと使おうとした画面でしか「見つからなくなった日時」を書かず、取り込まずに使っている間は、
/// 手で消した zip が印・検索の条件「見つからないファイル」・統計に出なかった。
/// </summary>
public sealed class MissingMarksSweepTests : IDisposable
{
    private const string ItemId = "local-1";
    private const string Memo = "自分で書いたメモ";

    private static readonly DateTimeOffset First = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-missing-sweep-" + Guid.NewGuid().ToString("N"));
    private readonly string _watched;
    private readonly string _elsewhere;
    private readonly AppPaths _paths;
    private readonly DataStore _store;

    public MissingMarksSweepTests()
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

    private static LocalFileRecord FileAt(string hash, params string[] paths) => new() { Hash = hash, Paths = paths, SizeBytes = 3 };

    private static LocalFolderRecord FolderAt(string path, DateTimeOffset? missingSince = null)
        => new() { Path = path, RegisteredAt = DateTimeOffset.UnixEpoch, MissingSince = missingSince };

    private string ExistingFile(string name)
    {
        var path = Path.Combine(_elsewhere, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    private string ExistingFolder(string name) => Directory.CreateDirectory(Path.Combine(_elsewhere, name)).FullName;

    private string Gone(string name) => Path.Combine(_elsewhere, name);

    private Task SaveAsync(IReadOnlyList<LocalFileRecord> files, IReadOnlyList<LocalFolderRecord>? folders = null)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Local = new LocalBlock { LocalFiles = files, LocalFolders = folders ?? [], Memo = Memo },
        });

    private async Task<ItemRecord> ItemAsync() => (await _store.Items.LoadAsync(ItemId))!;

    // ---- 起動時の見回り ----

    [Fact]
    public async Task 起動時の見回りは_手で消したファイルとフォルダに日時を付け_場所とメモは残す()
    {
        var goneFile = Gone("消した.zip");
        var goneFolder = Gone("消したフォルダ");
        await SaveAsync([FileAt("AAAA", goneFile)], [FolderAt(goneFolder)]);
        var before = DateTimeOffset.Now;

        var written = await new MissingMarksSweep(_store).SweepAsync();

        Assert.Equal([ItemId], written);
        var item = await ItemAsync();
        var file = item.Local.LocalFiles.Single();
        Assert.InRange(file.MissingSince!.Value, before, DateTimeOffset.Now);
        Assert.Equal([goneFile], file.Paths);
        Assert.InRange(item.Local.LocalFolders.Single().MissingSince!.Value, before, DateTimeOffset.Now);
        Assert.Equal(goneFolder, item.Local.LocalFolders.Single().Path);
        Assert.True(item.HasMissingFile);
        Assert.Equal(Memo, item.Local.Memo);
    }

    [Fact]
    public async Task また見つかったら_起動時の見回りが日時を消す()
    {
        await SaveAsync(
            [FileAt("AAAA", ExistingFile("戻した.zip")) with { MissingSince = First }],
            [FolderAt(ExistingFolder("戻したフォルダ"), First)]);

        var written = await new MissingMarksSweep(_store).SweepAsync();

        Assert.Equal([ItemId], written);
        var item = await ItemAsync();
        Assert.Null(item.Local.LocalFiles.Single().MissingSince);
        Assert.Null(item.Local.LocalFolders.Single().MissingSince);
        Assert.NotNull(item.Local.LocalFolders.Single().LastSeenAt);
        Assert.False(item.HasMissingFile);
        Assert.Equal(Memo, item.Local.Memo);
    }

    /// <summary>
    /// 一度も在ると確かめていない（見た日時が空の）フォルダを在ると見たら、見た日時を入れる。取り込みの数え直しと同じ決まり
    /// （file-lifecycle.md 気になった所19。前は見回りだけが空のまま残した）。在ると確かめた日時のあるフォルダは、毎回は書き直さない。
    /// </summary>
    [Fact]
    public async Task 見た日時が空の在るフォルダには_見回りが見た日時を入れる()
    {
        var seen = ExistingFolder("確かめたフォルダ");
        var neverSeen = ExistingFolder("まだ確かめていないフォルダ");
        await SaveAsync([], [FolderAt(seen) with { LastSeenAt = First }, FolderAt(neverSeen)]);
        var before = DateTimeOffset.Now;

        var written = await new MissingMarksSweep(_store).SweepAsync();

        Assert.Equal([ItemId], written);
        var folders = (await ItemAsync()).Local.LocalFolders;
        Assert.Equal(First, folders.Single(folder => folder.Path == seen).LastSeenAt);
        Assert.InRange(folders.Single(folder => folder.Path == neverSeen).LastSeenAt!.Value, before, DateTimeOffset.Now);
        Assert.Empty(await new MissingMarksSweep(_store).SweepAsync());
    }

    [Fact]
    public async Task 無い間は最初の日時のままで_変わりが無ければ書かない()
    {
        await SaveAsync([FileAt("AAAA", Gone("消した.zip")) with { MissingSince = First }], [FolderAt(Gone("消したフォルダ"), First)]);

        var written = await new MissingMarksSweep(_store).SweepAsync();

        Assert.Empty(written);
        var item = await ItemAsync();
        Assert.Equal(First, item.Local.LocalFiles.Single().MissingSince);
        Assert.Equal(First, item.Local.LocalFolders.Single().MissingSince);
    }

    /// <summary>外付けを外している間は「無い」と書かない（取り込みの見回りと同じ考え）。</summary>
    [Fact]
    public async Task つながっていないドライブの上のファイルとフォルダには_書かない()
    {
        var offline = UnresolvedMergeTests.MissingVolumeFolder();
        await SaveAsync([FileAt("AAAA", Path.Combine(offline, "外付けの上.zip"))], [FolderAt(Path.Combine(offline, "外付けのフォルダ"))]);

        var written = await new MissingMarksSweep(_store).SweepAsync();

        Assert.Empty(written);
        var item = await ItemAsync();
        Assert.Null(item.Local.LocalFiles.Single().MissingSince);
        Assert.Null(item.Local.LocalFolders.Single().MissingSince);
        Assert.False(item.HasMissingFile);
    }

    /// <summary>外したファイルにも日時を書く今の作りはそのまま（ユーザ判断 2026-10-05）。印と条件は外したファイルを数えない。</summary>
    [Fact]
    public async Task 外したファイルにも日時を書き_印には数えない()
    {
        await SaveAsync([FileAt("AAAA", Gone("外した.zip")) with { Detached = true }]);

        await new MissingMarksSweep(_store).SweepAsync();

        var item = await ItemAsync();
        Assert.NotNull(item.Local.LocalFiles.Single().MissingSince);
        Assert.False(item.HasMissingFile);
    }

    /// <summary>
    /// 見回りは1本ずつ。起動時の見回りの途中で取り込みが始まっても、取り込みの見回り（フォルダの数え直しを含む）は
    /// 起動時の見回りが済むまで待ち、済んだ後は錠の中で今の値と同じなので書き直さない。
    /// </summary>
    [Fact]
    public async Task 起動時の見回りの途中で取り込みが始まっても_重ならず_同じ商品を二度書かない()
    {
        await SaveAsync([FileAt("AAAA", Gone("消した.zip"))], [FolderAt(Gone("消したフォルダ"))]);

        var sweepLooking = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var probes = 0;
        var sweep = new MissingMarksSweep(_store, () =>
        {
            var number = Interlocked.Increment(ref probes);
            return new FilePresenceProbe(fileExists: path =>
            {
                if (number == 1)
                {
                    sweepLooking.TrySetResult();
                    release.Task.Wait();
                }

                return File.Exists(path);
            });
        });

        var startup = sweep.SweepAsync();
        await sweepLooking.Task;

        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        var pipeline = new ImportPipeline(
            _store, client, new ImagePipeline(client, _paths, settings), () => settings, missingMarks: sweep);
        var import = pipeline.RunAsync(new ImportWorkSet([_watched]));

        // 重なるなら、空の取り込み元の取り込みはすぐに見回りへ進み、2つ目の見方を作る
        await Task.WhenAny(import, Task.Delay(500));
        Assert.Equal(1, Volatile.Read(ref probes));
        Assert.False(import.IsCompleted);

        release.SetResult();
        var written = await startup;
        await import;

        Assert.Equal([ItemId], written);
        Assert.Equal(2, Volatile.Read(ref probes));
        Assert.Equal(0, client.Calls);

        // 取り込みは起動時の見回りが付けた日時をそのまま残す（無い間は最初の日時）
        var item = await ItemAsync();
        Assert.True(item.Local.LocalFiles.Single().MissingSince <= DateTimeOffset.Now);
        Assert.NotNull(item.Local.LocalFolders.Single().MissingSince);

        // 取り込みが書き直していないことは、もう一度見回っても何も書かないことで確かめる
        Assert.Empty(await new MissingMarksSweep(_store).SweepAsync());
    }

    /// <summary>
    /// 取り込みの登録フォルダの判定も、見回りと同じ部品で根の答えを打ち切る（file-lifecycle.md 気になった所15）。
    /// 前は Directory.Exists を打ち切り無しで呼び、落ちた共有の上の登録フォルダで周回の頭が止まり得た。
    /// 根が答えない（打ち切った）ドライブは「つながっていない」側に倒し、「無い」とは書かない。
    /// </summary>
    [Fact]
    public async Task 取り込みの登録フォルダの判定は_根が答えなければ打ち切り_無いと書かない()
    {
        await SaveAsync([], [FolderAt(Gone("消したフォルダ"))]);

        // 根を見に行くと答えが返らない（落ちた共有）。待つ長さは0なので、時計に頼らず必ず打ち切られる
        var never = new TaskCompletionSource();
        var rootAsked = 0;
        var sweep = new MissingMarksSweep(_store, () => new FilePresenceProbe(
            rootExists: _ =>
            {
                Interlocked.Increment(ref rootAsked);
                never.Task.Wait();
                return true;
            },
            rootWait: TimeSpan.Zero));

        try
        {
            var client = new OffUiThreadTests.OfflineClient();
            var settings = new AppSettings { SaveImages = false };
            await new ImportPipeline(
                    _store, client, new ImagePipeline(client, _paths, settings), () => settings, missingMarks: sweep)
                .RunAsync(new ImportWorkSet([_watched]));

            Assert.Null((await ItemAsync()).Local.LocalFolders.Single().MissingSince);
            Assert.True(Volatile.Read(ref rootAsked) >= 1);
        }
        finally
        {
            never.TrySetResult();
        }
    }

    [Fact]
    public async Task 取り込みが先に同じ答えを書いていれば_起動時の見回りは書かない()
    {
        await SaveAsync([FileAt("AAAA", Gone("消した.zip"))], [FolderAt(Gone("消したフォルダ"))]);
        var client = new OffUiThreadTests.OfflineClient();
        var settings = new AppSettings { SaveImages = false };
        await new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings)
            .RunAsync(new ImportWorkSet([_watched]));
        var marked = (await ItemAsync()).Local;

        Assert.Empty(await new MissingMarksSweep(_store).SweepAsync());
        Assert.Equal(marked.LocalFiles.Single().MissingSince, (await ItemAsync()).Local.LocalFiles.Single().MissingSince);
        Assert.Equal(marked.LocalFolders.Single().MissingSince, (await ItemAsync()).Local.LocalFolders.Single().MissingSince);
    }

    // ---- 「見つからないファイルを探す」で見つからなかった物 ----

    [Fact]
    public async Task 探しても見つからなかったファイルに日時を付ける_つながっていないドライブの上の物には付けない()
    {
        var offline = Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "外付けの上.zip");
        await SaveAsync([FileAt("AAAA", Gone("消した.zip")), FileAt("BBBB", offline), FileAt("CCCC")]);
        var before = DateTimeOffset.Now;

        var result = await new MissingFileFinder(_store).FindAsync([_watched]);

        Assert.Equal(0, result.Relinked);
        var files = (await ItemAsync()).Local.LocalFiles.ToDictionary(file => file.Hash);
        Assert.InRange(files["AAAA"].MissingSince!.Value, before, DateTimeOffset.Now);
        Assert.InRange(files["CCCC"].MissingSince!.Value, before, DateTimeOffset.Now);
        Assert.Null(files["BBBB"].MissingSince);
        Assert.Equal([offline], files["BBBB"].Paths);
        Assert.Equal(Memo, (await ItemAsync()).Local.Memo);
    }

    [Fact]
    public async Task 探しても見つからなかったファイルに前から付いていた日時は_最初の日時のまま()
    {
        await SaveAsync([FileAt("AAAA", Gone("消した.zip")) with { MissingSince = First }]);

        await new MissingFileFinder(_store).FindAsync([_watched]);

        Assert.Equal(First, (await ItemAsync()).Local.LocalFiles.Single().MissingSince);
    }
}
