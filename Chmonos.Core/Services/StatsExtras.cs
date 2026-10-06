using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 統計の追加集計。
///
/// 基本の集計（<see cref="StatsService"/>）と同じ走査結果を受け取って組み立てる。
/// 数え方の原則は同じで、対象はファイルを持つitemだけ、非表示とR-18も含める。
/// </summary>
public static class StatsExtras
{
    /// <summary>
    /// 相関を出すのに必要な最低件数。
    /// これを下回ると数字が暴れて、無いはずの関係が見えてしまう。
    /// </summary>
    public const int CorrelationMinimum = 10;

    private static readonly int[] PriceEdges = [0, 500, 1000, 2000, 3000, 5000, 10000];

    private static readonly int[] WishEdges = [0, 100, 500, 1000, 5000, 10000];

    public static StatsSnapshot Enrich(
        StatsSnapshot snapshot,
        IReadOnlyList<ItemRecord> allItems,
        IReadOnlyList<ItemRecord> owned,
        AvatarRegistry registry)
    {
        var compatibility = AvatarCompatibilityIndex.Build(registry);

        return snapshot with
        {
            PriceChanges = PriceChanges(owned),
            PriceBuckets = Buckets(owned.Select(SpentOf).Where(price => price > 0), PriceEdges, "¥"),
            FreeItemCount = owned.Count(item => Purchases.FreeCountOf(item) > 0 && SpentOf(item) == 0),
            GiftedItemCount = owned.Count(Purchases.WasReceived),
            GiftedValueYen = owned.Sum(item => item.Local.Purchases
                .Where(purchase => purchase.Kind == PurchaseKind.Received)
                .Sum(purchase => (long)(purchase.Price ?? 0))),
            CategorySpend = SpendBy(owned, CategoryOf),
            UserTagSpend = SpendByMany(owned, item => item.Local.UserTags.Select(tag => tag.Top)),
            CategoryCounts = CountBy(owned, CategoryOf),
            UserTagCounts = CountByMany(owned, item => item.Local.UserTags.Select(tag => tag.Top)),
            MonthlyCategories = MonthlyCategories(owned, out var order),
            CategoryOrder = order,
            HeavyItems = owned
                .Select(item => new StatsHeavyItem
                {
                    ItemId = item.Id,
                    Name = item.DisplayName,
                    Bytes = PhysicalSizeOf(item),
                })
                .Where(entry => entry.Bytes > 0)
                .OrderByDescending(entry => entry.Bytes)
                .Take(10)
                .ToList(),
            ShopsBoughtOnce = ShopCounts(owned).Count(pair => pair.Value == 1),
            ShopsBoughtMany = ShopCounts(owned).Count(pair => pair.Value > 1),
            ShopsByCount = ShopsByCount(owned),
            AttributeDistributions = Distributions(owned),
            AttributeCorrelations = Correlations(owned),
            CorrelationMinimum = CorrelationMinimum,
            Wearables = Wearables(owned, registry, compatibility),
            UnsortedOwnedCount = owned.Count(item => item.Local.UserTags.Count == 0),
            HiddenCount = allItems.Count(item => item.Local.IsHidden),
            EndOfSaleCount = owned.Count(item => item.Booth.IsEndOfSale),
            SoldOutCount = owned.Count(item => item.Booth.IsSoldOut && !item.Booth.IsEndOfSale),
            EndOfSaleSpentYen = owned.Where(item => item.Booth.IsEndOfSale).Sum(item => SpentOf(item)),
            LocalOnlyCount = owned.Count(item => item.IsLocalOnly),
            LocalOnlySpentYen = owned.Where(item => item.IsLocalOnly).Sum(item => SpentOf(item)),
            WishBuckets = Buckets(
                owned.Select(item => (long)item.Booth.WishListsCount).Where(count => count > 0), WishEdges, string.Empty),
        };
    }

    private static string CategoryOf(ItemRecord item)
        => string.IsNullOrWhiteSpace(item.CategoryName) ? "分類なし" : item.CategoryName!;

    private static long SpentOf(ItemRecord item) => Purchases.SelfSpendOf(item);

