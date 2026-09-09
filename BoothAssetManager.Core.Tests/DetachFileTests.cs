using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 間違って紐付いたファイルを直す経路。IDは書き換えず、ファイルを未確定へ戻す。
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
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 });
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

    [Fact]
    public async Task RemovesTheFileAndPutsItBackInUnresolved()
    {
        await SaveItemAsync("111", fileCount: 2);

        var outcome = await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        Assert.Equal(DetachOutcome.Detached, outcome);

        var item = await _store.Items.LoadAsync("111");
        Assert.NotNull(item);
        Assert.Single(item!.Local.LocalFiles);
        Assert.DoesNotContain(item.Local.LocalFiles, file => file.Hash == Hash);

        // 未確定に戻っていないと、正しい商品を選び直す場所が無い
        Assert.Contains(_store.Unresolved.Load(), file => file.Hash == Hash);
    }

    /// <summary>
    /// 外しただけでは次の取り込みで戻ってしまう。手掛かりから商品IDが1つに決まるファイルは、
    /// 取り込みのたびに同じ商品へ自動で紐付くため。外した記録が要る。
    /// </summary>
    [Fact]
    public async Task RecordsThatTheFileDoesNotBelongToThatItem()
    {
        await SaveItemAsync("111", fileCount: 2);

        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        var detached = DetachedIndex.From(_store.Detached.Load());
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

        var item = await _store.Items.LoadAsync("111");
        Assert.NotNull(item);
        Assert.Empty(item!.Local.LocalFiles);
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
    public async Task ForgetsTheRecordWhenTheUserAssignsItBack()
    {
        await SaveItemAsync("111", fileCount: 2);
        await _service.DetachFileAsync("111", Hash, deleteItemWhenEmpty: false);

        await _service.AssignItemIdAsync(Hash, "111");

        Assert.Empty(_store.Detached.Load());
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
        Assert.True(DetachedIndex.From(_store.Detached.Load()).IsDetached(Hash, "111"));
    }
}
