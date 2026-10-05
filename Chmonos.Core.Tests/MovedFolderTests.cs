using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 登録したフォルダ（<see cref="LocalFolderRecord"/>）を移したときに、「見つからないファイルを探す」が候補を見せ、
/// 人が選んだら場所を差し替える（見つからない・移動の点検 10-A・ユーザ判断 2026-10-05）。
/// フォルダは場所が同一性なので、前は移すと「見つかりません」のままで、登録し直すしかなかった。
/// **推した物は候補として見せるだけで、勝手に差し替えない**（CLAUDE.md「推定した値を勝手に入れない」）。
/// </summary>
public class MovedFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _search;
    private readonly DataStore _store;
    private readonly MissingFileFinder _finder;
    private readonly ItemService _service;

    public MovedFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-movedfolder-" + Guid.NewGuid().ToString("N"));
        _search = Path.Combine(_root, "search");
        Directory.CreateDirectory(_search);

        var paths = new AppPaths(Path.Combine(_root, "store"));
        paths.EnsureCreated();
        _store = new DataStore(paths);
        _finder = new MissingFileFinder(_store);

        // 商品は先に置くので、BOOTH へは行かない
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("このテストではBOOTHへ行かないはず");
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

    /// <summary>3ファイル・600バイトの展開フォルダを作る。</summary>
    private static string MakeFolder(string path, int extraBytes = 0)
    {
        Directory.CreateDirectory(Path.Combine(path, "texture"));
        File.WriteAllBytes(Path.Combine(path, "costume.unitypackage"), new byte[300 + extraBytes]);
        File.WriteAllBytes(Path.Combine(path, "texture", "a.psd"), new byte[200]);
        File.WriteAllBytes(Path.Combine(path, "texture", "b.psd"), new byte[100]);
        return path;
    }

    private async Task SaveItemWithFolderAsync(string itemId, string folderPath, int fileCount = 3, long totalBytes = 600)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = itemId,
            Booth = new BoothBlock { Name = "作り物の衣装", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                LocalFolders =
                [
                    new LocalFolderRecord
                    {
                        Path = folderPath,
                        FileCount = fileCount,
                        TotalBytes = totalBytes,
                        RegisteredAt = DateTimeOffset.Now,
                    },
                ],
            },
        });

    // ---- 合い方の決まり ----

    private static LocalFolderRecord Record(string path, int count = 3, long bytes = 600)
        => new() { Path = path, FileCount = count, TotalBytes = bytes };

    [Fact]
    public void 名前とファイル数と大きさが同じなら_いちばん強い合い方()
        => Assert.Equal(
            FolderMatchKind.NameAndContents,
            MovedFolderCandidates.Match(Record(@"D:\old\costume_v1"), @"E:\new\Costume_V1", 3, 600));

    [Fact]
    public void 名前が違ってもファイル数と大きさが同じなら_候補になる()
        => Assert.Equal(
            FolderMatchKind.Contents,
            MovedFolderCandidates.Match(Record(@"D:\old\costume_v1"), @"E:\new\renamed", 3, 600));

    [Fact]
    public void 名前だけ同じでも_候補になる()
        => Assert.Equal(
            FolderMatchKind.Name,
            MovedFolderCandidates.Match(Record(@"D:\old\costume_v1\"), @"E:\new\costume_v1", 4, 900));

    [Fact]
    public void 名前もファイル数と大きさも違えば_候補にしない()
        => Assert.Null(MovedFolderCandidates.Match(Record(@"D:\old\costume_v1"), @"E:\new\other", 3, 601));

    [Fact]
    public void 数えた物が空の記録は_ファイル数と大きさでは合わせない()
        // 登録のときに読めなかった記録は 0 件・0 バイト。空のフォルダがみな候補になってしまう
        => Assert.Null(MovedFolderCandidates.Match(Record(@"D:\old\costume_v1", 0, 0), @"E:\new\empty", 0, 0));

    [Fact]
    public void 中を数えられなかったフォルダは_名前でだけ合わせる()
    {
        Assert.Null(MovedFolderCandidates.Match(Record(@"D:\old\costume_v1"), @"E:\new\renamed", null, null));
        Assert.Equal(
            FolderMatchKind.Name,
            MovedFolderCandidates.Match(Record(@"D:\old\costume_v1"), @"E:\new\costume_v1", null, null));
    }

    [Fact]
    public void 木を1回たどって_フォルダごとに中の全部のファイル数と大きさを数える()
    {
        var folder = MakeFolder(Path.Combine(_search, "costume_v1"));

        var measured = MovedFolderCandidates.MeasureTree(_search, CancellationToken.None);

        var top = Assert.Single(measured, entry => entry.Path == folder);
        Assert.Equal((3, 600L), (top.FileCount!.Value, top.TotalBytes!.Value));
        Assert.Equal(
            (2, 300L),
            measured.Single(entry => entry.Path == Path.Combine(folder, "texture")) is var texture
                ? (texture.FileCount!.Value, texture.TotalBytes!.Value)
                : default);
        // 登録の数え方（RegisteredFolderSet.Measure）と同じ数になる。違うと移しただけの物が合わない
        Assert.Equal(Scanning.RegisteredFolderSet.Measure(folder), (top.FileCount!.Value, top.TotalBytes!.Value));
    }

    // ---- 探す ----

    [Fact]
    public async Task 移した登録フォルダは_探すと候補に出るが_場所は変えない()
    {
        var old = Path.Combine(_root, "old", "costume_v1");
        await SaveItemWithFolderAsync("9900501", old);
        // 親の sub もほかにファイルを持たないので同じ数になるが、包んでいるだけなので候補に出さない
        var moved = MakeFolder(Path.Combine(_search, "sub", "costume_v1"));
        MakeFolder(Path.Combine(_search, "renamed"));
        MakeFolder(Path.Combine(_search, "unrelated"), extraBytes: 1);

        var result = await _finder.FindAsync([_search]);

        var missing = Assert.Single(result.MissingFolders);
        Assert.Equal("9900501", missing.ItemId);
        Assert.Equal("作り物の衣装", missing.ItemName);
        Assert.Equal(old, missing.Path);
        Assert.Equal(
            [(moved, FolderMatchKind.NameAndContents), (Path.Combine(_search, "renamed"), FolderMatchKind.Contents)],
            missing.Candidates.Select(candidate => (candidate.Path, candidate.Kind)));

        // 候補を見せるだけで、記録は人が選ぶまで変えない
        Assert.Equal(old, Assert.Single((await _store.Items.LoadAsync("9900501"))!.Local.LocalFolders).Path);
    }

    [Fact]
    public async Task 在る登録フォルダは_見つからない物に出さない()
    {
        var here = MakeFolder(Path.Combine(_root, "library", "costume_v1"));
        await SaveItemWithFolderAsync("9900502", here);
        MakeFolder(Path.Combine(_search, "costume_v1"));

        var result = await _finder.FindAsync([_search]);

        Assert.Empty(result.MissingFolders);
    }

    [Fact]
    public async Task ほかの登録に使われているフォルダは_候補にしない()
    {
        await SaveItemWithFolderAsync("9900503", Path.Combine(_root, "old", "costume_v1"));
        var taken = MakeFolder(Path.Combine(_search, "costume_v1"));
        await SaveItemWithFolderAsync("9900504", taken);

        var result = await _finder.FindAsync([_search]);

        Assert.Empty(Assert.Single(result.MissingFolders).Candidates);
    }

    [Fact]
    public async Task 候補が無くても_見つからない登録フォルダとして返す()
    {
        await SaveItemWithFolderAsync("9900505", Path.Combine(_root, "old", "costume_v1"));

        var result = await _finder.FindAsync([_search]);

        Assert.Empty(Assert.Single(result.MissingFolders).Candidates);
    }

    // ---- 人が選んで差し替える ----

    [Fact]
    public async Task 選んだ場所に差し替え_見つからなくなった日時を消し_中の未確定を片付ける()
    {
        var old = Path.Combine(_root, "old", "costume_v1");
        await SaveItemWithFolderAsync("9900506", old);
        await _store.Items.ChangeLocalAsync(
            "9900506",
            local => local with { LocalFolders = [local.LocalFolders[0] with { MissingSince = DateTimeOffset.Now }] },
            LocalOwners.Import);
        var moved = MakeFolder(Path.Combine(_search, "costume_v1"));
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = "CCCC",
                Paths = [Path.Combine(moved, "costume.unitypackage")],
                SizeBytes = 300,
                ModifiedAtUtc = DateTimeOffset.UnixEpoch,
                FirstSeenAt = DateTimeOffset.UnixEpoch,
            },
        ]);

        var outcome = await _service.RelocateFolderAsync("9900506", old, moved);

        Assert.Equal(FolderRelocation.Moved, outcome);
        var folder = Assert.Single((await _store.Items.LoadAsync("9900506"))!.Local.LocalFolders);
        Assert.Equal(moved, folder.Path);
        Assert.Null(folder.MissingSince);
        Assert.Equal((3, 600L), (folder.FileCount, folder.TotalBytes));
        Assert.Empty(_store.Unresolved.Load());
    }

    [Fact]
    public async Task 差し替える間に取り込みが足したファイルが残る()
    {
        var old = Path.Combine(_root, "old", "costume_v1");
        await SaveItemWithFolderAsync("9900507", old);
        var moved = MakeFolder(Path.Combine(_search, "costume_v1"));

        var outcome = FolderRelocation.RecordGone;
        await ItemLockRace.WhileAnotherWriterChangesAsync(
            _store,
            "9900507",
            local => ItemLockRace.AddFile(local),
            async () => outcome = await _service.RelocateFolderAsync("9900507", old, moved));

        Assert.Equal(FolderRelocation.Moved, outcome);
        var after = (await _store.Items.LoadAsync("9900507"))!.Local;
        Assert.Equal("BBBB", Assert.Single(after.LocalFiles).Hash);
        Assert.Equal(moved, Assert.Single(after.LocalFolders).Path);
    }

    [Fact]
    public async Task 選んだ場所がほかの商品に登録されていれば_差し替えない()
    {
        var old = Path.Combine(_root, "old", "costume_v1");
        await SaveItemWithFolderAsync("9900508", old);
        var taken = MakeFolder(Path.Combine(_search, "costume_v1"));
        await SaveItemWithFolderAsync("9900509", taken);

        var outcome = await _service.RelocateFolderAsync("9900508", old, taken);

        Assert.Equal(FolderRelocation.RegisteredElsewhere, outcome);
        Assert.Equal(old, Assert.Single((await _store.Items.LoadAsync("9900508"))!.Local.LocalFolders).Path);
    }

    [Fact]
    public async Task 選んだ場所が無くなっていれば_差し替えない()
    {
        var old = Path.Combine(_root, "old", "costume_v1");
        await SaveItemWithFolderAsync("9900510", old);

        var outcome = await _service.RelocateFolderAsync("9900510", old, Path.Combine(_search, "gone"));

        Assert.Equal(FolderRelocation.TargetMissing, outcome);
        Assert.Equal(old, Assert.Single((await _store.Items.LoadAsync("9900510"))!.Local.LocalFolders).Path);
    }

    [Fact]
    public async Task その間に登録が外されていれば_何も書かない()
    {
        var moved = MakeFolder(Path.Combine(_search, "costume_v1"));
        await SaveItemWithFolderAsync("9900511", moved);
        await _service.UnregisterFolderAsync("9900511", moved);

        var outcome = await _service.RelocateFolderAsync("9900511", Path.Combine(_root, "old", "costume_v1"), moved);

        Assert.Equal(FolderRelocation.RecordGone, outcome);
        Assert.Empty((await _store.Items.LoadAsync("9900511"))!.Local.LocalFolders);
    }
}