    /// <summary>実占有（在る場所だけ。<see cref="ItemRecord.ActualDiskBytes"/>・点検の7）。統計の合計と同じ答え。</summary>
    private static long PhysicalSizeOf(ItemRecord item) => item.ActualDiskBytes;

    /// <summary>
    /// 買った時の価格と今の価格を比べる。
    /// BOOTHに現存するvariationだけを突き合わせる（消えたものは比べようがない）。
    /// </summary>
    private static List<StatsPriceChange> PriceChanges(IReadOnlyList<ItemRecord> owned)
    {
        var result = new List<StatsPriceChange>();

        foreach (var item in owned)
        {
            var current = FirstWins.Map(item.Booth.Variations, v => v.Id, v => v.Price, EqualityComparer<long>.Default);
            var paid = 0L;
            var now = 0L;
            var matched = false;

            foreach (var record in item.Local.Purchases)
            {
                // バリエーションを指していない記録は比べられない。
                // 今いくらかを引く先が無いので、値上がりの判定から外す
                if (record.Kind != PurchaseKind.ForSelf || record.Price is not { } price
                    || record.VariationId is not { } variationId
                    || !current.TryGetValue(variationId, out var nowPrice))
                {
                    continue;
                }

                paid += price;
                now += nowPrice;
                matched = true;
            }

            if (matched && paid != now)
            {
                result.Add(new StatsPriceChange
                {
                    ItemId = item.Id,
                    Name = item.DisplayName,
                    PaidYen = paid,
                    CurrentYen = now,
                });
            }
        }

        return result.OrderByDescending(entry => Math.Abs(entry.DiffYen)).ToList();
    }

    private static List<StatsBucket> Buckets(IEnumerable<long> values, int[] edges, string prefix)
    {
        var list = values.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        var buckets = new List<StatsBucket>();

        for (var i = 0; i < edges.Length; i++)
        {
            var low = edges[i];
            var high = i + 1 < edges.Length ? edges[i + 1] : (int?)null;

            var count = high is { } upper
                ? list.Count(value => value >= low && value < upper)
                : list.Count(value => value >= low);

            buckets.Add(new StatsBucket
            {
                Label = high is { } shown ? $"{prefix}{low:N0}〜{prefix}{shown:N0}" : $"{prefix}{low:N0}〜",
                Count = count,
            });
        }

        return buckets;
    }

