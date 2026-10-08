using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 数の範囲（ユーザ案「上下指定・数値・単品」）。スライダ2本と数の欄。指定していない側は制限しない。
/// </summary>
public sealed class RangeModule : SearchModule
{
    private readonly Func<ItemRecord, string?, IReadOnlyList<long>> _values;
    /// <summary>分布の帯の高さ（px）。棒の高さをここに収める。</summary>
    public const double HistogramHeight = 22;

    /// <summary>分布の帯の棒の数。多くしても細くて読めないので、目盛（10刻み）と噛み合う数にする。</summary>
    private const int HistogramBuckets = 40;

    private string _minText = string.Empty;
    private string _maxText = string.Empty;
    private bool _minEnabled = true;
    private bool _maxEnabled = true;
    private bool _ignoreOutliers = true;
    private bool _matchAll;
    private long? _outlierFence;
    private int _outlierCount;
    private bool _valuesFromState;
    private bool _defaultsApplied;
    private ChoiceOption? _source;
    private double _sliderMaximum = 100;

    /// <param name="values">商品と元（価格の「購入額／BOOTHの価格」）→ 照らす数（どれか1つでも範囲に入れば当たり）。</param>
    /// <param name="sources">数の元の選択肢。先頭が既定。</param>
    public RangeModule(
        SearchModuleKind kind,
        Func<ItemRecord, string?, IReadOnlyList<long>> values,
        string unit,
        IReadOnlyList<ChoiceOption>? sources = null)
        : base(kind)
    {
        _values = values;
        Unit = unit;
        Sources = sources ?? [];
        _source = Sources.FirstOrDefault();
    }

    public string Unit { get; }

    public IReadOnlyList<ChoiceOption> Sources { get; }

    public bool HasSources => Sources.Count > 0;

    public ChoiceOption? Source
    {
        get => _source;
        set
        {
            if (value is not null && SetField(ref _source, value))
            {
                // 元を変えたら数の意味が変わる（購入額と BOOTH の価格）。幅も既定に取り直す
                _valuesFromState = false;
                _defaultsApplied = false;
                OnPropertyChanged(nameof(UnpricedLabel));
                RefreshBounds();
                NotifyChanged();
            }
        }
    }

    /// <summary>元ごとの、手元の商品の数の全部。両端・分布の帯をここから出す。検索側が入れる。</summary>
    public Func<string?, IEnumerable<long>>? AllValuesOf { get; set; }

    /// <summary>
    /// スライダの左端。**いつでも0**（ユーザ指示 2026-09-16）。
    /// 手元の商品によって左端が動く方が分かりにくい（同じ位置が日によって違う数を指す）。
    /// </summary>
    public const double SliderMinimum = 0;

    /// <summary>
    /// 数が詰まっていない帯の上端（価格＝100円。BOOTH の有料販売は100円から）。
    ///
    /// 0 を含めた対数だと、この帯が目盛の大半を取ってしまう（価格・上限500円で 0〜100円が約74%・実測）。
    /// **飛ばさず、左端の1目盛ぶんに畳む**：0〜ここは直線、ここから上限は対数（切れ目でつながるので、どの数も指せる）。
    /// 0（無料）も 50円 も選べるままにする——ユーザ指示「0から100連続的に指定できるようにしたいが、この問題は解決したい」。
    /// 小さい数にも意味がある条件（スキ数）は 0 のままにする（<see cref="Floor"/> を入れない）。
    /// </summary>
    public int Floor { get; init; }

    /// <summary>0〜<see cref="Floor"/> に割り当てる左端の幅（%）。目盛の刻み（10%）に合わせて、切れ目が目盛の上に来るようにする。</summary>
    private const double FloorBand = 10;

    private bool UsesFloor => Floor > 0 && SliderMaximum > Floor;

    public double SliderMaximum
    {
        get => _sliderMaximum;
        private set => SetField(ref _sliderMaximum, value);
    }

