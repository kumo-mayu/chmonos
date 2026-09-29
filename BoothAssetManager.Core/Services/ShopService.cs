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

    /// <summary>BOOTH のショップの変わらない ID（取得した商品から）。星とメモの記録に、つなぎ直す手がかりとして控える。</summary>
    public string? Uuid { get; init; }

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

/// <summary>ショップの画像を取り直した結果。何が変わったかを画面に伝える。</summary>
public sealed record ShopImageRefresh
{
    public bool IconUpdated { get; init; }

    public bool BannerUpdated { get; init; }

    /// <summary>BOOTHにバナーが無かった。</summary>
    public bool BannerAbsent { get; init; }

    /// <summary>ページを読めなかった。何も判断していない。</summary>
    public bool Failed { get; init; }

    public string? IconPath { get; init; }

    public string? BannerPath { get; init; }
}

public interface IShopService
{
    Task<IReadOnlyList<ShopSummary>> LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopItem>> LoadItemsAsync(string subdomain, CancellationToken cancellationToken = default);

    /// <summary>読んである商品から集計する。JSONは読まない（画面は起動時に読んだ写しを渡す）。</summary>
    IReadOnlyList<ShopSummary> Summarize(IEnumerable<ItemRecord> items);

    /// <summary>読んである商品から、そのショップの商品を引く。JSONは読まない。</summary>
    IReadOnlyList<ShopItem> ItemsOf(IEnumerable<ItemRecord> items, string subdomain);

    Task<string?> EnsureBannerAsync(
        string subdomain,
        ImagePipeline images,
        CancellationToken cancellationToken = default);

    Task<ShopImageRefresh> RefreshImagesAsync(
        string subdomain,
        ImagePipeline images,
        CancellationToken cancellationToken = default);

    /// <summary>アイコンを取りに行く店を選ぶ（手元に無く、商品JSONにURLがある店）。</summary>
    IReadOnlyList<ShopSummary> ShopsNeedingIcons(IEnumerable<ShopSummary> shops);

    /// <summary>選んだ店のアイコンを順に取る。</summary>
    Task<int> SyncIconsAsync(
        IReadOnlyList<ShopSummary> shops,
        ImagePipeline images,
        Func<string, string, Task>? onFetched = null,
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
    private readonly Func<AppSettings> _currentSettings;
    private readonly Booth.IBoothClient? _client;

    public ShopService(DataStore store, AppSettings? settings = null, Booth.IBoothClient? client = null)
        : this(store, SettingsSource.Fixed(settings), client)
    {
    }

    /// <param name="currentSettings">使うたびに今の設定を返すもの（<see cref="SettingsSource"/>）。</param>
    public ShopService(DataStore store, Func<AppSettings> currentSettings, Booth.IBoothClient? client = null)
    {
        _store = store;
        _currentSettings = currentSettings;
        _client = client;
    }

    /// <summary>今の設定。**抱えずに毎回読む。**</summary>
    private AppSettings _settings => _currentSettings();

    public async Task<IReadOnlyList<ShopSummary>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return Summarize(loaded.Items);
    }

    /// <summary>
    /// 読んである商品から集計する。
    ///
    /// **全商品のJSONを読み直さない。**ショップ一覧は開くたびに全件を読み直していて、開くのが遅かった
    /// （ユーザ指摘 2026-09-12）。画面は、検索画面が起動時に読んだ写しを渡す。
    /// 知らせとバナーの記録は小さいので毎回読む。
    /// </summary>
    public IReadOnlyList<ShopSummary> Summarize(IEnumerable<ItemRecord> items)
    {
        var updatedIds = _store.Notifications.Load()
            .Where(record => !record.IsRead
                && !record.IsResolved
                && record.Kind == NotificationKind.ItemUpdated
                && record.ItemId is not null)
            .Select(record => record.ItemId!)
            .ToHashSet(StringComparer.Ordinal);

        var bannerRecords = _store.ShopBanners.Load();

        // アイコンとバナーは置き場を1回だけ列挙して引く。店ごとに探すと「店の数×置き場の中の数」で伸び、
        // 500店・1000枚で集計が 372ms かかっていた（ShopIconIndex）
        var icons = _store.Paths.ReadShopIconIndex();

        return items
            // 束ねる鍵はユーザが入れたショップも見る。**商品が非公開でも
            // ショップは見られる場合がある**ので、URLを貼れば本物のショップに正しく入る
            .Where(item => item.ShopSubdomain is not null)
            .GroupBy(item => item.ShopSubdomain!, StringComparer.OrdinalIgnoreCase)
            .Select(group => Summarize(group, updatedIds, bannerRecords, icons))
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
        return ItemsOf(loaded.Items, subdomain);
    }

