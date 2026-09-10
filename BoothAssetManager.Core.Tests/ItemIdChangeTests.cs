using System.Net;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothAssetManager.Core.Storage;
using Xunit;

namespace BoothAssetManager.Core.Tests;

/// <summary>
/// 商品まるごとを別のIDへ移す。
///
/// 仮IDで登録したものの本物のIDが後から分かったときに要る。
/// **IDは書き換えない。**新しいIDの商品へ中身を移し、元の商品を消す——
/// 商品IDはファイル名にもフォルダ名にもなっていて、他の商品からも
/// 名前で参照されているので、IDだけ書き換えると参照が全部迷子になる。
/// </summary>
public class ItemIdChangeTests : IDisposable
{
    private const string LocalId = "local-3f9c1b7e";
    private const string RealId = "5927710";

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    public ItemIdChangeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(new Handler()), settings);
        _service = new ItemService(_store, client, new ImagePipeline(client, paths, settings), settings);
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

    /// <summary>RealId は普通に返す。それ以外のIDは404。</summary>
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            if (!url.Contains(RealId, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($$"""
                        {
                          "id": {{RealId}},
                          "name": "オリジナル3Dモデル『Bird/鳥』",
                          "shop": { "name": "alpha", "subdomain": "alpha", "url": "https://alpha.booth.pm/" },
                          "images": [],
                          "variations": [ { "id": 900, "name": null, "price": 1000 } ]
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

    private static LocalFileRecord File(string hash, long? variationId = null) => new()
    {
        Hash = hash,
        Paths = [$@"C:\dl\{hash}.zip"],
        SizeBytes = 100,
        VariationId = variationId,
    };

    private async Task SaveLocalItemAsync(LocalBlock local)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = LocalId,
            Booth = new BoothBlock(),
            Local = local,
        });

    // ---- 移し替えそのもの ----

    [Fact]
    public async Task MovesEverythingAndRemovesTheOldItem()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            DisplayName = "とりさん",
            Memo = "頭が大きい",
            LocalFiles = [File("aaa")],
            Purchases = [new Purchase { Price = 1000 }],
        });

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync(LocalId, RealId));

        var moved = await _store.Items.LoadAsync(RealId);

        Assert.NotNull(moved);
        Assert.Equal("aaa", Assert.Single(moved!.Local.LocalFiles).Hash);
        Assert.Equal("頭が大きい", moved.Local.Memo);
        Assert.Equal("とりさん", moved.Local.DisplayName);
        Assert.Null(await _store.Items.LoadAsync(LocalId));
    }

    /// <summary>移した先の情報はBOOTHから取る。仮IDのままでは名前も画像も増えない。</summary>
    [Fact]
    public async Task FetchesTheTargetFromBooth()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Items.LoadAsync(RealId);

        Assert.Equal("オリジナル3Dモデル『Bird/鳥』", moved!.Booth.Name);
        Assert.NotNull(moved.Booth.FetchedAt);
        Assert.False(moved.IsLocalOnly);
    }

    /// <summary>
    /// **移した先が非公開でも止めない。**買って手元にあるものを、
    /// 移し先が取れないという理由で消してはいけない。
    /// </summary>
    [Fact]
    public async Task StillMovesWhenTheTargetIsNotOnBooth()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync(LocalId, "9999999"));

        var moved = await _store.Items.LoadAsync("9999999");

        Assert.Single(moved!.Local.LocalFiles);
        Assert.Null(moved.Booth.FetchedAt);
    }

    [Fact]
    public async Task RefusesToMoveOntoItself()
        => Assert.Equal(ItemIdChangeOutcome.SameId, await _service.ChangeItemIdAsync(LocalId, LocalId));

    [Fact]
    public async Task SaysWhenTheSourceIsGone()
        => Assert.Equal(ItemIdChangeOutcome.SourceMissing, await _service.ChangeItemIdAsync("nope", RealId));

    // ---- 移せないもの ----

    /// <summary>
    /// **「どのバリエーションか」は持って行けない。**移した先のIDは別物で、
    /// 数字が偶然一致すると間違ったまま黙って通る。
    /// </summary>
    [Fact]
    public async Task DropsTheVariationLinkOnFiles()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa", variationId: 900)] });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Items.LoadAsync(RealId);

        Assert.Null(Assert.Single(moved!.Local.LocalFiles).VariationId);
    }

    /// <summary>購入記録も同じ。**ただし金額は残る**——支出から落としてはいけない。</summary>
    [Fact]
    public async Task DropsTheVariationLinkButKeepsThePrice()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            Purchases = [new Purchase { VariationId = 111, Price = 1500 }],
        });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var purchase = Assert.Single((await _store.Items.LoadAsync(RealId))!.Local.Purchases);

        Assert.Null(purchase.VariationId);
        Assert.Equal(1500, purchase.Price);
    }

    /// <summary>検出した対応アバターは移さない。この商品の説明文から取ったものだから。</summary>
    [Fact]
    public async Task DropsTheDetectedAvatars()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            Avatars = [new AvatarLink { AvatarItemId = "4897493", Confirmed = true }],
            AvatarBases = [new AvatarBaseLink { BaseName = "+Head" }],
        });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Items.LoadAsync(RealId);

        Assert.Empty(moved!.Local.Avatars);
        Assert.Empty(moved.Local.AvatarBases);
    }

    /// <summary>着せた記録はユーザのもの。検出が触らないので、そのまま持って行く。</summary>
    [Fact]
    public async Task KeepsTheUsageTheUserRecorded()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            UsedOn = [new AvatarUsage { AvatarItemId = "4897493", Note = "肩を詰めた" }],
        });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var usage = Assert.Single((await _store.Items.LoadAsync(RealId))!.Local.UsedOn);

        Assert.Equal("肩を詰めた", usage.Note);
    }

    // ---- 移した先に既に中身があるとき ----

    private async Task SaveTargetAsync(LocalBlock local)
        => await _store.Items.SaveAsync(new ItemRecord
        {
            Id = RealId,
            Booth = new BoothBlock { FetchedAt = DateTimeOffset.Now, Name = "鳥" },
            Local = local,
        });

    /// <summary>移した先の入力は潰さない。そちらもユーザが入れたもの。</summary>
    [Fact]
    public async Task DoesNotOverwriteWhatTheTargetAlreadyHas()
    {
        await SaveTargetAsync(new LocalBlock { DisplayName = "とり（本物）" });
        await SaveLocalItemAsync(new LocalBlock { DisplayName = "とりさん", LocalFiles = [File("aaa")] });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        Assert.Equal("とり（本物）", (await _store.Items.LoadAsync(RealId))!.Local.DisplayName);
    }

    /// <summary>メモは**どちらも捨てない**。片方だけ残すと書いたことが黙って消える。</summary>
    [Fact]
    public async Task KeepsBothMemos()
    {
        await SaveTargetAsync(new LocalBlock { Memo = "先にあったメモ" });
        await SaveLocalItemAsync(new LocalBlock { Memo = "移す側のメモ", LocalFiles = [File("aaa")] });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var memo = (await _store.Items.LoadAsync(RealId))!.Local.Memo;

        Assert.Contains("先にあったメモ", memo, StringComparison.Ordinal);
        Assert.Contains("移す側のメモ", memo, StringComparison.Ordinal);
    }

    // ---- 二重計上の疑い ----

    /// <summary>金額が一致する記録は疑う。バリエーションでは突き合わせられないので。</summary>
    [Fact]
    public async Task SuspectsPurchasesWithTheSamePrice()
    {
        await SaveTargetAsync(new LocalBlock { Purchases = [new Purchase { VariationId = 900, Price = 1000 }] });
        await SaveLocalItemAsync(new LocalBlock { Purchases = [new Purchase { Price = 1000 }] });

        var plan = await _service.PlanItemIdChangeAsync(LocalId, RealId);

        var duplicate = Assert.Single(plan!.Duplicates);
        Assert.Equal(0, duplicate.Index);
        Assert.Equal(1000, duplicate.Price);
        Assert.Equal(1, duplicate.MatchingAtTarget);
    }

    /// <summary>種類が違えば別の買い物。自分用と贈答は疑わない。</summary>
    [Fact]
    public async Task DoesNotSuspectADifferentKind()
    {
        await SaveTargetAsync(new LocalBlock
        {
            Purchases = [new Purchase { VariationId = 900, Price = 1000, Kind = PurchaseKind.Given }],
        });
        await SaveLocalItemAsync(new LocalBlock { Purchases = [new Purchase { Price = 1000 }] });

        Assert.Empty((await _service.PlanItemIdChangeAsync(LocalId, RealId))!.Duplicates);
    }

    /// <summary>金額が未入力なら突き合わせようがない。疑わない。</summary>
    [Fact]
    public async Task DoesNotSuspectWhenThePriceIsBlank()
    {
        await SaveTargetAsync(new LocalBlock { Purchases = [new Purchase { VariationId = 900 }] });
        await SaveLocalItemAsync(new LocalBlock { Purchases = [new Purchase()] });

        Assert.Empty((await _service.PlanItemIdChangeAsync(LocalId, RealId))!.Duplicates);
    }

    /// <summary>二重と判断したものは移さない。それ以外は移す。</summary>
    [Fact]
    public async Task SkipsThePurchasesTheUserCalledDuplicates()
    {
        await SaveTargetAsync(new LocalBlock { Purchases = [new Purchase { VariationId = 900, Price = 1000 }] });
        await SaveLocalItemAsync(new LocalBlock
        {
            Purchases =
            [
                new Purchase { Price = 1000 },
                new Purchase { Price = 500, Kind = PurchaseKind.Given },
            ],
        });

        await _service.ChangeItemIdAsync(LocalId, RealId, new HashSet<int> { 0 });

        var purchases = (await _store.Items.LoadAsync(RealId))!.Local.Purchases;

        Assert.Equal(2, purchases.Count);
        Assert.Contains(purchases, purchase => purchase.Price == 500);
        Assert.Single(purchases, purchase => purchase.Price == 1000);
    }

    // ---- 下見 ----

    /// <summary>移せないものは名指しで出す。件数だけにしない。</summary>
    [Fact]
    public async Task NamesWhatCannotBeMoved()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa", variationId: 5)],
            Purchases = [new Purchase { VariationId = 5, Price = 100 }],
            Avatars = [new AvatarLink { AvatarItemId = "4897493", Source = AvatarLinkSource.Manual }],
        });

        var plan = await _service.PlanItemIdChangeAsync(LocalId, RealId);

        Assert.Contains(plan!.Dropped, thing => thing.Reason == DroppedReason.VariationLink);
        Assert.Contains(plan.Dropped, thing => thing.Reason == DroppedReason.Detected);

        // 手で指定したものが消えることは、はっきり言う
        Assert.Contains(plan.Dropped, thing => thing.Text.Contains("手で指定した", StringComparison.Ordinal));
    }

    /// <summary>下見では何も書かない。押す前に消えていては困る。</summary>
    [Fact]
    public async Task WritesNothingWhilePlanning()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        await _service.PlanItemIdChangeAsync(LocalId, RealId);

        Assert.NotNull(await _store.Items.LoadAsync(LocalId));
        Assert.Null(await _store.Items.LoadAsync(RealId));
    }

    /// <summary>BOOTHで見つかるかを先に出す。見つからなくても止めないが、黙って進めない。</summary>
    [Fact]
    public async Task SaysWhetherTheTargetWasFoundOnBooth()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        Assert.True((await _service.PlanItemIdChangeAsync(LocalId, RealId))!.TargetFoundOnBooth);
        Assert.False((await _service.PlanItemIdChangeAsync(LocalId, "9999999"))!.TargetFoundOnBooth);
    }

    /// <summary>
    /// 外した記録は移した先のIDへ読み替える。読み替えないと、
    /// 「この商品のものではない」と言ったファイルが次の取り込みで戻ってくる。
    /// </summary>
    [Fact]
    public async Task RewritesTheDetachedRecords()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _store.Detached.SaveAsync(
        [
            new DetachedFile
            {
                Hash = "bbb",
                ItemId = LocalId,
                Paths = [@"C:\dl\bbb.zip"],
                DetachedAt = DateTimeOffset.Now,
            },
        ]);

        await _service.ChangeItemIdAsync(LocalId, RealId);

        Assert.Equal(RealId, Assert.Single(_store.Detached.Load()).ItemId);
    }
}
