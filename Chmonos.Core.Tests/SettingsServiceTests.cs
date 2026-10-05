using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-settings-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _service = new SettingsService(_store);
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

    private Task SaveItemAsync(string id, bool hidden)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "item " + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { IsHidden = hidden },
        });

    [Fact]
    public async Task ListsOnlyHiddenItems()
    {
        await SaveItemAsync("1", hidden: true);
        await SaveItemAsync("2", hidden: false);

        var hidden = await _service.LoadHiddenAsync();

        Assert.Equal(["1"], hidden.Select(item => item.ItemId));
    }

    /// <summary>非表示は設定からしか戻せないので、ここが効かないと二度と出てこない。</summary>
    [Fact]
    public async Task UnhideBringsTheItemBack()
    {
        await SaveItemAsync("1", hidden: true);

        await _service.UnhideAsync("1");

        Assert.Empty(await _service.LoadHiddenAsync());
        var item = await _store.Items.LoadAsync("1");
        Assert.False(item!.Local.IsHidden);
    }

    private Task SaveItemWithDetachedAsync(string id)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord { Hash = "AAAA", Paths = [@"C:\作り物\外した.zip"], SizeBytes = 1, Detached = true },
                    new LocalFileRecord { Hash = "CCCC", Paths = [@"C:\作り物\持っている.zip"], SizeBytes = 1 },
                ],
            },
        });

    [Fact]
    public async Task 外した記録を消すと_その行だけが消える()
    {
        await SaveItemWithDetachedAsync("9900021");

        await _service.ForgetDetachedAsync("AAAA", "9900021");

        var files = (await _store.Items.LoadAsync("9900021"))!.Local.LocalFiles;
        Assert.Equal(["CCCC"], files.Select(file => file.Hash));
    }

    /// <summary>
    /// 外した記録を消す間に取り込みが同じ商品へファイルを足しても、足したファイルは消えない
    /// （2026-10-05・file-lifecycle.md「気になった所」3）。前は錠の外で読んだ写しで localFiles ごと書いていた。
    /// </summary>
    [Fact]
    public async Task 外した記録を消す間に取り込みが足したファイルが残る()
    {
        await SaveItemWithDetachedAsync("9900022");

        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            "9900022",
            local => ItemLockRace.AddFile(local),
            () => _service.ForgetDetachedAsync("AAAA", "9900022"));

        var files = (await _store.Items.LoadAsync("9900022"))!.Local.LocalFiles;
        Assert.Equal(["CCCC", "BBBB"], files.Select(file => file.Hash));
    }

    [Fact]
    public async Task RestoringAnExcludedFileRemovesTheEntry()
    {
        await _store.Excluded.SaveAsync(
        [
            new ExcludedEntry { Hash = "AAAA", Paths = ["x.zip"], ExcludedAt = DateTimeOffset.Now, Reason = "BOOTH商品ではない" },
            new ExcludedEntry { Hash = "BBBB", Paths = ["y.zip"], ExcludedAt = DateTimeOffset.Now },
        ]);

        await _service.RestoreExcludedAsync("AAAA");

        Assert.Equal(["BBBB"], (await _service.LoadExcludedAsync()).Select(entry => entry.Hash));
    }

    /// <summary>
    /// 設定の画面は新しい順に並べる。同じ日時（まとめて除外した物）は、記録の後ろ（後から足した物）を上にして、
    /// 読み直すたびに上下が入れ替わらないようにする。
    /// </summary>
    [Fact]
    public async Task ExcludedFilesComeNewestFirst_AndLaterEntriesWinTies()
    {
        var older = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(9));
        var newer = older.AddDays(3);
        await _store.Excluded.SaveAsync(
        [
            new ExcludedEntry { Hash = "OLD", Paths = ["old.zip"], ExcludedAt = older },
            new ExcludedEntry { Hash = "TIE-1", Paths = ["tie1.zip"], ExcludedAt = newer },
            new ExcludedEntry { Hash = "NEWEST", Paths = ["newest.zip"], ExcludedAt = newer.AddHours(1) },
            new ExcludedEntry { Hash = "TIE-2", Paths = ["tie2.zip"], ExcludedAt = newer },
        ]);

        var loaded = await _service.LoadExcludedAsync();

        Assert.Equal(["NEWEST", "TIE-2", "TIE-1", "OLD"], loaded.Select(entry => entry.Hash));
    }

    /// <summary>
    /// 除外の記録は呼んだスレッドの外で読む（設定の画面が開くたびに読む。5,000 件で約 15ms・上限なし）。
    /// 記録を握って読めなくしておくと、呼んだスレッドで読んで結果を包む作り（<c>Task.FromResult</c>）なら呼んだ所で失敗する。
    /// 外で読む作りなら呼び出しはすぐ返り、失敗は待った先で届く。
    /// （<c>async</c> の関数の中で同期に読む作りは、これでは見分けられない。スレッドを控える口が読みの中に無いため）
    /// </summary>
    [Fact]
    public async Task LoadExcludedAsyncDoesNotReadOnTheCallingThread()
    {
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = "AAAA", Paths = ["x.zip"], ExcludedAt = DateTimeOffset.Now }]);
        var path = Path.Combine(_root, "excluded.json");
        Assert.True(File.Exists(path));

        Task<IReadOnlyList<ExcludedFile>>? task = null;
        Exception? thrownOnCaller = null;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var caller = new Thread(() =>
            {
                try
                {
                    task = _service.LoadExcludedAsync();
                }
                catch (Exception exception)
                {
                    thrownOnCaller = exception;
                }
            });
            caller.Start();
            caller.Join();

            Assert.Null(thrownOnCaller);
            Assert.NotNull(task);

            // 握っている間に読みに行った（外のスレッドで失敗する）。待つのは握ったままにして、読めてしまう順を作らない
            await Assert.ThrowsAsync<IOException>(() => task!);
        }
    }

    [Fact]
    public async Task SavesAndReloadsSettings()
    {
        await _service.UpdateAsync(current => current with { ShowAdult = false, RefreshIntervalDays = 21 });

        var reloaded = _store.Settings.Load();

        Assert.False(reloaded.ShowAdult);
        Assert.Equal(21, reloaded.RefreshIntervalDays);
    }

    /// <summary>保存先が空でも落ちない（初回起動でディレクトリが無い）。</summary>
    [Fact]
    public async Task ReportsZeroUsageOnAnEmptyStore()
    {
        var usage = await _service.LoadUsageAsync();

        Assert.Equal(0, usage.ImageCount);
        Assert.Equal(_root, usage.Root);
    }

    /// <summary>
    /// 保存が重なっても、手元の設定はディスクと同じ最後の値になる。
    /// 前は錠を出てから手元へ代入していたので、先に書いた方が後から代入され、古い値を持ち続け得た。
    /// </summary>
    [Fact]
    public async Task KeepsTheLatestSettingsWhenSavesOverlap()
    {
        await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() =>
            _service.UpdateAsync(current => current with { FetchIntervalMs = current.FetchIntervalMs + 1 }))));

        var onDisk = _store.Settings.Load();
        Assert.Equal(AppSettings.MinFetchIntervalMs + 40, onDisk.FetchIntervalMs);
        Assert.Equal(onDisk.FetchIntervalMs, _service.Current.FetchIntervalMs);
    }
}
