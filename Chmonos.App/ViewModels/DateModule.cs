using System.Globalization;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 日付の範囲（ユーザ案「カレンダー」）。欄に打つか、右のカレンダーで選ぶ。指定した日をまるまる含む。
/// 打った文字の読み方は <see cref="DateText"/>（年の無い日付は今日以前で最も近い日、月だけなら月初め／月末）。
/// </summary>
public sealed class DateModule : SearchModule
{
    /// <summary>商品の日付のどれかが条件に当たるか。日付を1つも持たない商品は false。</summary>
    private readonly Func<ItemRecord, Func<DateOnly, bool>, bool> _any;
    private string _sinceText = string.Empty;
    private string _tillText = string.Empty;
    private bool _sinceEnabled = true;
    private bool _tillEnabled = true;
    private bool _valuesFromState;
    private bool _defaultsApplied;
    private DateOnly? _dataSince;
    private DateOnly? _dataTill;

    /// <summary>日付が1つの項目（公開日）。</summary>
    public DateModule(SearchModuleKind kind, Func<ItemRecord, DateOnly?> value)
        : this(kind, (item, predicate) => value(item) is { } date && predicate(date))
    {
    }

    /// <summary>
    /// 日付をいくつも持つ項目（入手日：購入記録ごとの日付。メモ45）。**どれか1つが範囲に入れば当たる**（2-A）。
    /// 一覧を作らずに照らせるよう、当てる条件を渡して「どれかが当たるか」を答えさせる（全件を照らすので）。
    /// </summary>
    public DateModule(SearchModuleKind kind, Func<ItemRecord, Func<DateOnly, bool>, bool> any)
        : base(kind)
        => _any = any;

    /// <summary>
    /// 日付をいくつも持ち、**最初の1つだけで見るか全部で見るかを選べる**項目（入手日・ユーザ判断 2026-10-06・メモ82・83、判断8）。
    /// <paramref name="first"/> は商品の代表の日付（購入の日付のうち最も早い物、無ければ商品の入手日）。既定は「最初の購入のみ」。
    /// </summary>
    public DateModule(SearchModuleKind kind, Func<ItemRecord, Func<DateOnly, bool>, bool> any, Func<ItemRecord, DateOnly?> first)
        : this(kind, any)
    {
        _first = first;
        PurchaseScopes = [new ChoiceOption(FirstPurchase, "最初の購入のみ"), new ChoiceOption(AllPurchases, "すべての購入")];
        _purchaseScope = PurchaseScopes[0];
    }

    /// <summary>最初の購入だけで見る（状態の <c>choice</c> の値）。</summary>
    public const string FirstPurchase = "first";

    /// <summary>購入ごとの日付の全部で見る（どれか1つが範囲に入れば当たる）。</summary>
    public const string AllPurchases = "all";

    private readonly Func<ItemRecord, DateOnly?>? _first;
    private ChoiceOption? _purchaseScope;

    /// <summary>「最初の購入のみ」「すべての購入」（入手日だけ。公開日は空）。</summary>
    public IReadOnlyList<ChoiceOption> PurchaseScopes { get; } = [];

    public bool HasPurchaseScopes => PurchaseScopes.Count > 0;

    /// <summary>
    /// 最初の購入だけで見るか（既定）、購入ごとの日付の全部で見るか。
    /// 切り替えても打った日付は残す——「2025年3月」を入れたまま、買い足した分も含めて見直す使い方を崩さない。空欄が指す手元の端だけ取り直す。
    /// </summary>
    public ChoiceOption? PurchaseScope
    {
        get => _purchaseScope;
        set
        {
            if (value is not null && SetField(ref _purchaseScope, value))
            {
                RefreshBounds();
                NotifyChanged();
            }
        }
    }

