using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>時系列の1区切り（月または年）。金額が0の区切りも詰めずに残す。</summary>
public sealed record StatsPeriod
{
    /// <summary>並べ替え用のキー。月なら <c>2026-04</c>、年なら <c>2026</c>。</summary>
    public required string Key { get; init; }

    /// <summary>軸に出す短い表記。</summary>
    public required string Label { get; init; }

    public required long SpentYen { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>支出で並べる棒1本（ショップ）。</summary>
public sealed record StatsSpendBar
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required long SpentYen { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>容量で並べる棒1本（カテゴリ）。</summary>
public sealed record StatsSizeBar
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required long Bytes { get; init; }

    public required int ItemCount { get; init; }
}

/// <summary>件数で並べる棒1本（アバター）。</summary>
public sealed record StatsCountBar
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required int ItemCount { get; init; }

    /// <summary>「対応アバター未設定」のような、集計の外側を示す行。控えめに出す。</summary>
    public bool IsResidual { get; init; }
}

/// <summary>
/// 積み残し。ここだけは「所持しているもの」ではなく「手を付ける必要があるもの」を数えるので、
/// 上のタイル群とは対象範囲が違う（ファイルを持たないitemも編集の対象になる）。
/// </summary>
public sealed record StatsBacklog
{
    public required int UnresolvedCount { get; init; }

    public required int NeedsUserTagCount { get; init; }

    public required int MissingFileCount { get; init; }
}

/// <summary>件数で並べる汎用の区切り（価格帯・スキ数・属性の分布）。</summary>
public sealed record StatsBucket
{
    public required string Label { get; init; }

    public required int Count { get; init; }
}

/// <summary>買った時と今で価格が変わった商品1件。</summary>
public sealed record StatsPriceChange
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required int PaidYen { get; init; }

    public required int CurrentYen { get; init; }

    public int DiffYen => CurrentYen - PaidYen;
}

/// <summary>容量の大きい商品1件。</summary>
public sealed record StatsHeavyItem
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required long Bytes { get; init; }
}

/// <summary>月ごとのカテゴリ構成。買うものの移り変わりを見る。</summary>
public sealed record StatsMonthlyCategory
{
    public required string Label { get; init; }

    public required IReadOnlyDictionary<string, int> Counts { get; init; }

    public required int Total { get; init; }
}

/// <summary>属性1軸の分布。</summary>
public sealed record StatsAttributeDistribution
{
    public required string Name { get; init; }

    /// <summary>評価済みのitem数。未評価は0ではなく「値が無い」として除く。</summary>
    public required int Rated { get; init; }

    public required double Average { get; init; }

    /// <summary>0-19 / 20-39 / 40-59 / 60-79 / 80-100 の5区切り。</summary>
    public required IReadOnlyList<int> Buckets { get; init; }
}

/// <summary>属性2軸の相関。</summary>
public sealed record StatsAttributeCorrelation
{
    public required string A { get; init; }

    public required string B { get; init; }

    /// <summary>両方を評価済みのitem数。</summary>
    public required int Count { get; init; }

    /// <summary>ピアソンの相関係数（-1〜1）。</summary>
    public required double R { get; init; }
}

/// <summary>統計画面に出すもの一式。1回の走査で全部作る。</summary>
public sealed record StatsSnapshot
{
    /// <summary>ファイルかフォルダを持っているitem数。統計の集計対象そのもの。</summary>
    public required int OwnedCount { get; init; }

    /// <summary>ローカルに情報だけあるものも含めた総数。</summary>
    public required int KnownCount { get; init; }

    public required long SpentYen { get; init; }

    /// <summary>ギフトで貰ったvariationの数。支出には入れない。</summary>
    public required int GiftedCount { get; init; }

    /// <summary>
    /// 人に贈った回数と、そのために払った額。
    ///
    /// 集計対象は「ファイルを持つitem」と決めてあるが、贈った商品は手元にファイルが来ないので
    /// そこから外れる。よってここだけは全itemから数える。<see cref="SpentYen"/> と混ぜると
    /// 集計対象の定義が壊れるので、画面でも別のタイルに出す。
    ///
    /// 同じ商品を3人に贈れば3回・3回ぶんの額になる。
    /// </summary>
    public required int GivenCount { get; init; }

    public required long GivenSpentYen { get; init; }

    /// <summary>0円のvariationの数。支出には入るが0なので、別に数えて内訳が読めるようにする。</summary>
    public required int FreeCount { get; init; }

    /// <summary>
    /// 有償の購入記録が1件も無いitem数。
    /// 金額に入っていない分があることを隠さないために持つ。
    /// </summary>
    public required int UnpricedItemCount { get; init; }

