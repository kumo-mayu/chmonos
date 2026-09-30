using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 管理対象からまとめて除外する（2026-10-01）。
///
/// 前は1個ごとに命令を呼び、1個ごとに除外の記録と未確定の記録を丸ごと読み書きしていた（5,000 個で約2分34秒）。
/// 1回で書く形にしても、結果は1個ずつ外したときと同じで、取り込みや人の書き込みと重なっても欠けない・戻らないことを確かめる。
/// </summary>
public sealed class BulkExcludeTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-bulk-exclude-" + Guid.NewGuid().ToString("N"));

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

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験は BOOTH へ行かないはず");
    }

    private (DataStore Store, ItemService Service) NewLibrary(string name)
    {
        var paths = new AppPaths(Path.Combine(_root, name));
        paths.EnsureCreated();
        var store = new DataStore(paths);
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        return (store, new ItemService(store, client, new ImagePipeline(client, paths)));
    }

    private UnresolvedFile Unresolved(string name) => new()
    {
        Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))),
        Paths = [Path.Combine(_root, "files", name)],
        SizeBytes = 10,
        ModifiedAtUtc = At,
        FirstSeenAt = At,
    };

    private static HashSet<string> Hashes(IEnumerable<UnresolvedFile> files)
        => files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Hashes(IEnumerable<ExcludedEntry> entries)
        => entries.Select(entry => entry.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 同じ場面（既に除外に在る物・未確定に無い物・同じ中身の重なりを含む）で、1回で外した結果と1個ずつ外した結果が同じ。
    /// 既に在った除外は、日時も理由も先に外したときのまま。
    /// </summary>
    [Fact]
    public async Task ExcludingAtOnceEndsLikeExcludingOneByOne()
    {
        var targets = Enumerable.Range(0, 30).Select(index => Unresolved($"target-{index:00}.bin")).ToList();
        var kept = Enumerable.Range(0, 10).Select(index => Unresolved($"kept-{index:00}.bin")).ToList();
        var notListed = Unresolved("not-listed.bin");
        var already = new ExcludedEntry { Hash = targets[3].Hash, Paths = targets[3].Paths, ExcludedAt = At.AddDays(-5), Reason = "前に外した" };
        List<UnresolvedFile> request = [.. targets, notListed, targets[7]];

        async Task<DataStore> RunAsync(string name, Func<ItemService, Task> exclude)
        {
            var (store, service) = NewLibrary(name);
            await store.Unresolved.SaveAsync([.. kept.Take(5), .. targets, .. kept.Skip(5)]);
            await store.Excluded.SaveAsync([already]);
            await exclude(service);
            return store;
        }

        var once = await RunAsync("once", service => service.ExcludeAsync(request, "試験"));
        var each = await RunAsync("each", async service =>
        {
            foreach (var file in request)
            {
                await service.ExcludeAsync([file], "試験");
            }
        });

        Assert.Equal(Hashes(kept), Hashes(once.Unresolved.Load()));
        Assert.Equal(Hashes(each.Unresolved.Load()), Hashes(once.Unresolved.Load()));

        var excluded = once.Excluded.Load();
        Assert.Equal(Hashes(each.Excluded.Load()), Hashes(excluded));
        Assert.Equal(targets.Count + 1, excluded.Count);
        var earlier = Assert.Single(excluded, entry => entry.Hash == already.Hash);
        Assert.Equal("前に外した", earlier.Reason);
        Assert.Equal(already.ExcludedAt, earlier.ExcludedAt);
        Assert.All(excluded.Where(entry => entry.Hash != already.Hash), entry => Assert.Equal("試験", entry.Reason));
        Assert.Equal(notListed.Paths, Assert.Single(excluded, entry => entry.Hash == notListed.Hash).Paths);
    }

    /// <summary>どちらの記録も1回だけ書く。変える物が無ければ書かない。</summary>
    [Fact]
    public async Task EachRecordIsWrittenOnceAndNotAtAllWhenNothingChanges()
    {
        var (store, service) = NewLibrary("once");
        var targets = Enumerable.Range(0, 50).Select(index => Unresolved($"target-{index:00}.bin")).ToList();
        await store.Unresolved.SaveAsync([.. targets]);
        var excludedBefore = store.Excluded.WriteCount;
        var unresolvedBefore = store.Unresolved.WriteCount;

        await service.ExcludeAsync(targets, "試験");

        Assert.Equal(excludedBefore + 2, store.Excluded.WriteCount);
        Assert.Equal(unresolvedBefore + 2, store.Unresolved.WriteCount);

        // もう一度同じ物を外しても、どちらも書き直さない
        await service.ExcludeAsync(targets, "試験");
        await service.ExcludeAsync([], "試験");

        Assert.Equal(excludedBefore + 2, store.Excluded.WriteCount);
        Assert.Equal(unresolvedBefore + 2, store.Unresolved.WriteCount);
    }

    /// <summary>まとめて外した物を、まとめて戻せる（外す前の未確定の記録のまま・ほかの除外は残る）。</summary>
    [Fact]
    public async Task ExcludedFilesComeBackTogether()
    {
        var (store, service) = NewLibrary("undo");
        var targets = Enumerable.Range(0, 20).Select(index => Unresolved($"target-{index:00}.bin")).ToList();
        var other = new ExcludedEntry { Hash = Unresolved("other.bin").Hash, ExcludedAt = At, Reason = "前に外した" };
        await store.Unresolved.SaveAsync([.. targets]);
        await store.Excluded.SaveAsync([other]);

        await service.ExcludeAsync(targets, "試験");
        Assert.Empty(store.Unresolved.Load());

        await service.UndoExcludeAsync(targets);

        Assert.Equal(Hashes(targets), Hashes(store.Unresolved.Load()));
        Assert.Equal(other.Hash, Assert.Single(store.Excluded.Load()).Hash);
    }

    /// <summary>
    /// 取り込みの終わりの書き込み（見つけた物で一覧を作り直す）と重なっても、外した物は未確定へ戻らず、取り込みが新しく見つけた物は欠けない。
    /// 取り込みがもう1つの除外を足す書き込み（設定の解除・別の除外）と重なっても、除外の記録は欠けない。どちらが先に錠を取っても同じ。
    /// </summary>
    [Fact]
    public async Task ExcludingWhileAnImportWritesNeitherLosesNorRestoresEntries()
    {
        var scanned = new RegisteredFolderSet([Path.Combine(_root, "files")]);
        var offline = new RegisteredFolderSet([]);

        for (var round = 0; round < 6; round++)
        {
            var (store, service) = NewLibrary($"round{round}");
            var known = Enumerable.Range(0, 40).Select(index => Unresolved($"round{round}-known-{index:00}.bin")).ToList();
            var fresh = Enumerable.Range(0, 5).Select(index => Unresolved($"round{round}-new-{index:00}.bin")).ToList();
            var elsewhere = Unresolved($"round{round}-elsewhere.bin");
            await store.Unresolved.SaveAsync([.. known]);

            // 取り込みが始めに読んだ一覧と、走査で見つけた物（前からの物も、もう一度見つかる）
            var lastWritten = store.Unresolved.Load();
            var found = known.Concat(fresh).ToList();
            var excluded = known.Take(25).ToList();

            var work = new List<Task>
            {
                Task.Run(() => service.ExcludeAsync(excluded, "試験")),
            };
            work.Insert(round % 2, Task.Run(() => store.Unresolved.UpdateAsync(
                current => UnresolvedMerge.ForImport(current, lastWritten, found, scanned, offline))));
            work.Insert(round % 3 == 0 ? 0 : work.Count, Task.Run(() => service.ExcludeAsync([elsewhere], "別の除外")));
            await Task.WhenAll(work);

            Assert.Equal(Hashes(known.Skip(25).Concat(fresh)), Hashes(store.Unresolved.Load()));
            Assert.Equal(Hashes(excluded.Append(elsewhere)), Hashes(store.Excluded.Load()));
        }
    }

    /// <summary>
    /// 除外の記録に書けなかったら、未確定の記録には触らない（ファイルがどちらの記録にも無くなる形にしない）。
    /// 書けないのは、ほかのアプリが除外の記録を開いたままにしている形で作る。
    /// </summary>
    [Fact]
    public async Task WhenTheExcludedRecordCannotBeWrittenTheUnresolvedRecordIsLeftAlone()
    {
        var (store, service) = NewLibrary("locked");
        var targets = Enumerable.Range(0, 5).Select(index => Unresolved($"target-{index:00}.bin")).ToList();
        await store.Unresolved.SaveAsync([.. targets]);
        var unresolvedBefore = store.Unresolved.WriteCount;

        using (new FileStream(store.Paths.ExcludedFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => service.ExcludeAsync(targets, "試験"));
        }

        Assert.Equal(unresolvedBefore, store.Unresolved.WriteCount);
        Assert.Equal(Hashes(targets), Hashes(store.Unresolved.Load()));
    }
}
