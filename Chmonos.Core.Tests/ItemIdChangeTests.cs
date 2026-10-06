using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

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
    private readonly Handler _handler = new();

    public ItemIdChangeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var paths = new AppPaths(Path.Combine(_root, "library"));
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var client = new BoothClient(new HttpClient(_handler), settings, TestWait.None);
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
        /// <summary>
        /// BOOTH から取っている最中に1回だけ走らせる（その数秒の間に取り込みや人が書く様子を作る）。
        /// </summary>
        public Func<Task>? DuringFetch { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (DuringFetch is { } during)
            {
                DuringFetch = null;
                during().GetAwaiter().GetResult();
            }

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

    /// <summary>
    /// BOOTHが「無い」と答えたIDへ移した商品は、未確定の「見つからないIDのまま登録」と同じ状態になる
    /// （ユーザ判断 2026-09-29）。前は空の商品を作るだけで印も予定日も持たず、⑦に乗らないので公開されても情報を取れなかった。
    /// 回数は非公開と確定する回数から始める（1から始めると次の404で印が外れる）。
    /// </summary>
    [Fact]
    public async Task MovingToAnIdMissingOnBoothPutsItOnTheRecheckLikeADelistedItem()
    {
        await SaveLocalItemAsync(new LocalBlock { DisplayName = "とりさん", LocalFiles = [File("aaa")] });

        await _service.ChangeItemIdAsync(LocalId, "9999999");

        var moved = (await _store.Items.LoadAsync("9999999"))!;

        Assert.True(moved.Local.IsDelisted);
        Assert.Equal(new AppSettings().NotFoundThreshold, moved.Local.ConsecutiveNotFoundCount);
        Assert.NotNull(moved.Local.NextFetchDueAt);
        Assert.Null(moved.Local.LastFetchedAt);

        // 人が付けた名前は移る（空の商品の側に名前は無い）
        Assert.Equal("とりさん", moved.Local.DisplayName);
    }

    /// <summary>
    /// 移し先の問い合わせが一時的に届かなかったときは、販売終了の印を付けず、予定日を今にして次の⑦で取りに行く
    /// （ユーザ判断 2026-09-29）。前は予定日を持たない空の商品のままで、後から情報を取りに行かなかった
    /// </summary>
    [Fact]
    public async Task MovingWhileBoothIsUnreachableFetchesItOnTheNextRefresh()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var offline = new OffUiThreadTests.OfflineClient();
        var service = new ItemService(_store, offline, new ImagePipeline(offline, paths, settings), settings);
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        Assert.Equal(ItemIdChangeOutcome.Moved, await service.ChangeItemIdAsync(LocalId, "9999999"));

        var moved = (await _store.Items.LoadAsync("9999999"))!;
        Assert.False(moved.Local.IsDelisted);
        Assert.Equal(0, moved.Local.ConsecutiveNotFoundCount);
        Assert.True(moved.Local.NextFetchDueAt <= DateTimeOffset.Now);
        Assert.Single(moved.Local.LocalFiles);
    }

    /// <summary>既に手元にある商品へ移すときは、その商品の取得の記録を触らない（持ち主は⑦）。</summary>
    [Fact]
    public async Task MovingOntoAnExistingItemKeepsItsFetchRecord()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = "9999999",
            Booth = new BoothBlock(),
            Local = new LocalBlock { ConsecutiveNotFoundCount = 1 },
        });

        await _service.ChangeItemIdAsync(LocalId, "9999999");

        var moved = (await _store.Items.LoadAsync("9999999"))!;

        Assert.False(moved.Local.IsDelisted);
        Assert.Equal(1, moved.Local.ConsecutiveNotFoundCount);
    }

    [Fact]
    public async Task RefusesToMoveOntoItself()
        => Assert.Equal(ItemIdChangeOutcome.SameId, await _service.ChangeItemIdAsync(LocalId, LocalId));

    [Fact]
    public async Task SaysWhenTheSourceIsGone()
        => Assert.Equal(ItemIdChangeOutcome.SourceMissing, await _service.ChangeItemIdAsync("9900999", RealId));

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

    /// <summary>
    /// 改変が指しているIDも読み替える。
    ///
    /// **改変は「そのとき何を使ったか」という過去の事実。**IDを移しても使った事実は
    /// 変わらないので、指す先だけを付け替える。読み替えないと、消えたIDを指したまま
    /// 「手元に無い」と出続ける。
    /// </summary>
    [Fact]
    public async Task 改変が指している商品IDも移す()
    {
        var record = new ModificationRecord
        {
            Id = "mod-11112222",
            AvatarItemId = "4897493",
            Name = "普段着",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            Members =
            [
                new ModificationMember { ItemId = LocalId, FileHash = "abc" },
                new ModificationMember { ItemId = "6580186" },
            ],
        };

        await _store.Modifications.SaveAsync(record);
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Modifications.LoadAsync("mod-11112222");

        Assert.Equal([RealId, "6580186"], moved!.Members.Select(member => member.ItemId));

        // どのファイルを使ったかは移しても変わらない
        Assert.Equal("abc", moved.Members[0].FileHash);
    }

    /// <summary>アバターとして指されている場合も同じ（改変はアバター1体に属する）。</summary>
    [Fact]
    public async Task 改変が指しているアバターのIDも移す()
    {
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-33334444",
            AvatarItemId = LocalId,
            Name = "制服",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        });

        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        Assert.Equal(RealId, (await _store.Modifications.LoadAsync("mod-33334444"))!.AvatarItemId);
    }

    /// <summary>関係の無い改変は触らない。触った跡（更新日時）も付けない。</summary>
    [Fact]
    public async Task 関係の無い改変は触らない()
    {
        var updatedAt = DateTimeOffset.Now.AddDays(-30);
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-55556666",
            AvatarItemId = "4897493",
            Name = "よそ",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            Members = [new ModificationMember { ItemId = "6580186" }],
        });

        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await _service.ChangeItemIdAsync(LocalId, RealId);

        Assert.Equal(updatedAt, (await _store.Modifications.LoadAsync("mod-55556666"))!.UpdatedAt);
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

        Assert.Equal(ItemIdTargetStatus.Found, (await _service.PlanItemIdChangeAsync(LocalId, RealId))!.TargetOnBooth);
        Assert.Equal(ItemIdTargetStatus.NotFound, (await _service.PlanItemIdChangeAsync(LocalId, "9999999"))!.TargetOnBooth);
    }

    /// <summary>
    /// 一時的に届かないのを「BOOTHにある」と読まない（点検 2026-09-29・19）。
    /// 読むと窓が「移すときにBOOTHから商品情報を取得します」と言い切っていた。「無い」とも言わない
    /// </summary>
    [Fact]
    public async Task PlanDoesNotReadAnUnreachableTargetAsFound()
    {
        var paths = new AppPaths(Path.Combine(_root, "library"));
        var settings = new AppSettings { FetchIntervalMs = 0, SaveImages = false };
        var offline = new OffUiThreadTests.OfflineClient();
        var service = new ItemService(_store, offline, new ImagePipeline(offline, paths, settings), settings);
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        var plan = (await service.PlanItemIdChangeAsync(LocalId, "9999999"))!;

        Assert.Equal(ItemIdTargetStatus.Unknown, plan.TargetOnBooth);
        Assert.False(plan.TargetFoundOnBooth);
    }

    /// <summary>
    /// 外した印は移した先へ一緒に移る。移らないと、
    /// 「この商品のものではない」と言ったファイルが次の取り込みで戻ってくる。
    /// </summary>
    [Fact]
    public async Task CarriesTheDetachedMarkToTheTarget()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa"), File("bbb") with { Detached = true }] });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Items.LoadAsync(RealId);
        Assert.NotNull(moved);
        Assert.True(moved!.Local.LocalFiles.Single(file => file.Hash == "bbb").Detached);
        Assert.False(moved.Local.LocalFiles.Single(file => file.Hash == "aaa").Detached);
    }

    /// <summary>
    /// 移す先で外していたファイルを移す元が持っていても、外した印は下りない（2026-10-05・file-lifecycle.md「気になった所」12）。
    /// 前は取り込みと同じ「両方が外していた時だけ残す」で、移す元の外していない行が勝ち、移す先で「この商品のものではない」と
    /// 決めたファイルが黙って持ち物に戻っていた。移した先の入力は潰さない（ほかの欄と同じ）。
    /// </summary>
    [Fact]
    public async Task 移す先で外していたファイルは外したまま残る()
    {
        await SaveTargetAsync(new LocalBlock { LocalFiles = [File("aaa") with { Detached = true }, File("ccc")] });
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa"), File("bbb")] });

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync(LocalId, RealId));

        var moved = (await _store.Items.LoadAsync(RealId))!.Local.LocalFiles;
        Assert.True(moved.Single(file => file.Hash == "aaa").Detached);
        Assert.False(moved.Single(file => file.Hash == "bbb").Detached);
        Assert.False(moved.Single(file => file.Hash == "ccc").Detached);
    }

    /// <summary>移す先が持っている（外していない）ファイルを移す元が外していても、移す先の答えのまま。</summary>
    [Fact]
    public async Task 移す先が持っているファイルは移す元で外していても持ち物のまま()
    {
        await SaveTargetAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa") with { Detached = true }, File("bbb")] });

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync(LocalId, RealId));

        Assert.False((await _store.Items.LoadAsync(RealId))!.Local.LocalFiles.Single(file => file.Hash == "aaa").Detached);
    }

    // ---- 画像とお気に入り ----

    private const string UserImageFile = "user-1a2b3c4d.webp";

    private string WriteImage(string itemId, string fileName, string content = "絵")
    {
        var dir = _store.Paths.ItemImagesDir(itemId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// 自分で足した画像・お気に入り・役割・サムネイルの指定は移る。
    /// 前は引き継がず、元の商品を消すときに画像のフォルダごと消えて二度と取り返せなかった。
    /// </summary>
    [Fact]
    public async Task CarriesUserImagesFavoriteRolesAndThumbnail()
    {
        WriteImage(LocalId, UserImageFile);
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            UserImages = [new UserImage { FileName = UserImageFile, Caption = "着せたところ" }],
            ImageRoles = new Dictionary<string, ImageRole> { [UserImageFile] = ImageRole.Modified },
            ThumbnailImage = UserImageFile,
            IsFavorite = true,
        });

        Assert.Equal(ItemIdChangeOutcome.Moved, await _service.ChangeItemIdAsync(LocalId, RealId));

        var moved = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Equal("着せたところ", Assert.Single(moved.UserImages).Caption);
        Assert.Equal(ImageRole.Modified, moved.ImageRoles[UserImageFile]);
        Assert.Equal(UserImageFile, moved.ThumbnailImage);
        Assert.True(moved.IsFavorite);
        Assert.True(System.IO.File.Exists(Path.Combine(_store.Paths.ItemImagesDir(RealId), UserImageFile)));
        Assert.False(Directory.Exists(_store.Paths.ItemImagesDir(LocalId)));
    }

    /// <summary>
    /// 同じ名前の画像は同じ絵（保存名が中身のハッシュ）。1枚にまとめ、移した先のファイルと記録を残す。
    /// 覚え書きは空いている方を埋める。
    /// </summary>
    [Fact]
    public async Task MergesTheSamePictureIntoOne()
    {
        WriteImage(LocalId, UserImageFile, "移す側");
        WriteImage(RealId, UserImageFile, "先にあった");
        await SaveTargetAsync(new LocalBlock { UserImages = [new UserImage { FileName = UserImageFile }] });
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            UserImages = [new UserImage { FileName = UserImageFile, Caption = "覚え書き" }],
        });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Equal("覚え書き", Assert.Single(moved.UserImages).Caption);
        Assert.Equal("先にあった", System.IO.File.ReadAllText(Path.Combine(_store.Paths.ItemImagesDir(RealId), UserImageFile)));
    }

    /// <summary>
    /// サムネイルの指定は他の1つだけの欄と同じく移した先が優先。お気に入りはどちらかが付けていれば付ける。
    /// </summary>
    [Fact]
    public async Task KeepsTheTargetsThumbnailAndFavoriteFromEither()
    {
        WriteImage(LocalId, UserImageFile);
        await SaveTargetAsync(new LocalBlock { ThumbnailImage = "abcdef01.webp" });
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            UserImages = [new UserImage { FileName = UserImageFile }],
            ThumbnailImage = UserImageFile,
            IsFavorite = true,
        });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Equal("abcdef01.webp", moved.ThumbnailImage);
        Assert.True(moved.IsFavorite);
    }

    /// <summary>
    /// BOOTHの画像に付けた指定は移した先の画像と名前が合わないので移らない。**下見で名指しする。**
    /// </summary>
    [Fact]
    public async Task NamesTheChoicesOnBoothImagesThatCannotMove()
    {
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            ThumbnailImage = "abcdef01.webp",
            ImageRoles = new Dictionary<string, ImageRole>
            {
                ["abcdef02.webp"] = ImageRole.Modified,
                [UserImageFile] = ImageRole.Modified,
            },
        });

        var plan = await _service.PlanItemIdChangeAsync(LocalId, RealId);

        var dropped = Assert.Single(plan!.Dropped, thing => thing.Reason == DroppedReason.BoothImageChoice);
        Assert.Contains("2 件", dropped.Text, StringComparison.Ordinal);

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Null(moved.ThumbnailImage);
        Assert.Equal([UserImageFile], moved.ImageRoles.Keys);
    }

    /// <summary>
    /// 画像を写せなければ**何も書かずに元を残す**。元を消すと画像のフォルダごと消えるため。
    /// BOOTH から取るより前に確かめるので、取ってきた空の商品も残らない。
    /// </summary>
    [Fact]
    public async Task LeavesEverythingWhenTheImagesCannotBeCopied()
    {
        var path = WriteImage(LocalId, UserImageFile);
        await SaveLocalItemAsync(new LocalBlock
        {
            LocalFiles = [File("aaa")],
            UserImages = [new UserImage { FileName = UserImageFile }],
        });

        ItemIdChangeOutcome outcome;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = await _service.ChangeItemIdAsync(LocalId, RealId);
        }

        Assert.Equal(ItemIdChangeOutcome.ImagesNotMoved, outcome);
        Assert.NotNull(await _store.Items.LoadAsync(LocalId));
        Assert.True(System.IO.File.Exists(path));
        Assert.Null(await _store.Items.LoadAsync(RealId));
    }

    // ---- 取っている間に書かれた物 ----

    /// <summary>
    /// 移す元は取得の後で読み直す。前は取る前の写しを移したので、
    /// BOOTH から取っている数秒の間に取り込みが足したファイルが、元の商品ごと消えていた。
    /// </summary>
    [Fact]
    public async Task MovesWhatWasAddedToTheSourceWhileFetching()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        _handler.DuringFetch = () => _store.Items.ChangeLocalAsync(
            LocalId,
            current => current with { LocalFiles = [.. current.LocalFiles, File("bbb")], Memo = "取っている間のメモ" },
            [LocalField.LocalFiles, LocalField.Memo]);

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Equal(["aaa", "bbb"], moved.LocalFiles.Select(file => file.Hash).Order());
        Assert.Equal("取っている間のメモ", moved.Memo);
    }

    /// <summary>
    /// 取っている間に同じIDの商品が別の道で作られたら、丸ごと書かずに重ねる（L13 と同じ形）。
    /// 前は取ってきた商品で丸ごと書いたので、そちらに入ったファイルと名前が消えていた。
    /// </summary>
    [Fact]
    public async Task LayersOntoATargetCreatedWhileFetching()
    {
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });
        _handler.DuringFetch = () => SaveTargetAsync(new LocalBlock { DisplayName = "先に作られた", LocalFiles = [File("ccc")] });

        await _service.ChangeItemIdAsync(LocalId, RealId);

        var moved = await _store.Items.LoadAsync(RealId);
        Assert.Equal("先に作られた", moved!.Local.DisplayName);
        Assert.Equal(["aaa", "ccc"], moved.Local.LocalFiles.Select(file => file.Hash).Order());

        // booth は取ってきた方が重なる
        Assert.Equal("オリジナル3Dモデル『Bird/鳥』", moved.Booth.Name);
    }

    /// <summary>
    /// 未確定の「このIDで登録」も同じ。取っている間に取り込みが同じ商品へ足したファイルを、
    /// 作った時点の写しの [このファイル] で置き換えていた。
    /// </summary>
    [Fact]
    public async Task AssigningKeepsFilesAddedWhileFetching()
    {
        await _store.Unresolved.UpdateAsync(list =>
        {
            list.Add(new UnresolvedFile
            {
                Hash = "aaa",
                Paths = [@"C:\dl\aaa.zip"],
                SizeBytes = 100,
                ModifiedAtUtc = DateTimeOffset.Now,
                FirstSeenAt = DateTimeOffset.Now,
            });
            return list;
        });
        _handler.DuringFetch = () => SaveTargetAsync(new LocalBlock { Memo = "取り込みが作った", LocalFiles = [File("ccc")] });

        Assert.True(await _service.AssignItemIdAsync("aaa", RealId));

        var item = (await _store.Items.LoadAsync(RealId))!.Local;
        Assert.Equal("取り込みが作った", item.Memo);
        Assert.Equal(["aaa", "ccc"], item.LocalFiles.Select(file => file.Hash).Order());
    }

    /// <summary>フォルダの登録も同じ。取っている間と測っている間に足されたフォルダを消さない。</summary>
    [Fact]
    public async Task RegisteringAFolderKeepsFoldersAddedWhileFetching()
    {
        var folder = Path.Combine(_root, "展開した");
        Directory.CreateDirectory(folder);
        _handler.DuringFetch = () => SaveTargetAsync(new LocalBlock
        {
            LocalFolders = [new LocalFolderRecord { Path = @"D:\先に登録", RegisteredAt = DateTimeOffset.Now }],
        });

        Assert.Equal(FolderRegistration.Registered, await _service.RegisterFolderAsync(RealId, folder));

        var folders = (await _store.Items.LoadAsync(RealId))!.Local.LocalFolders.Select(record => record.Path);
        Assert.Contains(@"D:\先に登録", folders);
        Assert.Contains(folder, folders);
    }

    /// <summary>
    /// 改変の付け替えは錠の中で今の値に当てる。全件を読んでから書くまでの間に改変の画面が書いた名前を、
    /// 読んだ写しで丸ごと書いて消していた。
    /// </summary>
    [Fact]
    public async Task MovingModificationsKeepsWhatWasWrittenMeanwhile()
    {
        await _store.Modifications.SaveAsync(new ModificationRecord
        {
            Id = "mod-77778888",
            AvatarItemId = "4897493",
            Name = "前の名前",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            Members = [new ModificationMember { ItemId = LocalId }],
        });
        await SaveLocalItemAsync(new LocalBlock { LocalFiles = [File("aaa")] });

        // 改変の画面が書いている最中（錠を持ったまま）に付け替えを始める
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var screen = Task.Run(() => _store.Modifications.UpdateAsync("mod-77778888", record =>
        {
            entered.Set();
            release.Wait();
            return record with { Name = "画面で直した名前" };
        }));
        entered.Wait();

        var change = _service.ChangeItemIdAsync(LocalId, RealId);
        await Task.WhenAny(change, Task.Delay(500));
        release.Set();
        await Task.WhenAll(screen, change);

        var record = await _store.Modifications.LoadAsync("mod-77778888");
        Assert.Equal("画面で直した名前", record!.Name);
        Assert.Equal(RealId, Assert.Single(record.Members).ItemId);
    }
}