    /// <summary>同じ中身を1回だけ数えた容量。「持っているアセットの大きさ」。</summary>
    public required long LogicalBytes { get; init; }

    /// <summary>重複コピーを含む、実際にドライブを占有している量。</summary>
    public required long PhysicalBytes { get; init; }

    /// <summary>うち重複コピーの分。<see cref="PhysicalBytes"/> と <see cref="LogicalBytes"/> の差。</summary>
    public long DuplicateBytes => PhysicalBytes - LogicalBytes;

    /// <summary>
    /// 重複の中身。空く量の大きい順。
    ///
    /// **合計だけでは触る場所が分からない。**<see cref="DuplicateBytes"/> は
    /// 「どれだけ重複しているか」しか言えず、
    /// しかも1商品の中しか見ていないので商品またぎを取り逃していた。
    /// </summary>
    public IReadOnlyList<DuplicateGroup> Duplicates { get; init; } = [];

    /// <summary>1つ残して他を消したら空く合計。</summary>
    public long ReclaimableBytes => Duplicates.Sum(group => group.ReclaimableBytes);

    public required int ShopCount { get; init; }

    /// <summary>月別。買っていない月も0として残す（間が空いたことも情報のため）。</summary>
    public required IReadOnlyList<StatsPeriod> Months { get; init; }

    public required IReadOnlyList<StatsPeriod> Years { get; init; }

    /// <summary>入手日が分からず、月別に入れられなかった分。</summary>
    public required long UndatedSpentYen { get; init; }

    public required int UndatedCount { get; init; }

    /// <summary>入手日をファイルの日付で代えたitem数。推定がどれだけ混ざっているかを示す。</summary>
    public required int FallbackDatedCount { get; init; }

    public required IReadOnlyList<StatsSpendBar> Shops { get; init; }

    public required IReadOnlyList<StatsSizeBar> Categories { get; init; }

    public required IReadOnlyList<StatsCountBar> Avatars { get; init; }

    /// <summary>アバターの紐付けが1件も無いか。無ければ画面は誘導だけを出す。</summary>
    public required bool HasAnyAvatarLink { get; init; }

    public required StatsBacklog Backlog { get; init; }

    // ── ここから下は「選んで出す」もの。どれも同じ走査で作る ──

    /// <summary>買った時より高くなった／安くなった商品。BOOTHに現存するvariationだけを比べる。</summary>
    public IReadOnlyList<StatsPriceChange> PriceChanges { get; init; } = [];

    /// <summary>価格帯ごとの点数。</summary>
    public IReadOnlyList<StatsBucket> PriceBuckets { get; init; } = [];

    /// <summary>0円で手に入れたitem数（ギフトは別に数える）。</summary>
    public int FreeItemCount { get; init; }

    /// <summary>ギフトで貰ったitem数と、その定価の合計（自分は払っていない）。</summary>
    public int GiftedItemCount { get; init; }

    public long GiftedValueYen { get; init; }

    public IReadOnlyList<StatsSpendBar> CategorySpend { get; init; } = [];

    public IReadOnlyList<StatsSpendBar> UserTagSpend { get; init; } = [];

    /// <summary>月ごとのカテゴリ構成。買うものの移り変わり。</summary>
    public IReadOnlyList<StatsMonthlyCategory> MonthlyCategories { get; init; } = [];

    /// <summary>上の積み上げに使うカテゴリの並び（多い順）。</summary>
    public IReadOnlyList<string> CategoryOrder { get; init; } = [];

    public IReadOnlyList<StatsHeavyItem> HeavyItems { get; init; } = [];

    /// <summary>1点だけ買ったショップ数と、2点以上買ったショップ数。</summary>
    public int ShopsBoughtOnce { get; init; }

    public int ShopsBoughtMany { get; init; }

    /// <summary>点数の多いショップ（支出順とは顔ぶれが変わる）。</summary>
    public IReadOnlyList<StatsSpendBar> ShopsByCount { get; init; } = [];

    /// <summary>カテゴリ・userTagの点数構成。</summary>
    public IReadOnlyList<StatsCountBar> CategoryCounts { get; init; } = [];

    public IReadOnlyList<StatsCountBar> UserTagCounts { get; init; } = [];

    public IReadOnlyList<StatsAttributeDistribution> AttributeDistributions { get; init; } = [];

    public IReadOnlyList<StatsAttributeCorrelation> AttributeCorrelations { get; init; } = [];

    /// <summary>相関を出すのに必要な最低件数。これに満たない組は出さない。</summary>
    public int CorrelationMinimum { get; init; }

