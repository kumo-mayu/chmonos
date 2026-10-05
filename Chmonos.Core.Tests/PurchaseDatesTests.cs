using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Xunit;

namespace Chmonos.Core.Tests;

/// <summary>
/// 購入記録ごとの日付（メモ45）。場面ごとに「購入の日付あり／なし × 商品の日付あり／なし／ファイルの日付だけ」の表で確かめる。
/// ファイルの日付は本物のファイルの更新日時から来るので、一時フォルダに作って日時を書き込む。
/// </summary>
public sealed class PurchaseDatesTests : IDisposable
{
    private static readonly DateOnly Bought = new(2025, 3, 10);
    private static readonly DateOnly Entered = new(2024, 6, 1);
    private static readonly DateOnly FileDay = new(2023, 1, 20);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-purchase-dates-" + Guid.NewGuid().ToString("N"));

    public PurchaseDatesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>商品の日付の3通り。</summary>
    public enum ItemDate
    {
        Entered,
        None,
        FileOnly,
    }

    private ItemRecord Item(string id, ItemDate itemDate, params Purchase[] purchases)
    {
        var path = Path.Combine(_root, id + ".zip");
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, FileDay.ToDateTime(new TimeOnly(12, 0)));

        // 「なし」はファイルの日付も取れない（在らないパス）
        var paths = itemDate == ItemDate.None ? [Path.Combine(_root, "missing-" + id + ".zip")] : new List<string> { path };