    private bool FirstOnly => _first is not null && _purchaseScope?.Key != AllPurchases;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// 手元の商品の日付の全部。両端（足したときの既定）をここから出す。検索側が入れる。
    /// 引数は最初の購入だけで見ているか（入手日。最初の購入だけなら、買い足した日は端に入れない）。
    /// </summary>
    public Func<bool, IEnumerable<DateOnly>>? AllDatesOf { get; set; }

    public string SinceText
    {
        get => _sinceText;
        set
        {
            if (SetField(ref _sinceText, value ?? string.Empty))
            {
                // 始まりは終わりを越えられない（数の範囲と同じ作法）
                FixCrossing(moveSince: true);
                RaiseSince();
                NotifyChangedSoon();
            }
        }
    }

    public string TillText
    {
        get => _tillText;
        set
        {
            if (SetField(ref _tillText, value ?? string.Empty))
            {
                FixCrossing(moveSince: false);
                RaiseTill();
                NotifyChangedSoon();
            }
        }
    }

    /// <summary>
    /// 始まり・終わりをそれぞれ効かせるか（既定は両方・ユーザ指示 2026-09-16「カレンダーにもトグルをつけた方がわかりやすい」）。
    ///
    /// 空欄を「制限なし」と読ませるより、切ってあることが見えた方が分かる。切った側は日付を残したまま効かない。
    /// </summary>
    public bool SinceEnabled
    {
        get => _sinceEnabled;
        set
        {
            if (SetField(ref _sinceEnabled, value))
            {
                FixCrossing(moveSince: true);
                RaiseSince();
                NotifyChanged();
            }
        }
    }

    public bool TillEnabled
    {
        get => _tillEnabled;
        set
        {
            if (SetField(ref _tillEnabled, value))
            {
                FixCrossing(moveSince: false);
                RaiseTill();
                NotifyChanged();
            }
        }
    }

    /// <summary>効いている始まりの日。切っていれば null。欄が空なら手元で一番古い日を境にする。</summary>
    public DateOnly? Since => _sinceEnabled ? DateText.Parse(_sinceText, isEnd: false, Today) ?? _dataSince : null;

    /// <summary>効いている終わりの日。切っていれば null。欄が空なら手元で一番新しい日を境にする。</summary>
    public DateOnly? Till => _tillEnabled ? DateText.Parse(_tillText, isEnd: true, Today) ?? _dataTill : null;