    /// <summary>所有アバターごとに着られる所持商品の数。直接対応と素体経由を分ける。</summary>
    public IReadOnlyList<StatsAvatarWearable> Wearables { get; init; } = [];

    /// <summary>所持しているのにuserTagを付けていないitem数。いわゆる積み。</summary>
    public int UnsortedOwnedCount { get; init; }

    public int HiddenCount { get; init; }

    /// <summary>販売終了・売り切れの点数と、それに払った額。手元にしか無いもの。</summary>
    public int EndOfSaleCount { get; init; }

    public int SoldOutCount { get; init; }

    public long EndOfSaleSpentYen { get; init; }

    /// <summary>
    /// BOOTHに無い商品として登録したものの点数と、それに払った額。
    ///
    /// **販売終了と同じ行に混ぜない。**販売終了は「BOOTHの価格が観測できていた商品」で、
    /// こちらは「ユーザが入れた額しかない商品」——金額の意味が違う。
    /// 「集計できない分は別枠に出す」がそのまま当てはまる。
    /// </summary>
    public int LocalOnlyCount { get; init; }

    public long LocalOnlySpentYen { get; init; }

    /// <summary>スキ数の分布。人気商品を買うか、ニッチを掘るか。</summary>
    public IReadOnlyList<StatsBucket> WishBuckets { get; init; } = [];
}

/// <summary>所有アバター1体について、着られる所持商品の数。</summary>
public sealed record StatsAvatarWearable
{
    public required string AvatarItemId { get; init; }

    public required string Name { get; init; }

    public required int DirectCount { get; init; }

    public required int ViaBaseCount { get; init; }

    public int Total => DirectCount + ViaBaseCount;
}

public interface IStatsService
{
    Task<StatsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// ライブラリ全体の集計。
///
/// 数え方は決定事項に従う：
/// ・集計対象はファイル（またはフォルダ）を持つitemだけ。持っていないものは資産ではない
/// ・非表示のitemも含める。非表示は見せ方の操作であって「持っていないことにする」ではない
/// ・R-18も金額・容量に含める。表示設定で総額が変わると資産の把握に使えなくなる
/// ・支出はギフトを除いた購入価格の合計。BOOTHから消えたvariationも含める
/// ・時系列の軸は入手日。手入力が無ければファイルの日付で代え、代えた件数を持ち回る
///
/// 積み残しだけは対象範囲が違う（<see cref="StatsBacklog"/> のコメント参照）。
/// </summary>
public sealed class StatsService : IStatsService
{
    private readonly DataStore _store;

    public StatsService(DataStore store)
    {
        _store = store;
    }

    public async Task<StatsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        var registry = _store.Avatars.Load();
        // 統計の画面は画面のスレッドから開く。上の await の続きもそこへ戻るので、未確定の記録（件数に比例して大きい）は裏で読む
        var unresolved = (await _store.Unresolved.LoadAsync(cancellationToken)).Count;

        return Build(loaded.Items, registry, unresolved);
    }

