using System.Globalization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>「最近」の帯の範囲（ユーザ判断 2026-10-06：「一週間」「一か月」「一年」「全期間」から選べる）。</summary>
public enum RecentPeriod
{
    Week,
    Month,
    Year,
    All,
}

/// <summary>
/// 最近（ユーザ判断 2026-10-06・案A）。記録の種類（Unityへ送った・商品ページを開いた・取り込んだ）ごとに、
/// **最後にそれをしてからの日数**を軸にして、価格・属性と同じ分布の帯とスライダ2本で範囲を選ぶ（「7〜28日前に開いた」）。
///
/// 日数は暦の日で数える（今日＝0日前・昨日＝1日前）。経った時間（24時間ごと）で数えると、昨夜開いた物が「今日」に入ったり入らなかったりして、
/// 帯の棒とつまみの言う「n日前」が人の数え方とずれる。日付は「今」と同じ時差で読む（PC の時差の設定に左右されない）。
///
/// **帯の範囲**（<see cref="Period"/>）は見え方だけで、結果は日数の上下で決まる。いちばん古い記録がとても昔だと、全期間の帯は
/// 最近の数日が左端の1本に潰れる（ユーザ指摘）。範囲を短くすると、右端の1本に「それより前」をまとめ、右のつまみをそこへ置けば
/// 「それより前もすべて」になる（状態の上限は空）。範囲を変えて選んでいた日数がはみ出したら端へ寄せる——端は「それより前もすべて」なので、
/// 結果は広がるだけで、選んでいた商品は消えない。
///
/// **記録の無い商品**：除かないときは外す（どの日数にも入らない）。除くときは出す——「除く＝除かないときに当てはまる物、以外」のまま。
/// 範囲・日付の条件は値の分からない商品を除くときも外すが、ここで記録が無いのは「分からない」ではなく「まだしていない」なので、
/// 「最近30日に開いた物を除く」には一度も開いていない物も入るのが読みどおり（表は `SearchRecentTests`）。
/// </summary>
public sealed class RecentModule : SearchModule
{
    public const int WeekDays = 7;

    /// <summary>一か月は30日（月の長さで右端が28〜31日に揺れると、同じつまみの位置が月によって違う日数を指す）。</summary>
    public const int MonthDays = 30;

    public const int YearDays = 365;

    /// <summary>
    /// 帯の棒の数の上限。一年は週ごとの52本（日ごとの365本は細くて読めない）。全期間も同じ本数までにする。
    /// 一週間・一か月は日ごと（7本・30本）。
    /// </summary>
    private const int MaxBars = 52;

    private ChoiceOption _selected;
    private RecentPeriod _period = DefaultPeriod;
    private int _low;

    /// <summary>上の端（何日前まで）。null は右端＝それより前もすべて。</summary>
    private int? _high;
    private bool _isSortedByThis;
    private RelayCommand? _sort;

    private RecentTimes _times = RecentTimes.Empty;
    private IReadOnlyCollection<ItemRecord> _items = [];
    private DateTimeOffset _now = DateTimeOffset.Now;
    private bool _hasRecords;

    /// <summary>選んだ種類の記録のうち、いちばん古い日数（全期間の右端）。</summary>
    private int _oldest;

    /// <summary>
    /// 足したときの帯の範囲。一か月にする：「最近」で探すのは数日〜数週間前の物が多く、日ごとの30本なら1本ずつ読める。
    /// 一週間だと Unityへ送った記録は空のことが多く、一年は週ごとの棒になって数日前が1本に混ざる。
    /// </summary>
    public const RecentPeriod DefaultPeriod = RecentPeriod.Month;

    public RecentModule()
        : base(SearchModuleKind.Recent)
    {
        Options =
        [
            new ChoiceOption("used", "Unity送信"),
            new ChoiceOption("viewed", "商品閲覧"),
            new ChoiceOption("added", "取り込み"),
        ];
        _selected = Options[0];
    }

    public IReadOnlyList<ChoiceOption> Options { get; }

