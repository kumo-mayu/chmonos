using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// バナーについて分かっていること。
/// 画面が「場所を空けて待つ」か「最初から出さない」かを決めるために要る。
/// </summary>
public enum ShopBannerState
{
    /// <summary>まだ調べていない。取りに行くまで有無が分からない。</summary>
    Unknown,

    /// <summary>手元にある。</summary>
    Present,

    /// <summary>調べた結果、置いていなかった。</summary>
    Absent,
}

/// <summary>ショップ1件の集計。</summary>
public sealed record ShopSummary
{
    /// <summary>集計キー。ショップ名は変わり得るので、こちらを同一性に使う。</summary>
    public required string Subdomain { get; init; }

    public required string Name { get; init; }

    public string? Url { get; init; }

    public string? ThumbnailUrl { get; init; }

    /// <summary>ローカルに落としたアイコン。まだ無ければ null（頭文字のタイルで代える）。</summary>
    public string? IconPath { get; init; }

    /// <summary>ローカルに落としたバナー。まだ無ければ null。</summary>
    public string? BannerPath { get; init; }

    /// <summary>
    /// バナーの有無が分かっているか。分かっていない間だけ画面で場所を空けて待ち、
    /// 分かっている店では最初から正しい高さで開く（後から差し込まれて下にずれない）。
    /// </summary>
    public ShopBannerState BannerState { get; init; }

    /// <summary>ローカルに情報を持っている商品数（所持していないものも含む）。</summary>
    public required int KnownCount { get; init; }

    /// <summary>ファイルを持っている商品数。「所持」の定義は全画面で揃える。</summary>
    public required int OwnedCount { get; init; }

    /// <summary>支出。ギフトは除き、未入力は0として扱う。</summary>
    public required long SpentYen { get; init; }

    /// <summary>最後に入手した日。手入力が無いitemはファイルの日付で代える（商品ページと同じ扱い）。</summary>
    public DateOnly? LastAcquiredAt { get; init; }

    /// <summary>その日がファイルの日付から来ているか。内部の扱いを隠さないために持ち回る。</summary>
    public bool LastAcquiredIsFallback { get; init; }

    /// <summary>更新の知らせが未読で残っている商品数。</summary>
    public int UpdatedCount { get; init; }
}

/// <summary>ショップ画面に出す1商品。</summary>
public sealed record ShopItem
{
    public required ItemRecord Item { get; init; }

    public required bool IsOwned { get; init; }

    public required long SizeBytes { get; init; }

    public DateOnly? AcquiredAt { get; init; }

    public bool AcquiredIsFallback { get; init; }
}

public interface IShopService
{
    Task<IReadOnlyList<ShopSummary>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopItem>> LoadItemsAsync(string subdomain, CancellationToken cancellationToken = default);

    Task<string?> EnsureBannerAsync(
        string subdomain,
        ImagePipeline images,
        CancellationToken cancellationToken = default);