    private static Dictionary<string, int> ShopCounts(IReadOnlyList<ItemRecord> owned)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in owned)
        {
            if (item.ShopSubdomain is not { } subdomain)
            {
                continue;
            }

            counts[subdomain] = counts.TryGetValue(subdomain, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static List<StatsSpendBar> ShopsByCount(IReadOnlyList<ItemRecord> owned)
        => owned
            .Where(item => item.ShopSubdomain is not null)
            .GroupBy(item => item.ShopSubdomain!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StatsSpendBar
            {
                Key = group.Key,
                Label = group.First().ShopName ?? group.Key,
                SpentYen = group.Sum(item => SpentOf(item)),
                ItemCount = group.Count(),
            })
            .OrderByDescending(bar => bar.ItemCount)
            .ThenByDescending(bar => bar.SpentYen)
            .Take(10)
            .ToList();

    private static List<StatsSpendBar> SpendBy(IReadOnlyList<ItemRecord> owned, Func<ItemRecord, string> key)
        => owned
            .GroupBy(key, StringComparer.CurrentCulture)
            .Select(group => new StatsSpendBar
            {
                Key = group.Key,
                Label = group.Key,
                SpentYen = group.Sum(item => SpentOf(item)),
                ItemCount = group.Count(),
            })
            .Where(bar => bar.SpentYen > 0)
            .OrderByDescending(bar => bar.SpentYen)
            .ToList();

    /// <summary>
    /// 1つのitemが複数の分類に属する場合（userTagのトップは複数選べる）。
    /// 金額は分類ごとに満額を数える。合計は支出と一致しないので、画面でその旨を書く。
    /// </summary>
    private static List<StatsSpendBar> SpendByMany(
        IReadOnlyList<ItemRecord> owned,
        Func<ItemRecord, IEnumerable<string>> keys)
    {
        var spend = new Dictionary<string, (long Yen, int Count)>(StringComparer.CurrentCulture);

        foreach (var item in owned)
        {
            var amount = SpentOf(item);

            foreach (var key in keys(item).Distinct(StringComparer.CurrentCulture))
            {
                var current = spend.TryGetValue(key, out var existing) ? existing : (Yen: 0L, Count: 0);
                spend[key] = (current.Yen + amount, current.Count + 1);
            }
        }

        return spend
            .Where(pair => pair.Value.Yen > 0)
            .Select(pair => new StatsSpendBar
            {
                Key = pair.Key,
                Label = pair.Key,
                SpentYen = pair.Value.Yen,
                ItemCount = pair.Value.Count,
            })
            .OrderByDescending(bar => bar.SpentYen)
            .ToList();
    }

    private static List<StatsCountBar> CountBy(IReadOnlyList<ItemRecord> owned, Func<ItemRecord, string> key)
        => owned
            .GroupBy(key, StringComparer.CurrentCulture)
            .Select(group => new StatsCountBar { Key = group.Key, Label = group.Key, ItemCount = group.Count() })
            .OrderByDescending(bar => bar.ItemCount)
            .ToList();

    private static List<StatsCountBar> CountByMany(
        IReadOnlyList<ItemRecord> owned,
        Func<ItemRecord, IEnumerable<string>> keys)
    {
        var counts = new Dictionary<string, int>(StringComparer.CurrentCulture);
        var untagged = 0;

        foreach (var item in owned)
        {
            var list = keys(item).Distinct(StringComparer.CurrentCulture).ToList();

            if (list.Count == 0)
            {
                untagged++;
                continue;
            }

            foreach (var key in list)
            {
                counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
            }
        }

        var bars = counts
            .Select(pair => new StatsCountBar { Key = pair.Key, Label = pair.Key, ItemCount = pair.Value })
            .OrderByDescending(bar => bar.ItemCount)
            .ToList();

        if (untagged > 0)
        {
            bars.Add(new StatsCountBar
            {
                Key = string.Empty,
                Label = "未設定",
                ItemCount = untagged,
                IsResidual = true,
            });
        }

        return bars;
    }

    /// <summary>
    /// 月ごとのカテゴリ構成。買うものが移り変わったかを見る。
    /// 種類が多いと読めないので、上位だけを軸にして残りは「その他」へ寄せる。
    /// </summary>
    private static List<StatsMonthlyCategory> MonthlyCategories(
        IReadOnlyList<ItemRecord> owned,
        out IReadOnlyList<string> order)
    {
        const int TopCategories = 5;
        const string Other = "その他";

        var top = owned
            .GroupBy(CategoryOf, StringComparer.CurrentCulture)
            .OrderByDescending(group => group.Count())
            .Take(TopCategories)
            .Select(group => group.Key)
            .ToList();

        var months = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var item in owned)
        {
            // 件数は商品で数える物なので、代表の日付（最も早い購入）の月に1回だけ入れる（メモ45）
            var acquired = PurchaseDates.ResolveEarliest(item);
            if (acquired.Value is not { } date)
            {
                continue;
            }

            var key = $"{date.Year:D4}-{date.Month:D2}";
            if (!months.TryGetValue(key, out var counts))
            {
                counts = new Dictionary<string, int>(StringComparer.CurrentCulture);
                months[key] = counts;
            }

            var category = CategoryOf(item);
            var bucket = top.Contains(category, StringComparer.CurrentCulture) ? category : Other;
            counts[bucket] = counts.TryGetValue(bucket, out var current) ? current + 1 : 1;
        }

        var hasOther = months.Values.Any(counts => counts.ContainsKey(Other));
        order = hasOther ? [.. top, Other] : top;

        return months
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new StatsMonthlyCategory
            {
                Label = pair.Key[5..],
                Counts = pair.Value,
                Total = pair.Value.Values.Sum(),
            })
            .ToList();
    }

    private static List<StatsAttributeDistribution> Distributions(IReadOnlyList<ItemRecord> owned)
    {
        var names = owned
            .SelectMany(item => item.Local.Attributes.Keys)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture);

        var result = new List<StatsAttributeDistribution>();

        foreach (var name in names)
        {
            var values = owned
                .Where(item => item.Local.Attributes.ContainsKey(name))
                .Select(item => item.Local.Attributes[name])
                .ToList();

            if (values.Count == 0)
            {
                continue;
            }

            var buckets = new int[5];
            foreach (var value in values)
            {
                buckets[Math.Clamp(value / 20, 0, 4)]++;
            }

            result.Add(new StatsAttributeDistribution
            {
                Name = name,
                Rated = values.Count,
                Average = values.Average(),
                Buckets = buckets,
            });
        }

        return result.OrderByDescending(entry => entry.Rated).ToList();
    }

    /// <summary>
    /// 属性2軸の相関（ピアソン）。両方を評価済みのitemだけで計算する。
    /// 件数が少ないと数字が暴れるので、閾値に満たない組は返さない。
    /// </summary>
    private static List<StatsAttributeCorrelation> Correlations(IReadOnlyList<ItemRecord> owned)
    {
        var names = owned
            .SelectMany(item => item.Local.Attributes.Keys)
            .Distinct(StringComparer.CurrentCulture)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        var result = new List<StatsAttributeCorrelation>();

        for (var i = 0; i < names.Count; i++)
        {
            for (var j = i + 1; j < names.Count; j++)
            {
                var pairs = owned
                    .Where(item => item.Local.Attributes.ContainsKey(names[i])
                        && item.Local.Attributes.ContainsKey(names[j]))
                    .Select(item => (X: (double)item.Local.Attributes[names[i]],
                                     Y: (double)item.Local.Attributes[names[j]]))
                    .ToList();

                if (pairs.Count < CorrelationMinimum)
                {
                    continue;
                }

                var r = Pearson(pairs);
                if (double.IsNaN(r))
                {
                    continue;
                }

                result.Add(new StatsAttributeCorrelation
                {
                    A = names[i],
                    B = names[j],
                    Count = pairs.Count,
                    R = r,
                });
            }
        }

        return result.OrderByDescending(entry => Math.Abs(entry.R)).ToList();
    }

    private static double Pearson(IReadOnlyList<(double X, double Y)> pairs)
    {
        var meanX = pairs.Average(pair => pair.X);
        var meanY = pairs.Average(pair => pair.Y);

        var covariance = pairs.Sum(pair => (pair.X - meanX) * (pair.Y - meanY));
        var varianceX = pairs.Sum(pair => Math.Pow(pair.X - meanX, 2));
        var varianceY = pairs.Sum(pair => Math.Pow(pair.Y - meanY, 2));

        // どちらかの軸が全部同じ値だと割れない。関係が無いのではなく求まらない
        return varianceX == 0 || varianceY == 0
            ? double.NaN
            : covariance / Math.Sqrt(varianceX * varianceY);
    }

    /// <summary>
    /// 所有アバターごとに、着られる所持商品の数。
    /// 素体経由は推定なので、直接対応と分けたまま返す。
    /// </summary>
    private static List<StatsAvatarWearable> Wearables(
        IReadOnlyList<ItemRecord> owned,
        AvatarRegistry registry,
        AvatarCompatibilityIndex compatibility)
    {
        var ownedIds = owned.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var mine = registry.Entries
            .Where(entry => AvatarService.IsAvatar(entry)
                && (entry.IsOwnedManually || ownedIds.Contains(entry.ItemId)))
            .ToList();

        if (mine.Count == 0)
        {
            return [];
        }

        var direct = new Dictionary<string, int>(StringComparer.Ordinal);
        var viaBase = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in owned)
        {
            foreach (var (avatarId, match) in compatibility.Resolve(item.Local))
            {
                var bucket = match == AvatarMatch.Direct ? direct : viaBase;
                bucket[avatarId] = bucket.TryGetValue(avatarId, out var current) ? current + 1 : 1;
            }
        }

        return mine
            .Select(entry => new StatsAvatarWearable
            {
                AvatarItemId = entry.ItemId,
                Name = entry.DisplayName ?? entry.BoothName ?? entry.ItemId,
                DirectCount = direct.TryGetValue(entry.ItemId, out var d) ? d : 0,
                ViaBaseCount = viaBase.TryGetValue(entry.ItemId, out var v) ? v : 0,
            })
            .OrderByDescending(entry => entry.Total)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCulture)
            .ToList();
    }
}