    public ChoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(SortHint));
                RefreshHistogram();
                NotifyChanged();
            }
        }
    }

    public RecentKind SelectedKind => KindOf(_selected.Key);

    private static RecentKind KindOf(string key) => key switch
    {
        "viewed" => RecentKind.Viewed,
        "added" => RecentKind.Added,
        _ => RecentKind.Used,
    };

    /// <summary>
    /// 並べ替えの項目の名前（「商品ページを開いた日」）。表示順の欄とこの条件のボタンで同じ言い方にする（ユーザ判断 2026-10-06）。
    /// 前の表示順は「使った日」「見た日」で、条件の「Unityへ送った」「商品ページを開いた」と同じ物だと読めなかった。
    /// </summary>
    public static string DateLabel(RecentKind kind) => kind switch
    {
        RecentKind.Viewed => "商品閲覧日",
        RecentKind.Added => "取り込み日",
        _ => "Unity送信日",
    };

    public RecentPeriod Period
    {
        get => _period;
        set
        {
            if (_period == value)
            {
                return;
            }

            // 全期間の右端はいちばん古い記録で決まるので、帯を数え直してから寄せる
            _period = value;
            RefreshHistogram();
            FitToPeriod();
            OnPeriodChanged();
            OnBoundsChanged();
            OnPropertyChanged(nameof(LowDays));
            OnPropertyChanged(nameof(HighDays));
            NotifyChanged();
        }
    }

    // 4つの切り替えボタン（つながったボタン）に結ぶ。選ばれていない側から false が来ても何もしない
    public bool IsWeek
    {
        get => _period == RecentPeriod.Week;
        set => SetPeriodFrom(value, RecentPeriod.Week);
    }

    public bool IsMonth
    {
        get => _period == RecentPeriod.Month;
        set => SetPeriodFrom(value, RecentPeriod.Month);
    }

    public bool IsYear
    {
        get => _period == RecentPeriod.Year;
        set => SetPeriodFrom(value, RecentPeriod.Year);
    }

    public bool IsAll
    {
        get => _period == RecentPeriod.All;
        set => SetPeriodFrom(value, RecentPeriod.All);
    }

    private void SetPeriodFrom(bool on, RecentPeriod period)
    {
        if (on)
        {
            Period = period;
        }
    }

    /// <summary>右端が「それより前もすべて」か（全期間の右端は、いちばん古い記録）。</summary>
    public bool HasOutsideBar => _period != RecentPeriod.All;

    /// <summary>帯とつまみの右端の日数。全期間はいちばん古い記録（今日だけなら1）。</summary>
    public int Span => _period switch
    {
        RecentPeriod.Week => WeekDays,
        RecentPeriod.Month => MonthDays,
        RecentPeriod.Year => YearDays,
        _ => Math.Max(1, _oldest),
    };

    /// <summary>下の端（何日前から）。</summary>
    public int LowDays => _low;

    /// <summary>上の端（何日前まで）。null は右端（それより前もすべて）。</summary>
    public int? HighDays => _high;

    /// <summary>
    /// つまみの位置（0〜100）。**位置で持ち、日数には割り戻す**（価格のスライダと同じ）。スライダの右端を日数に結ぶと、
    /// 範囲を変えた瞬間に右端が決まる前の値へ丸められ、それが上限として書き戻されうる。目盛は等間隔（日数）。
    /// </summary>
    public double LowPosition
    {
        get => ToPosition(Math.Min(_low, Span));
        set
        {
            // 上下は越えられない（価格・属性と同じ）。止まった値へつまみを戻す
            var days = Math.Min(ToDays(value), _high is { } high ? Math.Min(high, Span) : Span);
            if (days != _low)
            {
                _low = days;
                OnRangeChanged();
            }

            OnPropertyChanged(nameof(LowPosition));
        }
    }

    public double HighPosition
    {
        get => ToPosition(_high is { } high ? Math.Min(high, Span) : Span);
        set
        {
            var days = Math.Max(ToDays(value), Math.Min(_low, Span));
            int? next = days >= Span ? null : days;
            if (next != _high)
            {
                _high = next;
                OnRangeChanged();
            }

            OnPropertyChanged(nameof(HighPosition));
        }
    }

    private int ToDays(double position) => (int)Math.Round(Math.Clamp(position, 0, 100) / 100 * Span, MidpointRounding.AwayFromZero);

    private double ToPosition(int days) => Math.Clamp(days * 100.0 / Span, 0, 100);

    /// <summary>
    /// 目盛の間（日数）。一週間は1日・一か月は1週間・一年は約1か月（30日）。全期間は長さで選ぶ（2か月までは週・2年までは30日・その先は1年）。
    /// 対数にしないのは、「n日前」を読むのに目盛が等しい方が数えやすく、潰れる問題は帯の範囲を短くして解くため。
    /// </summary>
    public int TickDays => _period switch
    {
        RecentPeriod.Week => 1,
        RecentPeriod.Month => 7,
        RecentPeriod.Year => 30,
        _ => Span <= 60 ? 7 : Span <= 730 ? 30 : 365,
    };

    /// <summary>目盛の間（つまみの位置 0〜100 の上の幅）。</summary>
    public double TickFrequency => TickDays * 100.0 / Span;

    /// <summary>矢印キーで1日ずつ動く（位置で持つので、1日ぶんの幅を渡す）。</summary>
    public double DayStep => 100.0 / Span;

    public string ScaleHint => TickDays switch
    {
        1 => "1目盛が1日です。",
        7 => "1目盛が1週間です。",
        365 => "1目盛が1年です。",
        _ => $"1目盛が{TickDays}日です。",
    };

    public string MinimumLabel => "今日";

    public string MaximumLabel => HasOutsideBar ? $"{Span}日以上前" : $"{Span}日前";

    /// <summary>選んだ日数の範囲を言い表した物（帯の上と要約に出す）。</summary>
    public string RangeText => _high switch
    {
        null => _low == 0 ? "記録あり" : $"{_low}日以上前",
        { } high when high == _low => _low == 0 ? "今日" : $"{_low}日前",
        { } high => _low == 0 ? $"今日〜{high}日前" : $"{_low}〜{high}日前",
    };

    /// <summary>選んだ種類の記録が1件でもあるか。無ければ帯の代わりに一言出す。</summary>
    public bool HasRecords => _hasRecords;

    /// <summary>分布の帯。範囲を短くしているときは、右端の1本が「それより前」（<see cref="HistogramBar.IsOutside"/>）。</summary>
    public IReadOnlyList<HistogramBar> Histogram { get; private set; } = [];

    public bool HasHistogram => Histogram.Count > 0;

    /// <summary>
    /// 表示順をこの記録の新しい順にする（ユーザ判断 2026-10-06）。絞ってから「いつの順か」を見たいので、条件の中から1回で切り替えられるようにする。
    /// 検索側が入れる。
    /// </summary>
    public Action<RecentKind>? SortRequested { get; set; }

    public RelayCommand SortCommand => _sort ??= new RelayCommand(() => SortRequested?.Invoke(SelectedKind));

    /// <summary>今の表示順がこの記録の新しい順か。検索側が並べ替えのたびに入れる。そうならボタンを押せなくする。</summary>
    public bool IsSortedByThis
    {
        get => _isSortedByThis;
        set
        {
            if (SetField(ref _isSortedByThis, value))
            {
                OnPropertyChanged(nameof(CanSort));
                OnPropertyChanged(nameof(SortHint));
            }
        }
    }

    public bool CanSort => !_isSortedByThis;

    public string SortHint => _isSortedByThis
        ? "この順で並んでいます。"
        : $"表示順を「{DateLabel(SelectedKind)}が新しい順」にします。";

    /// <summary>足しただけで絞る（記録の無い商品が外れる）。数の範囲と同じく、足すこと自体が「この記録で選ぶ」という意思表示。</summary>
    protected override bool HasCondition => true;

    public override bool SupportsExclude => true;

    /// <summary>暦の日で何日前か（今日＝0）。先の時刻（時計のずれ）は今日に数える。</summary>
    public static int DaysAgo(DateTimeOffset at, DateTimeOffset now)
        => Math.Max(0, (now.Date - at.ToOffset(now.Offset).Date).Days);

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (context.Recent.Of(item.Id, SelectedKind) is not { } at)
        {
            return false;
        }

        var days = DaysAgo(at, context.Now);
        return days >= _low && (_high is not { } high || days <= high);
    }

    protected override string SummaryHead => $"{Label}（{_selected.Label}）";

    protected override string SummaryJoiner => " ";

    protected override string SummaryBody => RangeText;

    /// <summary>
    /// 手元の商品と足跡と今を受け取り、帯を描き直す。検索側が読み込み・絞り直しのたびに呼ぶ。
    /// 同じ足跡・同じ商品の並び・同じ日なら描き直さない（足跡は変わらなければ同じ入れ物が来る・<c>RecentTracker.AllTimes</c>）。
    /// </summary>
    public void SetRecords(RecentTimes times, IReadOnlyCollection<ItemRecord> items, DateTimeOffset now)
    {
        if (ReferenceEquals(times, _times) && ReferenceEquals(items, _items) && now.Date == _now.Date && now.Offset == _now.Offset)
        {
            _now = now;
            return;
        }

        _times = times;
        _items = items;
        _now = now;
        RefreshHistogram();
    }

    /// <summary>
    /// 帯を数え直す。日数は箱に入れずに数える（価格・属性の帯と同じ。数千件で1回ごとに配列を作らない）。
    /// 全期間はいちばん古い記録で棒の幅が決まるので、先に1回なめて古さを見る。
    /// </summary>
    private void RefreshHistogram()
    {
        var kind = SelectedKind;
        var oldest = -1;
        foreach (var item in _items)
        {
            if (_times.Of(item.Id, kind) is { } at)
            {
                oldest = Math.Max(oldest, DaysAgo(at, _now));
            }
        }

        _hasRecords = oldest >= 0;
        _oldest = Math.Max(0, oldest);

        var span = Span;
        var outside = HasOutsideBar;

        // 範囲を区切るときは 0〜span-1 日を棒に配り、span 日以上前は右端の1本。全期間は 0〜span 日の全部を棒に配る
        var covered = outside ? span : span + 1;
        var dayBars = Math.Min(covered, MaxBars);
        var counts = new int[dayBars];
        var older = 0;
        if (_hasRecords)
        {
            foreach (var item in _items)
            {
                if (_times.Of(item.Id, kind) is not { } at)
                {
                    continue;
                }

                var days = DaysAgo(at, _now);
                if (outside && days >= span)
                {
                    older++;
                }
                else
                {
                    counts[Math.Min(days * dayBars / covered, dayBars - 1)]++;
                }
            }
        }

        Histogram = Bars(counts, outside ? older : null);
        OnPropertyChanged(nameof(Histogram));
        OnPropertyChanged(nameof(HasHistogram));
        OnPropertyChanged(nameof(HasRecords));
        OnBoundsChanged();
    }

    /// <summary>
    /// 棒の高さ。**高さは範囲の中の棒で決め、「それより前」の1本は帯の高さで止める**——古い記録が多いと、
    /// その1本に合わせて範囲の中の棒が潰れる（帯の範囲を短くした意味が無くなる）。
    /// </summary>
    internal static IReadOnlyList<HistogramBar> Bars(int[] counts, int? older)
    {
        var peak = counts.Length == 0 ? 0 : counts.Max();
        if (peak == 0 && older is null or 0)
        {
            return [];
        }

        var height = RangeModule.HistogramHeight;
        var bars = counts
            .Select(count => new HistogramBar(count == 0 || peak == 0 ? 0 : Math.Max(2, count * height / peak)))
            .ToList();

        if (older is { } outside)
        {
            var tall = outside == 0 ? 0 : peak == 0 ? height : Math.Clamp(outside * height / peak, 2, height);
            bars.Add(new HistogramBar(tall, IsOutside: true));
        }

        return bars;
    }

    /// <summary>
    /// 範囲を変えたら、はみ出した日数を端へ寄せる。端は「それより前もすべて」なので、選んでいた商品は残る（広がるだけ）。
    /// 範囲を長くしたときは日数を変えない（右端に置いていた上限は新しい右端へ付いていく＝それより前もすべてのまま）。
    /// </summary>
    private void FitToPeriod()
    {
        var span = Span;
        if (_high is { } high && high >= span)
        {
            _high = null;
        }

        if (_low > span)
        {
            _low = span;
        }
    }

    private void OnPeriodChanged()
    {
        OnPropertyChanged(nameof(Period));
        OnPropertyChanged(nameof(IsWeek));
        OnPropertyChanged(nameof(IsMonth));
        OnPropertyChanged(nameof(IsYear));
        OnPropertyChanged(nameof(IsAll));
        OnPropertyChanged(nameof(HasOutsideBar));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void OnBoundsChanged()
    {
        OnPropertyChanged(nameof(Span));
        OnPropertyChanged(nameof(TickDays));
        OnPropertyChanged(nameof(TickFrequency));
        OnPropertyChanged(nameof(DayStep));
        OnPropertyChanged(nameof(ScaleHint));
        OnPropertyChanged(nameof(MaximumLabel));
        OnPropertyChanged(nameof(LowPosition));
        OnPropertyChanged(nameof(HighPosition));
    }

    /// <summary>つまみが動いた。数と要約はすぐ、絞り直しは止まってから1回。</summary>
    private void OnRangeChanged()
    {
        OnPropertyChanged(nameof(LowDays));
        OnPropertyChanged(nameof(HighDays));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(SummaryText));
        NotifyChangedSoon();
    }

    /// <summary>足したときの姿（記録のある商品すべて）に戻す。記録の種類と帯の範囲は残す（何で見るか・どう見るかは値ではない）。</summary>
    public override void Clear()
    {
        _low = 0;
        _high = null;
        OnPropertyChanged(nameof(LowDays));
        OnPropertyChanged(nameof(HighDays));
        OnPropertyChanged(nameof(LowPosition));
        OnPropertyChanged(nameof(HighPosition));
        OnPropertyChanged(nameof(RangeText));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    private static string KeyOf(RecentPeriod period) => period switch
    {
        RecentPeriod.Week => "week",
        RecentPeriod.Year => "year",
        RecentPeriod.All => "all",
        _ => "month",
    };

    private static RecentPeriod PeriodOf(string? key) => key switch
    {
        "week" => RecentPeriod.Week,
        "month" => RecentPeriod.Month,
        "year" => RecentPeriod.Year,
        "all" => RecentPeriod.All,
        _ => DefaultPeriod,
    };

    /// <summary>下の端は日数、上の端は日数か空（右端＝それより前もすべて）。帯の範囲は見え方だけなので指紋に入らない欄に置く。</summary>
    protected override SearchModuleState Write(SearchModuleState state) => state with
    {
        Choice = _selected.Key,
        Period = KeyOf(_period),
        Min = _low.ToString(CultureInfo.InvariantCulture),
        Max = _high?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
    };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        _period = PeriodOf(state.Period);
        _low = ParseNumber(state.Min) is >= 0 and var low ? low : 0;
        _high = ParseNumber(state.Max) is >= 0 and var high ? Math.Max(high, _low) : null;

        // 区切った範囲の右端より先の上限は「それより前もすべて」と同じに読む（画面のつまみも右端に出る）
        if (_period != RecentPeriod.All)
        {
            FitToPeriod();
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(SortHint));
        OnPropertyChanged(nameof(LowDays));
        OnPropertyChanged(nameof(HighDays));
        OnPeriodChanged();
        RefreshHistogram();
    }
}
