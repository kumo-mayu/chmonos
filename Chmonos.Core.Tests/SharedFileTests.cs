using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 1つのファイルを2つ以上の商品に結ぶ（ユーザ判断 2026-11-11 plan 27。セット商品と中身の各商品の両方を「持っている」にする）。
/// 片方への操作は、その商品の記録だけを変える。ファイルそのもの（場所が増えた・無くなった）と除外の一覧は全体に効く。
/// </summary>
public sealed class SharedFileTests : IDisposable
{
    private const string SetId = "9900901";
    private const string PartId = "9900902";
    private const string ThirdId = "9900903";

    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public SharedFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "chmonos-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _paths = new AppPaths(Path.Combine(_root, "library"));
        _paths.EnsureCreated();
        _store = new DataStore(_paths);

        // 手元の記録だけを扱う。BOOTH へ行けば落ちる
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, _paths));
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
    }

    private async Task SaveItemAsync(string id, params LocalFileRecord[] files)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物の商品" + id, FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock { LocalFiles = files },
        });

    /// <summary>手掛かりの無いファイルを作る（取り込みでも BOOTH へは行かない）。返すのは場所とハッシュ。</summary>
    private async Task<(string Path, string Hash)> MakeFileAsync(string name = "作り物_セット.zip", string content = "作り物の中身")
    {
        var path = Path.Combine(_root, "files", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return (path, await FileHasher.ComputeSha256Async(path));
    }

    /// <summary>2つの商品が同じファイルを持つ形を、人の操作（両方に付ける）で作る。</summary>
    private async Task<(string Path, string Hash)> ShareBetweenSetAndPartAsync()
    {
        var (path, hash) = await MakeFileAsync();
        await SaveItemAsync(SetId, new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = new FileInfo(path).Length });
        await SaveItemAsync(PartId);
        Assert.Equal(FileAttachResult.Attached, (await _service.AttachFileAsync(PartId, path, otherHolders: OtherHolders.Share)).Result);
        return (path, hash);
    }

    private async Task<LocalBlock> LocalOfAsync(string id) => (await _store.Items.LoadAsync(id))!.Local;

    [Fact]
    public async Task 両方に付けるとこの商品に付き_ほかの商品の記録は変わらない()
    {
        var (path, hash) = await MakeFileAsync();
        await SaveItemAsync(SetId, new LocalFileRecord { Hash = hash, Paths = [path], SizeBytes = 1, VariationId = 7 });
        await SaveItemAsync(PartId);
        await _store.Unresolved.SaveAsync(
            [new UnresolvedFile { Hash = hash, Paths = [path], SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now }]);
        var before = await LocalOfAsync(SetId);

        var outcome = await _service.AttachFileAsync(PartId, path, otherHolders: OtherHolders.Share);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        Assert.Empty(outcome.Holders);
        var mine = Assert.Single((await LocalOfAsync(PartId)).LocalFiles);
        Assert.Equal(hash, mine.Hash);
        Assert.False(mine.Detached);

        // 向こうの行は、外した印も種類も前のまま（書き換えていない）
        Assert.Equal(before.LocalFiles, (await LocalOfAsync(SetId)).LocalFiles);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>両方に付けた後でも、次に付ける商品にはまた聞く（決まった組でも、付けるたびに聞く。plan 27「最初は残す」）。</summary>
    [Fact]
    public async Task 両方に付けた後も_次の商品に付けるときは持ち主を全員返して聞く()
    {
        var (path, _) = await ShareBetweenSetAndPartAsync();
        await SaveItemAsync(ThirdId);

        var asked = await _service.AttachFileAsync(ThirdId, path);

        Assert.Equal(FileAttachResult.OwnedElsewhere, asked.Result);
        Assert.Equal([SetId, PartId], asked.Holders.Select(holder => holder.ItemId).Order());
        Assert.Empty((await LocalOfAsync(ThirdId)).LocalFiles);
    }

    [Fact]
    public async Task 付け直すと持ち主の全員から外す()
    {
        var (path, _) = await ShareBetweenSetAndPartAsync();
        await SaveItemAsync(ThirdId);

        var taken = await _service.AttachFileAsync(ThirdId, path, otherHolders: OtherHolders.TakeOver);

        Assert.Equal(FileAttachResult.Attached, taken.Result);
        Assert.True(Assert.Single((await LocalOfAsync(SetId)).LocalFiles).Detached);
        Assert.True(Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Detached);
        Assert.False(Assert.Single((await LocalOfAsync(ThirdId)).LocalFiles).Detached);
    }

    /// <summary>
    /// 片方から外しても、もう片方は持ったまま。ファイルの行き先は決まっているので未確定には出さない
    /// （出すとそこから除外・別の商品への登録ができ、持っている商品の手元の物まで変わる）。
    /// </summary>
    [Fact]
    public async Task 片方から外すとその商品にだけ外した印が付き_未確定には出ない()
    {
        var (_, hash) = await ShareBetweenSetAndPartAsync();

        var outcome = await _service.DetachFileAsync(PartId, hash, deleteItemWhenEmpty: false);

        Assert.Equal(DetachOutcome.ItemNowEmpty, outcome);
        Assert.True(Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Detached);
        Assert.False(Assert.Single((await LocalOfAsync(SetId)).LocalFiles).Detached);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>最後の持ち主から外したときは、今までどおり未確定へ戻す。</summary>
    [Fact]
    public async Task 両方から外すと最後に外したときに未確定へ出る()
    {
        var (path, hash) = await ShareBetweenSetAndPartAsync();

        await _service.DetachFileAsync(PartId, hash, deleteItemWhenEmpty: false);
        Assert.Empty(_store.Unresolved.Load());

        await _service.DetachFileAsync(SetId, hash, deleteItemWhenEmpty: false);
        var unresolved = Assert.Single(_store.Unresolved.Load());
        Assert.Equal([path], unresolved.Paths);
    }

    /// <summary>
    /// 外した後でもう片方が持ち続けていると、「この商品に戻す」は今は通さない（窓を出さない操作で黙って共有にしない）。
    /// 戻したいときは、商品ページの「追加…」で選び直し、窓で「両方に付ける」を選ぶ。
    /// </summary>
    [Fact]
    public async Task 片方から外した後_もう片方が持っていれば戻すは通さない()
    {
        var (_, hash) = await ShareBetweenSetAndPartAsync();
        await _service.DetachFileAsync(PartId, hash, deleteItemWhenEmpty: false);

        Assert.Equal(ReattachOutcome.OwnedElsewhere, await _service.ReattachFileAsync(PartId, hash));
        Assert.True(Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Detached);
    }

    /// <summary>除外の一覧は全体に効く：除外しても持つ商品の記録はそのまま、次に付ける商品は除外を解くかを聞かれる。</summary>
    [Fact]
    public async Task 除外は持つ商品の記録を変えず_次に付ける商品では除外として返る()
    {
        var (path, hash) = await ShareBetweenSetAndPartAsync();
        var before = (await LocalOfAsync(SetId)).LocalFiles;
        await SaveItemAsync(ThirdId);

        await _service.ExcludeAsync(
            [new UnresolvedFile { Hash = hash, Paths = [path], SizeBytes = 1, ModifiedAtUtc = DateTimeOffset.Now, FirstSeenAt = DateTimeOffset.Now }],
            reason: null);

        Assert.Equal(before, (await LocalOfAsync(SetId)).LocalFiles);
        Assert.False(Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Detached);
        Assert.Equal(FileAttachResult.Excluded, (await _service.AttachFileAsync(ThirdId, path, otherHolders: OtherHolders.Share)).Result);
    }

    /// <summary>ファイルそのものは全体に効く：移した先を取り込むと、持つ商品の両方に新しい場所が付く。</summary>
    [Fact]
    public async Task 移した先を取り込むと両方の商品に新しい場所が付く()
    {
        var (path, _) = await ShareBetweenSetAndPartAsync();
        var moved = Path.Combine(_root, "moved", "作り物_セット.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Move(path, moved);

        await NewPipeline().RunAsync(new ImportWorkSet([Path.GetDirectoryName(moved)!]));

        Assert.Contains(moved, Assert.Single((await LocalOfAsync(SetId)).LocalFiles).Paths);
        Assert.Contains(moved, Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Paths);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>除外した中身は、持つ商品にも新しい場所を足さない（取り込みは除外の前で止まる）。記録は消さない。</summary>
    [Fact]
    public async Task 除外した中身を移した先を取り込んでも_持つ商品に場所を足さず記録も消さない()
    {
        var (path, hash) = await ShareBetweenSetAndPartAsync();
        await _store.Excluded.SaveAsync([new ExcludedEntry { Hash = hash, Paths = [path], ExcludedAt = DateTimeOffset.Now }]);
        var moved = Path.Combine(_root, "moved", "作り物_セット.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.Copy(path, moved);

        var summary = await NewPipeline().RunAsync(new ImportWorkSet([Path.GetDirectoryName(moved)!]));

        Assert.Equal(1, summary.FilesExcluded);
        Assert.DoesNotContain(moved, Assert.Single((await LocalOfAsync(SetId)).LocalFiles).Paths);
        Assert.DoesNotContain(moved, Assert.Single((await LocalOfAsync(PartId)).LocalFiles).Paths);
    }

    /// <summary>
    /// 見つからないファイルの掃除は、ファイルが無いという全体の事実で決まるので、持つ商品の両方から記録を消す。
    /// 窓の数は商品ごとの記録の数（同じファイルでも2件）。
    /// </summary>
    [Fact]
    public async Task 見つからない記録の掃除は持つ商品の両方から消す()
    {
        var gone = Path.Combine(_root, "消えた", "作り物_セット.zip");
        LocalFileRecord Missing() => new() { Hash = "H-SHARED", Paths = [gone], SizeBytes = 3, MissingSince = DateTimeOffset.Now.AddDays(-1) };
        await SaveItemAsync(SetId, Missing());
        await SaveItemAsync(PartId, Missing());

        var cleaner = new MissingRecordCleaner(_store);
        var done = await cleaner.ForgetAsync(await cleaner.PlanAsync());

        Assert.Equal(new MissingRecordCleanup(Items: 2, Files: 2, Folders: 0, Unowned: 2), done);
        Assert.Empty((await LocalOfAsync(SetId)).LocalFiles);
        Assert.Empty((await LocalOfAsync(PartId)).LocalFiles);
    }

    [Fact]
    public async Task 引越しの後も両方の商品が同じファイルを持つ()
    {
        var (_, hash) = await ShareBetweenSetAndPartAsync();
        var destination = Path.Combine(_root, "moved-library");

        Assert.True(StoreMover.Move(_paths.Root, destination).Succeeded);

        var moved = new DataStore(new AppPaths(destination));
        Assert.Equal(hash, Assert.Single((await moved.Items.LoadAsync(SetId))!.Local.AttachedFiles).Hash);
        Assert.Equal(hash, Assert.Single((await moved.Items.LoadAsync(PartId))!.Local.AttachedFiles).Hash);
    }

    [Fact]
    public async Task 控えから戻しても両方の商品が同じファイルを持つ()
    {
        var (_, hash) = await ShareBetweenSetAndPartAsync();
        var zip = Path.Combine(_root, "backup.zip");
        var restored = Path.Combine(_root, "restored");

        BackupArchive.Export(_paths.Root, zip, includeImages: false);
        BackupArchive.Restore(zip, restored);

        var store = new DataStore(new AppPaths(restored));
        Assert.Equal(hash, Assert.Single((await store.Items.LoadAsync(SetId))!.Local.AttachedFiles).Hash);
        Assert.Equal(hash, Assert.Single((await store.Items.LoadAsync(PartId))!.Local.AttachedFiles).Hash);
    }

    private ImportPipeline NewPipeline()
    {
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), settings, TestWait.None);
        return new ImportPipeline(_store, client, new ImagePipeline(client, _paths, settings), settings);
    }
}
