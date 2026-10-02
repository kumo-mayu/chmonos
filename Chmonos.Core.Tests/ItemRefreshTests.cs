using System.Net;
using Chmonos.Core.Booth;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 再取得の最中に人が編集したときの振る舞い。
///
/// 再取得はBOOTHへ2回問い合わせるので、始めてから書き終わるまでに数秒かかる。
/// 「取り込み中もアプリを使える」ようにすると、**その数秒はユーザが編集している時間**になる。
/// </summary>
public class ItemRefreshTests : IDisposable
{
    private const string ItemId = "9907001";

    private static readonly string ItemJson = """
        {
          "id": 9907001,
          "name": "真・アバターペンシステム",
          "description": "アバターに組み込むペンシステムです。",
          "price": "¥ 2,500",
          "url": "https://booth.pm/ja/items/9907001",
          "shop": { "name": "Sample Gates", "subdomain": "samplerin", "url": "https://samplerin.booth.pm/" },
          "images": [],
          "variations": [
            { "id": 12826082, "name": null, "price": 2500 }
          ]
        }
        """;

    private readonly string _root;
    private readonly DataStore _store;
    private readonly ItemService _service;

    /// <summary>HTMLを取りに来た瞬間に走らせる。取得の最中に人が編集した、という状況を作る。</summary>
    private Func<Task>? _whileFetchingHtml;

    /// <summary>BOOTHへ行った先。何本出たかを見る。</summary>
    private readonly List<string> _requests = [];

    /// <summary>商品JSONを404にする。非公開になった状況を作る。</summary>
    private bool _itemJsonNotFound;

    /// <summary>商品JSONの代わりに返す本文。200 でも JSON でない応答を作る。</summary>
    private string? _itemJsonBody;

    /// <summary>商品JSONの代わりに返す失敗の状態。5xx や 429 の状況を作る。</summary>
    private HttpStatusCode? _itemJsonFailure;

    public ItemRefreshTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bam-refresh-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(_root);
        paths.EnsureCreated();
        _store = new DataStore(paths);

        var settings = new AppSettings { FetchIntervalMs = 0 };
        // 再試行の間合い（2秒→8秒）は待たない（5xx を返す試験がある）
        var client = new BoothClient(new HttpClient(new StubHandler(this)), settings, delay: (_, _) => Task.CompletedTask);
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

    private sealed class StubHandler(ItemRefreshTests owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();

            lock (owner._requests)
            {
                owner._requests.Add(url);
            }

            if (url.EndsWith(".json", StringComparison.Ordinal))
            {
                if (owner._itemJsonFailure is { } failure)
                {
                    return new HttpResponseMessage(failure);
                }

                return owner._itemJsonNotFound
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(owner._itemJsonBody ?? ItemJson) };
            }

            if (url.Contains("/items/", StringComparison.Ordinal))
            {
                if (owner._whileFetchingHtml is { } edit)
                {
                    await edit();
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body></body></html>"),
                };
            }