    Task<int> SyncMissingIconsAsync(
        ImagePipeline images,
        Func<string, string, Task>? onFetched = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// ショップ単位の集計。
///
/// 数え方は決定事項に合わせてある：
/// ・「所持」はローカルファイル（またはフォルダ登録）を1つ以上持つこと
/// ・非表示のitemはショップの件数から除く（統計には含めるが、それは統計側の話）
/// ・R-18は設定で表示を切っているときだけ件数から除く
/// ・支出はギフトを除いた購入価格の合計。BOOTHから消えたvariationも含める
///
/// 集計キーはサブドメイン。ショップ名は変わり得るので、名前で束ねると同じ店が割れる。
/// </summary>
public sealed class ShopService : IShopService
{
    private readonly DataStore _store;
    private readonly AppSettings _settings;
    private readonly Booth.IBoothClient? _client;

    public ShopService(DataStore store, AppSettings? settings = null, Booth.IBoothClient? client = null)
    {
        _store = store;
        _settings = settings ?? new AppSettings();
        _client = client;
    }

    public async Task<IReadOnlyList<ShopSummary>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        var updatedIds = _store.Notifications.Load()
            .Where(record => !record.IsRead
                && record.Kind == NotificationKind.ItemUpdated
                && record.ItemId is not null)
            .Select(record => record.ItemId!)
            .ToHashSet(StringComparer.Ordinal);

        var bannerRecords = _store.ShopBanners.Load();

        return loaded.Items
            .Where(item => item.Booth.Shop is not null)
            .GroupBy(item => item.Booth.Shop!.Subdomain, StringComparer.OrdinalIgnoreCase)
            .Select(group => Summarize(group, updatedIds, bannerRecords))
            .OrderByDescending(shop => shop.OwnedCount)
            .ThenBy(shop => shop.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// そのショップの商品を、ローカルに情報があるものだけ返す。
    /// BOOTHの全商品を出すわけではない（持っていないものは取りに行っていない）。
    /// </summary>
    public async Task<IReadOnlyList<ShopItem>> LoadItemsAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);

        return loaded.Items
            .Where(item => item.Booth.Shop is not null
                && string.Equals(item.Booth.Shop.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase)
                && IsCounted(item))
            .Select(item =>
            {
                var acquired = AcquiredDateResolver.Resolve(item);

                return new ShopItem
                {
                    Item = item,
                    IsOwned = IsOwned(item),
                    SizeBytes = SizeOf(item),
                    AcquiredAt = acquired.Value,
                    AcquiredIsFallback = acquired.IsFallback,
                };
            })
            .OrderByDescending(entry => entry.AcquiredAt ?? DateOnly.MinValue)
            .ThenBy(entry => entry.Item.Booth.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    private ShopSummary Summarize(
        IGrouping<string, ItemRecord> group,
        IReadOnlySet<string> updatedIds,
        IReadOnlyList<ShopBannerRecord> bannerRecords)
    {
        // 名前は最後に取得したものを採る。改名されたら新しい方に寄せたい
        var shop = group
            .OrderByDescending(item => item.Booth.FetchedAt)
            .Select(item => item.Booth.Shop!)
            .First();

        var counted = group.Where(IsCounted).ToList();
        var owned = counted.Where(IsOwned).ToList();

        // 入手日は商品ページと同じ求め方をする。ここだけ手入力に限ると、
        // 商品ページには日付が出ているのに一覧では空、という食い違いが起きる
        var latest = owned
            .Select(AcquiredDateResolver.Resolve)
            .Where(acquired => acquired.HasValue)
            .OrderByDescending(acquired => acquired.Value)
            .FirstOrDefault();

        return new ShopSummary
        {
            Subdomain = shop.Subdomain,
            Name = shop.Name,
            Url = shop.Url,
            ThumbnailUrl = shop.ThumbnailUrl,
            IconPath = Existing(_store.Paths.ShopIconFile(shop.Subdomain, shop.ThumbnailUrl)),
            BannerPath = Existing(_store.Paths.ShopBannerFile(shop.Subdomain)),
            BannerState = BannerStateOf(shop.Subdomain, bannerRecords),
            KnownCount = counted.Count,
            OwnedCount = owned.Count,
            SpentYen = owned.Sum(item => (long)Spent(item)),
            LastAcquiredAt = latest.Value,
            LastAcquiredIsFallback = latest.IsFallback,
            UpdatedCount = counted.Count(item => updatedIds.Contains(item.Id)),
        };
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    /// <summary>
    /// バナーの有無が分かっているか。
    ///
    /// 手元にあれば Present。調べて無かった記録があり、まだ確かめ直す時期でなければ Absent。
    /// 記録が無いか、確かめ直す時期が来ているなら Unknown
    /// （見に行った結果バナーが現れることがあるので、答えが変わり得る間は「分からない」扱いにする）。
    /// </summary>
    private ShopBannerState BannerStateOf(string subdomain, IReadOnlyList<ShopBannerRecord> records)
    {
        if (File.Exists(_store.Paths.ShopBannerFile(subdomain)))
        {
            return ShopBannerState.Present;
        }

        var known = records.FirstOrDefault(record =>
            string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

        if (known is null)
        {
            return ShopBannerState.Unknown;
        }

        var recheckAfter = TimeSpan.FromDays(Math.Max(1, _settings.ShopBannerRecheckDays));
        return DateTimeOffset.Now - known.CheckedAt < recheckAfter
            ? ShopBannerState.Absent
            : ShopBannerState.Unknown;
    }

    /// <summary>
    /// そのショップのバナーを用意する。既にあれば何もしない。
    ///
    /// バナーのURLはショップページのHTMLにしか無く、ファイル名は乱数（UUID v4）なので
    /// 商品の情報からは導けない。ページは100KB超あるが、目当ての要素は先頭付近にあるので
    /// 見つかった時点で受信を打ち切る。
    ///
    /// バナーを置いていないショップもある（実測で10店中2店）。それを確かめるには
    /// 最後まで読むしかないので、結果を記録して二度は探しに行かない。
    /// </summary>
    /// <returns>用意できたバナーのパス。バナーが無い／取れなかったときは null。</returns>
    public async Task<string?> EnsureBannerAsync(
        string subdomain,
        ImagePipeline images,
        CancellationToken cancellationToken = default)
    {
        var path = _store.Paths.ShopBannerFile(subdomain);
        var local = Existing(path);

        var records = _store.ShopBanners.Load();
        var known = records.FirstOrDefault(record =>
            string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

        // 確かめてから日が浅いうちは見に行かない。ページ1枚ぶんの通信が要るため
        var recheckAfter = TimeSpan.FromDays(Math.Max(1, _settings.ShopBannerRecheckDays));
        if (known is not null && DateTimeOffset.Now - known.CheckedAt < recheckAfter)
        {
            return local;
        }

        var lookup = await FindBannerUrlAsync(subdomain, cancellationToken);

        // 通信に失敗しただけのときは何も決めない。ここで「バナー無し」と記録すると、
        // たまたま繋がらなかった一回のせいで、次に確かめるまで出なくなってしまう
        if (lookup.Status == BannerLookupStatus.Failed)
        {
            return local;
        }

        var sourceUrl = lookup.Url;
        var hasBanner = lookup.Status == BannerLookupStatus.Found;

        // 同じURLのものを既に持っていれば、落とし直さずに日付だけ更新する
        var alreadyHave = hasBanner
            && local is not null
            && string.Equals(known?.SourceUrl, sourceUrl, StringComparison.Ordinal);

        if (hasBanner && !alreadyHave)
        {
            if (await images.SyncShopBannerAsync(subdomain, sourceUrl!, cancellationToken))
            {
                local = path;
            }
            else
            {
                // 画像だけ取れなかった。次に開いたときにやり直せるよう、記録は残さない
                return local;
            }
        }

        records.RemoveAll(record => string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));
        records.Add(new ShopBannerRecord
        {
            Subdomain = subdomain,
            HasBanner = hasBanner,
            SourceUrl = sourceUrl ?? known?.SourceUrl,
            CheckedAt = DateTimeOffset.Now,
        });

        await _store.ShopBanners.SaveAsync(records, cancellationToken);

        // BOOTH側から消えていても、手元にあるものは消さずに出す（商品画像と同じ扱い）
        return local;
    }

    /// <summary>
    /// ショップページからバナーのURLを拾う。
    ///
    /// ショップのルート（<c>https://{sub}.booth.pm/</c>）はCloudflareに弾かれるが、
    /// <c>/items</c> は普通に返る。
    /// </summary>
    private async Task<BannerLookup> FindBannerUrlAsync(string subdomain, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return new BannerLookup(BannerLookupStatus.Failed, null);
        }

        var result = await _client.GetTextUntilAsync(
            $"https://{subdomain}.booth.pm/items",
            html => BannerPattern.IsMatch(html),
            cancellationToken: cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return new BannerLookup(BannerLookupStatus.Failed, null);
        }

        var match = BannerPattern.Match(result.Value);

        // ページは読めた。バナーが見当たらなければ「置いていない」と判断してよい
        return match.Success
            ? new BannerLookup(BannerLookupStatus.Found, System.Net.WebUtility.HtmlDecode(match.Groups[1].Value))
            : new BannerLookup(BannerLookupStatus.NotPresent, null);
    }

    /// <summary>
    /// バナー探しの結果。「置いていない」と「見に行けなかった」を分ける。
    /// 一緒くたにすると、繋がらなかっただけのショップを「バナー無し」と覚えてしまう。
    /// </summary>
    private enum BannerLookupStatus
    {
        Found,
        NotPresent,
        Failed,
    }

    private readonly record struct BannerLookup(BannerLookupStatus Status, string? Url);

    /// <summary>ショップのヘッダ画像。classが先、srcが後という並びで出てくる。</summary>
    private static readonly System.Text.RegularExpressions.Regex BannerPattern = new(
        "<img[^>]*class=\"[^\"]*header-image[^\"]*\"[^>]*src=\"([^\"]+)\"",
        System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// まだ持っていないショップのアイコンを順に落とす。
    ///
    /// URLは商品JSONにしか入っておらず、既に取り込み済みのitemでは
    /// アイコンだけが抜けている。1件ごとに知らせるのは、全部揃うまで
    /// 画面を待たせずに、届いたものから差し替えたいため。
    /// </summary>
    public async Task<int> SyncMissingIconsAsync(
        ImagePipeline images,
        Func<string, string, Task>? onFetched = null,
        CancellationToken cancellationToken = default)
    {
        var shops = await LoadAsync(cancellationToken);
        var fetched = 0;

        foreach (var shop in shops.Where(entry => entry.IconPath is null && entry.ThumbnailUrl is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await images.SyncShopIconAsync(shop.Subdomain, shop.ThumbnailUrl, cancellationToken))
            {
                continue;
            }

            fetched++;

            if (onFetched is not null)
            {
                await onFetched(shop.Subdomain, _store.Paths.ShopIconFile(shop.Subdomain, shop.ThumbnailUrl));
            }
        }

        return fetched;
    }

    /// <summary>
    /// 件数に数えるか。非表示は外し、R-18は設定で表示を切っているときだけ外す。
    /// 「持っていないことにする」ではないので、統計側では別の判断になる。
    /// </summary>
    private bool IsCounted(ItemRecord item)
        => !item.Local.IsHidden && (_settings.ShowAdult || !item.Booth.IsAdult);

    /// <summary>所持＝ローカルにファイルかフォルダを持っている。全画面で同じ定義を使う。</summary>
    private static bool IsOwned(ItemRecord item)
        => item.Local.LocalFiles.Count > 0 || item.Local.LocalFolders.Count > 0;

    /// <summary>
    /// 支出。ギフトは自分の支出ではないので除き、未入力は0として扱う。
    /// BOOTH側から消えたvariationも、払った事実は変わらないので含める。
    /// </summary>
    private static int Spent(ItemRecord item)
        => item.Local.OrderedVariations
            .Where(variation => !variation.IsGifted)
            .Sum(variation => variation.Price ?? 0);

    /// <summary>同じ中身のファイルは1回だけ数える。複数箇所に置いていても容量は1つ分。</summary>
    private static long SizeOf(ItemRecord item)
        => item.Local.LocalFiles
            .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
            .Sum(file => file.SizeBytes)
            + item.Local.LocalFolders.Sum(folder => folder.TotalBytes);
}