    /// <summary>読んである商品から、そのショップの商品を引く。JSONは読まない（<see cref="Summarize"/> と同じ理由）。</summary>
    public IReadOnlyList<ShopItem> ItemsOf(IEnumerable<ItemRecord> items, string subdomain)
    {
        return items
            .Where(item => item.ShopSubdomain is not null
                && string.Equals(item.ShopSubdomain, subdomain, StringComparison.OrdinalIgnoreCase)
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
            .ThenBy(entry => entry.Item.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>
    /// そのショップの商品のうち、件数から外しているものの数（非表示・R-18を出さない設定）。
    /// ショップの画面が空になったときに、なぜ無いのかと戻し方を言うため（動線の点検 D8）。
    /// 非表示でR-18のものは非表示の方に数える（戻すのは非表示が先）
    /// </summary>
    public (int Hidden, int Adult) ExcludedOf(IEnumerable<ItemRecord> items, string subdomain)
    {
        var hidden = 0;
        var adult = 0;
        foreach (var item in items)
        {
            if (item.ShopSubdomain is null
                || !string.Equals(item.ShopSubdomain, subdomain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.Local.IsHidden)
            {
                hidden++;
            }
            else if (!_settings.ShowAdult && item.Booth.IsAdult)
            {
                adult++;
            }
        }

        return (hidden, adult);
    }

    private ShopSummary Summarize(
        IGrouping<string, ItemRecord> group,
        IReadOnlySet<string> updatedIds,
        IReadOnlyList<ShopBannerRecord> bannerRecords,
        ShopIconIndex icons)
    {
        // 名前は最後に取得したものを採る。改名されたら新しい方に寄せたい。
        // BOOTHから取れていない商品だけのショップは、ユーザが入れた名前しか無い
        var observed = group
            .Where(item => item.Booth.Shop is not null)
            .OrderByDescending(item => item.Booth.FetchedAt)
            .Select(item => item.Booth.Shop!)
            .FirstOrDefault();

        var subdomain = group.Key;
        var name = observed?.Name
            ?? group.Select(item => item.ShopName).FirstOrDefault(text => text is { Length: > 0 })
            ?? subdomain;

        var counted = group.Where(IsCounted).ToList();
        var owned = counted.Where(IsOwned).ToList();

        // 入手日は商品ページと同じ求め方をする。ここだけ手入力に限ると、
        // 商品ページには日付が出ているのに一覧では空、という食い違いが起きる
        var latest = owned
            .Select(AcquiredDateResolver.Resolve)
            .Where(acquired => acquired.HasValue)
            .OrderByDescending(acquired => acquired.Value)
            .FirstOrDefault();

        var hasBanner = icons.HasBanner(subdomain);

        return new ShopSummary
        {
            Subdomain = subdomain,
            Name = name,
            Uuid = observed?.Uuid,
            Url = observed?.Url,
            ThumbnailUrl = observed?.ThumbnailUrl,
            IconPath = icons.FindIcon(subdomain),
            BannerPath = hasBanner ? _store.Paths.ShopBannerFile(subdomain) : null,
            BannerState = BannerStateOf(subdomain, hasBanner, bannerRecords),
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
    private ShopBannerState BannerStateOf(string subdomain, bool hasBanner, IReadOnlyList<ShopBannerRecord> records)
    {
        if (hasBanner)
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

        // 通信をまたぐので、書くときは読み直す。古い写しを丸ごと書き戻すと、
        // 別のショップを開いて調べた記録が消えて、次に開いたときまた取りに行く
        await SaveBannerRecordAsync(
            subdomain,
            new ShopBannerRecord
            {
                Subdomain = subdomain,
                HasBanner = hasBanner,
                SourceUrl = sourceUrl ?? known?.SourceUrl,
                CheckedAt = DateTimeOffset.Now,
            },
            cancellationToken);

        // BOOTH側から消えていても、手元にあるものは消さずに出す（商品画像と同じ扱い）
        return local;
    }

    private Task SaveBannerRecordAsync(string subdomain, ShopBannerRecord record, CancellationToken cancellationToken)
        => _store.ShopBanners.UpdateAsync(
            records =>
            {
                records.RemoveAll(entry => string.Equals(entry.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));
                records.Add(record);
                return records;
            },
            cancellationToken);

    /// <summary>
    /// このショップの画像を今すぐ取り直す。
    ///
    /// 待つのをやめて確かめたいときのための入口（外部で更新を知ったときなど）。
    /// 使う手順は普段と同じで、違うのは「まだ期限じゃない」を無視する点だけ。
    ///
    /// ショップページにはバナーとアイコンの両方が載っているので、1回の取得で
    /// 両方を見る。アイコンのURLは普段は商品JSONから来るが、そちらはitemを
    /// 取り直すまで古いままなので、ここではページ側の値を使う。
    /// </summary>
    public async Task<ShopImageRefresh> RefreshImagesAsync(
        string subdomain,
        ImagePipeline images,
        CancellationToken cancellationToken = default)
    {
        // 手元だけのショップにはBOOTHのページが無い。叩いても404が返るだけ
        if (LocalShopKey.IsLocal(subdomain))
        {
            return new ShopImageRefresh { Failed = true };
        }

        if (_client is null)
        {
            return new ShopImageRefresh { Failed = true };
        }

        // バナーとアイコンの両方が見つかったら、そこで受信をやめる
        var result = await _client.GetTextUntilAsync(
            $"https://{subdomain}.booth.pm/items",
            html => BannerPattern.IsMatch(html) && IconPattern.IsMatch(html),
            cancellationToken: cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return new ShopImageRefresh { Failed = true };
        }

        var html = result.Value;
        var iconBefore = _store.Paths.FindShopIcon(subdomain);
        var bannerPath = _store.Paths.ShopBannerFile(subdomain);

        var records = _store.ShopBanners.Load();
        var known = records.FirstOrDefault(record =>
            string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

        var iconMatch = IconPattern.Match(html);
        if (iconMatch.Success)
        {
            // このパターンはURL全体に一致する
            await images.SyncShopIconAsync(
                subdomain,
                System.Net.WebUtility.HtmlDecode(iconMatch.Value),
                cancellationToken);
        }

        var bannerMatch = BannerPattern.Match(html);
        var bannerUrl = bannerMatch.Success
            ? System.Net.WebUtility.HtmlDecode(bannerMatch.Groups[1].Value)
            : null;

        // URLが同じなら中身も同じ（BOOTHのバナーは名前が乱数なので、差し替えれば必ずURLが変わる）。
        // 押されたからといって同じ絵をもう一度落とすのは、最大4MBの無駄になる
        var bannerChanged = bannerUrl is not null
            && (!File.Exists(bannerPath) || !string.Equals(known?.SourceUrl, bannerUrl, StringComparison.Ordinal));

        var bannerSaved = bannerChanged
            && await images.SyncShopBannerAsync(subdomain, bannerUrl!, cancellationToken);

        await SaveBannerRecordAsync(
            subdomain,
            new ShopBannerRecord
            {
                Subdomain = subdomain,
                HasBanner = bannerUrl is not null,
                SourceUrl = bannerUrl,
                CheckedAt = DateTimeOffset.Now,
            },
            cancellationToken);

        var iconAfter = _store.Paths.FindShopIcon(subdomain);

        return new ShopImageRefresh
        {
            IconUpdated = iconAfter is not null && !string.Equals(iconAfter, iconBefore, StringComparison.OrdinalIgnoreCase),
            BannerUpdated = bannerSaved,
            BannerAbsent = bannerUrl is null,
            IconPath = iconAfter,
            BannerPath = Existing(bannerPath),
        };
    }

    /// <summary>ショップのアイコン。ページ上は128x128で出ている。</summary>
    private static readonly System.Text.RegularExpressions.Regex IconPattern = new(
        "https://booth\\.pximg\\.net/c/[0-9x]+/users/[0-9]+/icon_image/[^\"'\\s]+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// ショップページからバナーのURLを拾う。
    ///
    /// ショップのルート（<c>https://{sub}.booth.pm/</c>）はCloudflareに弾かれるが、
    /// <c>/items</c> は普通に返る。
    /// </summary>
    private async Task<BannerLookup> FindBannerUrlAsync(string subdomain, CancellationToken cancellationToken)
    {
        // 手元だけのショップにはBOOTHのページが無い
        if (LocalShopKey.IsLocal(subdomain))
        {
            return new BannerLookup(BannerLookupStatus.Failed, null);
        }

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
        => await SyncIconsAsync(ShopsNeedingIcons(await LoadAsync(cancellationToken)), images, onFetched, cancellationToken);

    /// <summary>
    /// アイコンを取りに行く店（手元に無く、商品JSONにURLがある店）。
    /// **選ぶのは保存側で、画面は選んだ一覧を渡して取らせる**——画面がカードのアイコンを見て数えると、
    /// 見えていないカードまで絵を読み始める（行を単位にした仮想化の狙いが崩れる）
    /// </summary>
    public IReadOnlyList<ShopSummary> ShopsNeedingIcons(IEnumerable<ShopSummary> shops)
    {
        // 一度当たった店はしばらく休む（ユーザ判断 2026-09-21・G5）。
        // 間隔はバナーと同じ設定を使う——どちらも「ショップの画像を確かめ直す頻度」で、分ける理由が無い
        var since = DateTimeOffset.Now.AddDays(-Math.Max(1, _settings.ShopBannerRecheckDays));
        var checkedAt = _store.ShopBanners.Load()
            .Where(record => record.IconCheckedAt is not null)
            .ToDictionary(record => record.Subdomain, record => record.IconCheckedAt!.Value, StringComparer.OrdinalIgnoreCase);

        return shops
            .Where(shop => shop.IconPath is null && shop.ThumbnailUrl is not null)
            .Where(shop => !checkedAt.TryGetValue(shop.Subdomain, out var last) || last < since)
            .ToList();
    }

    /// <summary>アイコンを取りに行ったことを控える（取れても取れなくても）。</summary>
    private Task NoteIconCheckedAsync(string subdomain, CancellationToken cancellationToken)
        => _store.ShopBanners.UpdateAsync(
            records =>
            {
                var index = records.FindIndex(record =>
                    string.Equals(record.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

                if (index >= 0)
                {
                    records[index] = records[index] with { IconCheckedAt = DateTimeOffset.Now };
                }
                else
                {
                    records.Add(new ShopBannerRecord
                    {
                        Subdomain = subdomain,

                        // バナーはまだ調べていない。調べた日時を入れると、バナー探しの方まで休んでしまう
                        HasBanner = false,
                        CheckedAt = DateTimeOffset.MinValue,
                        IconCheckedAt = DateTimeOffset.Now,
                    });
                }

                return records;
            },
            cancellationToken);

    public async Task<int> SyncIconsAsync(
        IReadOnlyList<ShopSummary> shops,
        ImagePipeline images,
        Func<string, string, Task>? onFetched = null,
        CancellationToken cancellationToken = default)
    {
        var fetched = 0;

        foreach (var shop in shops)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var got = await images.SyncShopIconAsync(shop.Subdomain, shop.ThumbnailUrl, cancellationToken);

            // **取りに行ったことを記録する**（ユーザ判断 2026-09-21・G5）。
            // 記録が無かったので、取れない店へはショップ一覧を開くたびに何度でも取りに行っていた
            await NoteIconCheckedAsync(shop.Subdomain, cancellationToken);

            if (!got || _store.Paths.FindShopIcon(shop.Subdomain) is not { } path)
            {
                continue;
            }

            fetched++;

            if (onFetched is not null)
            {
                await onFetched(shop.Subdomain, path);
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
        => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0;

    /// <summary>
    /// 支出。貰い物は自分の支出ではないので除き、未入力は0として扱う。
    /// BOOTH側から消えたvariationも、払った事実は変わらないので含める。
    /// 贈答ぶんは自分用と混ぜず、統計で別に出す（ここは所持しているものの集計なので現れない）。
    /// </summary>
    private static int Spent(ItemRecord item) => Purchases.SelfSpendOf(item);

    /// <summary>同じ中身のファイルは1回だけ数える。複数箇所に置いていても容量は1つ分。</summary>
    private static long SizeOf(ItemRecord item)
        => item.Local.OwnedFiles
            .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
            .Sum(file => file.SizeBytes)
            + item.Local.LocalFolders.Sum(folder => folder.TotalBytes);
}