    /// <summary>
    /// 分布の帯（ユーザ判断 2026-09-16・案5）。商品がどこに集まっているかが見えると、範囲を決められる。
    /// スライダと同じ対数の配り方で数えるので、帯の位置とつまみの位置が合う。
    /// </summary>
    public IReadOnlyList<HistogramBar> Histogram { get; private set; } = [];

    public bool HasHistogram => Histogram.Count > 0;

    /// <summary>「外れ値を無視」を出すか（価格だけ）。</summary>
    public bool SupportsOutliers { get; init; }

    /// <summary>
    /// 桁違いに高い数を、目盛の幅と照合の両方から外すか（既定は外す・ユーザ判断 2026-09-16）。
    ///
    /// 支援用の種類（0円・150円の商品に 99,999円の種類がある）や、販売を止めるためのあり得ない高値は、値段ではなく目印。
    /// **外すのは数だけで、商品は外さない**——その商品は他の種類の価格で照らす。止め値の種類しか無い商品は「価格が分からない」扱いになる。
    /// 境は <see cref="Outliers"/>（95%の位置の5倍以上）。
    /// </summary>
    public bool IgnoreOutliers
    {
        get => _ignoreOutliers;
        set
        {
            if (!SetField(ref _ignoreOutliers, value))
            {
                return;
            }

            // 右端に置いていた上限は、新しい右端へ付いていく（外れ値を戻せばその分まで、外せば手前まで）
            var wasAtEnd = ParseAmount(_maxText) is not { } oldMax || oldMax >= (long)SliderMaximum;
            RefreshBounds();

            var end = (long)SliderMaximum;
            if (wasAtEnd || ParseAmount(_maxText) > end)
            {
                _maxText = end.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(MaxText));
            }

            if (ParseAmount(_minText) > end)
            {
                _minText = end.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(MinText));
            }

