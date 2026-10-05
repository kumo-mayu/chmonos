using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 動かしたファイルを中身で見つけて結び直す（G17）。
/// ファイルの同一性はハッシュで持っているので、移した・名前を変えただけなら元に戻せる。
/// </summary>
public class MissingFileFinderTests : IDisposable
{
    private readonly string _root;
    private readonly string _watched;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly MissingFileFinder _finder;

    public MissingFileFinderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-missing-" + Guid.NewGuid().ToString("N"));
        _watched = Path.Combine(_root, "watched");
        Directory.CreateDirectory(_watched);

        _paths = new AppPaths(Path.Combine(_root, "store"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);
        _finder = new MissingFileFinder(_store);
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

        GC.SuppressFinalize(this);
    }

    private async Task<string> SaveItemWithFileAsync(string itemId, string fileName, string content)
    {
        var path = Path.Combine(_watched, fileName);
        await File.WriteAllTextAsync(path, content);

        var hash = await FileHasher.ComputeSha256Async(path);
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "テスト" },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = hash,
                        SizeBytes = new FileInfo(path).Length,
                        Paths = [path],
                    },
                ],
            },
        });

        return path;
    }

    [Fact]
    public async Task RelinksAFileThatMovedWithinTheWatchedFolder()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");

        // 移した（名前も変えた）
        var moved = Path.Combine(_watched, "sub", "衣装_v2.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(path, moved);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(1, result.MissingBefore);
        Assert.Equal(1, result.Relinked);
        Assert.Equal(0, result.StillMissing);

        var item = await _store.Items.LoadAsync("111");
        Assert.Equal(moved, Assert.Single(Assert.Single(item!.Local.LocalFiles).Paths));
    }

    [Fact]
    public async Task SaysNothingIsMissingWhenEveryFileIsWhereItShouldBe()
    {
        await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(0, result.MissingBefore);
        Assert.Equal(0, result.Relinked);
    }

    /// <summary>監視フォルダの外へ出た物は見つからない。そう言えるようにしておく。</summary>
    [Fact]
    public async Task ReportsWhatItCouldNotFind()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        File.Delete(path);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(1, result.MissingBefore);
        Assert.Equal(0, result.Relinked);
        Assert.Equal(1, result.StillMissing);
    }

    /// <summary>今つながっていないフォルダは「無い」ではなく「見られなかった」。</summary>
    [Fact]
    public async Task TellsApartFoldersItCouldNotReach()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        File.Delete(path);

        var result = await _finder.FindAsync([Path.Combine(_root, "外付け")]);

        Assert.Equal(Path.Combine(_root, "外付け"), Assert.Single(result.Unreachable));
        Assert.Equal(0, result.Relinked);
    }

    /// <summary>
    /// 計算したハッシュは走査の控えに足す（2026-09-24）。前は捨てていたので、探すたびに同じファイルを読み直していた。
    /// 控えにあった物は残す（錠の中で今の控えに足す）。
    /// </summary>
    [Fact]
    public async Task HashesAreKeptInTheScanCache()
    {
        var path = await SaveItemWithFileAsync("111", "衣装.zip", "なかみ");
        var moved = Path.Combine(_watched, "移した.zip");
        File.Move(path, moved);
        File.WriteAllText(Path.Combine(_watched, "別の.zip"), "べつの"); // 同じ大きさで中身が違う物
        await _store.ScanCache.SaveAsync(
        [
            new ScanCacheEntry { Path = @"Z:\前から.zip", SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.UnixEpoch, Hash = "ab" },
        ]);

        var first = await _finder.FindAsync([_watched]);

        Assert.Equal((1, 1, 2), (first.MissingBefore, first.Relinked, first.Hashed));
        var cached = _store.ScanCache.Load();
        Assert.Contains(cached, entry => entry.Path == moved);
        Assert.Contains(cached, entry => entry.Path == Path.Combine(_watched, "別の.zip"));
        Assert.Contains(cached, entry => entry.Path == @"Z:\前から.zip");

        // 記録がまた古い場所を指しても、控えから引けるので読み直さない
        await _store.Items.ChangeLocalAsync(
            "111",
            local => local with { LocalFiles = [local.LocalFiles[0] with { Paths = [path] }] },
            [LocalField.LocalFiles]);

        var second = await _finder.FindAsync([_watched]);

        Assert.Equal(1, second.Relinked);
        Assert.Equal(0, second.Hashed);
    }

    // ---- 場所が空のファイル（ユーザ判断 2026-10-04）----
    // 取り込みはディスクに無いと見た場所を記録から外すので、全部外れたファイルは「無い場所」を持たない。
    // 検索の条件「見つからないファイル」とカードの印が数えるのはこの形なので、探す側もこれを探す。

    /// <summary>監視フォルダに中身を置き、その中身を持つ「場所が空の」ファイルを記録する。</summary>
    private async Task<(string Path, LocalFileRecord File)> EmptyRecordOfAsync(string fileName, string content, bool detached = false)
    {
        var path = Path.Combine(_watched, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
        return (path, new LocalFileRecord
        {
            Hash = await FileHasher.ComputeSha256Async(path),
            SizeBytes = new FileInfo(path).Length,
            Paths = [],
            Detached = detached,
        });
    }

    private Task SaveAsync(string itemId, params LocalFileRecord[] files) => _store.Items.SaveAsync(new ItemRecord
    {
        Id = itemId,
        Booth = new BoothBlock { FetchedAt = DateTimeOffset.UnixEpoch, Name = "作り物の商品" },
        Local = new LocalBlock { LocalFiles = [.. files] },
    });

    private async Task<LocalFileRecord> OnlyFileOfAsync(string itemId)
        => Assert.Single((await _store.Items.LoadAsync(itemId))!.Local.LocalFiles);

    [Fact]
    public async Task AFileWithNoPathLeftIsFoundByItsContentAndGetsThePlace()
    {
        var (path, file) = await EmptyRecordOfAsync(Path.Combine("sub", "移した.zip"), "なかみ");
        await SaveAsync("111", file);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal((1, 1, 0), (result.MissingBefore, result.Relinked, result.StillMissing));
        Assert.Equal([path], (await OnlyFileOfAsync("111")).Paths);
        Assert.False((await _store.Items.LoadAsync("111"))!.HasMissingFile);
    }

    [Fact]
    public async Task AFileWithNoPathLeftStaysEmptyWhenNothingMatches()
    {
        var (path, file) = await EmptyRecordOfAsync("消した.zip", "なかみ");
        File.Delete(path);
        File.WriteAllText(Path.Combine(_watched, "別の.zip"), "べつの"); // 大きさは同じで中身が違う
        await SaveAsync("111", file);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal((1, 0, 1), (result.MissingBefore, result.Relinked, result.StillMissing));
        Assert.Empty((await OnlyFileOfAsync("111")).Paths);
    }

    /// <summary>外したファイルは場所が空でも探さない（数えもしない）。</summary>
    [Fact]
    public async Task ADetachedFileWithNoPathIsNotSearched()
    {
        var (_, file) = await EmptyRecordOfAsync("外した.zip", "なかみ", detached: true);
        await SaveAsync("111", file);

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal(0, result.MissingBefore);
        Assert.Empty((await OnlyFileOfAsync("111")).Paths);
    }

    /// <summary>
    /// 同じ中身を2つの商品が持っていれば、どちらにも場所が入る（中身で数えるので1件）。
    /// 監視フォルダに同じ中身が2つ置かれていても、足すのは1か所（どれを使っても同じ中身）。
    /// 場所が空の商品と、無い場所を持つ商品が同じ中身でも、どちらも結び直る。
    /// </summary>
    [Fact]
    public async Task TheSameContentIsCountedOnceAndEveryOwnerGetsOnePlace()
    {
        var (first, file) = await EmptyRecordOfAsync("写し1.zip", "おなじ");
        var second = Path.Combine(_watched, "写し2.zip");
        File.Copy(first, second);
        await SaveAsync("111", file);
        await SaveAsync("222", file);
        await SaveAsync("333", file with { Paths = [Path.Combine(_watched, "元の場所.zip")] });

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal((1, 1, 0), (result.MissingBefore, result.Relinked, result.StillMissing));
        foreach (var itemId in new[] { "111", "222", "333" })
        {
            var placed = Assert.Single((await OnlyFileOfAsync(itemId)).Paths);
            Assert.Contains(placed, new[] { first, second });
        }
    }

    /// <summary>
    /// 探している間（読み終えて中身を確かめている間）に、取り込みが同じファイルへ別の場所を足し、人がメモを書いても、
    /// どれも消えない。探した側は錠の中で今の値に場所を足すだけ。
    /// </summary>
    [Fact]
    public async Task AnImportWritingTheSameItemWhileSearchingKeepsBothPlaces()
    {
        var (found, file) = await EmptyRecordOfAsync("見つかる.zip", "なかみ");
        var outside = Path.Combine(_root, "監視の外", "取り込んだ.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.Copy(found, outside);
        await SaveAsync("111", file);

        var wrote = 0;
        var progress = new InlineProgress(() =>
        {
            if (Interlocked.Exchange(ref wrote, 1) == 1)
            {
                return;
            }

            // 取り込みと同じ口（錠の中で今の値に当てる）。探す側が商品を読んだ後、書く前に割り込む
            _store.Items.ChangeLocalAsync(
                "111",
                local => local with { LocalFiles = [local.LocalFiles[0] with { Paths = [outside] }] },
                LocalOwners.Import).GetAwaiter().GetResult();
            _store.Items.ChangeLocalAsync(
                "111", local => local with { Memo = "人が書いたメモ" }, [LocalField.Memo]).GetAwaiter().GetResult();
        });

        var result = await _finder.FindAsync([_watched], progress);

        Assert.Equal(1, wrote);
        Assert.Equal(1, result.Relinked);
        var item = await _store.Items.LoadAsync("111");
        Assert.Equal([outside, found], Assert.Single(item!.Local.LocalFiles).Paths);
        Assert.Equal("人が書いたメモ", item.Local.Memo);
    }

    /// <summary>その場で受ける進み具合の受け手（<c>Progress</c> と違い、どこへも運ばない）。</summary>
    // ---- つながっていないドライブの上（2026-10-05・file-lifecycle.md「気になった所」1）----
    // 取り込みは外付けの上の場所を外さない（LocalFileMerger）。探す側も同じ判定にそろえる。

    /// <summary>
    /// 外付けを外したまま、同じ中身が監視フォルダにある。外付けの上の場所は「見えない」だけなので外さず、付け替えもしない。
    /// 前は在るかをファイルだけで見ていたので、外付けの上の場所を監視フォルダの場所に差し替えていた。
    /// </summary>
    [Fact]
    public async Task 外付けを外している間は_外付けの上の場所を外さず付け替えない()
    {
        var offline = Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "外付けの上.zip");
        var (_, file) = await EmptyRecordOfAsync("写し.zip", "なかみ");
        await SaveAsync("9900001", file with { Paths = [offline] });

        var result = await _finder.FindAsync([_watched]);

        Assert.Equal([offline], (await OnlyFileOfAsync("9900001")).Paths);
        Assert.Equal((0, 0), (result.MissingBefore, result.Relinked));
    }

    /// <summary>
    /// 場所の1つが外付けの上なら、ほかの場所が無くても「無くなった」とは言えない（<see cref="LocalFilePresence"/> と同じ決まり）。
    /// 外付けの上の場所を残し、つながったドライブの上の無い場所も付け替えない（取り込みに任せる）。
    /// </summary>
    [Fact]
    public async Task 場所の1つが外付けの上なら_見つからない物に数えず場所も変えない()
    {
        var offline = Path.Combine(UnresolvedMergeTests.MissingVolumeFolder(), "外付けの上.zip");
        var gone = Path.Combine(_root, "消した.zip");
        var (_, file) = await EmptyRecordOfAsync("写し.zip", "なかみ");
        await SaveAsync("9900002", file with { Paths = [offline, gone] });

        var result = await _finder.FindAsync([_watched]);

        var after = await OnlyFileOfAsync("9900002");
        Assert.Equal([offline, gone], after.Paths);
        Assert.Null(after.MissingSince);
        Assert.Equal(0, result.MissingBefore);
    }

    private sealed class InlineProgress(Action onReport) : IProgress<(int Hashed, string? Detail)>
    {
        public void Report((int Hashed, string? Detail) value) => onReport();
    }
}
