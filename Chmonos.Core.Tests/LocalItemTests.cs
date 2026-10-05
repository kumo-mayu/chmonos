using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 未確定の第三の出口——「BOOTHに無い商品として登録する」。
///
/// 非公開・削除済みの商品は、買っていて手元にファイルがあってもIDが分からない。
/// 確定（BOOTHから取れる）でも除外（BOOTH商品ではない）でもないので、
/// この出口が無いと未確定に永久に溜まるか、統計から消えるかの二択になる。
/// </summary>
public class LocalItemTests : IDisposable
{
    private const string Hash = "3f9c1b7e5a2d4088";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public LocalItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-local-" + Guid.NewGuid().ToString("N"));
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("仮IDの商品ではBOOTHへ行かないはず");
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

    private async Task SeedUnresolvedAsync()
    {
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = Hash,
                Paths = [Path.Combine(_root, "MysteryOutfit_v1.2.zip")],
                SizeBytes = 4096,
                ModifiedAtUtc = DateTimeOffset.UtcNow,
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
        ]);
    }

    [Fact]
    public async Task RegistersWithAHashDerivedId()
    {
        await SeedUnresolvedAsync();

        var itemId = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");

        Assert.Equal("local-3f9c1b7e", itemId);
        Assert.Contains("local-3f9c1b7e", _store.Items.EnumerateItemIds());
    }

    /// <summary>買って手元にあるものなので、ファイルは必ず付いてくる。</summary>
    [Fact]
    public async Task KeepsTheFileAndClearsTheUnresolvedEntry()
    {
        await SeedUnresolvedAsync();

        var itemId = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");
        var item = await _store.Items.LoadAsync(itemId!);

        Assert.Single(item!.Local.LocalFiles);
        Assert.True(item.IsOwned);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>
    /// 選んだ複数のファイルを1回で1つの商品にする（ユーザ指示 2026-10-02）。仮IDは渡した先頭のファイルから決まり、
    /// 未確定に無いファイル（先に片付いた物）は飛ばし、ほかの未確定は残す。
    /// </summary>
    [Fact]
    public async Task RegistersSeveralFilesAsOneItemInTheGivenOrder()
    {
        UnresolvedFile Make(string hash, string name) => new()
        {
            Hash = hash,
            Paths = [Path.Combine(_root, name)],
            SizeBytes = 10,
            ModifiedAtUtc = DateTimeOffset.UtcNow,
            FirstSeenAt = DateTimeOffset.UtcNow,
        };
        await _store.Unresolved.SaveAsync(
        [
            Make("aaaa000000000001", "body.zip"),
            Make("bbbb000000000002", "extra.psd"),
            Make("cccc000000000003", "other.zip"),
        ]);

        var itemId = await _service.RegisterLocalItemAsync(
            ["bbbb000000000002", "ffff000000000009", "aaaa000000000001"], "まとめた衣装");

        Assert.Equal(LocalItemId.For("bbbb000000000002"), itemId);
        var item = await _store.Items.LoadAsync(itemId!);
        Assert.Equal("まとめた衣装", item!.Local.DisplayName);
        Assert.Equal(
            ["bbbb000000000002", "aaaa000000000001"],
            item.Local.LocalFiles.Select(file => file.Hash).ToArray());
        Assert.Equal("cccc000000000003", Assert.Single(_store.Unresolved.Load()).Hash);
    }

    [Fact]
    public async Task RegistersNothingWhenNoneOfTheFilesAreUnresolved()
    {
        await SeedUnresolvedAsync();

        Assert.Null(await _service.RegisterLocalItemAsync(["ffff000000000009"], "謎の衣装"));
        Assert.Single(_store.Unresolved.Load());
        Assert.Empty(_store.Items.EnumerateItemIds());
    }

    /// <summary>
    /// 未確定の候補からこの商品を確かめるとき、題は付けた名前で出す。
    /// BOOTH の側の名前は無いので、そこだけを見ると仮の ID（local-…）が題になっていた。
    /// </summary>
    [Fact]
    public async Task PreviewShowsTheGivenNameNotTheProvisionalId()
    {
        await SeedUnresolvedAsync();
        var itemId = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");

        var (preview, error, notOnBooth) = await _service.PreviewWithReasonAsync(itemId!);

        Assert.Null(error);
        Assert.False(notOnBooth);
        Assert.Equal("謎の衣装", preview!.Name);
        Assert.Equal(itemId, preview.Id);
        Assert.True(preview.IsAlreadyOwned);
    }

    /// <summary>
    /// 観測していないので <c>Booth.FetchedAt</c> は null のまま。
    /// 登録日時を入れると⑦の期限計算も「最終取得」の表示も狂う。
    /// </summary>
    [Fact]
    public async Task LeavesTheBoothBlockUnobserved()
    {
        await SeedUnresolvedAsync();

        var item = await _store.Items.LoadAsync((await _service.RegisterLocalItemAsync(Hash, "謎の衣装"))!);

        Assert.Null(item!.Booth.FetchedAt);
        Assert.False(item.Booth.WasEverFetched);
        Assert.Null(item.Booth.Name);
        Assert.Null(item.Local.NextFetchDueAt);
    }

    /// <summary>ユーザが付けた名前が画面に出る名前になる。</summary>
    [Fact]
    public async Task UsesTheNameTheUserGave()
    {
        await SeedUnresolvedAsync();

        var item = await _store.Items.LoadAsync((await _service.RegisterLocalItemAsync(Hash, "  謎の衣装  "))!);

        Assert.Equal("謎の衣装", item!.Local.DisplayName);
        Assert.Equal("謎の衣装", item.DisplayName);
        Assert.True(item.IsLocalOnly);
    }

    /// <summary>
    /// **仮IDではBOOTHへ問い合わせない。**
    /// 存在しないIDなので、叩けば404が返るだけ（このテストのハンドラは例外を投げる）。
    /// </summary>
    [Fact]
    public async Task NeverAsksBoothAboutALocalId()
    {
        await SeedUnresolvedAsync();
        var itemId = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");

        Assert.Equal(RefreshOutcome.NotOnBooth, await _service.RefreshAsync(itemId!));
    }

    /// <summary>⑦の対象にも入らない。</summary>
    [Fact]
    public async Task IsNotPickedUpByTheDueRefresh()
    {
        await SeedUnresolvedAsync();
        var itemId = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");

        // 期限を無理に入れても対象にしない（手でJSONを書いた場合に相当する）
        var item = await _store.Items.LoadAsync(itemId!);
        await _store.Items.SaveAsync(item! with
        {
            Local = item.Local with { NextFetchDueAt = DateTimeOffset.Now.AddDays(-1) },
        });

        var due = await new DueRefresh(_store, _service).FindDueAsync(DateTimeOffset.Now);

        Assert.DoesNotContain(itemId, due);
    }

    /// <summary>同じファイルをもう一度登録しても、同じ商品に行き着く（仮IDはハッシュから決まる）。</summary>
    [Fact]
    public async Task RegisteringTheSameFileTwiceLandsOnTheSameItem()
    {
        await SeedUnresolvedAsync();
        var first = await _service.RegisterLocalItemAsync(Hash, "謎の衣装");

        await SeedUnresolvedAsync();
        var second = await _service.RegisterLocalItemAsync(Hash, "謎の衣装（改）");

        Assert.Equal(first, second);
        Assert.Single(_store.Items.EnumerateItemIds());

        var item = await _store.Items.LoadAsync(second!);
        Assert.Equal("謎の衣装（改）", item!.Local.DisplayName);
        Assert.Single(item.Local.LocalFiles);
    }

    /// <summary>未確定に無いファイルは登録しない。</summary>
    [Fact]
    public async Task DoesNothingWhenTheFileIsNotUnresolved()
    {
        Assert.Null(await _service.RegisterLocalItemAsync(Hash, "謎の衣装"));
        Assert.Empty(_store.Items.EnumerateItemIds());
    }

    /// <summary>
    /// BOOTHページのURLは作らない。仮IDで組むと404のページへ送ることになる。
    /// </summary>
    [Fact]
    public async Task HasNoBoothPageUrl()
    {
        await SeedUnresolvedAsync();
        var item = await _store.Items.LoadAsync((await _service.RegisterLocalItemAsync(Hash, "謎の衣装"))!);

        Assert.Null(BoothClient.PageUrlFor(item!));
    }

    /// <summary>普通の商品にはちゃんと出る（上のテストが常にnullを返しても通らないように）。</summary>
    [Fact]
    public void StillBuildsTheUrlForARealItem()
    {
        var item = new ItemRecord
        {
            Id = "5927710",
            Booth = new BoothBlock(),
            Local = new LocalBlock(),
        };

        Assert.Equal("https://booth.pm/ja/items/5927710", BoothClient.PageUrlFor(item));
    }

    /// <summary>
    /// 名前は両方とも検索対象。
    /// BOOTHが復活したあとも、自分で付けた名前で探せなければ意味が無い。
    /// </summary>
    [Fact]
    public void SearchesBothNames()
    {
        var item = new ItemRecord
        {
            Id = "12345",
            Booth = new BoothBlock { Name = "オリジナル3Dモデル『Bird/鳥』" },
            Local = new LocalBlock { DisplayName = "とりさん" },
        };

        var haystack = SearchText.Build(item);

        Assert.Contains("とりさん", haystack.Folded(SearchField.Name), StringComparison.Ordinal);
        Assert.Contains("bird", haystack.Folded(SearchField.Name), StringComparison.OrdinalIgnoreCase);
    }
}
