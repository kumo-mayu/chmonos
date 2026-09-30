using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 未確定の記録（unresolved.json）の読み書きを、呼んだスレッドの外で行う（2026-09-30）。
///
/// 記録は件数に比例して大きくなる（8万件で 37.7MB）。登録の命令と「未確定」を開く命令は画面のスレッドから来て、
/// 最初の <c>await</c> より前の同期の読みと、錠が空いているときの読み直しが、そのまま画面のスレッドで走っていた
/// （登録のたびに 330〜540ms・開くたびに 150〜290ms 止まる）。
///
/// ここで確かめるのは3つ：どのスレッドで読むか／外へ出しても結果が変わらないこと／
/// 登録・均し・取り込みの書き込みが重なっても、一覧が欠けない・戻らないこと。
/// スレッドは、試験が自分で作った1本（スレッドプールの外）から呼んで見分ける。時計には頼らない。
/// </summary>
public sealed class UnresolvedOffThreadTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bam-unresolved-thread-" + Guid.NewGuid().ToString("N"));
    private readonly DataStore _store;
    private readonly ItemService _service;

    public UnresolvedOffThreadTests()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // BOOTH へは行かない道だけを通す。行ったら落とす
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("この試験は BOOTH へ行かないはず");
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

    /// <summary>読まれた（値を入れられた）スレッドを控える型。JSON から組むときに、読んでいるスレッドで入る。</summary>
    private sealed class ThreadProbe
    {
        private static int s_readOn;

        private string _value = string.Empty;

        public static int ReadOn => Volatile.Read(ref s_readOn);

        public static void Reset() => Volatile.Write(ref s_readOn, 0);

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                Volatile.Write(ref s_readOn, Environment.CurrentManagedThreadId);
            }
        }
    }

    /// <summary>
    /// 自分で作った1本のスレッドで走らせ、そのスレッドの番号と結果を返す。スレッドプールの外なので、
    /// 中で <c>Task.Run</c> した仕事がこのスレッドで走ることは無い（待っている間、このスレッドは塞がっている）。
    /// </summary>
    private static (int Caller, T Result) OnOwnThread<T>(Func<T> body)
    {
        var caller = 0;
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            try
            {
                result = body();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("呼んだスレッドで失敗した", failure);
        }

        return (caller, result);
    }

    private JsonFileStore<ThreadProbe> ProbeStore(bool offCallerThread)
    {
        var path = Path.Combine(_root, $"probe-{offCallerThread}.json");
        JsonStore.Write(path, new ThreadProbe { Value = "初め" });
        ThreadProbe.Reset();
        return new JsonFileStore<ThreadProbe>(path) { UpdatesOffCallerThread = offCallerThread };
    }

    // ---- どのスレッドで読むか ----

    /// <summary>同期の読みは、呼んだスレッドで読む（だから画面のスレッドから呼ぶと止まる）。</summary>
    [Fact]
    public void LoadReadsOnTheCallingThread()
    {
        var store = ProbeStore(offCallerThread: false);

        var (caller, loaded) = OnOwnThread(store.Load);

        Assert.Equal("初め", loaded.Value);
        Assert.Equal(caller, ThreadProbe.ReadOn);
    }

    [Fact]
    public void LoadAsyncReadsOffTheCallingThread()
    {
        var store = ProbeStore(offCallerThread: false);

        var (caller, loaded) = OnOwnThread(() => store.LoadAsync().GetAwaiter().GetResult());

        Assert.Equal("初め", loaded.Value);
        Assert.NotEqual(0, ThreadProbe.ReadOn);
        Assert.NotEqual(caller, ThreadProbe.ReadOn);
    }

    /// <summary>
    /// 印の無い窓口は、錠が空いていれば読み直しも変え方の関数も呼んだスレッドで走る（設定など。書いた値を画面が持つ物はこのまま）。
    /// </summary>
    [Fact]
    public void UpdateRereadsOnTheCallingThreadByDefault()
    {
        var store = ProbeStore(offCallerThread: false);
        var changedOn = 0;

        var (caller, rereadOnThread) = OnOwnThread(() =>
        {
            var task = store.UpdateAsync(current =>
            {
                changedOn = Environment.CurrentManagedThreadId;
                return current;
            });

            // 読み直しは、最初の本当の待ち（書き出し）より前に済んでいる
            var rereadOn = ThreadProbe.ReadOn;
            task.GetAwaiter().GetResult();
            return rereadOn;
        });

        Assert.Equal(caller, rereadOnThread);
        Assert.Equal(caller, changedOn);
    }

    /// <summary>印を付けた窓口（未確定の記録）は、読み直しも変え方の関数も呼んだスレッドの外で走り、結果は同じ。</summary>
    [Fact]
    public void UpdateRunsOffTheCallingThreadWhenMarked()
    {
        var store = ProbeStore(offCallerThread: true);
        var changedOn = 0;
        var rereadOn = 0;

        var (caller, written) = OnOwnThread(() => store.UpdateAsync(current =>
        {
            rereadOn = ThreadProbe.ReadOn;
            changedOn = Environment.CurrentManagedThreadId;
            return new ThreadProbe { Value = current.Value + "→次" };
        }).GetAwaiter().GetResult());

        Assert.NotEqual(caller, rereadOn);
        Assert.NotEqual(caller, changedOn);
        Assert.Equal("初め→次", written.Value);
        Assert.Equal("初め→次", store.Load().Value);
    }

    [Fact]
    public void TryUpdateRunsOffTheCallingThreadWhenMarkedAndStillSkipsTheWrite()
    {
        var store = ProbeStore(offCallerThread: true);
        var changedOn = 0;
        var before = store.WriteCount;

        var (caller, wrote) = OnOwnThread(() => store.TryUpdateAsync(_ =>
        {
            changedOn = Environment.CurrentManagedThreadId;
            return null;
        }).GetAwaiter().GetResult());

        Assert.NotEqual(caller, changedOn);
        Assert.False(wrote);
        Assert.Equal(before, store.WriteCount);
    }

    // ---- 錠を持ったまま、変え方の中で待つ形（TryUpdateAwaitingAsync） ----

    /// <summary>
    /// 変え方の中で待っている間も錠を持っている：その間に来た別の書き換えは、終わるまで待ってから、書いた後の値に当たる。
    /// （登録が「探す → 商品を保存 → 外す」の間に、取り込みの書き込みが割り込まない）
    /// </summary>
    [Fact]
    public async Task TheAwaitingUpdateKeepsTheLockWhileItsChangeWaits()
    {
        var path = Path.Combine(_root, "list.json");
        var store = new JsonFileStore<List<string>>(path) { UpdatesOffCallerThread = true };
        await store.SaveAsync(["a"]);

        var inside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = store.TryUpdateAwaitingAsync(async current =>
        {
            inside.SetResult();
            await release.Task;
            current.Add("b");
            return current;
        });

        await inside.Task;
        List<string>? seenBySecond = null;
        var second = store.UpdateAsync(current =>
        {
            seenBySecond = [.. current];
            current.Add("c");
            return current;
        });

        // 2本目は錠を待っている。1本目を進めるまで終わらない（時計で待たず、1本目が先に終わることで確かめる）
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.True(await first);
        await second;

        Assert.Equal(["a", "b"], seenBySecond);
        Assert.Equal(["a", "b", "c"], store.Load());
    }

    [Fact]
    public async Task TheAwaitingUpdateWritesNothingWhenItsChangeReturnsNullOrThrows()
    {
        var path = Path.Combine(_root, "list.json");
        var store = new JsonFileStore<List<string>>(path) { UpdatesOffCallerThread = true };
        await store.SaveAsync(["a"]);
        var before = store.WriteCount;

        Assert.False(await store.TryUpdateAwaitingAsync(_ => Task.FromResult<List<string>?>(null)));
        await Assert.ThrowsAsync<IOException>(() => store.TryUpdateAwaitingAsync(async current =>
        {
            current.Add("書かれない");
            await Task.Yield();
            throw new IOException("商品の保存に失敗した");
        }));

        Assert.Equal(before, store.WriteCount);
        Assert.Equal(["a"], store.Load());

        // 投げた後も錠は返っている（次の書き換えが通る）
        await store.UpdateAsync(current => [.. current, "b"]);
        Assert.Equal(["a", "b"], store.Load());
    }

    /// <summary>
    /// 見つからないIDのまま登録する道も、結果は前と同じ：商品ができてファイルが付き、一覧から外れる。
    /// 未確定に無いファイル・仮IDでは何も書かない。
    /// </summary>
    [Fact]
    public async Task AssigningAnUnpublishedIdStillMovesTheFileToTheItem()
    {
        var file = Unresolved("hidden.bin");
        var other = Unresolved("other.bin");
        await _store.Unresolved.SaveAsync([file, other]);
        var before = _store.Unresolved.WriteCount;

        Assert.False(await _service.AssignUnpublishedItemIdAsync(Unresolved("missing.bin").Hash, "1000003", "作り物の商品"));
        Assert.False(await _service.AssignUnpublishedItemIdAsync(file.Hash, LocalItemId.For(file.Hash), "作り物の商品"));
        Assert.Equal(before, _store.Unresolved.WriteCount);
        Assert.Empty(_store.Items.EnumerateItemIds());

        Assert.True(await _service.AssignUnpublishedItemIdAsync(file.Hash, "1000003", "作り物の商品"));

        var item = await _store.Items.LoadAsync("1000003");
        Assert.Equal(file.Hash, Assert.Single(item!.Local.LocalFiles).Hash);
        Assert.Equal("作り物の商品", item.Local.DisplayName);
        Assert.True(item.Local.IsDelisted);
        Assert.Equal(other.Hash, Assert.Single(_store.Unresolved.Load()).Hash);
    }

    /// <summary>未確定の記録の窓口に印が付いている（外すと、登録のたびに画面が止まる形に戻る）。</summary>
    [Fact]
    public void TheUnresolvedStoreIsMarked()
        => Assert.True(_store.Unresolved.UpdatesOffCallerThread);

    // ---- 結果が変わらないこと・重なっても欠けない・戻らないこと ----

    private UnresolvedFile Unresolved(string name) => new()
    {
        Hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))),
        Paths = [Path.Combine(_root, "files", name)],
        SizeBytes = 10,
        ModifiedAtUtc = At,
        FirstSeenAt = At,
    };

    private static HashSet<string> Hashes(IEnumerable<UnresolvedFile> files)
        => files.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 登録（BOOTHに無い商品）と「未確定」を開くときの均しが重なっても、登録した物は一覧へ戻らず、ほかの物は欠けない。
    /// 均しは、商品が既に持っている1件（確定の途中で落ちて両方に残った形）も外す。
    /// </summary>
    [Fact]
    public async Task RegisteringWhileReconcilingNeitherLosesNorRestoresEntries()
    {
        var files = Enumerable.Range(0, 40).Select(index => Unresolved($"loose-{index:00}.bin")).ToList();
        await _store.Unresolved.SaveAsync([.. files]);

        // 0番は、商品が既に持っている（均しが外す）
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "1000001",
            Booth = new BoothBlock(),
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = files[0].Hash, Paths = files[0].Paths, SizeBytes = 10 }],
            },
        });

        var registered = files.Skip(1).Take(12).ToList();
        var work = new List<Task>();
        foreach (var file in registered)
        {
            work.Add(Task.Run(() => _service.RegisterLocalItemAsync(file.Hash, "作り物の商品")));
            work.Add(Task.Run(() => _service.ReconcileUnresolvedAsync()));
        }

        await Task.WhenAll(work);

        // 均しが1回も当たらなかった回に備えて、最後にもう1回（開き直しに当たる）
        await _service.ReconcileUnresolvedAsync();

        Assert.Equal(Hashes(files.Skip(13)), Hashes(_store.Unresolved.Load()));
        Assert.Equal(1 + registered.Count, _store.Items.EnumerateItemIds().Count);
        foreach (var file in registered)
        {
            var item = await _store.Items.LoadAsync(LocalItemId.For(file.Hash));
            Assert.Equal(file.Hash, Assert.Single(item!.Local.LocalFiles).Hash);
        }
    }

    /// <summary>
    /// 取り込みの終わりの書き込み（見つけた物で一覧を作り直す）と登録が重なっても、登録した物は一覧へ戻らず、
    /// 取り込みが新しく見つけた物は欠けない。どちらが先に錠を取っても同じ結果になる。
    /// </summary>
    [Fact]
    public async Task RegisteringWhileAnImportWritesNeitherLosesNorRestoresEntries()
    {
        var scanned = new RegisteredFolderSet([Path.Combine(_root, "files")]);
        var offline = new RegisteredFolderSet([]);

        for (var round = 0; round < 6; round++)
        {
            var known = Enumerable.Range(0, 20).Select(index => Unresolved($"round{round}-known-{index:00}.bin")).ToList();
            var fresh = Enumerable.Range(0, 5).Select(index => Unresolved($"round{round}-new-{index:00}.bin")).ToList();
            await _store.Unresolved.SaveAsync([.. known]);

            // 取り込みが始めに読んだ一覧と、走査で見つけた物（前からの物も、もう一度見つかる）
            var lastWritten = _store.Unresolved.Load();
            var found = known.Concat(fresh).ToList();
            var registered = known.Take(5).ToList();

            var work = registered
                .Select(file => Task.Run(() => _service.RegisterLocalItemAsync(file.Hash, "作り物の商品")))
                .Cast<Task>()
                .ToList();
            work.Insert(round % work.Count, Task.Run(() => _store.Unresolved.UpdateAsync(
                current => UnresolvedMerge.ForImport(current, lastWritten, found, scanned, offline))));
            await Task.WhenAll(work);

            Assert.Equal(Hashes(known.Skip(5).Concat(fresh)), Hashes(_store.Unresolved.Load()));
        }
    }

    /// <summary>外す物が一覧に無ければ、記録を書き直さない（同じ中身の数十MBを書かない）。在れば外す。</summary>
    [Fact]
    public async Task RemovingAnEntryThatIsNotListedDoesNotRewriteTheFile()
    {
        var kept = Unresolved("kept.bin");
        var gone = Unresolved("gone.bin");
        await _store.Unresolved.SaveAsync([kept, gone]);
        var before = _store.Unresolved.WriteCount;

        await _service.ExcludeAsync(Unresolved("elsewhere.bin").Hash, [Path.Combine(_root, "files", "elsewhere.bin")], reason: null);

        Assert.Equal(before, _store.Unresolved.WriteCount);
        Assert.Equal(Hashes([kept, gone]), Hashes(_store.Unresolved.Load()));

        await _service.ExcludeAsync(gone.Hash, gone.Paths, reason: null);

        Assert.NotEqual(before, _store.Unresolved.WriteCount);
        Assert.Equal(kept.Hash, Assert.Single(_store.Unresolved.Load()).Hash);
    }

    /// <summary>フォルダを商品として登録したとき、配下に未確定が無ければ記録を書かず、在れば配下の物だけ外す。</summary>
    [Fact]
    public async Task RegisteringAFolderRemovesOnlyTheEntriesUnderIt()
    {
        var empty = Directory.CreateDirectory(Path.Combine(_root, "files", "empty")).FullName;
        var unpacked = Directory.CreateDirectory(Path.Combine(_root, "files", "unpacked")).FullName;
        var outside = Unresolved("outside.bin");
        var inside = Unresolved(Path.Combine("unpacked", "inside.bin"));
        await _store.Unresolved.SaveAsync([outside, inside]);
        await _store.Items.SaveAsync(new ItemRecord { Id = "1000002", Booth = new BoothBlock(), Local = new LocalBlock() });
        var before = _store.Unresolved.WriteCount;

        Assert.True(await _service.RegisterFolderAsync("1000002", empty));

        Assert.Equal(before, _store.Unresolved.WriteCount);
        Assert.Equal(2, _store.Unresolved.Load().Count);

        Assert.True(await _service.RegisterFolderAsync("1000002", unpacked));

        Assert.Equal(outside.Hash, Assert.Single(_store.Unresolved.Load()).Hash);
    }

    /// <summary>統計の未確定の数は、記録を裏で読んでも同じ数になる。</summary>
    [Fact]
    public async Task StatsStillCountTheUnresolvedFiles()
    {
        await _store.Unresolved.SaveAsync([Unresolved("a.bin"), Unresolved("b.bin"), Unresolved("c.bin")]);

        var loaded = await new StatsService(_store).LoadAsync();

        Assert.Equal(3, loaded.Backlog.UnresolvedCount);
    }

    // ---- 頭だけ読んで「在るか」を答える（起動の画面決め） ----

    private string Json(string name, string text, bool bom = false)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(bom));
        return path;
    }

    [Fact]
    public void ArrayHasItemsIsFalseForAMissingOrEmptyList()
    {
        Assert.False(JsonStore.ArrayHasItems(Path.Combine(_root, "none.json")));
        Assert.False(JsonStore.ArrayHasItems(Json("empty.json", "[]")));
        Assert.False(JsonStore.ArrayHasItems(Json("spaced.json", "\n  // 手で直した\n  [\n  ]\n")));
        Assert.False(JsonStore.ArrayHasItems(Json("bom.json", "[]", bom: true)));
    }

    [Fact]
    public async Task ArrayHasItemsIsTrueOnceThereIsOneEntry()
    {
        await _store.Unresolved.SaveAsync([Unresolved("a.bin")]);

        Assert.True(JsonStore.ArrayHasItems(_store.Unresolved.Path));
        Assert.True(JsonStore.ArrayHasItems(Json("bom-one.json", "[ { } ]", bom: true)));
        Assert.True(JsonStore.ArrayHasItems(Json("number.json", "[1]")));
    }

    /// <summary>頭の 4KB で決まらないとき（長いコメント）は、全部読んで数える。</summary>
    [Fact]
    public void ArrayHasItemsFallsBackToAFullReadWhenTheHeadIsNotEnough()
    {
        var comment = "/* " + new string('x', 6000) + " */";

        Assert.False(JsonStore.ArrayHasItems(Json("long-empty.json", comment + "[]")));
        Assert.True(JsonStore.ArrayHasItems(Json("long-one.json", comment + "[{}]")));
    }

    /// <summary>配列でない・JSON でない頭は、丸ごと読むときと同じく例外になる（黙って「無い」としない）。</summary>
    [Fact]
    public void ArrayHasItemsThrowsForSomethingThatIsNotAList()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JsonStore.ArrayHasItems(Json("object.json", "{}")));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JsonStore.ArrayHasItems(Json("broken.json", "こわれた")));
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => JsonStore.ArrayHasItems(Json("blank.json", "")));
    }
}