    /// <summary>実際の集計。ファイル読み込みから切り離してあるのでテストから直に呼べる。</summary>
    public static StatsSnapshot Build(
        IReadOnlyList<ItemRecord> items,
        AvatarRegistry registry,
        int unresolvedCount)
    {
        var owned = items.Where(IsOwned).ToList();

        var spent = 0L;
        var gifted = 0;
        var free = 0;
        var unpriced = 0;
        var logical = 0L;
        var physical = 0L;
        var undatedSpent = 0L;
        var undated = 0;
        var fallbackDated = 0;
        var withoutAvatar = 0;

        var months = new Dictionary<string, (long Spent, int Count)>(StringComparer.Ordinal);
        var years = new Dictionary<string, (long Spent, int Count)>(StringComparer.Ordinal);
        var shops = new Dictionary<string, (string Name, long Spent, int Count)>(StringComparer.OrdinalIgnoreCase);
        var categories = new Dictionary<string, (long Bytes, int Count)>(StringComparer.CurrentCulture);
        var avatars = new Dictionary<string, int>(StringComparer.Ordinal);

        // 素体経由の展開は相性の索引に任せる（検索・商品ページと同じ規則で数えるため）
        var compatibility = AvatarCompatibilityIndex.Build(registry);

        foreach (var item in owned)
        {
            var itemSpent = (long)Purchases.SelfSpendOf(item);

            spent += itemSpent;
            gifted += Purchases.ReceivedCountOf(item);
            free += Purchases.FreeCountOf(item);

            // 「払ったはずだが記録が無い」を数える。金額の欠けを黙って0で埋めない
            if (!Purchases.HasPricedSelfPurchase(item))
            {
                unpriced++;
            }

            var itemPhysical = PhysicalSizeOf(item);
            logical += LogicalSizeOf(item);
            physical += itemPhysical;

            var acquired = AcquiredDateResolver.Resolve(item);
            if (acquired.Value is { } date)
            {
                if (acquired.IsFallback)
                {
                    fallbackDated++;
                }

                Add(months, $"{date.Year:D4}-{date.Month:D2}", itemSpent);
                Add(years, $"{date.Year:D4}", itemSpent);
            }
            else
            {
                undatedSpent += itemSpent;
                undated++;
            }

            if (item.ShopSubdomain is { } subdomain)
            {
                var shopName = item.ShopName ?? subdomain;
                var current = shops.TryGetValue(subdomain, out var existing)
                    ? existing
                    : (Name: shopName, Spent: 0L, Count: 0);
                shops[subdomain] = (shopName, current.Spent + itemSpent, current.Count + 1);
            }

            var category = string.IsNullOrWhiteSpace(item.CategoryName)
                ? "分類なし"
                : item.CategoryName!;
            var bucket = categories.TryGetValue(category, out var size) ? size : (Bytes: 0L, Count: 0);
            categories[category] = (bucket.Bytes + itemPhysical, bucket.Count + 1);

            CountAvatars(item, compatibility, avatars, ref withoutAvatar);
        }

        var avatarBars = BuildAvatarBars(owned, registry, avatars, withoutAvatar);

        var snapshot = new StatsSnapshot
        {
            OwnedCount = owned.Count,
            KnownCount = items.Count,
            SpentYen = spent,
            GiftedCount = gifted,
            GivenCount = items.Sum(Purchases.GivenCountOf),
            GivenSpentYen = items.Sum(item => (long)Purchases.GivenSpendOf(item)),
            FreeCount = free,
            UnpricedItemCount = unpriced,
            LogicalBytes = logical,
            PhysicalBytes = physical,

            // 所持しているものだけを見る。手元に無いものは容量を食っていない
            Duplicates = DuplicateFinder.Find(owned),
            ShopCount = shops.Count,
            Months = FillMonths(months),
            Years = FillYears(years),
            UndatedSpentYen = undatedSpent,
            UndatedCount = undated,
            FallbackDatedCount = fallbackDated,
            Shops = shops
                .OrderByDescending(pair => pair.Value.Spent)
                .ThenByDescending(pair => pair.Value.Count)
                .Select(pair => new StatsSpendBar
                {
                    Key = pair.Key,
                    Label = pair.Value.Name,
                    SpentYen = pair.Value.Spent,
                    ItemCount = pair.Value.Count,
                })
                .ToList(),
            Categories = categories
                .OrderByDescending(pair => pair.Value.Bytes)
                .Select(pair => new StatsSizeBar
                {
                    Key = pair.Key,
                    Label = pair.Key,
                    Bytes = pair.Value.Bytes,
                    ItemCount = pair.Value.Count,
                })
                .ToList(),
            Avatars = avatarBars,
            HasAnyAvatarLink = avatars.Count > 0,
            Backlog = new StatsBacklog
            {
                UnresolvedCount = unresolvedCount,
                NeedsUserTagCount = items.Count(item => item.Local.UserTags.Count == 0),
                MissingFileCount = items.Count(item =>
                    item.Local.OwnedFiles.Any(file => file.Paths.Count == 0)),
            },
        };

        // 選んで出す項目は同じ走査結果から組み立てる
        return StatsExtras.Enrich(snapshot, items, owned, registry);
    }

