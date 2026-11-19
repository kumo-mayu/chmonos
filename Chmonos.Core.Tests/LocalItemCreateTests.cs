using Chmonos.Core.Booth;
using Chmonos.Core.Commands;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// ファイルを持たない「BOOTHに無い商品」を名前だけで作る（2026-11-20 のメモ「ファイルの無いlocalItemが作成できない気がする」）。
/// 前は未確定のファイルを選ばないと作れず、仮IDもそのファイルのハッシュから決めていた。
/// </summary>
public class LocalItemCreateTests : IDisposable
{
    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public LocalItemCreateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-local-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        // BOOTHへは一切行かないはずなので、行ったら失敗させる
        var client = new BoothClient(new HttpClient(new UnreachableHandler()), new AppSettings { FetchIntervalMs = 0 }, TestWait.None);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths));
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("名前だけで作る商品ではBOOTHへ行かないはず");
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

    /// <summary>名前だけで、ファイルを持たない（＝持っていない）仮IDの商品ができる。観測していないので booth は空のまま、⑦にも乗らない。</summary>
    [Fact]
    public async Task CreatesAFilelessLocalItemFromANameAlone()
    {
        var itemId = await _service.CreateLocalItemAsync("  手で作った衣装  ");

        Assert.NotNull(itemId);
        Assert.True(LocalItemId.IsLocal(itemId));
        Assert.True(StoreIds.IsItemId(itemId));
        var item = await _store.Items.LoadAsync(itemId);
        Assert.Equal("手で作った衣装", item!.Local.DisplayName);
        Assert.True(item.IsLocalOnly);
        Assert.False(item.IsOwned);
        Assert.Empty(item.Local.LocalFiles);
        Assert.Null(item.Booth.FetchedAt);
        Assert.Null(item.Local.NextFetchDueAt);
        Assert.Equal(RefreshOutcome.NotOnBooth, await _service.RefreshAsync(itemId));
    }

    /// <summary>名前の無い仮IDの商品は、画面に local-… しか出ない。API からも来得るので Core で断る。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefusesAnEmptyName(string name)
    {
        Assert.Null(await _service.CreateLocalItemAsync(name));
        Assert.Empty(_store.Items.EnumerateItemIds());
    }

    /// <summary>
    /// 乱数の桁はハッシュ由来の8桁と違うので、後で同じ先頭8桁のファイルを「BOOTHに無い商品」として登録しても、
    /// 「同じファイルなら同じ商品」の道でこの商品へ入らない。
    /// </summary>
    [Fact]
    public void TheRandomFormNeverMeetsTheHashForm()
    {
        var random = LocalItemId.NewUnique();

        Assert.Equal(LocalItemId.Prefix.Length + LocalItemId.RandomLength, random.Length);
        Assert.NotEqual(LocalItemId.For("0123456789abcdef0123").Length, random.Length);
        Assert.True(StoreIds.IsItemId(random));
    }

    /// <summary>
    /// **乱数がたまたま在る商品と重なったら、在る商品を触らずに引き直す。**ファイルから作る道は在れば足すが、
    /// こちらで足すと関わりの無い商品の名前やメモを書き換えてしまう。
    /// </summary>
    [Fact]
    public async Task DrawsAgainWithoutTouchingAnItemThatHasTheSameId()
    {
        const string taken = "local-aaaaaaaaaaaa";
        const string fresh = "local-bbbbbbbbbbbb";
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = taken,
            Booth = new BoothBlock(),
            Local = new LocalBlock { DisplayName = "前からある商品", Memo = "人が書いたメモ" },
        });
        var draws = new Queue<string>([taken, fresh]);
        _service.NewLocalItemId = draws.Dequeue;

        var itemId = await _service.CreateLocalItemAsync("新しい商品");

        Assert.Equal(fresh, itemId);
        var kept = await _store.Items.LoadAsync(taken);
        Assert.Equal("前からある商品", kept!.Local.DisplayName);
        Assert.Equal("人が書いたメモ", kept.Local.Memo);
        Assert.Equal("新しい商品", (await _store.Items.LoadAsync(fresh))!.Local.DisplayName);
    }

    /// <summary>乱数の元が壊れて同じ値しか出さないときは、回り続けずに止まる。</summary>
    [Fact]
    public async Task GivesUpWhenEveryDrawIsTaken()
    {
        const string taken = "local-cccccccccccc";
        await _store.Items.SaveAsync(new ItemRecord { Id = taken, Booth = new BoothBlock(), Local = new LocalBlock { DisplayName = "前からある商品" } });
        _service.NewLocalItemId = () => taken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateLocalItemAsync("新しい商品"));
        Assert.Equal("前からある商品", (await _store.Items.LoadAsync(taken))!.Local.DisplayName);
    }

    /// <summary>作った後に商品ページの「追加…」でファイルを付けると、持っている商品になる。</summary>
    [Fact]
    public async Task AFileCanBeAttachedLater()
    {
        var itemId = (await _service.CreateLocalItemAsync("後からファイルを付ける商品"))!;
        var file = Path.Combine(_root, "files", "作り物.psd");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, [1, 2, 3, 4]);

        var outcome = await _service.AttachFileAsync(itemId, file);

        Assert.Equal(FileAttachResult.Attached, outcome.Result);
        var item = await _store.Items.LoadAsync(itemId);
        Assert.True(item!.IsOwned);
        Assert.Equal("後からファイルを付ける商品", item.Local.DisplayName);
    }

    /// <summary>命令の口を通すと、作った商品のIDが返る（画面はこれで商品ページを開く）。空の名前は失敗の文で返す。</summary>
    [Fact]
    public async Task TheCommandReturnsTheNewItem()
    {
        var handler = new CommandHandler(null!, _service);

        var saved = Assert.IsType<CommandResult.ItemSaved>(await handler.ExecuteAsync(new UiCommand.CreateLocalItem("命令で作った商品")));
        Assert.True(LocalItemId.IsLocal(saved.ItemId));
        Assert.IsType<CommandResult.Failed>(await handler.ExecuteAsync(new UiCommand.CreateLocalItem(" ")));
    }
}
