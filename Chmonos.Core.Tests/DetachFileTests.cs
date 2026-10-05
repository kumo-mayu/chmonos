using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 間違って紐付いたファイルを直す経路。IDは書き換えず、ファイルを未確定へ戻す。
/// 外したファイルは行を消さずに印を付けて残す（ユーザ判断 2026-09-12）。
/// </summary>
public class DetachFileTests : IDisposable
{
    private const string Hash = "AAAA1111";

    private readonly string _root;
    private readonly string _library;
    private readonly string _file;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public DetachFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-detach-" + Guid.NewGuid().ToString("N"));
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_root);

        _file = Path.Combine(_root, "衣装_1.02.zip");
        File.WriteAllText(_file, "dummy");

        var paths = new AppPaths(_library);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // 外すだけなのでBOOTHへは行かない。行ったら失敗させる
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
    }

    private async Task SaveItemAsync(string id, int fileCount = 1, bool withFolder = false)
    {
        var files = Enumerable.Range(0, fileCount)
            .Select(index => new LocalFileRecord
            {
                Hash = index == 0 ? Hash : $"BBBB{index}",
                Paths = [index == 0 ? _file : Path.Combine(_root, $"other{index}.zip")],
                SizeBytes = 5,
            })
            .ToList();

        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "テスト商品", FetchedAt = DateTimeOffset.Now },
            Local = new LocalBlock
            {
                LocalFiles = files,
                LocalFolders = withFolder
                    ? [new LocalFolderRecord { Path = _root, RegisteredAt = DateTimeOffset.Now }]
                    : [],
                Memo = "自分で書いたメモ",
            },
        });
    }

    private async Task<ItemRecord> LoadAsync(string id)
    {
        var item = await _store.Items.LoadAsync(id);
        Assert.NotNull(item);
        return item!;
    }

    /// <summary>
    /// 外したファイルは行を残して印を付ける。手掛かりでこの商品に紐付いていたこと自体は確かなので、
    /// 消すと何を外したのかが見えなくなる（ユーザ指摘）。所持の数には入れない。
    /// </summary>
    [Fact]
    public async Task KeepsTheFileMarkedAndPutsItBackInUnresolved()
    {
        await SaveItemAsync("111", fileCount: 2);

        var outcome = await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        Assert.Equal(DetachOutcome.Detached, outcome);

        var item = await LoadAsync("111");
        Assert.Equal(2, item.Local.LocalFiles.Count);
        Assert.True(item.Local.LocalFiles.Single(file => file.Hash == Hash).Detached);
        Assert.DoesNotContain(item.Local.OwnedFiles, file => file.Hash == Hash);

        // 未確定に戻っていないと、正しい商品を選び直す場所が無い
        Assert.Contains(_store.Unresolved.Load(), file => file.Hash == Hash);
    }

    /// <summary>
    /// 外して未確定へ戻すとき、ダウンロード元の記録をそのファイルから読んで入れる（ユーザ判断 2026-09-30）。
    /// 未確定の画面は開くたびには読み直さないので、入れないと展開した中身が元zipの束に入らない。
    /// </summary>
    [Fact]
    public async Task PutsTheDownloadRecordOnTheUnresolvedEntry()
    {
        await SaveItemAsync("111", fileCount: 2);
        File.WriteAllText(
            _file + ":Zone.Identifier",
            "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=D:\\落とした物\\まとめ_1.00.zip\r\nHostUrl=https://example.invalid/file\r\n");

        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var entry = Assert.Single(_store.Unresolved.Load(), file => file.Hash == Hash);
        Assert.Equal("D:\\落とした物\\まとめ_1.00.zip", entry.ZoneReferrerUrl);
        Assert.Equal("https://example.invalid/file", entry.ZoneHostUrl);
    }

    /// <summary>記録の無いファイルは、値なしで未確定へ戻る（読めないことで外す操作を止めない）。</summary>
    [Fact]
    public async Task LeavesTheDownloadRecordEmptyWhenTheFileHasNone()
    {
        await SaveItemAsync("111", fileCount: 2);

        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var entry = Assert.Single(_store.Unresolved.Load(), file => file.Hash == Hash);
        Assert.Null(entry.ZoneReferrerUrl);
    }

    /// <summary>
    /// 外しただけでは次の取り込みで戻ってしまう。手掛かりから商品IDが1つに決まるファイルは、
    /// 取り込みのたびに同じ商品へ自動で紐付くため。取り込みは印からそれを知る。
    /// </summary>
    [Fact]
    public async Task TheImportLearnsThatTheFileDoesNotBelongToThatItem()
    {
        await SaveItemAsync("111", fileCount: 2);

        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var detached = DetachedIndex.From([await LoadAsync("111")]);
        Assert.True(detached.IsDetached(Hash, "111"));

        // 止めるのはその商品への紐付けだけ。他の商品には自由に付いてよい
        Assert.False(detached.IsDetached(Hash, "222"));
    }

    /// <summary>手元に何も無くなっても、情報だけ残す状態は普通（贈った商品と同じ）なので勝手に消さない。</summary>
    [Fact]
    public async Task KeepsTheItemWhenItBecomesEmpty()
    {
        await SaveItemAsync("111");

        var outcome = await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        Assert.Equal(DetachOutcome.ItemNowEmpty, outcome);

        var item = await LoadAsync("111");
        Assert.Empty(item.Local.OwnedFiles);
        Assert.False(item.IsOwned);
        Assert.Equal("自分で書いたメモ", item.Local.Memo);
    }

    [Fact]
    public async Task DeletesTheItemWhenAsked()
    {
        await SaveItemAsync("111");

        var outcome = await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: true);

        Assert.Equal(DetachOutcome.ItemDeleted, outcome);
        Assert.Null(await _store.Items.LoadAsync("111"));
    }

    /// <summary>フォルダ登録も所持のうち。残っていれば「空になった」ではない。</summary>
    [Fact]
    public async Task DoesNotCountAsEmptyWhileAFolderIsStillRegistered()
    {
        await SaveItemAsync("111", withFolder: true);

        var outcome = await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: true);

        Assert.Equal(DetachOutcome.Detached, outcome);
        Assert.NotNull(await _store.Items.LoadAsync("111"));
    }

    /// <summary>ユーザが選び直したのなら、こちらが覚えていて弾き続ける方がおかしい。</summary>
    [Fact]
    public async Task ClearsTheMarkWhenTheUserAssignsItBack()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        await _service.AssignItemIdAsync(Hash, "111");

        var item = await LoadAsync("111");
        Assert.Equal(2, item.Local.OwnedFiles.Count);
        Assert.DoesNotContain(item.Local.LocalFiles, file => file.Detached);
    }

    /// <summary>実体が消えているファイルを未確定に並べても、紐付け直す相手がいない。</summary>
    [Fact]
    public async Task DoesNotOfferAFileThatIsNoLongerOnDisk()
    {
        await SaveItemAsync("111", fileCount: 2);
        File.Delete(_file);

        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        Assert.Empty(_store.Unresolved.Load());

        // それでも「この商品のものではない」は覚えておく
        Assert.True((await LoadAsync("111")).Local.LocalFiles.Single(file => file.Hash == Hash).Detached);
    }

    /// <summary>灰色の行の「この商品に戻す」。印を下ろし、未確定からも消す。</summary>
    [Fact]
    public async Task ReattachesTheFile()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var outcome = await _service.ReattachFileAsync("111", Hash);

        Assert.Equal(ReattachOutcome.Reattached, outcome);
        Assert.Equal(2, (await LoadAsync("111")).Local.OwnedFiles.Count);
        Assert.DoesNotContain(_store.Unresolved.Load(), file => file.Hash == Hash);
    }

    /// <summary>外した後で別の商品へ紐付けたなら戻さない。同じファイルが2つの商品の持ち物になる。</summary>
    [Fact]
    public async Task DoesNotReattachAFileNowOwnedByAnotherItem()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);
        await SaveItemAsync("222");

        var outcome = await _service.ReattachFileAsync("111", Hash);

        Assert.Equal(ReattachOutcome.OwnedElsewhere, outcome);
        Assert.True((await LoadAsync("111")).Local.LocalFiles.Single(file => file.Hash == Hash).Detached);
    }

    /// <summary>外していないファイルには印を書き出さない。全ファイルに false が並ぶと読みにくい。</summary>
    [Fact]
    public async Task WritesTheMarkOnlyForDetachedFiles()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var json = string.Join("\n", Directory.EnumerateFiles(_library, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Contains("111", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"detached\""));
    }

    /// <summary>
    /// 外す印は書く直前の今の一覧に付ける。在るかを見る・未確定の錠を待つ間に取り込みが足したファイルを、
    /// 始めに読んだ写しの一覧で書いて消していた。空になったかも今の値で見る。
    /// </summary>
    [Fact]
    public async Task KeepsFilesAddedWhileWaitingForTheUnresolvedList()
    {
        await SaveItemAsync("111");

        // 未確定の画面が一覧を書いている最中（錠を持ったまま）に外し始める
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var screen = Task.Run(() => _store.Unresolved.UpdateAsync(list =>
        {
            entered.Set();
            release.Wait();
            return list;
        }));
        entered.Wait();

        var detach = _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: true);
        await Task.WhenAny(detach, Task.Delay(500));
        Assert.False(detach.IsCompleted);

        // その間に取り込みが同じ商品へファイルを足す
        await _store.Items.ChangeLocalAsync(
            "111",
            current => current with
            {
                LocalFiles = [.. current.LocalFiles, new LocalFileRecord { Hash = "CCCC1", Paths = [_file], SizeBytes = 5 }],
            },
            LocalOwners.Import);

        release.Set();
        await screen;

        // 足されたファイルがあるので、空にはならず商品も消えない
        Assert.Equal(DetachOutcome.Detached, await detach);

        var item = await LoadAsync("111");
        Assert.True(item.Local.LocalFiles.Single(file => file.Hash == Hash).Detached);
        Assert.False(item.Local.LocalFiles.Single(file => file.Hash == "CCCC1").Detached);
    }

    /// <summary>戻すときも、確かめている間に足されたファイルを消さない（全件を読むので時間がかかる）。</summary>
    [Fact]
    public async Task ReattachingWritesOntoTheCurrentList()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        Assert.Equal(ReattachOutcome.Reattached, await _service.ReattachFileAsync("111", Hash));
        Assert.Equal(ReattachOutcome.Missing, await _service.ReattachFileAsync("111", Hash));

        var item = await LoadAsync("111");
        Assert.All(item.Local.LocalFiles, file => Assert.False(file.Detached));
        Assert.Equal("自分で書いたメモ", item.Local.Memo);
    }
}