            OnPropertyChanged(nameof(LowPosition));
            OnPropertyChanged(nameof(HighPosition));
            NotifyChanged();
        }
    }

    /// <summary>
    /// 外れ値の説明。境の値だけを言い、外れ値の有無に関わらず常に出す（メモ33-②：数や有無で言い方が変わるのが分かりにくい）。
    /// 境が出せないとき（価格の付いた商品が無い・払った額）は括弧を付けない
    /// </summary>
    public string OutlierLabel => _outlierFence is { } fence
        ? $"外れ値（{fence.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以上）を無視"
        : "外れ値を無視";

    /// <summary>「すべての価格が範囲内の商品のみ」を出すか（価格だけ。スキ数は1商品に1つなので、どれかと全部が同じ）。</summary>
    public bool SupportsMatchAll { get; init; }

    /// <summary>
    /// 照らす数が**全部**範囲に入る商品だけにするか（ユーザ判断 2026-10-06・メモ82-6。既定は切＝どれか1つが入れば当たり）。
    ///
    /// 無料版と支援版のある商品は、どれかで見ると「500円以下」にも「3,000円以上」にも出る。「この幅に全部の種類が収まる商品」を探す手が無かった。
    /// 除くときの意味は他の条件と同じ「除かないときに当たる物、以外」（＝範囲の外の数を1つでも持つ商品）。数の分からない商品は除くときも外す。
    /// 外れ値を無視しているときは、外した数は見ない（支援用の 99,999円の種類があっても、残りが全部範囲に入れば当たる）。
    /// </summary>
    public bool MatchAll
    {
        get => _matchAll;
        set
        {
            if (SetField(ref _matchAll, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>「購入価格が未設定の商品も表示」を出すか（価格だけ。スキ数は BOOTH の商品なら必ずある）。</summary>
    public bool SupportsUnpriced { get; init; }

    /// <summary>元ごとの、値の無い商品も足すチェックの文。検索側が入れる。</summary>
    public Func<string?, string>? UnpricedLabelOf { get; init; }

    /// <summary>
    /// 値の無い商品も足すチェックの文。元で出し分ける（ユーザ判断 2026-10-06：BOOTHの価格の間に「購入価格が未設定」と出ると、
    /// 何が未設定の商品を足すのかが元と食い違う）。
    /// </summary>
    public string UnpricedLabel => UnpricedLabelOf?.Invoke(_source?.Key) ?? string.Empty;

    /// <summary>
    /// 照らす数が1つも無い商品も通すか（ユーザ判断 2026-10-06。既定は切＝前と同じく、数の分からない商品は範囲に入らない）。
    /// 「設定されていない」は**元の数が1つも無い**こと：払った額なら値段を入れていない、BOOTH の価格ならバリエーションが無い。
    /// 外れ値を外した結果1つも残らない商品（止め値の種類しか無い）は、価格は設定されているので含めない。
    /// **除くときも通す**（文の「も表示」のとおり、除いて出る物に足す。除くときは数の分からない商品を外すのが既定なので、ここで戻せる）。
    /// 有料・無料の同じ名前のチェックとは同期しない（ユーザ判断 2026-10-06。条件ごとに別々に持つ）。
    /// </summary>
    public bool IncludeUnpriced
    {
        get => _includeUnpriced;
        set
        {
            if (SetField(ref _includeUnpriced, value))
            {
                NotifyChanged();
            }
        }
    }

    private bool _includeUnpriced;

    private bool IncludesUnpricedNow => SupportsUnpriced && _includeUnpriced;

    /// <summary>外れ値を外す数の元を、外れ値の無い物（価格の払った額）にしているキー。その元では外れ値を探さない。</summary>
    public string? NoOutlierSource { get; init; }

    /// <summary>
    /// 今の元で「外れ値を無視」が効くか。払った額は自分で入れた数で、外れ値は無いので効かない（押せなくして薄くする。ユーザ判断 2026-10-03・メモ16-③）。
    /// <see cref="IgnoreOutliers"/> の値は残す
    /// </summary>
    public bool OutliersApply => SupportsOutliers && (NoOutlierSource is null || _source?.Key != NoOutlierSource);

    private bool IgnoresOutliersNow => SupportsOutliers && _ignoreOutliers && _outlierFence is not null;

    public string MinText
    {
        get => _minText;
        set
        {
            if (SetField(ref _minText, value ?? string.Empty))
            {
                // 下限は上限を越えられない（ユーザ判断 2026-09-16）。触った側を相手に合わせる
                FixCrossing(moveMin: true);
                OnPropertyChanged(nameof(LowPosition));
                OnPropertyChanged(nameof(HasMin));

                // ドラッグ中はここが1秒に数十回来る。数はすぐ出し、絞り直しは止まってから
                NotifyChangedSoon();
            }
        }
    }

    public string MaxText
    {
        get => _maxText;
        set
        {
            if (SetField(ref _maxText, value ?? string.Empty))
            {
                FixCrossing(moveMin: false);
                OnPropertyChanged(nameof(HighPosition));
                OnPropertyChanged(nameof(HasMax));
                NotifyChangedSoon();
            }
        }
    }

    /// <summary>
    /// 下限を効かせるか（既定は効かせる・ユーザ指示 2026-09-16）。
    ///
    /// **端に寄せても効いたまま**にするために要る。前はつまみを端に置くと黙って「制限なし」になり、
    /// 「0以上」と「下限なし」を言い分けられなかった。片側を外したいときはここで切る。
    /// </summary>
    public bool MinEnabled
    {
        get => _minEnabled;
        set
        {
            if (SetField(ref _minEnabled, value))
            {
                // 切っている間は相手を越えていてよい（止める相手がいない）。入れ直したときに合わせる
                FixCrossing(moveMin: true);
                OnPropertyChanged(nameof(MinHint));
                NotifyChanged();
            }
        }
    }

    public bool MaxEnabled
    {
        get => _maxEnabled;
        set
        {
            if (SetField(ref _maxEnabled, value))
            {
                FixCrossing(moveMin: false);
                OnPropertyChanged(nameof(MaxHint));
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// 上下が入れ替わっていたら、**いま触った側**を相手に合わせる（ユーザ判断 2026-09-16：小さい方を下限と読むのではなく、越えられないようにする）。
    ///
    /// 上下のどちらかを切っているときは合わせない——止める相手がいないので、
    /// 「上限を切って下限だけを上まで動かす」が普通にできる。同じ数は許す（ちょうどその数を指せる）。
    /// 名前の付いた2つの行にしてあるので、小さい方を下限と読み替えると「下限」の行が上限として働き、
    /// 行の名前と左のトグルが指すものが食い違う。属性のスライダも越えられない作りで揃えている。
    /// </summary>
    private void FixCrossing(bool moveMin)
    {
        if (!_minEnabled || !_maxEnabled)
        {
            return;
        }

        var min = ParseAmount(_minText) ?? (long)SliderMinimum;
        var max = ParseAmount(_maxText) ?? (long)SliderMaximum;
        if (min <= max)
        {
            return;
        }

        if (moveMin)
        {
            _minText = max.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(MinText));
            OnPropertyChanged(nameof(LowPosition));
        }
        else
        {
            _maxText = min.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(MaxText));
            OnPropertyChanged(nameof(HighPosition));
        }
    }

    /// <summary>入れた値を消す手段を出すか（ユーザ指示 2026-09-16：入力が残る欄は、消せることが分かるようにする）。</summary>
    public bool HasMin => _minText.Length > 0;

    public bool HasMax => _maxText.Length > 0;

    public RelayCommand ClearMinCommand => _clearMin ??= new RelayCommand(() => MinText = string.Empty);

    public RelayCommand ClearMaxCommand => _clearMax ??= new RelayCommand(() => MaxText = string.Empty);

    private RelayCommand? _clearMin;
    private RelayCommand? _clearMax;

    /// <summary>スライダの両端がいくつなのか（目盛だけでは数が読めないので、端の数を添える）。</summary>
    public string MinimumLabel => "0" + Unit;

    /// <summary>目盛の配り方の説明（左端の1目盛に 0〜Floor を畳んでいることを、触る前に分かるように）。</summary>
    /// <summary>
    /// 効かせていない側に**使えない理由**を出す（`ui-rules.md`・E11）。
    /// 同じ欄の中で、理由の出る物と出ない物が混ざっていた。
    /// </summary>
    public string MinHint => MinEnabled ? ScaleHint : "下限を使うには、左のチェックを入れてください。";

    public string MaxHint => MaxEnabled ? ScaleHint : "上限を使うには、左のチェックを入れてください。";

    public string ScaleHint => UsesFloor
        ? $"左の1目盛が 0〜{Floor}{Unit}、その先は対数です。"
        : "目盛は対数です。";

    public string MaximumLabel => ((long)SliderMaximum).ToString("N0", CultureInfo.CurrentCulture) + Unit;

    /// <summary>効いている下限。切っていれば null（制限しない）。欄が空なら左端を下限とする。</summary>
    public long? Min => _minEnabled ? ParseAmount(_minText) ?? (long)SliderMinimum : null;

    /// <summary>効いている上限。切っていれば null。欄が空なら右端を上限とする。</summary>
    public long? Max => _maxEnabled ? ParseAmount(_maxText) ?? (long)SliderMaximum : null;

    /// <summary>
    /// 左のスライダの位置（0〜100）。**端も値として受ける**（左端＝手元の最小値以上）。切るのは左のトグル。
    ///
    /// スライダは位置で持ち、数には割り戻す。スライダの右端を数に結ぶと、
    /// 右端が決まる前に値が既定の右端（10）へ丸められ、それが上限として書き戻されうる。
    /// </summary>
    public double LowPosition
    {
        get => ToPosition(ParseAmount(_minText) ?? (long)SliderMinimum);
        set => MinText = ToNumber(value).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>右のスライダの位置（0〜100）。**端も値として受ける**（右端＝手元の最大値以下）。</summary>
    public double HighPosition
    {
        get => ToPosition(ParseAmount(_maxText) ?? (long)SliderMaximum);
        set => MaxText = ToNumber(value).ToString(CultureInfo.InvariantCulture);
    }

    private double Span => Math.Max(1, SliderMaximum - SliderMinimum);

    /// <summary>
    /// つまみの位置を数にする。**対数で配る**（ユーザ判断 2026-09-16・案1）。
    ///
    /// 等間隔だと、1件だけ高い商品があるだけで実際に使う範囲が潰れる
    /// （5万円の商品が1つあると、1目盛が500円になって2千円付近を指せない）。
    /// 価格もスキ数も安い側・少ない側に集まっているので、そこを広く使う。
    /// 0 を含められるよう、端からの差に 1 を足した対数で測る。
    /// </summary>
    private long ToNumber(double position)
    {
        var at = Math.Clamp(position, 0, 100);

        if (UsesFloor)
        {
            // 左端の1目盛は 0〜Floor を直線で（0も50円も指せる）。そこから先は Floor〜上限の対数
            if (at <= FloorBand)
            {
                return (long)Math.Round(at / FloorBand * Floor);
            }

            var ratio = (at - FloorBand) / (100 - FloorBand);
            var scaled = Math.Exp(Math.Log(Floor) + (ratio * (Math.Log(SliderMaximum) - Math.Log(Floor))));
            return (long)Math.Round(Math.Clamp(scaled, Floor, SliderMaximum));
        }

        var value = SliderMinimum + Math.Exp(at / 100.0 * Math.Log(1 + Span)) - 1;
        return (long)Math.Round(Math.Clamp(value, SliderMinimum, SliderMaximum));
    }

    private double ToPosition(long value)
    {
        if (UsesFloor)
        {
            if (value <= 0)
            {
                return 0;
            }

            if (value <= Floor)
            {
                return value / (double)Floor * FloorBand;
            }

            var ratio = Math.Log((double)value / Floor) / Math.Log(SliderMaximum / (double)Floor);
            return Math.Clamp(FloorBand + (ratio * (100 - FloorBand)), FloorBand, 100);
        }

        var offset = Math.Clamp(value - SliderMinimum, 0, Span);
        return Math.Clamp(100 * Math.Log(1 + offset) / Math.Log(1 + Span), 0, 100);
    }

    /// <summary>
    /// 両端と分布の帯を手元の商品から取り直す。
    /// **足したときの値は端から端まで**にする（ユーザ指示 2026-09-16）。状態から戻した値は上書きしない。
    /// </summary>
    public void RefreshBounds()
    {
        // 右端（空欄の上限が指す数）と外れ値の境が変わりうる
        Unprepare();
        var all = (AllValuesOf?.Invoke(_source?.Key) ?? []).ToList();

        // 外れ値の境は、外す前の数の全部から決める（元を変えれば取り直す）
        _outlierFence = OutliersApply ? Outliers.UpperFence(all) : null;
        OnPropertyChanged(nameof(OutliersApply));
        _outlierCount = _outlierFence is { } fence ? all.Count(value => value >= fence) : 0;
        var values = IgnoresOutliersNow ? all.Where(value => value < _outlierFence!.Value).ToList() : all;
        OnPropertyChanged(nameof(OutlierLabel));

        // 左端はいつでも0。右端は手元の一番大きい数（1件も無ければ仮に100）
        SliderMaximum = values.Count == 0 ? 100 : Math.Max(1, values.Max());
        RefreshHistogram(values);

        if (!_valuesFromState && !_defaultsApplied)
        {
            _defaultsApplied = true;
            _minText = "0";
            _maxText = ((long)SliderMaximum).ToString(CultureInfo.InvariantCulture);

            // **数が分かる商品が1件も無いときは、上下とも切って足す。**
            // 手元に購入額を1件も入れていないのに「価格」を足すと、足した瞬間に0件になってしまう
            _minEnabled = values.Count > 0;
            _maxEnabled = values.Count > 0;

            OnPropertyChanged(nameof(MinText));
            OnPropertyChanged(nameof(MaxText));
            OnPropertyChanged(nameof(HasMin));
            OnPropertyChanged(nameof(HasMax));
            OnPropertyChanged(nameof(MinEnabled));
            OnPropertyChanged(nameof(MaxEnabled));
            OnPropertyChanged(nameof(IsActive));
        }

        OnPropertyChanged(nameof(LowPosition));
        OnPropertyChanged(nameof(HighPosition));
        OnPropertyChanged(nameof(MinimumLabel));
        OnPropertyChanged(nameof(MaximumLabel));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    /// <summary>
    /// 棒ごとの数（左から）を帯の高さにする。属性のスライダの帯（<see cref="AttributeFilter"/>）も同じ描き方にそろえる。
    /// 1件しかない所も見えるように最低の高さを持たせ、0件の所は出さない。1件も無ければ帯を出さない
    /// </summary>
    internal static IReadOnlyList<HistogramBar> Bars(int[] counts)
    {
        var peak = counts.Length == 0 ? 0 : counts.Max();
        return peak == 0
            ? []
            : counts.Select(count => new HistogramBar(count == 0 ? 0 : Math.Max(2, count * HistogramHeight / peak))).ToList();
    }

    private void RefreshHistogram(IReadOnlyList<long> values)
    {
        var counts = new int[HistogramBuckets];
        foreach (var value in values)
        {
            var index = Math.Clamp((int)(ToPosition(value) / 100 * HistogramBuckets), 0, HistogramBuckets - 1);
            counts[index]++;
        }

        Histogram = Bars(counts);
        OnPropertyChanged(nameof(Histogram));
        OnPropertyChanged(nameof(HasHistogram));
    }

    /// <summary>
    /// 片側でも効かせていれば条件になっている。
    ///
    /// **足した時点で（既定で両側が効いて）絞り始める。**数の分からない商品——値段を入れていない・
    /// BOOTH に無い——は範囲のどこにも入らないので外れる。属性と同じ考え方で、足すこと自体が「この数で選ぶ」という意思表示。
    /// </summary>
    protected override bool HasCondition => _minEnabled || _maxEnabled;

    public override bool SupportsExclude => true;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (!HasCondition)
        {
            return true;
        }

        EnsurePrepared(context);
        if (IncludesUnpricedNow && IsUnpriced(item))
        {
            return true;
        }

        var (min, max) = (_preparedMin, _preparedMax);
        var values = KnownValues(item);
        if (SupportsMatchAll && _matchAll)
        {
            // 数の分からない商品は当てない（「全部が範囲に入る」の全部が空で真になるのを避ける）
            if (values.Count == 0)
            {
                return false;
            }

            foreach (var value in values)
            {
                if ((min is not null && value < min) || (max is not null && value > max))
                {
                    return false;
                }
            }

            return true;
        }

        foreach (var value in values)
        {
            if ((min is null || value >= min) && (max is null || value <= max))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 欄の字を数に読むのは絞り込みの1回で1回だけ（案c）。前は商品ごとに全角を畳んで読み直していた。
    /// </summary>
    protected override void PrepareCore(SearchModuleContext context) => (_preparedMin, _preparedMax) = (Min, Max);

    private long? _preparedMin;
    private long? _preparedMax;

    /// <summary>
    /// 数が分かっていて、除かないときに当たらない商品（ユーザ判断 2026-10-01）。どれか1つで見るときは**どの数も**範囲に入らない商品、
    /// 「すべての価格が範囲内」のときは**範囲の外の数を1つでも持つ**商品（メモ82-6。表は `docs/spec/search-filters.md`）。
    /// 数の分からない商品（値段を入れていない・外れ値を外したら1つも残らない）は、除くときも外す。
    /// </summary>
    protected override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
        => !HasCondition
            || (IncludesUnpricedNow && IsUnpriced(item))
            || (KnownValues(item).Count > 0 && !Matches(item, context));

    /// <summary>元の数が1つも無い（値段を入れていない・バリエーションが無い）。外れ値を外して残らない物は含めない。</summary>
    private bool IsUnpriced(ItemRecord item) => _values(item, _source?.Key).Count == 0;

    /// <summary>照らす数。外れ値を外していれば、その数だけを外す（商品は他の種類の価格で照らす）。</summary>
    private IReadOnlyList<long> KnownValues(ItemRecord item)
    {
        var values = _values(item, _source?.Key);
        if (!IgnoresOutliersNow)
        {
            return values;
        }

        var fence = _outlierFence!.Value;
        return values.Where(value => value < fence).ToList();
    }

    public override string ExcludedHint => "当てはまる商品と、数の分からない商品を除いています。押すと除くのをやめます。";

    protected override string SummaryHead => $"{Label}{(HasSources ? $"（{_source?.Label}）" : string.Empty)}";

    protected override string SummaryJoiner => " ";

    protected override string SummaryBody
    {
        get
        {
            var parts = new[]
            {
                Min is { } min ? $"{min.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以上" : null,
                Max is { } max ? $"{max.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以下" : null,
            }.OfType<string>().ToList();

            var outliers = IgnoresOutliersNow && _outlierCount > 0 ? "（外れ値を除く）" : string.Empty;
            var all = SupportsMatchAll && _matchAll ? "（すべての価格が範囲内）" : string.Empty;
            var unpriced = IncludesUnpricedNow ? "・" + UnpricedLabel : string.Empty;
            return string.Join(" ", parts) + outliers + all + unpriced;
        }
    }

    /// <summary>足したときの姿に戻す（上下とも効かせ、幅は端から端まで）。</summary>
    public override void Clear()
    {
        _minEnabled = true;
        _maxEnabled = true;
        _ignoreOutliers = true;
        _matchAll = false;
        _includeUnpriced = false;
        _valuesFromState = false;
        _defaultsApplied = false;
        OnPropertyChanged(nameof(MinEnabled));
        OnPropertyChanged(nameof(MaxEnabled));
        OnPropertyChanged(nameof(IgnoreOutliers));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(IncludeUnpriced));
        RefreshBounds();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Min = _minText,
            Max = _maxText,
            MinEnabled = _minEnabled,
            MaxEnabled = _maxEnabled,
            IgnoreOutliers = _ignoreOutliers,
            MatchAll = SupportsMatchAll && _matchAll,

            // 補助の切り替えの欄（三択と同じ欄）を使う。価格の条件の形は増えない
            Flag = IncludesUnpricedNow,
            Choice = _source?.Key,
        };

    protected override void Read(SearchModuleState state)
    {
        _minText = state.Min ?? string.Empty;
        _maxText = state.Max ?? string.Empty;
        _minEnabled = state.MinEnabled;
        _maxEnabled = state.MaxEnabled;
        _ignoreOutliers = state.IgnoreOutliers;
        _matchAll = SupportsMatchAll && state.MatchAll;
        _includeUnpriced = SupportsUnpriced && state.Flag;
        OnPropertyChanged(nameof(IgnoreOutliers));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(IncludeUnpriced));

        // 前に入れていた数があるなら、端から端までの既定で上書きしない。
        // 空で残っていたもの（端という意味）は、端の数を入れて見えるようにする
        _valuesFromState = _minText.Length > 0 || _maxText.Length > 0;
        _source = Sources.FirstOrDefault(option => option.Key == state.Choice) ?? Sources.FirstOrDefault();
        OnPropertyChanged(nameof(MinText));
        OnPropertyChanged(nameof(MaxText));
        OnPropertyChanged(nameof(MinEnabled));
        OnPropertyChanged(nameof(MaxEnabled));
        OnPropertyChanged(nameof(HasMin));
        OnPropertyChanged(nameof(HasMax));
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(UnpricedLabel));
        RefreshBounds();
    }
}
