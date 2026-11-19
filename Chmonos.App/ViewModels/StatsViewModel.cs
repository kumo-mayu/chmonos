using System.Collections.ObjectModel;
using System.Windows;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>月別グラフの棒1本。高さは決め打ちの描画領域に対する実寸で持つ。</summary>
public sealed class SpendBarViewModel
{
    /// <summary>グラフの描画領域の高さ。ここを基準に棒の高さを決める。</summary>
    public const double ChartHeight = 140;

    /// <summary>軸に出す短い名前（月は「09」、年は「2025」）。</summary>
    public required string Label { get; init; }

    /// <summary>月の下に添える年。左端と1月にだけ入れ、ほかは空（年が変わる所だけで読めるので）。</summary>
    public string YearLabel { get; init; } = string.Empty;

    /// <summary>吹き出しに出す、年まで入った名前（「2025年9月」）。</summary>
    public required string FullLabel { get; init; }

    public required long SpentYen { get; init; }

    public required int ItemCount { get; init; }

    public required double BarHeight { get; init; }

    /// <summary>山と、いちばん新しい区切りにだけ金額を出す。全部に出すと読めなくなる。</summary>
    public string ValueLabel { get; init; } = string.Empty;

    public bool IsPeak { get; init; }

    public string Tooltip => $"{FullLabel}：¥{SpentYen:N0}／{ItemCount} 件";
}

/// <summary>横棒1本。棒の長さはGridの星取りで表すので、長さをそのまま持つ。</summary>
public sealed class StatsRowViewModel : ViewModelBase
{
    public required string Label { get; init; }

    /// <summary>右端に出す値（金額・容量・件数）。</summary>
    public required string ValueText { get; init; }

    /// <summary>値の後ろに小さく添える補足。無ければ空。</summary>
    public string SubText { get; init; } = string.Empty;

    /// <summary>いちばん大きい行を1としたときの比。</summary>
    public required double Ratio { get; init; }

    public GridLength BarLength => new(Math.Max(Ratio, 0), GridUnitType.Star);

    public GridLength RestLength => new(Math.Max(1 - Ratio, 0), GridUnitType.Star);

    /// <summary>集計の外側を示す行（「未設定」など）。控えめに出す。</summary>
    public bool IsResidual { get; init; }

    public RelayCommand? OpenCommand { get; init; }

    public bool CanOpen => OpenCommand is not null;

    public string Tooltip { get; init; } = string.Empty;
}

/// <summary>積み残しの1行。</summary>
/// <summary>
/// 重複1組。**どれとどれか**と**消せばいくら空くか**を出す。
/// </summary>
public sealed class DuplicateRowViewModel(DuplicateGroup group)
{
    public string Label { get; } = group.Label;

    public string ReclaimText { get; } = Core.Models.DisplayText.Size(group.ReclaimableBytes) + " 空きます";

    /// <summary>
    /// 何が起きているかの一言。消し方が違うので種類を言い分ける。
    /// </summary>
    public string KindText { get; } = group.Kind switch
    {
        DuplicateKind.ArchiveAndUnpacked => "zipと展開したフォルダの両方があります。空く量は展開した方の分です",
        _ when group.CrossesItems => $"{group.Places.Count} 箇所にあり、別の商品にも紐付いています",
        _ => $"{group.Places.Count} 箇所に同じ中身があります",
    };

    /// <summary>置いてある場所。**消す前に見えている必要がある。**</summary>
    public IReadOnlyList<string> Places { get; } = group.Places.Select(place => place.Path).ToList();

    /// <summary>どの商品に紐付いているか。商品をまたぐときだけ意味がある。</summary>
    public string ItemsText { get; } = string.Join(
        "・",
        group.Places.Select(place => place.ItemName).Distinct());
}

public sealed class BacklogRowViewModel
{
    public required string Label { get; init; }

    public required int Count { get; init; }

    public required RelayCommand OpenCommand { get; init; }

    /// <summary>注意を促す度合い。色分けにだけ使う。</summary>
    public required string Severity { get; init; }
}

/// <summary>
/// 統計の欄を開いているか。画面は <c>Panels[shops]</c> のように欄の鍵で結ぶ（open.md 110）。
/// 覚えるのは起動の間ずっと居る <see cref="Services.StatsPanels"/>。ここは画面との橋渡しだけ
/// </summary>
public sealed class StatsPanelsViewModel(Services.StatsPanels memory)
{
    public bool this[string key]
    {
        get => memory.IsOpen(key);
        set => memory.SetOpen(key, value);
    }
}

/// <summary>期間の選び方の種類（ユーザ判断 2026-11-18・open.md 104）。</summary>
public enum StatsRangeKind
{
    LastMonths,
    ThisMonth,
    LastMonth,
    ThisYear,
    LastYear,

    /// <summary>年を選ぶ（右に年の欄が出る）。</summary>
    PickYear,

    /// <summary>開始と終了の日をカレンダーで選ぶ。</summary>
    Custom,

    /// <summary>絞らない。日付の分からない商品も入る。</summary>
    All,
}

/// <summary>集計期間の選び方。</summary>
public sealed class StatsRangeOption
{
    public required string Label { get; init; }

    public required StatsRangeKind Kind { get; init; }

    /// <summary>「直近 n ヶ月」の n。</summary>
    public int Months { get; init; }
}