    /// <summary>
    /// アバターの棒を作る。表示名は登録簿を正とし、無ければitem側のキャッシュ、
    /// それも無ければ商品IDをそのまま出す（消えた名前を勝手に埋めない）。
    /// </summary>
    private static List<StatsCountBar> BuildAvatarBars(
        IReadOnlyList<ItemRecord> owned,
        AvatarRegistry registry,
        Dictionary<string, int> counts,
        int withoutAvatar)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in registry.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.DisplayName)))
        {
            names[entry.ItemId] = entry.DisplayName!;
        }

        foreach (var link in owned.SelectMany(item => item.Local.Avatars))
        {
            if (!names.ContainsKey(link.AvatarItemId) && !string.IsNullOrWhiteSpace(link.Name))
            {
                names[link.AvatarItemId] = link.Name!;
            }
        }

        var bars = counts
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new StatsCountBar
            {
                Key = pair.Key,
                Label = names.TryGetValue(pair.Key, out var name) ? name : pair.Key,
                ItemCount = pair.Value,
            })
            .ToList();

        // 紐付けが1件も無いうちは「未設定」だけの棒を出しても読めないので、何かある時だけ添える
        if (withoutAvatar > 0 && bars.Count > 0)
        {
            bars.Add(new StatsCountBar
            {
                Key = string.Empty,
                Label = "対応アバター未設定",
                ItemCount = withoutAvatar,
                IsResidual = true,
            });
        }

        return bars;
    }

    /// <summary>
    /// このitemが対応しているアバターを数える。
    ///
    /// 同じアバターに複数の紐付けがあっても1件として数える（推定と手入力が重なることがある）。
    /// 素体に対応していれば、その素体を使っているアバターにも数える
    /// （素体向けの衣装は、その素体を使うアバターでも着られるため）。
    /// </summary>
    private static void CountAvatars(
        ItemRecord item,
        AvatarCompatibilityIndex compatibility,
        Dictionary<string, int> counts,
        ref int withoutAvatar)
    {
        var reached = compatibility.Resolve(item.Local);

        if (reached.Count == 0)
        {
            withoutAvatar++;
            return;
        }

        // 直接対応も素体経由も同じ1件として数える（見出しに「素体経由を含む」と断る）
        foreach (var id in reached.Keys)
        {
            counts[id] = counts.TryGetValue(id, out var current) ? current + 1 : 1;
        }
    }

    private static void Add(Dictionary<string, (long Spent, int Count)> buckets, string key, long spent)
    {
        var current = buckets.TryGetValue(key, out var existing) ? existing : (Spent: 0L, Count: 0);
        buckets[key] = (current.Spent + spent, current.Count + 1);
    }

    /// <summary>
    /// 最初の月から最後の月までを、買っていない月も含めて並べる。
    /// 買った月だけを詰めると、間が空いたことが見えなくなり時間軸として読めなくなる。
    /// </summary>
    private static IReadOnlyList<StatsPeriod> FillMonths(Dictionary<string, (long Spent, int Count)> buckets)
    {
        if (buckets.Count == 0)
        {
            return [];
        }

        var keys = buckets.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList();
        var start = ParseMonth(keys[0]);
        var end = ParseMonth(keys[^1]);

        var result = new List<StatsPeriod>();
        for (var cursor = start; cursor <= end; cursor = cursor.AddMonths(1))
        {
            var key = $"{cursor.Year:D4}-{cursor.Month:D2}";
            var value = buckets.TryGetValue(key, out var bucket) ? bucket : (Spent: 0L, Count: 0);

            result.Add(new StatsPeriod
            {
                Key = key,
                Label = $"{cursor.Month:D2}",
                SpentYen = value.Spent,
                ItemCount = value.Count,
            });
        }

        return result;
    }

    private static IReadOnlyList<StatsPeriod> FillYears(Dictionary<string, (long Spent, int Count)> buckets)
    {
        if (buckets.Count == 0)
        {
            return [];
        }

        var years = buckets.Keys.Select(int.Parse).ToList();

        var result = new List<StatsPeriod>();
        for (var year = years.Min(); year <= years.Max(); year++)
        {
            var value = buckets.TryGetValue($"{year:D4}", out var bucket) ? bucket : (Spent: 0L, Count: 0);

            result.Add(new StatsPeriod
            {
                Key = $"{year:D4}",
                Label = $"{year}",
                SpentYen = value.Spent,
                ItemCount = value.Count,
            });
        }

        return result;
    }

    private static DateOnly ParseMonth(string key)
        => new(int.Parse(key[..4]), int.Parse(key[5..]), 1);

    private static bool IsOwned(ItemRecord item)
        => item.Local.OwnedFiles.Count > 0 || item.Local.LocalFolders.Count > 0;

    /// <summary>同じ中身を1回だけ数えた大きさ。商品ページに出している容量と同じ求め方。</summary>
    private static long LogicalSizeOf(ItemRecord item)
        => item.Local.OwnedFiles
            .DistinctBy(file => file.Hash, StringComparer.OrdinalIgnoreCase)
            .Sum(file => file.SizeBytes)
            + item.Local.LocalFolders.Sum(folder => folder.TotalBytes);

    /// <summary>
    /// ドライブが実際に食われている量。同じ中身を2箇所に置いていれば2回分数える。
    /// 「どれだけ空けられるか」を知りたい時に要るのはこちら。
    /// </summary>
    private static long PhysicalSizeOf(ItemRecord item)
        => item.Local.OwnedFiles.Sum(file => file.SizeBytes * Math.Max(1, file.Paths.Count))
            + item.Local.LocalFolders.Sum(folder => folder.TotalBytes);
}
