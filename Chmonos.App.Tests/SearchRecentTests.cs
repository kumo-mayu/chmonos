using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.Tests;

/// <summary>
/// 検索の「最近」（ユーザ判断 2026-10-06・案A）：最後にそれをしてからの日数を、分布の帯とスライダ2本で選ぶ。帯の範囲は一週間・一か月・一年・全期間。
/// 今は 2026-10-06 12:00（+09:00）に止める（時計に左右されない）。商品の末尾の数字が「何日前に開いたか」、9900599 は記録が無い。
///
/// 除くときの表（記録の種類は「商品閲覧」）：
///
/// | 商品 | 開いた日 | 今日〜6日前 | 除く：今日〜6日前 | 30日以上前 | 除く：30日以上前 | 記録あり | 除く：記録あり |
/// |---|---|---|---|---|---|---|---|
/// | 0 | 今日 | 当たる | — | — | 当たる | 当たる | — |
/// | 6 | 6日前 | 当たる | — | — | 当たる | 当たる | — |
/// | 7 | 7日前 | — | 当たる | — | 当たる | 当たる | — |
/// | 30 | 30日前 | — | 当たる | 当たる | — | 当たる | — |
/// | 400 | 400日前 | — | 当たる | 当たる | — | 当たる | — |
/// | 記録なし | — | — | 当たる | — | 当たる | — | 当たる |
///
/// 記録の無い商品は、除かないときはどの日数にも入らず外れ、除くときは「除かないときに当てはまる物、以外」のまま出る
/// （範囲・日付と違い、記録が無いのは「分からない」ではなく「まだしていない」。「最近開いた物を除く」には一度も開いていない物も入る）。
/// </summary>
public class SearchRecentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));

    private static readonly int[] DaysAgo = [0, 1, 6, 7, 8, 28, 29, 30, 40, 400];

    private const string NoRecord = "9900599";

    private static string IdOf(int days) => (9900000 + days).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly ItemRecord[] Items =
    [
        .. DaysAgo.Select(days => Make.Item(IdOf(days), $"作り物 {days}日前")),
        Make.Item(NoRecord, "作り物 記録なし"),
    ];

    private static readonly RecentTimes Times = new(
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>(),
        DaysAgo.ToDictionary(IdOf, days => Now.AddDays(-days).AddHours(-1)));

    private static readonly SearchModuleContext Context = new(
        () => throw new InvalidOperationException("対応アバターの索引は使わない"),
        ModificationUsage.Empty,
        Times,
        null,
        Now);

    private static RecentModule Viewed(RecentPeriod period = RecentPeriod.Month)
    {
        var module = new RecentModule();
        module.Selected = module.Options.First(option => option.Key == "viewed");
        module.SetRecords(Times, Items, Now);
        module.Period = period;
        return module;
    }

    /// <summary>つまみを日数の所へ置く（画面と同じく位置で渡す）。上が null なら右端。</summary>
    private static void SetRange(RecentModule module, int low, int? high)
    {
        module.HighPosition = high is { } days ? days * 100.0 / module.Span : 100;
        module.LowPosition = low * 100.0 / module.Span;
    }

    private static string[] Passing(SearchModule module)
    {
        module.Prepare(Context);
        return Items.Where(item => module.Passes(item, Context))
            .Select(item => item.Id == NoRecord ? "なし" : (int.Parse(item.Id, System.Globalization.CultureInfo.InvariantCulture) - 9900000).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }

    [Theory]
    [InlineData(RecentPeriod.Month, 0, null, "0,1,6,7,8,28,29,30,40,400", "記録あり")]
    [InlineData(RecentPeriod.Month, 0, 0, "0", "今日")]
    [InlineData(RecentPeriod.Month, 0, 6, "0,1,6", "今日〜6日前")]
    [InlineData(RecentPeriod.Month, 7, 28, "7,8,28", "7〜28日前")]
    [InlineData(RecentPeriod.Month, 8, 8, "8", "8日前")]
    [InlineData(RecentPeriod.Month, 29, null, "29,30,40,400", "29日以上前")]
    [InlineData(RecentPeriod.Week, 7, null, "7,8,28,29,30,40,400", "7日以上前")]
    [InlineData(RecentPeriod.Year, 30, 40, "30,40", "30〜40日前")]
    [InlineData(RecentPeriod.All, 41, null, "400", "41日以上前")]
    public void 日数の範囲で当たる商品と_範囲の言い方(RecentPeriod period, int low, int? high, string expected, string text)
    {
        var module = Viewed(period);
        SetRange(module, low, high);

        Assert.Equal(expected.Split(','), Passing(module));
        Assert.Equal(text, module.RangeText);
        Assert.Equal($"最近（商品閲覧） {text}", module.SummaryText);
    }

    [Theory]
    [InlineData(0, 6, false, "0,1,6")]
    [InlineData(0, 6, true, "7,8,28,29,30,40,400,なし")]
    [InlineData(30, null, false, "30,40,400")]
    [InlineData(30, null, true, "0,1,6,7,8,28,29,なし")]
    [InlineData(0, null, false, "0,1,6,7,8,28,29,30,40,400")]
    [InlineData(0, null, true, "なし")]
    public void 除くときは_記録の無い商品も出る(int low, int? high, bool exclude, string expected)
    {
        var module = Viewed();
        SetRange(module, low, high);
        module.IsExcluded = exclude;

        Assert.Equal(expected.Split(','), Passing(module));
    }

    [Fact]
    public void 除くと要約の頭に除くが付く()
    {
        var module = Viewed();
        SetRange(module, 0, 6);
        module.IsExcluded = true;

        Assert.Equal("除く：最近（商品閲覧） 今日〜6日前", module.SummaryText);
    }

    [Fact]
    public void 足したときは一か月の帯で_記録のある商品すべて()
    {
        var module = new RecentModule();

        Assert.Equal(RecentPeriod.Month, module.Period);
        Assert.Equal(0, module.LowDays);
        Assert.Null(module.HighDays);
        Assert.True(module.IsActive);
        Assert.True(module.SupportsExclude);
    }

    [Fact]
    public void 範囲を短くすると_はみ出した日数は端に寄り_それより前もすべてになる()
    {
        var module = Viewed(RecentPeriod.Year);
        SetRange(module, 40, 60);
        Assert.Equal("40〜60日前", module.RangeText);

        // 一か月の右端は「30日以上前」。選んでいた 40〜60日前は、その中に入ったまま
        module.Period = RecentPeriod.Month;
        Assert.Equal(30, module.LowDays);
        Assert.Null(module.HighDays);
        Assert.Equal("30日以上前", module.RangeText);
        Assert.Equal(100, module.LowPosition);
        Assert.Equal(100, module.HighPosition);
        Assert.Equal(["30", "40", "400"], Passing(module));

        module.Period = RecentPeriod.Week;
        Assert.Equal("7日以上前", module.RangeText);
    }

    [Fact]
    public void 範囲を短くしても_中に収まる日数はそのまま()
    {
        var module = Viewed(RecentPeriod.Year);
        SetRange(module, 1, 6);

        module.Period = RecentPeriod.Week;

        Assert.Equal("1〜6日前", module.RangeText);
        Assert.Equal(["1", "6"], Passing(module));
    }

    [Fact]
    public void 範囲を長くすると_右端に置いた上限は新しい右端へ付いていく()
    {
        var module = Viewed(RecentPeriod.Week);
        SetRange(module, 6, null);
        var before = Passing(module);

        module.Period = RecentPeriod.Year;

        Assert.Equal("6日以上前", module.RangeText);
        Assert.Equal(100, module.HighPosition);
        Assert.Equal(before, Passing(module));

        module.Period = RecentPeriod.All;
        Assert.Equal(before, Passing(module));
    }

    [Fact]
    public void 帯は範囲の中を日ごとに数え_右端の1本にそれより前をまとめる()
    {
        var module = Viewed(RecentPeriod.Week);

        // 0〜6日前の7本と「7日以上前」の1本
        Assert.Equal(8, module.Histogram.Count);
        Assert.True(module.Histogram[^1].IsOutside);
        Assert.All(module.Histogram.Take(7), bar => Assert.False(bar.IsOutside));

        // 0・1・6日前は1件ずつ、2〜5日前は0件。それより前は7件
        Assert.Equal([true, true, false, false, false, false, true], module.Histogram.Take(7).Select(bar => bar.Height > 0));

        // それより前（7件）は範囲の中の山（1件）に合わせると7倍になるので、帯の高さで止める
        Assert.Equal(RangeModule.HistogramHeight, module.Histogram[^1].Height);
        Assert.Equal(RangeModule.HistogramHeight, module.Histogram[0].Height);
        Assert.Equal("7日以上前", module.MaximumLabel);
    }

    [Fact]
    public void 一か月は日ごとの30本_一年は週ごとの52本_全期間はいちばん古い記録まで()
    {
        var month = Viewed(RecentPeriod.Month);
        Assert.Equal(31, month.Histogram.Count);
        Assert.Equal(7, month.TickDays);

        var year = Viewed(RecentPeriod.Year);
        Assert.Equal(53, year.Histogram.Count);
        Assert.Equal(30, year.TickDays);
        Assert.Equal("365日以上前", year.MaximumLabel);

        // 400日前が1件だけ「それより前」
        Assert.True(year.Histogram[^1].IsOutside);
        Assert.True(year.Histogram[^1].Height > 0);

        var all = Viewed(RecentPeriod.All);
        Assert.Equal(400, all.Span);
        Assert.Equal(52, all.Histogram.Count);
        Assert.DoesNotContain(all.Histogram, bar => bar.IsOutside);
        Assert.Equal("400日前", all.MaximumLabel);
        Assert.False(all.HasOutsideBar);
    }

    [Fact]
    public void 記録の種類を変えると帯も数え直す_記録が無ければ帯を出さない()
    {
        var module = Viewed();
        Assert.True(module.HasRecords);
        Assert.True(module.HasHistogram);

        module.Selected = module.Options.First(option => option.Key == "used");

        Assert.False(module.HasRecords);
        Assert.False(module.HasHistogram);
        Assert.Empty(Passing(module));
    }

    [Fact]
    public void 日数は暦の日で数え_時差の違う時刻も今の時差で読む()
    {
        var now = new DateTimeOffset(2026, 10, 6, 0, 1, 0, TimeSpan.FromHours(9));

        Assert.Equal(1, RecentModule.DaysAgo(new DateTimeOffset(2026, 10, 5, 23, 59, 0, TimeSpan.FromHours(9)), now));
        Assert.Equal(0, RecentModule.DaysAgo(new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.Zero), now));
        Assert.Equal(0, RecentModule.DaysAgo(now.AddHours(3), now));
    }

    [Fact]
    public void 状態には日数と帯の範囲を書き_範囲の右端より先の上限はそれより前もすべてと読む()
    {
        var module = Viewed(RecentPeriod.Year);
        SetRange(module, 7, 28);

        var state = module.Save();
        Assert.Equal("viewed", state.Choice);
        Assert.Equal("year", state.Period);
        Assert.Equal("7", state.Min);
        Assert.Equal("28", state.Max);

        var open = new RecentModule();
        open.Load(state with { Period = "week", Min = "3", Max = "10" });
        Assert.Equal(RecentPeriod.Week, open.Period);
        Assert.Equal(3, open.LowDays);
        Assert.Null(open.HighDays);
        Assert.Equal(string.Empty, open.Save().Max);

        // 帯の範囲は見え方だけなので、同じ日数なら同じ条件
        Assert.Equal(state.Fingerprint, (state with { Period = "all" }).Fingerprint);
    }

    /// <summary>
    /// 「条件をクリア」で何も絞らない形になる（ユーザ判断 2026-11-10「1-A」・案X）。前は記録のある商品すべてに戻り、
    /// 記録の無い商品が絞られたまま0件になることがあった。記録の種類と帯の範囲は残す
    /// </summary>
    [Fact]
    public void 条件をクリアすると両方に印が入り何も絞らない_種類と帯の範囲は残る()
    {
        var module = Viewed(RecentPeriod.Year);
        SetRange(module, 7, 28);

        module.Clear();

        Assert.True(module.ShowRecorded);
        Assert.True(module.ShowUnrecorded);
        Assert.False(module.IsActive);
        Assert.Equal("絞っていません", module.CollapsedSummary);
        Assert.Equal("記録あり", module.RangeText);
        Assert.Equal(RecentPeriod.Year, module.Period);
        Assert.Equal("viewed", module.Selected.Key);
        Assert.Equal([.. DaysAgo.Select(days => days.ToString(System.Globalization.CultureInfo.InvariantCulture)), "なし"], Passing(module));
    }

    [Fact]
    public void 足したときは記録のある商品だけに印が入る()
    {
        var module = Viewed();

        Assert.True(module.ShowRecorded);
        Assert.False(module.ShowUnrecorded);
        Assert.DoesNotContain("なし", Passing(module));
    }

    /// <summary>
    /// 表示するものの3つの印の付け方と、除くときの表。除くは「除かないときに当てはまる物、以外」のまま。
    /// 帯と期間は記録のある商品だけに効く（下だけのときは範囲を見ない）
    /// </summary>
    [Theory]
    [InlineData(true, false, 0, 6, false, "0,1,6", "今日〜6日前")]
    [InlineData(true, true, 0, 6, false, "0,1,6,なし", "今日〜6日前（記録の無い商品も含める）")]
    [InlineData(false, true, 0, 6, false, "なし", "記録の無い商品だけ")]
    [InlineData(false, true, 0, null, false, "なし", "記録の無い商品だけ")]
    [InlineData(true, true, 0, 6, true, "7,8,28,29,30,40,400", "今日〜6日前（記録の無い商品も含める）")]
    [InlineData(false, true, 0, 6, true, "0,1,6,7,8,28,29,30,40,400", "記録の無い商品だけ")]
    public void 表示するもので_記録の無い商品を含める_だけにする(bool recorded, bool unrecorded, int low, int? high, bool exclude, string expected, string body)
    {
        var module = Viewed();
        SetRange(module, low, high);
        module.ShowUnrecorded = unrecorded;
        module.ShowRecorded = recorded;
        module.IsExcluded = exclude;

        Assert.True(module.IsActive);
        Assert.Equal(expected.Split(','), Passing(module));
        Assert.Equal($"{(exclude ? "除く：" : string.Empty)}最近（商品閲覧） {body}", module.SummaryText);
    }

    [Fact]
    public void 記録の無い商品だけのとき_記録の種類を変えると出る商品も変わる()
    {
        var module = Viewed();
        module.ShowUnrecorded = true;
        module.ShowRecorded = false;
        Assert.Equal(["なし"], Passing(module));

        // 送った記録は1件も無いので、全部が「記録の無い商品」
        module.Selected = module.Options.First(option => option.Key == "used");
        Assert.Equal(Items.Length, Passing(module).Length);
    }

    [Fact]
    public void 表示するものの最後の1つは外せない()
    {
        var module = Viewed();

        module.ShowRecorded = false;
        Assert.True(module.ShowRecorded);

        module.ShowUnrecorded = true;
        module.ShowRecorded = false;
        module.ShowUnrecorded = false;
        Assert.True(module.ShowUnrecorded);
        Assert.False(module.ShowRecorded);
    }

    [Fact]
    public void 件数は_範囲に入る記録のある商品と_記録の無い商品()
    {
        var module = Viewed();
        Assert.Equal("記録のある商品", module.RecordedLabel);

        SetRange(module, 0, 6);
        module.RefreshCounts(Items, Context);

        Assert.Equal("記録のある商品（3）", module.RecordedLabel);
        Assert.Equal("記録の無い商品（1）", module.UnrecordedLabel);
    }

    [Fact]
    public void 状態に表示するものを書き_読み戻すと同じ条件になる()
    {
        var module = Viewed();
        SetRange(module, 0, 6);
        module.ShowUnrecorded = true;

        var state = module.Save();
        Assert.True(state.ShowMatched);
        Assert.True(state.ShowUnspecified);

        // 含めるかどうかで結果が変わるので、指紋も違う
        Assert.NotEqual(state.Fingerprint, (state with { ShowUnspecified = false }).Fingerprint);

        var open = new RecentModule();
        open.Load(state);
        open.SetRecords(Times, Items, Now);
        Assert.True(open.ShowRecorded);
        Assert.True(open.ShowUnrecorded);
        Assert.Equal(["0", "1", "6", "なし"], Passing(open));

        // 外から使う API と同じ判定（Core）でも同じ結果
        var condition = SearchModuleConditions.From(state)!;
        Assert.Equal(Passing(open), Items.Where(item => condition.Matches(item, Context))
            .Select(item => item.Id == NoRecord ? "なし" : (int.Parse(item.Id, System.Globalization.CultureInfo.InvariantCulture) - 9900000).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// 前の版が書いた状態（表示するものの欄が無い）は、記録のある商品だけ（前と同じ結果）で読む。
    /// 両方を切った状態を手で書かれても、記録のある商品を出す側に戻す
    /// </summary>
    [Theory]
    [InlineData("""{"kind":"Recent","choice":"viewed","period":"month","min":"0","max":"6"}""", true, false, "0,1,6")]
    [InlineData("""{"kind":"Recent","choice":"viewed","period":"month","min":"0","max":"","showMatched":false,"showUnspecified":false}""", true, false, "0,1,6,7,8,28,29,30,40,400")]
    public void 前の版の状態は記録のある商品だけで読む(string json, bool recorded, bool unrecorded, string expected)
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<SearchModuleState>(json, Chmonos.Core.Storage.JsonStore.Options)!;

        var module = new RecentModule();
        module.Load(state);
        module.SetRecords(Times, Items, Now);

        Assert.Equal(recorded, module.ShowRecorded);
        Assert.Equal(unrecorded, module.ShowUnrecorded);
        Assert.True(module.IsActive);
        Assert.Equal(expected.Split(','), Passing(module));

        var condition = SearchModuleConditions.From(state)!;
        Assert.DoesNotContain(Items.Single(item => item.Id == NoRecord), Items.Where(item => condition.Matches(item, Context)));
    }

    /// <summary>
    /// 検索の画面で「条件をクリア」すると、記録の無い商品も出る（ユーザ指摘 2026-11-10：Unity送信・一か月の条件があると、クリアしても0件のままだった）。
    /// クリアした後も、足跡を読んで件数を数える（読まないと記録のある商品が0件に見える）
    /// </summary>
    [Fact]
    public Task 検索で条件をクリアすると_記録の無い商品も出て_件数も正しい() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900651", "作り物 送った"));
        await app.AddItemAsync(Make.Item("9900652", "作り物 送っていない甲"));
        await app.AddItemAsync(Make.Item("9900653", "作り物 送っていない乙"));
        await app.Services.Recent.TouchAsync("9900651", RecentKind.Used, Now.AddDays(-2));
        var main = await app.StartAsync();
        var search = main.Search;
        search.Clock = () => Now;

        var recent = (RecentModule)SearchModules.Add(search, SearchModuleKind.Recent);
        SetRange(recent, 0, 0);
        await UiThread.Until(() => search.ResultSummary == "0 件", "今日に送った商品は無い");

        search.ClearFiltersCommand.Execute(null);
        await UiThread.Until(() => search.ResultSummary == "3 件", "クリアで全部出る");

        Assert.False(recent.IsActive);
        Assert.Equal("記録のある商品（1）", recent.RecordedLabel);
        Assert.Equal("記録の無い商品（2）", recent.UnrecordedLabel);

        // 記録の無い商品だけにすると、一度も送っていない商品を探せる
        recent.ShowRecorded = false;
        await UiThread.Until(() => search.ResultSummary == "2 件", "送っていない商品だけ");
        Assert.Equal("最近（Unity送信） 記録の無い商品だけ", search.FilterSummary);
    });

    [Fact]
    public void 上下のつまみは越えられない()
    {
        var module = Viewed();
        SetRange(module, 10, 20);

        module.LowPosition = 25 * 100.0 / module.Span;
        Assert.Equal(20, module.LowDays);

        module.HighPosition = 5 * 100.0 / module.Span;
        Assert.Equal(20, module.HighDays);
    }

    /// <summary>
    /// 開いたまま日をまたいでも、窓が手前に来たら「最近」の今日・何日を数え直す（外部の点検 2026-10-07）。
    /// 前は絞り込みの時に決まった日のままで、昨日になった商品が「今日」に残った。時計は差し替えて進める
    /// </summary>
    [Fact]
    public Task 日をまたいで窓が手前に来たら_最近の今日を数え直す() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9900641", "作り物 今日見た"));
        await app.Services.Recent.TouchAsync("9900641", RecentKind.Viewed, Now);
        var main = await app.StartAsync();
        var search = main.Search;
        search.Clock = () => Now;

        var recent = (RecentModule)SearchModules.Add(search, SearchModuleKind.Recent);
        recent.Selected = recent.Options.First(option => option.Key == "viewed");
        SetRange(recent, 0, 0);
        await app.SettleAsync();
        Assert.Contains(search.ListItems, card => card.Item.Id == "9900641");

        // 翌日になって、窓が手前に来る
        search.Clock = () => Now.AddDays(1);
        main.NoteWindowActivated();
        await app.SettleAsync();

        Assert.DoesNotContain(search.ListItems, card => card.Item.Id == "9900641");
    });

    [Fact]
    public Task 新しい順に並べるを押すと_その記録の日の新しい順になり_押せなくなる() => TestApp.Run(async app =>
    {
        foreach (var (id, days) in new[] { ("9900611", 5), ("9900612", 1), ("9900613", 3) })
        {
            await app.AddItemAsync(Make.Item(id, "作り物 " + id));
            await app.Services.Recent.TouchAsync(id, RecentKind.Viewed, Now.AddDays(-days));
        }

        await app.AddItemAsync(Make.Item("9900614", "作り物 記録なし"));
        var main = await app.StartAsync();
        var search = main.Search;
        search.Clock = () => Now;

        var recent = (RecentModule)SearchModules.Add(search, SearchModuleKind.Recent);
        recent.Selected = recent.Options.First(option => option.Key == "viewed");
        await app.SettleAsync();

        Assert.Equal("3 件", search.ResultSummary);
        Assert.True(recent.CanSort);
        Assert.Equal("表示順を「商品閲覧日が新しい順」にします。", recent.SortHint);

        recent.SortCommand.Execute(null);
        await app.SettleAsync();

        Assert.Equal("商品閲覧日", search.SortField.Label);
        Assert.Equal("商品閲覧日が新しい順", search.Sort.Label);
        Assert.True(search.SortsDescending);
        Assert.Equal(["9900612", "9900613", "9900611"], search.ListItems.Select(card => card.Item.Id));
        Assert.False(recent.CanSort);

        // 向きを変える・記録の種類を変えると、また押せる
        search.SortsAscending = true;
        Assert.True(recent.CanSort);
        search.SortsDescending = true;
        Assert.False(recent.CanSort);
        recent.Selected = recent.Options.First(option => option.Key == "added");
        await app.SettleAsync();
        Assert.True(recent.CanSort);
    });

    [Fact]
    public Task 帯は手元の商品の足跡から描き_範囲で絞る() => TestApp.Run(async app =>
    {
        foreach (var (id, days) in new[] { ("9900621", 0), ("9900622", 2), ("9900623", 45) })
        {
            await app.AddItemAsync(Make.Item(id, "作り物 " + id));
            await app.Services.Recent.TouchAsync(id, RecentKind.Used, Now.AddDays(-days));
        }

        // 手元に無い商品の足跡は帯に入れない
        await app.Services.Recent.TouchAsync("9900629", RecentKind.Used, Now.AddDays(-1));
        var main = await app.StartAsync();
        var search = main.Search;
        search.Clock = () => Now;

        var recent = (RecentModule)SearchModules.Add(search, SearchModuleKind.Recent);
        await app.SettleAsync();

        Assert.Equal(3, recent.Histogram.Sum(bar => bar.Height > 0 ? 1 : 0));
        Assert.True(recent.Histogram[0].Height > 0);
        Assert.Equal(0, recent.Histogram[1].Height);
        Assert.True(recent.Histogram[^1].IsOutside);

        recent.Period = RecentPeriod.Week;
        recent.HighPosition = 2 * 100.0 / recent.Span;
        await UiThread.Until(() => search.ResultSummary == "2 件", "今日〜2日前で絞る");

        Assert.Equal("最近（Unity送信） 今日〜2日前", search.FilterSummary);
    });

    [Fact]
    public void 並べ替えの最近の3種は_条件の記録の種類と同じ言い方()
    {
        Assert.Equal("Unity送信日", RecentModule.DateLabel(RecentKind.Used));
        Assert.Equal("商品閲覧日", RecentModule.DateLabel(RecentKind.Viewed));
        Assert.Equal("取り込み日", RecentModule.DateLabel(RecentKind.Added));
    }

    /// <summary>
    /// 閲覧日で並べているとき、商品を開いた後にナビの「検索」で戻ると、並びが新しい足跡に合う（外部の点検 2026-10-07）。
    /// 前は条件を触るまで古い並びのままだった
    /// </summary>
    [Fact]
    public Task 閲覧日で並べているとき_ナビで検索へ戻ると_新しく開いた商品が先に来る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9901201", "作り物の衣装甲"));
        await app.AddItemAsync(Make.Item("9901202", "作り物の衣装乙"));
        await app.Services.Recent.TouchAsync("9901201", RecentKind.Viewed, DateTimeOffset.Now.AddHours(-2));
        await app.Services.Recent.TouchAsync("9901202", RecentKind.Viewed, DateTimeOffset.Now.AddHours(-3));
        var main = await app.StartAsync();
        var search = main.Search;
        search.SortField = search.SortFields.Single(field => field.Kind == SortKind.RecentlyViewed);
        search.SortsDescending = true;
        await app.SettleAsync();
        Assert.Equal("9901201", search.ListItems.First().Item.Id);

        // ほかの画面で乙を開いた（足跡が増えた）後に、ナビの「検索」で戻る
        await app.Services.Recent.TouchAsync("9901202", RecentKind.Viewed, DateTimeOffset.Now);
        main.ShowSearch();
        await app.SettleAsync();

        Assert.Equal("9901202", search.ListItems.First().Item.Id);
    });
}