/// <summary>
/// 統計画面。
///
/// 集計そのものは <see cref="StatsService"/> が持ち、ここは見せ方だけを決める。
/// 数え方の但し書き（推定で埋めた日付・金額の記録が無いもの）は畳まずに本文へ出す。
/// 集計結果を信じてよいかは、欠けの量が分からないと判断できないため。
/// </summary>
public sealed class StatsViewModel : ViewModelBase, ILeavingScreen
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    /// <summary>
    /// 離れたら読み込みを取り消す（既知 P8）。この画面は開くたびに作り直すので、離れた後の集計は誰も見ない。
    /// 全商品を読んで数えるので件数に比例して重く、離れた後も走り切って次の画面の読み込みとディスクを取り合っていた。読むだけなので止めてよい
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    private StatsSnapshot? _snapshot;
    private StatsRangeOption _range;
    private bool _isLoading = true;

    /// <param name="today">「今月」「直近 n ヶ月」の基準の日。試験で日付を決めるときだけ渡す（既定は今日）</param>
    public StatsViewModel(AppServiceContainer services, MainViewModel main, DateOnly? today = null)
    {
        _services = services;
        _main = main;
        _range = Ranges[0];
        _today = today ?? DateOnly.FromDateTime(DateTime.Today);
        _selectedYear = _today.Year;

        // 「開始と終了を選ぶ」を選んだ時に、何も選ばないうちから読める期間にしておく（今月の初めから今日まで）
        _customFrom = new DateTime(_today.Year, _today.Month, 1);
        _customTo = _today.ToDateTime(TimeOnly.MinValue);
        Panels = new StatsPanelsViewModel(services.StatsPanels);

        LoadAsync().Forget();
    }

    /// <summary>欄を開いているか（次の起動まで覚える）。</summary>
    public StatsPanelsViewModel Panels { get; }

    public void OnLeaving() => _leaving.Cancel();

    /// <summary>
    /// 期間の選び方（ユーザ判断 2026-11-18・open.md 104）。既定は前と同じ直近12ヶ月。
    /// 「全期間」は前の「全期間・年別」：棒の並べ方は期間の長さで決めるので「年別」は付けない
    /// </summary>
    public static IReadOnlyList<StatsRangeOption> Ranges { get; } =
    [
        new StatsRangeOption { Label = "直近 12 ヶ月", Kind = StatsRangeKind.LastMonths, Months = 12 },
        new StatsRangeOption { Label = "直近 24 ヶ月", Kind = StatsRangeKind.LastMonths, Months = 24 },
        new StatsRangeOption { Label = "今月", Kind = StatsRangeKind.ThisMonth },
        new StatsRangeOption { Label = "先月", Kind = StatsRangeKind.LastMonth },
        new StatsRangeOption { Label = "今年", Kind = StatsRangeKind.ThisYear },
        new StatsRangeOption { Label = "去年", Kind = StatsRangeKind.LastYear },
        new StatsRangeOption { Label = "年を選ぶ", Kind = StatsRangeKind.PickYear },
        new StatsRangeOption { Label = "開始と終了を選ぶ", Kind = StatsRangeKind.Custom },
        new StatsRangeOption { Label = "全期間", Kind = StatsRangeKind.All },
    ];

    public StatsRangeOption Range
    {
        get => _range;
        set
        {
            if (SetField(ref _range, value))
            {
                OnPropertyChanged(nameof(IsPickYear));
                OnPropertyChanged(nameof(IsCustom));
                RecomputePeriod();
            }
        }
    }

    // ---- 期間（open.md 104） ----

    private readonly DateOnly _today;
    private StatsSource? _source;

    /// <summary>選んだ期間の集計。全期間なら <see cref="_snapshot"/> と同じ物</summary>
    private StatsSnapshot? _period;

    private StatsNarrowed? _narrowed;
    private DateOnly? _latestDate;
    private int _periodVersion;

    /// <summary>期間で変わる欄の元。期間の集計がまだなら全期間</summary>
    private StatsSnapshot? Period => _period ?? _snapshot;

    public bool IsAllTime => _range.Kind == StatsRangeKind.All;

    public bool IsPickYear => _range.Kind == StatsRangeKind.PickYear;

    public bool IsCustom => _range.Kind == StatsRangeKind.Custom;

    /// <summary>「年を選ぶ」の候補。日付の分かる購入のある年と今年（新しい順）</summary>
    public IReadOnlyList<int> Years { get; private set; } = [];

    private int _selectedYear;

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetField(ref _selectedYear, value) && IsPickYear)
            {
                RecomputePeriod();
            }
        }
    }

    private DateTime? _customFrom;
    private DateTime? _customTo;

    /// <summary>「開始と終了を選ぶ」の開始の日。空なら始まりを切らない</summary>
    public DateTime? CustomFrom
    {
        get => _customFrom;
        set
        {
            if (SetField(ref _customFrom, value))
            {
                OnPropertyChanged(nameof(CustomFromText));
                if (IsCustom)
                {
                    RecomputePeriod();
                }
            }
        }
    }

    /// <summary>「開始と終了を選ぶ」の終了の日。空なら終わりを切らない</summary>
    public DateTime? CustomTo
    {
        get => _customTo;
        set
        {
            if (SetField(ref _customTo, value))
            {
                OnPropertyChanged(nameof(CustomToText));
                if (IsCustom)
                {
                    RecomputePeriod();
                }
            }
        }
    }

    public string CustomFromText => _customFrom is { } from ? $"{from:yyyy/MM/dd}" : "開始の日";

    public string CustomToText => _customTo is { } to ? $"{to:yyyy/MM/dd}" : "終了の日";

    /// <summary>
    /// 選んだ期間。全期間なら null（絞らない）。片方だけ選んだ「開始と終了」は、選んでいない端を開けておく
    /// </summary>
    public StatsDateRange? SelectedRange => _range.Kind switch
    {
        StatsRangeKind.LastMonths => StatsPeriods.LastMonths(_today, _range.Months, _latestDate),
        StatsRangeKind.ThisMonth => StatsPeriods.ThisMonth(_today),
        StatsRangeKind.LastMonth => StatsPeriods.LastMonth(_today),
        StatsRangeKind.ThisYear => StatsPeriods.Year(_today.Year),
        StatsRangeKind.LastYear => StatsPeriods.Year(_today.Year - 1),
        StatsRangeKind.PickYear => StatsPeriods.Year(_selectedYear),
        StatsRangeKind.Custom => StatsPeriods.Between(
            _customFrom is { } from ? DateOnly.FromDateTime(from) : DateOnly.MinValue,
            _customTo is { } to ? DateOnly.FromDateTime(to) : DateOnly.MaxValue),
        _ => null,
    };

    /// <summary>期間で変わる欄の見出しの横に出す、その欄が見ている期間（ユーザ判断 2026-11-18：どの期間を見ているか分かるように）</summary>
    public string PeriodText => SelectedRange is { } range ? StatsPeriods.Label(range) : "全期間";

    /// <summary>
    /// 期間に入らなかった、日付の分からない商品の数。期間で変わる欄の全部に効くので、グラフの下ではなく期間の欄のそばに出す
    /// </summary>
    public string PeriodNote => IsAllTime || _narrowed is not { UndatedOwnedCount: > 0 } narrowed
        ? string.Empty
        : $"日付の分からない {narrowed.UndatedOwnedCount:N0} 件は、期間で変わる欄に入っていません。";

    public bool HasPeriodNote => PeriodNote.Length > 0;

    // タイルの2行目：期間の値（ユーザ判断 2026-11-18：所持・支出・ショップ数は合計と期間の両方を常に出す）

    public string PeriodOwnedText => $"{PeriodText} {Period?.OwnedCount ?? 0:N0}";

    public string PeriodSpentText => $"{PeriodText} ¥{Period?.SpentYen ?? 0:N0}";

    public string PeriodGivenText => $"{PeriodText} ¥{Period?.GivenSpentYen ?? 0:N0}";

    public string PeriodGiftedText => $"{PeriodText} ¥{Period?.GiftedValueYen ?? 0:N0}";

    public string PeriodShopCountText => $"{PeriodText} {Period?.ShopCount ?? 0:N0}";

    public bool ShowsPeriodTiles => !IsAllTime;

    /// <summary>
    /// 期間を替えたら、期間で変わる欄だけを集計し直す。全商品は読み直さない（読み込みの時の控えを絞る）。
    /// 集計は裏で走らせ、続けて替えたら古い結果は捨てる
    /// </summary>
    private void RecomputePeriod()
    {
        // 読み込みの途中で替えられたら、数だけ進めて終わる（読み込みが済んだ所で版の違いを見て集計し直す）
        var version = ++_periodVersion;
        if (_source is not { } source)
        {
            return;
        }

        var range = SelectedRange;
        var useDetected = _services.Settings.UseDetectedVariationTargets;
        var token = _leaving.Token;
        Task.Run(() =>
        {
            if (range is not { } selected)
            {
                return ((StatsSnapshot?)null, (StatsNarrowed?)null);
            }

            var narrowed = StatsPeriods.Narrow(source.Dated, selected);
            return (StatsPeriods.Build(narrowed, source.Registry, source.UnresolvedCount, useDetected), narrowed);
        }, token).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                RunOnUiThread(() =>
                {
                    if (token.IsCancellationRequested || version != _periodVersion)
                    {
                        return;
                    }

                    (_period, _narrowed) = task.Result;
                    _period ??= _snapshot;
                    RebuildPeriodPanels();
                });
            }
        }, TaskScheduler.Default);
    }

    /// <summary>期間で変わる欄（open.md 104 の分け方）を組み直す</summary>
    private void RebuildPeriodPanels()
    {
        RebuildChart();
        RebuildShops();
        if (Period is not null)
        {
            Fill(PriceBuckets, Period.PriceBuckets.Select(b => (b.Label, (long)b.Count, $"{b.Count} 件")));
            Fill(CategorySpend, Period.CategorySpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
            Fill(UserTagSpend, Period.UserTagSpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
            Fill(CategoryCounts, Period.CategoryCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
            Fill(UserTagCounts, Period.UserTagCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
        }

        foreach (var name in new[]
        {
            nameof(IsAllTime), nameof(PeriodText), nameof(PeriodNote), nameof(HasPeriodNote),
            nameof(PeriodOwnedText), nameof(PeriodSpentText), nameof(PeriodGivenText), nameof(PeriodGiftedText), nameof(PeriodShopCountText),
            nameof(ShowsPeriodTiles), nameof(UnpricedNote), nameof(HasUnpricedNote), nameof(GiftText), nameof(FreeText),
        })
        {
            OnPropertyChanged(name);
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetField(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(HasData));
            }
        }
    }

    public bool HasData => !IsLoading && _snapshot is not null && _snapshot.OwnedCount > 0;

    /// <summary>1件も所持していないとき。集計するものが無いので誘導だけ出す。</summary>
    public bool IsEmpty => !IsLoading && (_snapshot is null || _snapshot.OwnedCount == 0);

    public ObservableCollection<SpendBarViewModel> Chart { get; } = [];

    public ObservableCollection<StatsRowViewModel> Avatars { get; } = [];

    public ObservableCollection<StatsRowViewModel> Shops { get; } = [];

    public ObservableCollection<StatsRowViewModel> Categories { get; } = [];

    public ObservableCollection<BacklogRowViewModel> Backlog { get; } = [];

    // ── 選んで足した項目 ──

    public ObservableCollection<StatsRowViewModel> PriceBuckets { get; } = [];

    public ObservableCollection<StatsRowViewModel> WishBuckets { get; } = [];

    public ObservableCollection<StatsRowViewModel> CategorySpend { get; } = [];

    public ObservableCollection<StatsRowViewModel> UserTagSpend { get; } = [];

    public ObservableCollection<StatsRowViewModel> CategoryCounts { get; } = [];

    public ObservableCollection<StatsRowViewModel> UserTagCounts { get; } = [];


    public ObservableCollection<StatsRowViewModel> HeavyItems { get; } = [];

    public ObservableCollection<StatsRowViewModel> Wearables { get; } = [];

    public ObservableCollection<PriceChangeRowViewModel> PriceChanges { get; } = [];

    public ObservableCollection<AttributeDistributionRowViewModel> AttributeDistributions { get; } = [];

    public ObservableCollection<StatsRowViewModel> AttributeCorrelations { get; } = [];

    // ---- 見出しのタイル ----

    public string OwnedText => $"{_snapshot?.OwnedCount ?? 0:N0}";

    /// <summary>情報だけ持っていて所持していないものは集計に入らないので、その差を書いておく。</summary>
    public string OwnedSubText
    {
        get
        {
            if (_snapshot is null)
            {
                return string.Empty;
            }

            var unowned = _snapshot.KnownCount - _snapshot.OwnedCount;
            return unowned <= 0 ? string.Empty : $"ほかに情報だけ {unowned:N0} 件";
        }
    }

    public string SpentText => $"¥{_snapshot?.SpentYen ?? 0:N0}";

    /// <summary>
    /// 贈答があるときだけ「自分用の」と断る。
    /// 贈答が無い人に毎回この断りを見せても、何と区別しているのか分からない。
    /// </summary>
    public string SpentLabel => HasGiven ? "自分用の累計支出" : "累計支出";

    public string SpentSubText
    {
        get
        {
            if (_snapshot is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            // 「ギフト」だけだと貰ったのか贈ったのか読めない。貰い物は自分が払っていない側
            if (_snapshot.GiftedCount > 0)
            {
                parts.Add($"貰った {_snapshot.GiftedCount} 件");
            }

            if (_snapshot.FreeCount > 0)
            {
                parts.Add($"無料 {_snapshot.FreeCount} 件");
            }

            return parts.Count == 0 ? string.Empty : $"{string.Join(" / ", parts)}は別枠";
        }
    }

    /// <summary>
    /// 贈答に使った額。
    ///
    /// 累計支出と分けるのは、統計の集計対象を「ファイルを持つitem」と決めてあるため。
    /// 贈った商品は手元にファイルが来ないので、そこから外れる。混ぜると集計対象の定義が壊れる。
    /// </summary>
    public string GivenText => $"¥{_snapshot?.GivenSpentYen ?? 0:N0}";

    /// <summary>件数ではなく回数。同じ商品を3人に贈れば3回。</summary>
    public string GivenSubText => $"贈った {_snapshot?.GivenCount ?? 0} 回";

    public bool HasGiven => _snapshot is { GivenCount: > 0 };

    /// <summary>
    /// ギフトで貰った額（定価の合計）。自分が払った額ではないので累計支出には混ぜず、別のタイルにする
    /// （貰った行の <c>Price</c> は定価。open.md 2026-11-18 のメモ）。
    /// </summary>
    public string GiftedText => $"¥{_snapshot?.GiftedValueYen ?? 0:N0}";

    public string GiftedSubText => $"貰った {_snapshot?.GiftedItemCount ?? 0} 件・定価の合計";

    public bool HasGifted => _snapshot is { GiftedItemCount: > 0 };

    /// <summary>贈答が無ければタイルは4枚。贈った・貰ったは、あるものだけ1枚ずつ足す（無い枠を空けておく意味がない）。</summary>
    public int TileColumns => 4 + (HasGiven ? 1 : 0) + (HasGifted ? 1 : 0);

    /// <summary>金額に入っていない分。総額を額面どおり受け取られないように必ず出す。</summary>
    public string UnpricedNote => Period is not { UnpricedItemCount: > 0 } period
        ? string.Empty
        : IsAllTime
            ? $"金額の記録が無い商品が {period.UnpricedItemCount:N0} 件あります。その分は総額に入っていません。"
            : $"この期間に、金額の記録が無い商品が {period.UnpricedItemCount:N0} 件あります。その分は期間の支出に入っていません。";

    public bool HasUnpricedNote => UnpricedNote.Length > 0;

    /// <summary>
    /// 帯に出ている数と同じ商品（今の期間のぶん）を、編集の画面の順に積んで開く。
    /// 検索の選択の帯の「まとめて編集する」と同じ口（<see cref="MainViewModel.ShowEditAsync"/>）。
    /// 数えている物は、無料・貰った物だけの商品も含む（0円と未入力は編集画面の空欄が 0 で記録されるため見分けられない）
    /// </summary>
    public RelayCommand EditUnpricedCommand => new(() =>
    {
        var ids = Period?.UnpricedItemIds ?? [];
        if (ids.Count > 0)
        {
            _main.ShowEditAsync(ids).Forget();
        }
    });

    public string SizeText => Core.Models.DisplayText.Size(_snapshot?.PhysicalBytes ?? 0);

    public string SizeSubText => _snapshot is null || _snapshot.DuplicateBytes <= 0
        ? string.Empty
        : $"うち重複コピー {Core.Models.DisplayText.Size(_snapshot.DuplicateBytes)}";

    public string ShopCountText => $"{_snapshot?.ShopCount ?? 0:N0}";

    // ---- 空けられる場所 ----

    /// <summary>
    /// 重複の一覧。空く量の大きい順。
    ///
    /// **合計だけでは触る場所が分からない**ので、組ごとに出す。
    /// </summary>
    /// <remarks>
    /// 集計1回につき1度だけ作る。前は読まれるたびに行を作り直していて、<see cref="HasDuplicates"/>・見出し・一覧の結び付けが
    /// それぞれ読むので、1回の表示で3回以上作り、一覧は毎回別の行（＝部品の作り直し）になっていた
    /// </remarks>
    public IReadOnlyList<DuplicateRowViewModel> Duplicates
    {
        get
        {
            if (!ReferenceEquals(_duplicatesOf, _snapshot))
            {
                _duplicatesOf = _snapshot;
                _duplicates = _snapshot?.Duplicates.Select(group => new DuplicateRowViewModel(group)).ToList() ?? [];
            }

            return _duplicates;
        }
    }

    private object? _duplicatesOf;
    private IReadOnlyList<DuplicateRowViewModel> _duplicates = [];

    public bool HasDuplicates => Duplicates.Count > 0;

    /// <summary>
    /// 重複の見出し。
    ///
    /// **無いことも言う。**黙って消すと、調べた結果ゼロだったのか
    /// 機能が無いのか分からない（メモが出ていないという指摘と同じ落とし穴）。
    /// </summary>
    public string DuplicateSummary => _snapshot is null
        ? string.Empty
        : HasDuplicates
            ? $"1つずつ残して他を消すと {Core.Models.DisplayText.Size(_snapshot.ReclaimableBytes)} 空きます"
            : "同じ中身を二重に持っているものはありません";

    // ---- 月別グラフの但し書き ----

    /// <summary>推定で埋めた日付がどれだけ混ざっているか。グラフの読み方が変わるので隠さない。</summary>
    public string ChartNote
    {
        get
        {
            if (_snapshot is null || Period is not { } period)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (period.FallbackDatedCount > 0)
            {
                parts.Add($"{period.FallbackDatedCount:N0} 件は入手日が未入力で、ファイルの日付で代えています");
            }

            // 期間を選んでいる間は、日付の分からない商品は期間の欄のそば（PeriodNote）で断る。グラフだけの話ではないので
            if (IsAllTime && _snapshot.UndatedCount > 0)
            {
                parts.Add($"日付の分からない {_snapshot.UndatedCount:N0} 件（¥{_snapshot.UndatedSpentYen:N0}）は含めていません");
            }

            return string.Join("。", parts);
        }
    }

    public bool HasChartNote => ChartNote.Length > 0;

    public bool HasChart => Chart.Count > 0;

    public string AxisTopText { get; private set; } = string.Empty;

    public string AxisMidText { get; private set; } = string.Empty;

    /// <summary>アバターの紐付けがまだ無いとき。数字ではなく次にやることを出す。</summary>
    public bool HasAvatars => Avatars.Count > 0;

    public bool HasBacklog => Backlog.Count > 0;

    private async Task LoadAsync()
    {
        // 読めなかったときも「読み込み中」を下ろす。成功した道でしか下ろしていなかったので、
        // 壊れた商品が1件あるだけで画面が空のまま戻らなかった（失敗そのものはログに残る）
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            RunOnUiThread(() => IsLoading = false);
        }
    }

    private async Task LoadCoreAsync()
    {
        var token = _leaving.Token;
        // アバターの欄は検索と同じ設定で、持っていないバリエーションの分を外す（open.md 106）
        var useDetected = _services.Settings.UseDetectedVariationTargets;
        // 全商品は1回だけ読み、期間を替えるたびには読み直さない（open.md 104）。期間の集計も読み込みと同じ裏で済ませる
        var range = SelectedRange;
        var option = _range;
        var version = _periodVersion;
        var (source, snapshot, period, narrowed, latest) = await Task.Run(async () =>
        {
            var source = await _services.Stats.LoadSourceAsync(token);
            var snapshot = StatsService.Build(source.Items, source.Registry, source.UnresolvedCount, useDetected);
            var latest = source.Dated.SelectMany(item => item.Purchases.Select(row => row.Date).Append(item.ItemDate)).Max();

            // 直近 n ヶ月の右端は先の日付の購入まで伸ばすので、最後の日付が分かってから期間を決め直す
            var selected = range is null ? null : (StatsDateRange?)(option.Kind == StatsRangeKind.LastMonths
                ? StatsPeriods.LastMonths(_today, option.Months, latest)
                : range);
            if (selected is not { } chosen)
            {
                return (source, snapshot, snapshot, (StatsNarrowed?)null, latest);
            }

            var narrowed = StatsPeriods.Narrow(source.Dated, chosen);
            return (source, snapshot, StatsPeriods.Build(narrowed, source.Registry, source.UnresolvedCount, useDetected), narrowed, latest);
        }, token);

        RunOnUiThread(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            _source = source;
            _snapshot = snapshot;
            _period = period;
            _narrowed = narrowed;
            _latestDate = latest;
            Years = snapshot.Years.Select(year => int.Parse(year.Key, System.Globalization.CultureInfo.InvariantCulture))
                .Append(_today.Year).Distinct().OrderByDescending(year => year).ToList();
            OnPropertyChanged(nameof(Years));
            IsLoading = false;

            RebuildPanels();
            RebuildPeriodPanels();

            // 読み込みの間に期間を替えられていたら、今の期間で集計し直す（読み込みは始めた時の期間で数えている）
            if (version != _periodVersion)
            {
                RecomputePeriod();
            }

            foreach (var name in new[]
            {
                nameof(OwnedText), nameof(OwnedSubText), nameof(SpentText), nameof(SpentSubText),
                nameof(SpentLabel), nameof(GivenText), nameof(GivenSubText), nameof(HasGiven),
                nameof(GiftedText), nameof(GiftedSubText), nameof(HasGifted),
                nameof(TileColumns),
                nameof(UnpricedNote), nameof(HasUnpricedNote), nameof(SizeText), nameof(SizeSubText),
                nameof(Duplicates), nameof(HasDuplicates), nameof(DuplicateSummary),
                nameof(ShopCountText), nameof(HasData), nameof(IsEmpty),
            })
            {
                OnPropertyChanged(name);
            }
        });
    }

    /// <summary>
    /// 月別（または年別）の棒を作り直す。
    /// 期間で切っても軸の目盛りは切った後の山に合わせる（見えていない月に合わせると棒が潰れる）。
    /// </summary>
    private void RebuildChart()
    {
        Chart.Clear();

        if (_snapshot is null || Period is not { } period0)
        {
            return;
        }

        // 日付の分かる購入が1件も無ければ、0円の目盛りだけ並べても読む物が無いので、今までどおりグラフごと出さない
        var source = _snapshot.Months.Count == 0 ? [] : ChartBars(period0, ChartRange(_snapshot, SelectedRange, _today));

        var peak = source.Count == 0 ? 0 : source.Max(bar => bar.Period.SpentYen);
        var scale = RoundUpAxis(peak);

        AxisTopText = $"¥{Format(scale)}";
        AxisMidText = $"¥{Format(scale / 2)}";

        for (var index = 0; index < source.Count; index++)
        {
            var (period, year, full) = source[index];
            var isPeak = peak > 0 && period.SpentYen == peak;
            var isLast = index == source.Count - 1;

            Chart.Add(new SpendBarViewModel
            {
                Label = period.Label,
                YearLabel = year,
                FullLabel = full,
                SpentYen = period.SpentYen,
                ItemCount = period.ItemCount,
                BarHeight = scale == 0 ? 0 : period.SpentYen / (double)scale * SpendBarViewModel.ChartHeight,
                ValueLabel = (isPeak || isLast) && period.SpentYen > 0 ? $"¥{Format(period.SpentYen)}" : string.Empty,
                IsPeak = isPeak,
            });
        }

        OnPropertyChanged(nameof(HasChart));
        OnPropertyChanged(nameof(ChartNote));
        OnPropertyChanged(nameof(HasChartNote));
        OnPropertyChanged(nameof(AxisTopText));
        OnPropertyChanged(nameof(AxisMidText));
    }

    /// <summary>
    /// グラフに並べる範囲。選んだ期間のうち開いた端（全期間・片方だけ選んだ「開始と終了」）は、
    /// 最初に買った月と、今月（先の日付の購入があればその月）で閉じる。開いたままだと西暦1年から棒を並べてしまう
    /// </summary>
    internal static StatsDateRange ChartRange(StatsSnapshot all, StatsDateRange? selected, DateOnly today)
    {
        var first = ParseMonth(all.Months[0].Key);
        var lastMonth = ParseMonth(all.Months[^1].Key);
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var end = (lastMonth > thisMonth ? lastMonth : thisMonth).AddMonths(1).AddDays(-1);

        var from = selected?.From ?? DateOnly.MinValue;
        var to = selected?.To ?? DateOnly.MaxValue;
        return new StatsDateRange(from == DateOnly.MinValue ? first : from, to == DateOnly.MaxValue ? end : to);
    }

    private static DateOnly ParseMonth(string key)
        => new(int.Parse(key[..4], System.Globalization.CultureInfo.InvariantCulture), int.Parse(key[5..], System.Globalization.CultureInfo.InvariantCulture), 1);

    /// <summary>
    /// 期間の月（24ヶ月まで）か年を、買っていない月・年も0円として並べる（並べ方は期間の長さで決める・open.md 104）。
    ///
    /// 集計は「最初に買った月から最後に買った月まで」しか持たないので、そこから切るだけだと、
    /// 12ヶ月を選んでいても買った月が1つなら棒が1本だけ浮き、どの時期なのかも読めなかった（点検 2026-09-23）。
    /// 期間の目盛りは常に全部出す。棒の値は期間で絞った集計から取るので、年の途中で切った期間でも外の月は混ざらない。
    ///
    /// 軸の月は「09」だけでは年が分からないので、左端と1月の下に年を添える。全部に年を付けると、
    /// 24ヶ月のとき狭い窓で目盛りの文字が重なる
    /// </summary>
    internal static List<(StatsPeriod Period, string Year, string Full)> ChartBars(StatsSnapshot period, StatsDateRange range)
    {
        var result = new List<(StatsPeriod, string, string)>();
        if (StatsPeriods.ByMonth(range))
        {
            var byKey = period.Months.ToDictionary(month => month.Key, StringComparer.Ordinal);
            for (var cursor = new DateOnly(range.From.Year, range.From.Month, 1); cursor <= range.To; cursor = cursor.AddMonths(1))
            {
                var key = $"{cursor.Year:D4}-{cursor.Month:D2}";
                var bar = byKey.TryGetValue(key, out var found)
                    ? found
                    : new StatsPeriod { Key = key, Label = $"{cursor.Month:D2}", SpentYen = 0, ItemCount = 0 };
                var year = result.Count == 0 || cursor.Month == 1 ? $"{cursor.Year}" : string.Empty;
                result.Add((bar, year, $"{cursor.Year}年{cursor.Month}月"));
            }

            return result;
        }

        var byYear = period.Years.ToDictionary(year => year.Key, StringComparer.Ordinal);
        for (var year = range.From.Year; year <= range.To.Year; year++)
        {
            var key = $"{year:D4}";
            var bar = byYear.TryGetValue(key, out var found)
                ? found
                : new StatsPeriod { Key = key, Label = key, SpentYen = 0, ItemCount = 0 };
            result.Add((bar, string.Empty, $"{year}年"));
        }

        return result;
    }

    private void RebuildPanels()
    {
        if (_snapshot is null)
        {
            return;
        }

        Avatars.Clear();
        var avatarMax = _snapshot.Avatars.Count == 0 ? 0 : _snapshot.Avatars.Max(bar => bar.ItemCount);
        foreach (var bar in _snapshot.Avatars)
        {
            Avatars.Add(new StatsRowViewModel
            {
                Label = bar.Label,
                ValueText = $"{bar.ItemCount:N0}",
                Ratio = avatarMax == 0 ? 0 : bar.ItemCount / (double)avatarMax,
                IsResidual = bar.IsResidual,
            });
        }

        RebuildShops();

        Categories.Clear();
        var categoryMax = _snapshot.Categories.Count == 0 ? 0 : _snapshot.Categories.Max(bar => bar.Bytes);
        foreach (var bar in _snapshot.Categories.Take(8))
        {
            var category = bar.Key;
            Categories.Add(new StatsRowViewModel
            {
                Label = bar.Label,
                ValueText = Core.Models.DisplayText.Size(bar.Bytes),
                SubText = $"{bar.ItemCount} 件",
                Ratio = categoryMax == 0 ? 0 : bar.Bytes / (double)categoryMax,
                OpenCommand = new RelayCommand(() => ShowCategory(category)),
                Tooltip = "このカテゴリの商品を検索画面で開きます。",
            });
        }

        RebuildBacklog();
        RebuildExtras();

        OnPropertyChanged(nameof(HasAvatars));
        OnPropertyChanged(nameof(HasBacklog));
    }

    /// <summary>選んで足した項目を組み立てる。</summary>
    private void RebuildExtras()
    {
        if (_snapshot is null)
        {
            return;
        }

        Fill(PriceBuckets, Period!.PriceBuckets.Select(b => (b.Label, (long)b.Count, $"{b.Count} 件")));
        Fill(WishBuckets, _snapshot.WishBuckets.Select(b => (b.Label, (long)b.Count, $"{b.Count} 件")));
        Fill(CategorySpend, Period!.CategorySpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
        Fill(UserTagSpend, Period!.UserTagSpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
        Fill(CategoryCounts, Period!.CategoryCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
        Fill(UserTagCounts, Period!.UserTagCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
        Fill(HeavyItems, _snapshot.HeavyItems.Select(b => (b.Name, b.Bytes, Core.Models.DisplayText.Size(b.Bytes))));

        // 素体経由は推定なので、直接対応と分けたまま出す
        Fill(Wearables, _snapshot.Wearables.Select(w =>
            (w.Name, (long)w.Total, w.ViaBaseCount > 0 ? $"{w.DirectCount} + 素体経由 {w.ViaBaseCount}" : $"{w.DirectCount}")));

        PriceChanges.Clear();
        foreach (var change in _snapshot.PriceChanges.Take(10))
        {
            var id = change.ItemId;
            PriceChanges.Add(new PriceChangeRowViewModel
            {
                Name = change.Name,
                PaidText = $"¥{change.PaidYen:N0}",
                CurrentText = $"¥{change.CurrentYen:N0}",
                DiffText = $"{(change.DiffYen > 0 ? "+" : string.Empty)}¥{change.DiffYen:N0}",
                IsUp = change.DiffYen > 0,
                OpenCommand = new RelayCommand(() => ShowItem(id)),
            });
        }

        RebuildAttributeDistributions();

        Fill(AttributeCorrelations, _snapshot.AttributeCorrelations.Select(c =>
            ($"{c.A} × {c.B}", (long)Math.Abs(Math.Round(c.R * 100)), $"{c.R:+0.00;-0.00;0.00}（{c.Count} 件）")));

        foreach (var name in new[]
        {
            nameof(HasPriceChanges), nameof(HasAttributeDistributions), nameof(HasCorrelations),
            nameof(HasWearables), nameof(CorrelationNote), nameof(GiftText), nameof(FreeText),
            nameof(RepeatText), nameof(EndOfSaleText), nameof(UnsortedText), nameof(HiddenText),
            nameof(HasLocalOnly), nameof(LocalOnlyText),
            nameof(UserTagSpendNote),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>棒の長さは最大値を1とした比で持つ。</summary>
    private static void Fill(
        ObservableCollection<StatsRowViewModel> into,
        IEnumerable<(string Label, long Value, string Text)> rows)
    {
        var list = rows.ToList();
        var peak = list.Count == 0 ? 0 : list.Max(row => row.Value);

        into.Clear();
        foreach (var row in list)
        {
            into.Add(new StatsRowViewModel
            {
                Label = row.Label,
                ValueText = row.Text,
                Ratio = peak == 0 ? 0 : row.Value / (double)peak,
            });
        }
    }

    private void ShowItem(string itemId) => ShowItemAsync(itemId).Forget();

    /// <summary>画面のスレッドを塞いで待たない（読み込みが遅いと、押した瞬間に画面が固まる）。</summary>
    private async Task ShowItemAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    /// <summary>
    /// ショップの欄に出す上位の数。前は支出順8・商品数順10の2つの欄で、1つにまとめたので多い方にそろえた（open.md 107）。
    /// 全部は出さない：ショップの数だけ縦に伸び、全部を見るならナビの「ショップ」がある
    /// </summary>
    public const int ShopRows = 10;

    public bool ShopOrderSpent
    {
        get => _services.StatsPanels.ShopOrder == StatsShopOrder.Spent;
        set => SetShopOrder(value, StatsShopOrder.Spent);
    }

    public bool ShopOrderCount
    {
        get => _services.StatsPanels.ShopOrder == StatsShopOrder.Count;
        set => SetShopOrder(value, StatsShopOrder.Count);
    }

    public bool ShopOrderWish
    {
        get => _services.StatsPanels.ShopOrder == StatsShopOrder.Wish;
        set => SetShopOrder(value, StatsShopOrder.Wish);
    }

    private void SetShopOrder(bool chosen, StatsShopOrder order)
    {
        if (!chosen || _services.StatsPanels.ShopOrder == order)
        {
            return;
        }

        _services.StatsPanels.ShopOrder = order;
        RebuildShops();
        OnPropertyChanged(nameof(ShopOrderSpent));
        OnPropertyChanged(nameof(ShopOrderCount));
        OnPropertyChanged(nameof(ShopOrderWish));
    }

    /// <summary>
    /// ショップの欄（open.md 107）。選んだ物の順に上位 <see cref="ShopRows"/> 件。右端は選んだ値、その後ろにほかの1つを小さく添える。
    /// 行はどれもショップの画面へ
    /// </summary>
    private void RebuildShops()
    {
        Shops.Clear();
        if (Period is null)
        {
            return;
        }

        var order = _services.StatsPanels.ShopOrder;
        long ValueOf(StatsSpendBar bar) => order switch
        {
            StatsShopOrder.Count => bar.ItemCount,
            StatsShopOrder.Wish => bar.WishCount,
            _ => bar.SpentYen,
        };

        var rows = StatsExtras.SortShops(Period!.Shops, order).Take(ShopRows).ToList();
        var max = rows.Count == 0 ? 0 : rows.Max(ValueOf);
        foreach (var bar in rows)
        {
            var subdomain = bar.Key;
            Shops.Add(new StatsRowViewModel
            {
                Label = bar.Label,
                ValueText = order switch
                {
                    StatsShopOrder.Count => $"{bar.ItemCount:N0} 件",
                    StatsShopOrder.Wish => $"スキ {bar.WishCount:N0}",
                    _ => $"¥{bar.SpentYen:N0}",
                },
                SubText = order == StatsShopOrder.Spent ? $"{bar.ItemCount} 件" : $"¥{bar.SpentYen:N0}",
                Ratio = max == 0 ? 0 : ValueOf(bar) / (double)max,
                OpenCommand = new RelayCommand(() => _main.ShowShopAsync(subdomain).Forget()),
                Tooltip = "このショップの画面を開きます。",
            });
        }
    }

    /// <summary>
    /// 属性の分布の区切りの幅（5・10・20。open.md 112）。全部の属性に同じ幅を当てる。
    /// アプリを閉じるまで覚える（統計は開くたびに作り直すので、起動の間ずっと居る <see cref="Services.StatsPanels"/> に持つ）
    /// </summary>
    private int AttributeBarWidthCore
    {
        get => _services.StatsPanels.AttributeBarWidth;
        set => _services.StatsPanels.AttributeBarWidth = value;
    }

    public bool AttributeWidth5
    {
        get => AttributeBarWidthCore == 5;
        set => SetAttributeBarWidth(value, 5);
    }

    public bool AttributeWidth10
    {
        get => AttributeBarWidthCore == 10;
        set => SetAttributeBarWidth(value, 10);
    }

    public bool AttributeWidth20
    {
        get => AttributeBarWidthCore == 20;
        set => SetAttributeBarWidth(value, 20);
    }

    public int AttributeBarWidth => AttributeBarWidthCore;

    private void SetAttributeBarWidth(bool chosen, int width)
    {
        // 選ばれた側だけを見る（つながったボタンは外れた側にも false が来る）
        if (!chosen || AttributeBarWidthCore == width)
        {
            return;
        }

        AttributeBarWidthCore = width;
        RebuildAttributeDistributions();
        OnPropertyChanged(nameof(AttributeWidth5));
        OnPropertyChanged(nameof(AttributeWidth10));
        OnPropertyChanged(nameof(AttributeWidth20));
        OnPropertyChanged(nameof(AttributeBarWidth));
    }

    private void RebuildAttributeDistributions()
    {
        AttributeDistributions.Clear();
        foreach (var distribution in _snapshot?.AttributeDistributions ?? [])
        {
            AttributeDistributions.Add(new AttributeDistributionRowViewModel(distribution, AttributeBarWidthCore));
        }
    }

    public bool HasPriceChanges => PriceChanges.Count > 0;

    public bool HasAttributeDistributions => AttributeDistributions.Count > 0;

    public bool HasCorrelations => AttributeCorrelations.Count > 0;

    public bool HasWearables => Wearables.Count > 0;

    /// <summary>相関が出ない理由をその場に書く。空欄のまま置かない。</summary>
    public string CorrelationNote => _snapshot is null || HasCorrelations
        ? string.Empty
        : $"両方を評価した商品が {_snapshot.CorrelationMinimum} 件以上ある組だけを表示します。";

    public string GiftText => Period is not { } period
        ? string.Empty
        : $"{period.GiftedItemCount} 件・定価で ¥{period.GiftedValueYen:N0}";

    public string FreeText => Period is not { } period
        ? string.Empty
        : $"{period.FreeItemCount} 件" + (period.OwnedCount == 0
            ? string.Empty
            : $"・所持の {period.FreeItemCount * 100.0 / period.OwnedCount:0.#}%");

    public string RepeatText => _snapshot is null
        ? string.Empty
        : $"1点だけ {_snapshot.ShopsBoughtOnce} 店 / 2点以上 {_snapshot.ShopsBoughtMany} 店";

    public string EndOfSaleText => _snapshot is null
        ? string.Empty
        : $"販売終了 {_snapshot.EndOfSaleCount} 件（¥{_snapshot.EndOfSaleSpentYen:N0}）／売り切れ {_snapshot.SoldOutCount} 件";

    /// <summary>
    /// BOOTHに無い商品として登録したもの。0件なら行ごと出さない。
    ///
    /// 販売終了と別の行にするのは金額の意味が違うため——
    /// あちらはBOOTHの価格を観測できていた商品で、こちらは自分で入れた額しかない。
    /// </summary>
    public bool HasLocalOnly => _snapshot is { LocalOnlyCount: > 0 };

    public string LocalOnlyText => _snapshot is null
        ? string.Empty
        : $"{_snapshot.LocalOnlyCount} 件・自分で入れた額 ¥{_snapshot.LocalOnlySpentYen:N0}";

    public string UnsortedText => _snapshot is null ? string.Empty : $"{_snapshot.UnsortedOwnedCount} 件";

    public string HiddenText => _snapshot is null ? string.Empty : $"{_snapshot.HiddenCount} 件";

    /// <summary>userTagは複数選べるので合計が支出と一致しない。そう書いておく。</summary>
    public string UserTagSpendNote => "ユーザータグは1つの商品に複数付くので、合計は累計支出と一致しません。";

    /// <summary>
    /// 積み残し。0件のものは行ごと出さない。
    /// 「片付いていること」を並べても手は動かせず、残っているものが埋もれるだけになる。
    /// </summary>
    private void RebuildBacklog()
    {
        Backlog.Clear();

        if (_snapshot is null)
        {
            return;
        }

        if (_snapshot.Backlog.UnresolvedCount > 0)
        {
            Backlog.Add(new BacklogRowViewModel
            {
                Label = "商品IDが未確定",
                Count = _snapshot.Backlog.UnresolvedCount,
                Severity = "Warn",
                OpenCommand = new RelayCommand(_main.ShowResolve),
            });
        }

        if (_snapshot.Backlog.NeedsUserTagCount > 0)
        {
            Backlog.Add(new BacklogRowViewModel
            {
                Label = "ユーザータグが未設定",
                Count = _snapshot.Backlog.NeedsUserTagCount,
                Severity = "Plain",
                OpenCommand = new RelayCommand(() => _main.ShowEditAsync().Forget()),
            });
        }

        if (_snapshot.Backlog.MissingFileCount > 0)
        {
            Backlog.Add(new BacklogRowViewModel
            {
                Label = "ファイルが見つからない",
                Count = _snapshot.Backlog.MissingFileCount,
                Severity = "Bad",
                OpenCommand = new RelayCommand(ShowMissing),
            });
        }
    }

    private void ShowCategory(string category)
    {
        _main.Search.ShowOnlyCategory(category);
        _main.ShowSearch();
    }

    /// <summary>
    /// 見つからないファイルは、検索の条件「見つからないファイル」で開く（ユーザ判断 2026-11-18：検索に条件ができたので変えた。
    /// 2026-09-16 はその条件が無く、フォルダビューで見る事にしていた）。検索の画面なら、何が入ったかを見てから編集へ送れる
    /// </summary>
    private void ShowMissing()
    {
        _main.Search.ShowOnlyMissingFile();
        _main.ShowSearch();
    }

    /// <summary>
    /// 軸の上端。半端な最大値をそのまま上端にすると目盛りが読めないので、
    /// 上の桁で切り上げた区切りのいい値に合わせる。
    ///
    /// 必ず山より1段上に取る。ちょうど一致させると山の棒が枠いっぱいになり、
    /// 上に余地が無いのか切れているのかが見分けられなくなる。
    /// </summary>
    private static long RoundUpAxis(long peak)
    {
        if (peak <= 0)
        {
            return 0;
        }

        var magnitude = (long)Math.Pow(10, Math.Floor(Math.Log10(peak)));
        var step = magnitude / 2 == 0 ? magnitude : magnitude / 2;

        return ((long)Math.Floor(peak / (double)step) + 1) * step;
    }

    /// <summary>金額の短い表記。桁が多いと軸に収まらないので k / M で畳む。</summary>
    private static string Format(long yen) => yen switch
    {
        >= 1_000_000 => $"{yen / 1_000_000d:0.#}M",
        >= 1_000 => $"{yen / 1_000d:0.#}k",
        _ => $"{yen:N0}",
    };

}

/// <summary>買った時と今で価格が変わった商品1行。</summary>
public sealed class PriceChangeRowViewModel
{
    public required string Name { get; init; }

    public required string PaidText { get; init; }

    public required string CurrentText { get; init; }

    public required string DiffText { get; init; }

    /// <summary>値上がりしたか。色分けにだけ使う。</summary>
    public required bool IsUp { get; init; }

    public RelayCommand? OpenCommand { get; init; }
}

/// <summary>属性の分布の棒1本。</summary>
public sealed class AttributeBarViewModel
{
    /// <summary>描画領域（<see cref="AttributeDistributionRowViewModel.ChartHeight"/>）に対する実寸。</summary>
    public required double Height { get; init; }

    /// <summary>棒の下に出す値。値ごとに1本のときだけ入れる（区切りのときは下の「0」「100」で読む）。</summary>
    public string ValueLabel { get; init; } = string.Empty;

    public required string Tooltip { get; init; }
}

/// <summary>属性1軸の分布1行。分け方（幅）は統計の画面で選ぶ（open.md 112）。</summary>
public sealed class AttributeDistributionRowViewModel
{
    public AttributeDistributionRowViewModel(StatsAttributeDistribution distribution, int width)
    {
        Name = distribution.Name;
        SummaryText = $"{distribution.Rated} 件を評価済み・平均 {distribution.Average:0.#}%";

        var bars = StatsExtras.AttributeBars(distribution, width);
        IsByValue = bars.All(bar => bar.From == bar.To);
        var peak = bars.Count == 0 ? 0 : bars.Max(bar => bar.Count);
        Bars = bars
            .Select(bar => new AttributeBarViewModel
            {
                Height = peak == 0 ? 0 : bar.Count / (double)peak * ChartHeight,
                ValueLabel = IsByValue ? $"{bar.From}" : string.Empty,
                Tooltip = bar.From == bar.To ? $"{bar.From}%：{bar.Count} 件" : $"{bar.From}〜{bar.To}%：{bar.Count} 件",
            })
            .ToList();
        Counts = bars.Select(bar => bar.Count).ToList();
    }

    public string Name { get; }

    public string SummaryText { get; }

    public IReadOnlyList<AttributeBarViewModel> Bars { get; }

    public IReadOnlyList<int> Counts { get; }

    /// <summary>付いた値ごとに1本か。下の「0」「100」の字は区切りのときだけ出す（値ごとのときは棒の下に値が出る）。</summary>
    public bool IsByValue { get; }

    /// <summary>グラフの高さ。属性ごとの小さな札に並べるので低めで足りる。</summary>
    public const double ChartHeight = 56;
}