    /// <summary>
    /// 両端を手元の商品から取り直す。**足したときの日付は一番古い日〜一番新しい日**（数の範囲と同じ）。
    /// 日付の分かる商品が1件も無いときは、両方切って足す（足した瞬間に0件になるのを避ける）。
    /// </summary>
    public void RefreshBounds()
    {
        // 空欄の境が指す日（手元の端）が変わりうる
        Unprepare();
        var dates = (AllDatesOf?.Invoke(FirstOnly) ?? []).ToList();
        _dataSince = dates.Count == 0 ? null : dates.Min();
        _dataTill = dates.Count == 0 ? null : dates.Max();

        if (!_valuesFromState && !_defaultsApplied)
        {
            _defaultsApplied = true;
            _sinceText = DateTextOf(_dataSince);
            _tillText = DateTextOf(_dataTill);
            _sinceEnabled = dates.Count > 0;
            _tillEnabled = dates.Count > 0;
            OnPropertyChanged(nameof(SinceText));
            OnPropertyChanged(nameof(TillText));
            OnPropertyChanged(nameof(SinceEnabled));
            OnPropertyChanged(nameof(TillEnabled));
        }

        RaiseSince();
        RaiseTill();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    private static string DateTextOf(DateOnly? date)
        => date is { } value ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>始まりが終わりより後になったら、いま触った側を相手に合わせる（数の範囲と同じ）。</summary>
    private void FixCrossing(bool moveSince)
    {
        if (!_sinceEnabled || !_tillEnabled || Since is not { } since || Till is not { } till || since <= till)
        {
            return;
        }

        if (moveSince)
        {
            _sinceText = DateTextOf(till);
            OnPropertyChanged(nameof(SinceText));
        }
        else
        {
            _tillText = DateTextOf(since);
            OnPropertyChanged(nameof(TillText));
        }
    }

    /// <summary>カレンダーで選んだ日（欄の文字と同じもの）。</summary>
    public DateTime? SinceDate
    {
        get => Since?.ToDateTime(TimeOnly.MinValue);
        set => SinceText = value is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
    }

    public DateTime? TillDate
    {
        get => Till?.ToDateTime(TimeOnly.MinValue);
        set => TillText = value is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>
    /// どう読んだかを添える（「9/1」を去年と読んだ、などが分かるように）。読めなければ例を出す。
    ///
    /// **切っている側は「読めません」と言わない。**切ると値が無い扱いになるので、
    /// 日付が入っているのに「日付として読めません」と出ていた（2026-09-16 の確かめで見つけた）。
    /// </summary>
    public string SinceNote => Note(_sinceEnabled, _sinceText, isEnd: false, _dataSince, "始まり", "から");

    public string TillNote => Note(_tillEnabled, _tillText, isEnd: true, _dataTill, "終わり", "まで");

    /// <summary>入れた日付を消す手段を出すか（ユーザ指示 2026-09-16）。</summary>
    public bool HasSince => _sinceText.Length > 0;

    public bool HasTill => _tillText.Length > 0;

    public RelayCommand ClearSinceCommand => _clearSince ??= new RelayCommand(() => SinceText = string.Empty);

    public RelayCommand ClearTillCommand => _clearTill ??= new RelayCommand(() => TillText = string.Empty);

    private RelayCommand? _clearSince;
    private RelayCommand? _clearTill;

    protected override bool HasCondition => Since is not null || Till is not null;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        EnsurePrepared(context);
        if (_preparedSince is null && _preparedTill is null)
        {
            return true;
        }

        // 日付が分からない商品は、日付で絞った時点で外す。「値が小さい」ではなく「値が無い」ので、範囲のどこにも当てはまらない
        return FirstOnly
            ? _first!(item) is { } first && _inRange(first)
            : _any(item, _inRange);
    }

    /// <summary>
    /// 境の日付を読むのは絞り込みの1回で1回だけ（案c）。前は商品ごとに4回読み直していて（条件があるかを見る分と照らす分）、
    /// 公開日の条件が照らす重さのいちばん上に来ていた（1件 900ns のうち読み直しが大半）。範囲に入るかの関数もここで1回だけ作る
    /// </summary>
    protected override void PrepareCore(SearchModuleContext context)
    {
        var since = Since;
        var till = Till;
        (_preparedSince, _preparedTill) = (since, till);
        _inRange = date => (since is null || date >= since) && (till is null || date <= till);
    }

    private DateOnly? _preparedSince;
    private DateOnly? _preparedTill;
    private Func<DateOnly, bool> _inRange = _ => true;
    private static readonly Func<DateOnly, bool> AnyDate = _ => true;

    public override bool SupportsExclude => true;

    public override string ExcludedHint => "当てはまる商品と、日付の分からない商品を除いています。押すと除くのをやめます。";

    /// <summary>
    /// 日付が分かっていて、範囲の外の商品。日付の分からない商品は、除くときも外す（ユーザ判断 2026-10-01）。
    /// 日付をいくつも持つ商品は、どれも範囲に入らないときだけ残る（当たる＝どれか1つが入る、の裏）。
    /// 最初の購入だけで見るときは、最初の日付が範囲の外の商品（後で買い足した日が範囲に入っていても残る）。
    /// </summary>
    protected override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
    {
        EnsurePrepared(context);
        var known = FirstOnly ? _first!(item) is not null : _any(item, AnyDate);
        return (_preparedSince is null && _preparedTill is null) || (known && !Matches(item, context));
    }

    /// <summary>入手日は、最初の購入か全てかを頭に添える（同じ日付でも当たる物が違う）。</summary>
    protected override string SummaryHead => HasPurchaseScopes
        ? $"{Label}（{(FirstOnly ? "最初の購入" : "すべての購入")}）"
        : base.SummaryHead;

    protected override string SummaryJoiner => " ";

    protected override string SummaryBody
    {
        get
        {
            var parts = new[]
            {
                Since is { } since ? $"{since:yyyy-MM-dd}から" : null,
                Till is { } till ? $"{till:yyyy-MM-dd}まで" : null,
            }.OfType<string>();

            return string.Join(" ", parts);
        }
    }

    /// <summary>足したときの姿に戻す（両方効かせ、一番古い日〜一番新しい日）。</summary>
    public override void Clear()
    {
        _sinceEnabled = true;
        _tillEnabled = true;
        _valuesFromState = false;
        _defaultsApplied = false;
        _purchaseScope = PurchaseScopes.FirstOrDefault();
        OnPropertyChanged(nameof(SinceEnabled));
        OnPropertyChanged(nameof(TillEnabled));
        OnPropertyChanged(nameof(PurchaseScope));
        RefreshBounds();
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Min = _sinceText,
            Max = _tillText,
            MinEnabled = _sinceEnabled,
            MaxEnabled = _tillEnabled,
            Choice = _purchaseScope?.Key,
        };

    protected override void Read(SearchModuleState state)
    {
        _sinceText = state.Min ?? string.Empty;
        _tillText = state.Max ?? string.Empty;
        _sinceEnabled = state.MinEnabled;
        _tillEnabled = state.MaxEnabled;

        // 書いていない（公開日・手で消した）ときは既定の「最初の購入のみ」
        _purchaseScope = PurchaseScopes.FirstOrDefault(option => option.Key == state.Choice) ?? PurchaseScopes.FirstOrDefault();
        OnPropertyChanged(nameof(PurchaseScope));

        // 前に入れていた日付があるなら、両端の既定で上書きしない
        _valuesFromState = _sinceText.Length > 0 || _tillText.Length > 0;

        OnPropertyChanged(nameof(SinceText));
        OnPropertyChanged(nameof(TillText));
        OnPropertyChanged(nameof(SinceEnabled));
        OnPropertyChanged(nameof(TillEnabled));
        RefreshBounds();
    }

    private static string Note(bool enabled, string text, bool isEnd, DateOnly? fallback, string side, string suffix)
    {
        if (!enabled)
        {
            return $"{side}は指定なし";
        }

        if (text.Trim().Length == 0)
        {
            return fallback is { } edge ? $"{edge:yyyy年M月d日}{suffix}（手元の端）" : string.Empty;
        }

        return DateText.Parse(text, isEnd, Today) is { } date
            ? $"{date:yyyy年M月d日}{suffix}"
            : "日付として読めません。2026/9/1 のように入れてください。";
    }

    /// <summary>効かせていない側に、使えない理由を出す（`ui-rules.md`・E11）。</summary>
    public string SinceHint => SinceEnabled
        ? "日付は「2026-09-01」「9/1」のように入れられます。"
        : "始まりの日を使うには、左のチェックを入れてください。";

    public string TillHint => TillEnabled
        ? "日付は「2026-09-01」「9/1」のように入れられます。"
        : "終わりの日を使うには、左のチェックを入れてください。";

    private void RaiseSince()
    {
        OnPropertyChanged(nameof(SinceHint));
        OnPropertyChanged(nameof(SinceDate));
        OnPropertyChanged(nameof(SinceNote));
        OnPropertyChanged(nameof(HasSince));
    }

    private void RaiseTill()
    {
        OnPropertyChanged(nameof(TillHint));
        OnPropertyChanged(nameof(TillDate));
        OnPropertyChanged(nameof(TillNote));
        OnPropertyChanged(nameof(HasTill));
    }
}