            // 画像などは持っていない。落ちても取得は成立する
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private Task SaveItemAsync(LocalBlock local)
        => _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock { Name = "取り直す前の名前", FetchedAt = DateTimeOffset.Now },
            Local = local,
        });

    /// <summary>
    /// 5xx は <see cref="RefreshOutcome.ServerError"/>、429 は <see cref="RefreshOutcome.TemporaryFailure"/> で返す。
    /// ⑦は 5xx だけを打ち切りに数える（429 はこちらの出し過ぎなので数えない。ユーザ判断 2026-09-29）。
    /// どちらも予定日は動かさない。
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, RefreshOutcome.ServerError)]
    [InlineData(HttpStatusCode.InternalServerError, RefreshOutcome.ServerError)]
    [InlineData(HttpStatusCode.TooManyRequests, RefreshOutcome.TemporaryFailure)]
    public async Task TellsServerErrorsApartFromOtherTemporaryFailures(HttpStatusCode status, RefreshOutcome expected)
    {
        var due = DateTimeOffset.Now.AddDays(-3);
        await SaveItemAsync(new LocalBlock { NextFetchDueAt = due });
        _itemJsonFailure = status;

        Assert.Equal(expected, await _service.RefreshAsync(ItemId));
        Assert.Equal(due, (await _store.Items.LoadAsync(ItemId))!.Local.NextFetchDueAt);
    }

    /// <summary>
    /// 200 で JSON でない応答が来たら、投げずに「読めなかった」として返す。
    /// 前は投げていたので、⑦は毎回この商品で止まって残りに届かなかった。
    /// 予定日は普段の間隔で進める（動かさないと、毎回の⑦の先頭で同じ商品に1本を使い続けた）。
    /// booth と 404 の回数には触れない（読めていないので何も分かっていない）。
    /// </summary>
    [Fact]
    public async Task PutsOffAnUnreadableResponseUntilTheNextUsualCheck()
    {
        var due = DateTimeOffset.Now.AddDays(-3);
        await SaveItemAsync(new LocalBlock { NextFetchDueAt = due, ConsecutiveNotFoundCount = 1 });
        _itemJsonBody = "<html><body>メンテナンス中</body></html>";

        Assert.Equal(RefreshOutcome.Unreadable, await _service.RefreshAsync(ItemId));

        var item = await _store.Items.LoadAsync(ItemId);
        Assert.Equal("取り直す前の名前", item!.Booth.Name);
        Assert.Equal(1, item.Local.ConsecutiveNotFoundCount);
        Assert.True(item.Local.NextFetchDueAt > DateTimeOffset.Now.AddDays(1), "予定日が普段の間隔で先へ進むこと");
    }

    /// <summary>
    /// **これがA1で塞いだ穴。**
    ///
    /// 再取得は開始時点の <c>local</c> を抱えたまま数秒通信する。丸ごと書き戻す作りでは、
    /// その間に書かれたメモも分類も、取得が終わった瞬間に消えていた。
    /// </summary>
    [Fact]
    public async Task KeepsWhatTheUserTypedWhileTheFetchWasInFlight()
    {
        await SaveItemAsync(new LocalBlock { Memo = "取得前のメモ" });

        _whileFetchingHtml = () => _store.Items.SaveLocalAsync(
            ItemId,
            new LocalBlock
            {
                Memo = "取得中に書いたメモ",
                UserTags = [new UserTagAssignment { Top = "衣装" }],
            },
            LocalOwners.EditScreen);

        Assert.Equal(RefreshOutcome.Updated, await _service.RefreshAsync(ItemId));

        var reloaded = await _store.Items.LoadAsync(ItemId);

        Assert.Equal("取得中に書いたメモ", reloaded!.Local.Memo);
        Assert.Equal("衣装", reloaded.Local.UserTags[0].Top);

        // boothの側はちゃんと入れ替わっている
        Assert.Equal("真・アバターペンシステム", reloaded.Booth.Name);
        Assert.NotNull(reloaded.Local.LastFetchedAt);
        Assert.NotNull(reloaded.Local.NextFetchDueAt);
    }

    /// <summary>
    /// <c>ExistsOnBooth</c> は取り直した後のバリエーション一覧から入れ直す。
    /// 取得中にユーザが足した購入記録にも、その場で正しい値が入る。
    /// </summary>
    [Fact]
    public async Task MarksPurchasesAgainstTheVariationsJustFetched()
    {
        await SaveItemAsync(new LocalBlock());

        _whileFetchingHtml = () => _store.Items.SaveLocalAsync(
            ItemId,
            new LocalBlock
            {
                Purchases =
                [
                    new Purchase { VariationId = 12826082, Price = 2500 },
                    new Purchase { VariationId = 99999999, Price = 800 },
                ],
            },
            LocalOwners.EditScreen);

        await _service.RefreshAsync(ItemId);

        var reloaded = await _store.Items.LoadAsync(ItemId);

        Assert.Equal(2, reloaded!.Local.Purchases.Count);
        Assert.True(reloaded.Local.Purchases[0].ExistsOnBooth);
        Assert.False(reloaded.Local.Purchases[1].ExistsOnBooth);
    }

    /// <summary>
    /// 取り直しに成功したら、404だった印を全部落とす。
    ///
    /// **ここが印を外す唯一のきっかけ。**作者がたまたま商品ページを非公開にして
    /// いただけ、という場合はこれで復活する。日数で外す仕組みは要らない。
    /// </summary>
    [Fact]
    public async Task ClearsTheMissingImageMarkersWhenTheItemComesBack()
    {
        await SaveItemAsync(new LocalBlock());

        // 画像が404だったことにして印を置く
        var directory = Path.Combine(_root, "images", ItemId);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "deadbeef.missing"), []);
        File.WriteAllBytes(Path.Combine(directory, "cafef00d.missing"), []);

        Assert.Equal(RefreshOutcome.Updated, await _service.RefreshAsync(ItemId));

        Assert.Empty(Directory.EnumerateFiles(directory, "*.missing"));
    }

    /// <summary>
    /// 404が続いた商品は間隔を広げる。**ただし30日で打ち止め。**
    ///
    /// 確かめる間隔が「復活している期間」より長いと原理的に取り逃す。
    /// 季節ものは1ヶ月ほどしか公開されないので、そこが上限になる。
    /// 止めないのは、止めた瞬間に「戻ったこと」を知る手段が無くなるため。
    /// </summary>
    [Fact]
    public async Task SlowsDownButNeverStopsCheckingADelistedItem()
    {
        await SaveItemAsync(new LocalBlock());
        _itemJsonNotFound = true;

        // 1〜2回目は通常の間隔のまま（7日±3日）
        await _service.RefreshAsync(ItemId);
        var first = (await _store.Items.LoadAsync(ItemId))!.Local;
        Assert.InRange((first.NextFetchDueAt!.Value - DateTimeOffset.Now).TotalDays, 3, 11);
        Assert.False(first.IsDelisted);

        await _service.RefreshAsync(ItemId);

        // 3回目で販売終了と見なし、そこから30日±3日に広がる
        Assert.Equal(RefreshOutcome.Delisted, await _service.RefreshAsync(ItemId));

        var delisted = (await _store.Items.LoadAsync(ItemId))!.Local;
        Assert.True(delisted.IsDelisted);
        Assert.Equal(3, delisted.ConsecutiveNotFoundCount);
        Assert.InRange((delisted.NextFetchDueAt!.Value - DateTimeOffset.Now).TotalDays, 26, 34);
    }

    /// <summary>戻ってきたら一度で通常の間隔に戻る。</summary>
    [Fact]
    public async Task GoesBackToTheNormalIntervalAsSoonAsTheItemReturns()
    {
        await SaveItemAsync(new LocalBlock());
        _itemJsonNotFound = true;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await _service.RefreshAsync(ItemId);
        }

        _itemJsonNotFound = false;
        await _service.RefreshAsync(ItemId);

        var revived = (await _store.Items.LoadAsync(ItemId))!.Local;

        Assert.False(revived.IsDelisted);
        Assert.Equal(0, revived.ConsecutiveNotFoundCount);
        Assert.InRange((revived.NextFetchDueAt!.Value - DateTimeOffset.Now).TotalDays, 3, 11);
    }

    /// <summary>取り直しに失敗（404）したら印はそのまま。商品が生きている証拠が無い。</summary>
    [Fact]
    public async Task KeepsTheMarkersWhenTheItemItselfIsGone()
    {
        await SaveItemAsync(new LocalBlock());

        var directory = Path.Combine(_root, "images", ItemId);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "deadbeef.missing"), []);

        _itemJsonNotFound = true;

        await _service.RefreshAsync(ItemId);

        Assert.Single(Directory.EnumerateFiles(directory, "*.missing"));
    }

    /// <summary>
    /// 変わっていたら要確認へ書く。何が変わったかを並べるので、開かなくても判断できる。
    /// </summary>
    [Fact]
    public async Task WritesAnInboxEntryListingWhatChanged()
    {
        await SaveItemAsync(new LocalBlock { NotifyOnUpdate = true });

        await _service.RefreshAsync(ItemId);

        var notifications = _store.Notifications.Load();
        var entry = Assert.Single(notifications);

        Assert.Equal(NotificationKind.ItemUpdated, entry.Kind);
        Assert.Equal(ItemId, entry.ItemId);
        Assert.Contains("商品名", entry.Detail);
        Assert.Contains("取り直す前の名前", entry.Detail);
        Assert.Contains("真・アバターペンシステム", entry.Detail);
    }

    /// <summary>
    /// 消えたバリエーションは**名前で言う**（ユーザ要望 2026-09-18：件数だけでは何が消えたか分からない）。
    /// 名前はBOOTHにもう無いので、取り直す前の記録と、購入時に写した名前から引く。
    /// </summary>
    [Fact]
    public async Task NamesTheVariationsThatDisappeared()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock
            {
                Name = "取り直す前の名前",
                FetchedAt = DateTimeOffset.Now,
                Variations = [new BoothVariation { Id = 99999999, Name = "旧・支援版" }],
            },
            Local = new LocalBlock
            {
                // 手元のファイルは取り直す前の記録から、購入は写した名前から引く
                LocalFiles = [new LocalFileRecord { Hash = "AAAA", Paths = ["x.zip"], SizeBytes = 1, VariationId = 99999999 }],
                Purchases = [new Purchase { VariationId = 88888888, NameSnapshot = "旧・通常版", Price = 500 }],
            },
        });

        await _service.RefreshAsync(ItemId);

        var gone = Assert.Single(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.OrphanVariationLink);

        Assert.Equal("旧・支援版・旧・通常版", gone.Detail["消えたバリエーション：".Length..]);
    }

    /// <summary>
    /// 名前を持たなかったバリエーションは、ほかの画面と同じく「バリエーション選択なし」と言う（ユーザ判断 2026-09-29）。
    /// 在ったかも分からないものだけIDで言う。
    /// </summary>
    [Fact]
    public async Task CallsAnUnnamedVariationByTheSharedWord()
    {
        await _store.Items.SaveAsync(new ItemRecord
        {
            Id = ItemId,
            Booth = new BoothBlock
            {
                Name = "取り直す前の名前",
                FetchedAt = DateTimeOffset.Now,
                Variations = [new BoothVariation { Id = 99999999, Name = null }],
            },
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = "AAAA", Paths = ["x.zip"], SizeBytes = 1, VariationId = 99999999 }],
                Purchases = [new Purchase { VariationId = 77777777, Price = 500 }],
            },
        });

        await _service.RefreshAsync(ItemId);

        var gone = Assert.Single(
            _store.Notifications.Load(),
            entry => entry.Kind == NotificationKind.OrphanVariationLink);

        Assert.Equal($"{DisplayText.NoVariationName}・ID 77777777", gone.Detail["消えたバリエーション：".Length..]);
    }

    /// <summary>「知らせる」を切ってある商品には出さない。</summary>
    [Fact]
    public async Task StaysQuietForAnItemTheUserMuted()
    {
        await SaveItemAsync(new LocalBlock { NotifyOnUpdate = false });

        await _service.RefreshAsync(ItemId);

        Assert.Empty(_store.Notifications.Load());
    }

    /// <summary>同じ商品の未読は別の知らせとして溜めず、1件に重ねる。溜めても読む手間が増えるだけ。</summary>
    [Fact]
    public async Task KeepsOneUnreadEntryInsteadOfPilingThemUp()
    {
        await SaveItemAsync(new LocalBlock { NotifyOnUpdate = true });

        await _service.RefreshAsync(ItemId);

        // 名前を戻してもう一度走らせる（また「変わった」になる）
        var saved = await _store.Items.LoadAsync(ItemId);
        await _store.Items.SaveAsync(saved! with
        {
            Booth = saved.Booth with { Name = "また別の名前" },
        });

        await _service.RefreshAsync(ItemId);

        Assert.Single(_store.Notifications.Load());
    }

    /// <summary>
    /// 取り直す前の記録を、今の商品JSONと同じにしておく（1回取り直して、出た知らせを捨てる）。
    /// 試験の記録は名前しか持たないので、そのままだと価格・種類・説明文も「変わった」になり、1つの欄の動きが見えない
    /// </summary>
    private async Task SyncWithBoothAsync()
    {
        await SaveItemAsync(new LocalBlock { NotifyOnUpdate = true });
        await _service.RefreshAsync(ItemId);
        await _store.Notifications.UpdateAsync(_ => []);
    }

    /// <summary>
    /// 未読のうちに次の変化が来たら、前の知らせに重ねる（ユーザ判断 2026-10-02「重ねましょう」）。
    /// 前は差し替えていて、1回目の差（商品名が変わった）が消えていた。作った日時は最初のまま、重ねた日時を足す
    /// </summary>
    [Fact]
    public async Task StacksAChangeOntoTheUnreadEntryKeepingTheFirstChange()
    {
        await SyncWithBoothAsync();
        _itemJsonBody = ItemJson.Replace("真・アバターペンシステム", "新しい名前");
        await _service.RefreshAsync(ItemId);
        var first = Assert.Single(_store.Notifications.Load());

        // 2回目は価格だけが変わる（商品名は1回目の後と同じ）
        _itemJsonBody = _itemJsonBody.Replace("¥ 2,500", "¥ 3,000");
        await _service.RefreshAsync(ItemId);

        var entry = Assert.Single(_store.Notifications.Load());
        Assert.Equal(first.CreatedAt, entry.CreatedAt);
        Assert.NotNull(entry.UpdatedAt);
        Assert.Equal([BoothChanges.NameField, BoothChanges.PriceField], entry.Diffs.Select(diff => diff.Field));
        Assert.Equal(("真・アバターペンシステム", "新しい名前"), (entry.Diffs[0].Before, entry.Diffs[0].After));
        Assert.Equal(("¥ 2,500", "¥ 3,000"), (entry.Diffs[1].Before, entry.Diffs[1].After));
        Assert.Equal("商品名 真・アバターペンシステム → 新しい名前 / 価格 ¥ 2,500 → ¥ 3,000", entry.Detail);
    }

    /// <summary>既読にした後の変化は、今までどおり新しい知らせになる（読んだ物に足すと、新しく変わったことに気付けない）。</summary>
    [Fact]
    public async Task StartsANewEntryOnceTheOldOneWasRead()
    {
        await SyncWithBoothAsync();
        _itemJsonBody = ItemJson.Replace("真・アバターペンシステム", "新しい名前");
        await _service.RefreshAsync(ItemId);
        await _store.Notifications.UpdateAsync(list => list.Select(entry => entry with { IsRead = true }).ToList());

        _itemJsonBody = _itemJsonBody.Replace("¥ 2,500", "¥ 3,000");
        await _service.RefreshAsync(ItemId);

        var notifications = _store.Notifications.Load();
        Assert.Equal(2, notifications.Count);
        var fresh = Assert.Single(notifications, entry => !entry.IsRead);
        Assert.Equal(BoothChanges.PriceField, Assert.Single(fresh.Diffs).Field);
        Assert.Null(fresh.UpdatedAt);
    }

    /// <summary>未読のうちに全部が元へ戻ったら（価格が上がって戻った）、知らせることが無いので知らせごと消す。</summary>
    [Fact]
    public async Task DropsTheUnreadEntryWhenEverythingWentBack()
    {
        await SyncWithBoothAsync();
        _itemJsonBody = ItemJson.Replace("¥ 2,500", "¥ 3,000");
        await _service.RefreshAsync(ItemId);
        Assert.Single(_store.Notifications.Load());

        _itemJsonBody = ItemJson;
        await _service.RefreshAsync(ItemId);

        Assert.Empty(_store.Notifications.Load());
    }

    /// <summary>
    /// **取り直しは画像を落とさない。**梯子の規則をここだけ破らないため。
    /// 増えた画像は ImageBacklog が拾い、人が押したときは呼び出し側が取りに行く。
    /// </summary>
    [Fact]
    public async Task LeavesTheImagesToTheLadder()
    {
        await SaveItemAsync(new LocalBlock());

        await _service.RefreshAsync(ItemId);

        // この商品JSONは images が空なので、そもそも取りに行く先が無い。
        // 商品ページのHTMLと商品JSONの2本だけで終わっていることを見る
        Assert.Equal(2, _requests.Count);
    }

    /// <summary>取得の最中に商品を消されたら、書かずに終わる。消したものが戻ってきてはいけない。</summary>
    [Fact]
    public async Task DoesNotRecreateAnItemDeletedDuringTheFetch()
    {
        await SaveItemAsync(new LocalBlock());

        _whileFetchingHtml = () =>
        {
            File.Delete(Path.Combine(_root, "items", ItemId + ".json"));
            return Task.CompletedTask;
        };

        await _service.RefreshAsync(ItemId);

        Assert.False(_store.Items.Exists(ItemId));
    }
}