        return new ItemRecord
        {
            Id = id,
            Booth = new BoothBlock { Name = "作り物 " + id, FetchedAt = DateTimeOffset.Now, Shop = new BoothShop { Subdomain = "shop-a", Name = "作り物の店", Url = "https://shop-a.booth.pm/" } },
            Local = new LocalBlock
            {
                LocalFiles = [new LocalFileRecord { Hash = "h" + id, Paths = paths, SizeBytes = 10 }],
                Purchases = [.. purchases],
                AcquiredAt = itemDate == ItemDate.Entered ? Entered : null,
            },
        };
    }

    private static Purchase Buy(int price, DateOnly? at = null, PurchaseKind kind = PurchaseKind.ForSelf)
        => new() { VariationId = 1, Price = price, Kind = kind, PurchasedAt = at };

    // ---- 決まりそのもの ----

    /// <summary>購入1件の日付：欄があればそれ、無ければ商品の日付。代える場面ではファイルの日付まで下りる。</summary>
    [Theory]
    [InlineData(true, ItemDate.Entered, "2025-03-10", false)]
    [InlineData(true, ItemDate.None, "2025-03-10", false)]
    [InlineData(true, ItemDate.FileOnly, "2025-03-10", false)]
    [InlineData(false, ItemDate.Entered, "2024-06-01", false)]
    [InlineData(false, ItemDate.None, null, false)]
    [InlineData(false, ItemDate.FileOnly, "2023-01-20", true)]
    public void 購入1件の日付は欄があればそれ_無ければ商品の日付(bool purchaseDated, ItemDate itemDate, string? expected, bool fallback)
    {
        var item = Item("9900001", itemDate, Buy(500, purchaseDated ? Bought : null));

        var resolved = PurchaseDates.Resolve(item);

        Assert.Single(resolved);
        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), resolved[0].Date.Value);
        Assert.Equal(fallback, resolved[0].Date.IsFallback);

        // 代えない場面はファイルの日付を見ない
        var enteredExpected = itemDate == ItemDate.FileOnly && !purchaseDated ? null : expected;
        Assert.Equal(enteredExpected is null ? null : DateOnly.Parse(enteredExpected), PurchaseDates.EnteredEarliest(item));
    }

    /// <summary>購入記録の無い商品は、商品の日付（代える・代えない）がそのまま代表。</summary>
    [Theory]
    [InlineData(ItemDate.Entered, "2024-06-01", "2024-06-01")]
    [InlineData(ItemDate.None, null, null)]
    [InlineData(ItemDate.FileOnly, null, "2023-01-20")]
    public void 購入記録が無ければ商品の日付(ItemDate itemDate, string? entered, string? resolved)
    {
        var item = Item("9900002", itemDate);

        Assert.Equal(entered is null ? null : DateOnly.Parse(entered), PurchaseDates.EnteredEarliest(item));
        Assert.Equal(resolved is null ? null : DateOnly.Parse(resolved), PurchaseDates.ResolveEarliest(item).Value);
    }

    /// <summary>代表は最も早い購入（1-A）。最後に買った日は最も遅い購入。買った日の無い購入は商品の日付で並ぶ。</summary>
    [Fact]
    public void 代表は最も早い購入_最後は最も遅い購入()
    {
        var later = new DateOnly(2026, 2, 1);
        var item = Item("9900003", ItemDate.Entered, Buy(500, later), Buy(300));

        Assert.Equal(Entered, PurchaseDates.EnteredEarliest(item));
        Assert.Equal(Entered, PurchaseDates.ResolveEarliest(item).Value);
        Assert.Equal(later, PurchaseDates.ResolveLatest(item).Value);
    }

    /// <summary>買った日を全部に入れていれば、入手日より遅くても購入の日付が代表（入手日は購入記録の無いときの代わり）。</summary>
    [Fact]
    public void 全部の購入に日付があれば入手日は見ない()
    {
        var item = Item("9900004", ItemDate.Entered, Buy(500, Bought));

        Assert.Equal(Bought, PurchaseDates.EnteredEarliest(item));
        Assert.False(PurchaseDates.ResolveEarliest(item).IsFallback);
    }

    /// <summary>条件「入手日」はどれか1件が範囲に入れば当たる（2-A）。代えない。</summary>
    [Theory]
    [InlineData(true, ItemDate.Entered, true, true)]
    [InlineData(true, ItemDate.None, true, false)]
    [InlineData(true, ItemDate.FileOnly, true, false)]
    [InlineData(false, ItemDate.Entered, false, true)]
    [InlineData(false, ItemDate.None, false, false)]
    [InlineData(false, ItemDate.FileOnly, false, false)]
    public void どれか1件の日付が当たれば当たる(bool purchaseDated, ItemDate itemDate, bool inMarch2025, bool inJune2024)
    {
        // 2件目（入手日に買った分）は買った日を入れていない
        var item = Item("9900005", itemDate, Buy(300), Buy(500, purchaseDated ? Bought : null));

        Assert.Equal(inMarch2025, PurchaseDates.AnyEntered(item, date => date.Year == 2025 && date.Month == 3));
        Assert.Equal(inJune2024, PurchaseDates.AnyEntered(item, date => date.Year == 2024 && date.Month == 6));
        Assert.Equal(inMarch2025 || inJune2024, PurchaseDates.HasEntered(item));
    }

    // ---- JSON ----

    [Fact]
    public void 日付はpurchasedAtで書き_空なら書かない()
    {
        var dated = JsonSerializer.Serialize(Buy(500, Bought), JsonStore.Options);
        var undated = JsonSerializer.Serialize(Buy(500), JsonStore.Options);

        Assert.Contains("\"purchasedAt\": \"2025-03-10\"", dated, StringComparison.Ordinal);
        Assert.DoesNotContain("purchasedAt", undated, StringComparison.Ordinal);
        Assert.Equal(Bought, JsonSerializer.Deserialize<Purchase>(dated, JsonStore.Options)!.PurchasedAt);
    }

    // ---- 統計：月ごと・年ごとの支出 ----

    /// <summary>支出は購入1件ごとに、その購入の日付の月へ。買い足した月にも支出が出る。</summary>
    [Fact]
    public void 統計の支出は購入1件ごとの月へ入る()
    {
        var item = Item("9900010", ItemDate.Entered, Buy(300), Buy(500, Bought));

        var snapshot = StatsService.Build([item], new AvatarRegistry(), unresolvedCount: 0);

        Assert.Equal(300, snapshot.Months.Single(month => month.Key == "2024-06").SpentYen);
        Assert.Equal(500, snapshot.Months.Single(month => month.Key == "2025-03").SpentYen);
        Assert.Equal(300, snapshot.Years.Single(year => year.Key == "2024").SpentYen);
        Assert.Equal(500, snapshot.Years.Single(year => year.Key == "2025").SpentYen);
        Assert.Equal(0, snapshot.UndatedSpentYen);
    }

    /// <summary>同じ月に2回買っても、その月の商品の数は1件。</summary>
    [Fact]
    public void 同じ月の2回は商品1件として数える()
    {
        var item = Item("9900011", ItemDate.None, Buy(300, Bought), Buy(500, Bought.AddDays(3)));

        var snapshot = StatsService.Build([item], new AvatarRegistry(), unresolvedCount: 0);

        var march = snapshot.Months.Single(month => month.Key == "2025-03");
        Assert.Equal(800, march.SpentYen);
        Assert.Equal(1, march.ItemCount);
    }

    /// <summary>
    /// 月に入れられない支出・ファイルの日付で数えた商品の表。買った日を入れた購入は、入手日が空でもファイルの日付を使わない。
    /// </summary>
    [Theory]
    [InlineData(true, ItemDate.Entered, 0, 0, 0)]
    [InlineData(true, ItemDate.None, 0, 0, 0)]
    [InlineData(true, ItemDate.FileOnly, 0, 0, 0)]
    [InlineData(false, ItemDate.Entered, 0, 0, 0)]
    [InlineData(false, ItemDate.None, 500, 1, 0)]
    [InlineData(false, ItemDate.FileOnly, 0, 0, 1)]
    public void 統計の日付の分からない支出とファイルの日付の数(
        bool purchaseDated, ItemDate itemDate, long undatedYen, int undatedCount, int fallbackCount)
    {
        var item = Item("9900012", itemDate, Buy(500, purchaseDated ? Bought : null));

        var snapshot = StatsService.Build([item], new AvatarRegistry(), unresolvedCount: 0);

        Assert.Equal(undatedYen, snapshot.UndatedSpentYen);
        Assert.Equal(undatedCount, snapshot.UndatedCount);
        Assert.Equal(fallbackCount, snapshot.FallbackDatedCount);
    }

    /// <summary>贈った分は自分用の支出に入れないので、日付が無くても「日付の分からない商品」に数えない。</summary>
    [Fact]
    public void 贈った購入の日付は統計の月を決めない()
    {
        var item = Item("9900013", ItemDate.None, Buy(300, Bought), Buy(900, kind: PurchaseKind.Given));

        var snapshot = StatsService.Build([item], new AvatarRegistry(), unresolvedCount: 0);

        Assert.Equal(0, snapshot.UndatedCount);
        Assert.Equal(300, snapshot.Months.Single(month => month.Key == "2025-03").SpentYen);
    }

    // ---- 統計：月ごとのカテゴリ構成 ----

    /// <summary>件数は商品で数えるので、代表の日付（最も早い購入）の月に1回だけ。</summary>
    [Theory]
    [InlineData(true, ItemDate.Entered, "2024-06")]
    [InlineData(true, ItemDate.None, "2025-03")]
    [InlineData(true, ItemDate.FileOnly, "2023-01")]
    [InlineData(false, ItemDate.Entered, "2024-06")]
    [InlineData(false, ItemDate.None, null)]
    [InlineData(false, ItemDate.FileOnly, "2023-01")]
    public void カテゴリ構成は代表の日付の月に1回(bool purchaseDated, ItemDate itemDate, string? expectedMonth)
    {
        // 1件目は買った日を入れていない（商品の日付）、2件目は買った日があれば2025年3月
        var item = Item("9900020", itemDate, Buy(300), Buy(500, purchaseDated ? Bought : null));
        var snapshot = StatsService.Build([item], new AvatarRegistry(), unresolvedCount: 0);

        var enriched = StatsExtras.Enrich(snapshot, [item], [item], new AvatarRegistry());

        var months = enriched.MonthlyCategories.Select(month => month.Label).ToList();
        Assert.Equal(expectedMonth is null ? new List<string>() : [expectedMonth[5..]], months);
    }

    // ---- ショップ ----

    [Theory]
    [InlineData(true, ItemDate.Entered, "2024-06-01", false)]
    [InlineData(true, ItemDate.None, "2025-03-10", false)]
    [InlineData(true, ItemDate.FileOnly, "2023-01-20", true)]
    [InlineData(false, ItemDate.Entered, "2024-06-01", false)]
    [InlineData(false, ItemDate.None, null, false)]
    [InlineData(false, ItemDate.FileOnly, "2023-01-20", true)]
    public void ショップの商品の入手日は代表の日付(bool purchaseDated, ItemDate itemDate, string? expected, bool fallback)
    {
        var item = Item("9900040", itemDate, Buy(300), Buy(500, purchaseDated ? Bought : null));
        var shops = new ShopService(new DataStore(new AppPaths(_root)), new AppSettings());

        var entry = Assert.Single(shops.ItemsOf([item], "shop-a"));

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), entry.AcquiredAt);
        Assert.Equal(fallback, entry.AcquiredIsFallback);
    }

    [Theory]
    [InlineData(true, ItemDate.Entered, "2025-03-10", false)]
    [InlineData(true, ItemDate.None, "2025-03-10", false)]
    [InlineData(true, ItemDate.FileOnly, "2025-03-10", false)]
    [InlineData(false, ItemDate.Entered, "2024-06-01", false)]
    [InlineData(false, ItemDate.None, null, false)]
    [InlineData(false, ItemDate.FileOnly, "2023-01-20", true)]
    public void ショップの最後に買った日は最も遅い購入(bool purchaseDated, ItemDate itemDate, string? expected, bool fallback)
    {
        var item = Item("9900041", itemDate, Buy(300), Buy(500, purchaseDated ? Bought : null));
        var shops = new ShopService(new DataStore(new AppPaths(_root)), new AppSettings());

        var summary = Assert.Single(shops.Summarize([item]));

        Assert.Equal(expected is null ? null : DateOnly.Parse(expected), summary.LastAcquiredAt);
        Assert.Equal(fallback, summary.LastAcquiredIsFallback);
    }
}
