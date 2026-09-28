using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 商品IDは分かっているがBOOTHで見つからなかった商品を、そのIDのまま登録する（ユーザ判断 2026-09-29）。
///
/// 仮IDで登録すると⑦から外れ、再び公開されても情報を取れない。本物のIDで「BOOTHで公開されていない」商品として持ち、
/// ⑦で確かめ直して、公開されたら情報を取り、要確認に知らせる。
/// </summary>
public class UnpublishedItemTests : IDisposable
{
    private const string Hash = "9a4e7c2b1d3f5068";
    private const string ItemId = "6543210";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;
    private readonly Handler _handler = new();
    private readonly AppSettings _settings = new() { FetchIntervalMs = 0, SaveImages = false };

    public UnpublishedItemTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-unpublished-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var client = new BoothClient(new HttpClient(_handler), _settings);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths, _settings), _settings);
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

    /// <summary>BOOTHの答え。登録では問い合わせないはずなので、既定では問い合わせたら数える。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public bool Published { get; set; }

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            if (!Published)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.RequestUri!.ToString().EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{ItemId}},
                          "name": "季節の衣装セット",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 1, "name": null, "price": 500 } ]
                        }
                        """),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body></body></html>"),
            });
        }
    }

    private async Task SeedUnresolvedAsync()
    {
        await _store.Unresolved.SaveAsync(
        [
            new UnresolvedFile
            {
                Hash = Hash,
                Paths = [Path.Combine(_root, "SeasonOutfit_v2.zip")],
                SizeBytes = 2048,
                ModifiedAtUtc = DateTimeOffset.UtcNow,
                FirstSeenAt = DateTimeOffset.UtcNow,
            },
        ]);
    }

    [Fact]
    public async Task RegistersUnderTheRealIdWithTheFile()
    {
        await SeedUnresolvedAsync();

        Assert.True(await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, " 季節の衣装 "));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.NotNull(item);
        Assert.Equal("季節の衣装", item!.Local.DisplayName);
        Assert.Equal(Hash, Assert.Single(item.Local.LocalFiles).Hash);
        Assert.Empty(_store.Unresolved.Load());
    }

    /// <summary>直前の確かめで見つからなかったIDなので、登録では問い合わせない。</summary>
    [Fact]
    public async Task DoesNotAskBoothWhenRegistering()
    {
        await SeedUnresolvedAsync();

        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");

        Assert.Equal(0, _handler.Requests);
    }

    /// <summary>観測していないので booth は空。「一度も取れていない」は FetchedAt が null で見分ける。</summary>
    [Fact]
    public async Task LeavesTheBoothBlockUnobserved()
    {
        await SeedUnresolvedAsync();
        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");

        var item = await _store.Items.LoadAsync(ItemId);

        Assert.Null(item!.Booth.FetchedAt);
        Assert.Null(item.Booth.Name);
    }

    /// <summary>販売終了の商品と同じ扱いにする。回数は確定の回数から始める（1から始めると次の404で印が外れる）。</summary>
    [Fact]
    public async Task MarksItAsNotOnBoothLikeADelistedItem()
    {
        await SeedUnresolvedAsync();
        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");

        var item = await _store.Items.LoadAsync(ItemId);

        Assert.True(item!.Local.IsDelisted);
        Assert.Equal(_settings.NotFoundThreshold, item.Local.ConsecutiveNotFoundCount);
        Assert.NotNull(item.Local.NextFetchDueAt);
    }

    /// <summary>仮IDと違い、期限の来た商品の取り直し（⑦）に乗る。</summary>
    [Fact]
    public async Task IsPickedUpByTheDueRefresh()
    {
        await SeedUnresolvedAsync();
        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");

        // 予定日は販売終了の確かめ直しの間隔（30日±ばらつき）の先。それより十分先の時点で見る
        var due = await new DueRefresh(_store, _service).FindDueAsync(DateTimeOffset.Now.AddDays(365));

        Assert.Contains(ItemId, due);
    }

    /// <summary>まだ見つからなければ、回数が増えるだけで「公開されていない」ままにする。</summary>
    [Fact]
    public async Task StaysNotOnBoothWhileBoothStillSaysNotFound()
    {
        await SeedUnresolvedAsync();
        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");

        Assert.Equal(RefreshOutcome.Delisted, await _service.RefreshAsync(ItemId));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.True(item!.Local.IsDelisted);
        Assert.Equal(_settings.NotFoundThreshold + 1, item.Local.ConsecutiveNotFoundCount);
        Assert.Null(item.Booth.FetchedAt);
    }

    /// <summary>公開されたら情報を取り、印を外し、要確認に知らせる。付けた名前は残す。</summary>
    [Fact]
    public async Task FillsInAndTellsTheUserWhenItAppearsOnBooth()
    {
        await SeedUnresolvedAsync();
        await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装");
        _handler.Published = true;

        Assert.Equal(RefreshOutcome.Updated, await _service.RefreshAsync(ItemId));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.Equal("季節の衣装セット", item!.Booth.Name);
        Assert.NotNull(item.Booth.FetchedAt);
        Assert.False(item.Local.IsDelisted);
        Assert.Equal("季節の衣装", item.Local.DisplayName);
        Assert.Equal(Hash, Assert.Single(item.Local.LocalFiles).Hash);

        var notification = Assert.Single(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.ItemBackOnBooth);
        Assert.Equal(ItemId, notification.ItemId);
    }

    /// <summary>既に同じIDの商品があれば、ファイルを足すだけ。名前や取得の記録は上書きしない。</summary>
    [Fact]
    public async Task OnlyAddsTheFileWhenTheItemAlreadyExists()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now.AddDays(-3), Name = "季節の衣装セット" },
            Local = new LocalBlock { DisplayName = "自分で付けた名前" },
        });
        await SeedUnresolvedAsync();

        Assert.True(await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "ファイル名から作った名前"));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.Equal("自分で付けた名前", item!.Local.DisplayName);
        Assert.False(item.Local.IsDelisted);
        Assert.Equal(0, item.Local.ConsecutiveNotFoundCount);
        Assert.Equal(Hash, Assert.Single(item.Local.LocalFiles).Hash);
    }

    [Fact]
    public async Task RefusesALocalId()
    {
        await SeedUnresolvedAsync();

        Assert.False(await _service.AssignUnpublishedItemIdAsync(Hash, LocalItemId.For(Hash), "季節の衣装"));
        Assert.Single(_store.Unresolved.Load());
    }

    [Fact]
    public async Task DoesNothingWhenTheFileIsNotUnresolved()
    {
        Assert.False(await _service.AssignUnpublishedItemIdAsync(Hash, ItemId, "季節の衣装"));
        Assert.Null(await _store.Items.LoadAsync(ItemId));
    }
}
