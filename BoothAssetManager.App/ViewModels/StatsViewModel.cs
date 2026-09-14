using System.Collections.ObjectModel;
using System.Windows;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>月別グラフの棒1本。高さは決め打ちの描画領域に対する実寸で持つ。</summary>
public sealed class SpendBarViewModel
{
    /// <summary>グラフの描画領域の高さ。ここを基準に棒の高さを決める。</summary>
    public const double ChartHeight = 140;

    public required string Label { get; init; }

    public required long SpentYen { get; init; }

    public required int ItemCount { get; init; }

    public required double BarHeight { get; init; }

    /// <summary>山と、いちばん新しい区切りにだけ金額を出す。全部に出すと読めなくなる。</summary>
    public string ValueLabel { get; init; } = string.Empty;

    public bool IsPeak { get; init; }

    public string Tooltip => $"{Label}：¥{SpentYen:N0}／{ItemCount} 件";
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
        DuplicateKind.ArchiveAndUnpacked => "zipと展開済みの両方があります（展開した方を消した場合）",
        _ when group.CrossesItems => $"{group.Places.Count} 箇所にあります（別の商品にも紐付いています）",
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

/// <summary>集計期間の選び方。</summary>
public sealed class StatsRangeOption
{
    public required string Label { get; init; }

    /// <summary>月別で見せるときの表示月数。年別なら null。</summary>
    public int? Months { get; init; }
}

/// <summary>
/// 統計画面。
///
/// 集計そのものは <see cref="StatsService"/> が持ち、ここは見せ方だけを決める。
/// 数え方の但し書き（推定で埋めた日付・金額の記録が無いもの）は畳まずに本文へ出す。
/// 集計結果を信じてよいかは、欠けの量が分からないと判断できないため。
/// </summary>
public sealed class StatsViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private StatsSnapshot? _snapshot;
    private StatsRangeOption _range;
    private bool _isLoading = true;

    public StatsViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;
        _range = Ranges[0];

        LoadAsync().Forget();
    }

    public static IReadOnlyList<StatsRangeOption> Ranges { get; } =
    [
        new StatsRangeOption { Label = "直近 12 ヶ月", Months = 12 },
        new StatsRangeOption { Label = "直近 24 ヶ月", Months = 24 },
        new StatsRangeOption { Label = "年別（全期間）" },
    ];

    public StatsRangeOption Range
    {
        get => _range;
        set
        {
            if (SetField(ref _range, value))
            {
                RebuildChart();
            }
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

    public ObservableCollection<StatsRowViewModel> ShopsByCount { get; } = [];

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
    /// 贈答があるときだけ「（自分用）」と断る。
    /// 贈答が無い人に毎回この括弧を見せても、何と区別しているのか分からない。
    /// </summary>
    public string SpentLabel => HasGiven ? "累計支出（自分用）" : "累計支出";

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

    /// <summary>贈答が無ければタイルは4枚。無い枠を空けておく意味がない。</summary>
    public int TileColumns => HasGiven ? 5 : 4;

    /// <summary>金額に入っていない分。総額を額面どおり受け取られないように必ず出す。</summary>
    public string UnpricedNote => _snapshot is null || _snapshot.UnpricedItemCount == 0
        ? string.Empty
        : $"購入価格の記録が無い商品が {_snapshot.UnpricedItemCount:N0} 件あります。その分は総額に入っていません。";

    public bool HasUnpricedNote => UnpricedNote.Length > 0;

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
    public IReadOnlyList<DuplicateRowViewModel> Duplicates =>
        _snapshot?.Duplicates.Select(group => new DuplicateRowViewModel(group)).ToList() ?? [];

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
            if (_snapshot is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (_snapshot.FallbackDatedCount > 0)
            {
                parts.Add($"{_snapshot.FallbackDatedCount:N0} 件は入手日が未入力で、ファイルの日付で代えています");
            }

            if (_snapshot.UndatedCount > 0)
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
        var snapshot = await Task.Run(() => _services.Stats.LoadAsync());

        RunOnUiThread(() =>
        {
            _snapshot = snapshot;
            IsLoading = false;

            RebuildChart();
            RebuildPanels();

            foreach (var name in new[]
            {
                nameof(OwnedText), nameof(OwnedSubText), nameof(SpentText), nameof(SpentSubText),
                nameof(SpentLabel), nameof(GivenText), nameof(GivenSubText), nameof(HasGiven),
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

        if (_snapshot is null)
        {
            return;
        }

        var source = _range.Months is { } months
            ? _snapshot.Months.TakeLast(months).ToList()
            : _snapshot.Years.ToList();

        var peak = source.Count == 0 ? 0 : source.Max(period => period.SpentYen);
        var scale = RoundUpAxis(peak);

        AxisTopText = $"¥{Format(scale)}";
        AxisMidText = $"¥{Format(scale / 2)}";

        for (var index = 0; index < source.Count; index++)
        {
            var period = source[index];
            var isPeak = peak > 0 && period.SpentYen == peak;
            var isLast = index == source.Count - 1;

            Chart.Add(new SpendBarViewModel
            {
                Label = period.Label,
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

        Shops.Clear();
        var shopMax = _snapshot.Shops.Count == 0 ? 0 : _snapshot.Shops.Max(bar => bar.SpentYen);
        foreach (var bar in _snapshot.Shops.Take(8))
        {
            var subdomain = bar.Key;
            Shops.Add(new StatsRowViewModel
            {
                Label = bar.Label,
                ValueText = $"¥{bar.SpentYen:N0}",
                SubText = $"{bar.ItemCount} 件",
                Ratio = shopMax == 0 ? 0 : bar.SpentYen / (double)shopMax,
                OpenCommand = new RelayCommand(() => _main.ShowShopAsync(subdomain).Forget()),
                Tooltip = "このショップの画面を開きます。",
            });
        }

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
                Tooltip = "この分類の商品を検索画面で開きます。",
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

        Fill(PriceBuckets, _snapshot.PriceBuckets.Select(b => (b.Label, (long)b.Count, $"{b.Count} 件")));
        Fill(WishBuckets, _snapshot.WishBuckets.Select(b => (b.Label, (long)b.Count, $"{b.Count} 件")));
        Fill(CategorySpend, _snapshot.CategorySpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
        Fill(UserTagSpend, _snapshot.UserTagSpend.Select(b => (b.Label, b.SpentYen, $"¥{b.SpentYen:N0}")));
        Fill(CategoryCounts, _snapshot.CategoryCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
        Fill(UserTagCounts, _snapshot.UserTagCounts.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
        Fill(ShopsByCount, _snapshot.ShopsByCount.Select(b => (b.Label, (long)b.ItemCount, $"{b.ItemCount} 件")));
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

        AttributeDistributions.Clear();
        foreach (var distribution in _snapshot.AttributeDistributions)
        {
            var peak = distribution.Buckets.Count == 0 ? 0 : distribution.Buckets.Max();
            AttributeDistributions.Add(new AttributeDistributionRowViewModel
            {
                Name = distribution.Name,
                SummaryText = $"{distribution.Rated} 件を評価済み・平均 {distribution.Average:0.#}%",
                Counts = distribution.Buckets,
                BarHeights = distribution.Buckets
                    .Select(count => peak == 0 ? 0 : count / (double)peak * AttributeDistributionRowViewModel.ChartHeight)
                    .ToList(),
            });
        }

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

    private void ShowItem(string itemId)
    {
        var item = _services.Store.Items.LoadAsync(itemId).GetAwaiter().GetResult();
        if (item is not null)
        {
            _main.ShowItem(item);
        }
    }

    public bool HasPriceChanges => PriceChanges.Count > 0;

    public bool HasAttributeDistributions => AttributeDistributions.Count > 0;

    public bool HasCorrelations => AttributeCorrelations.Count > 0;

    public bool HasWearables => Wearables.Count > 0;

    /// <summary>相関が出ない理由をその場に書く。空欄のまま置かない。</summary>
    public string CorrelationNote => _snapshot is null || HasCorrelations
        ? string.Empty
        : $"両方を評価した商品が {_snapshot.CorrelationMinimum} 件に満たない組は出していません。"
            + "件数が少ないと相関の数字が暴れるためです。";

    public string GiftText => _snapshot is null
        ? string.Empty
        : $"{_snapshot.GiftedItemCount} 件（定価にして ¥{_snapshot.GiftedValueYen:N0}）";

    public string FreeText => _snapshot is null
        ? string.Empty
        : $"{_snapshot.FreeItemCount} 件" + (_snapshot.OwnedCount == 0
            ? string.Empty
            : $"（所持の {_snapshot.FreeItemCount * 100.0 / _snapshot.OwnedCount:0.#}%）");

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
        : $"{_snapshot.LocalOnlyCount} 件（自分で入れた額 ¥{_snapshot.LocalOnlySpentYen:N0}）";

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
                Label = "商品ID が未確定",
                Count = _snapshot.Backlog.UnresolvedCount,
                Severity = "Warn",
                OpenCommand = new RelayCommand(_main.ShowResolve),
            });
        }

        if (_snapshot.Backlog.NeedsUserTagCount > 0)
        {
            Backlog.Add(new BacklogRowViewModel
            {
                Label = "ユーザータグ 未設定（要編集）",
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

    private void ShowMissing()
    {
        _main.Search.ShowOnlyMissing();
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

/// <summary>属性1軸の分布1行。0-19 / 20-39 / … の5区切り。</summary>
public sealed class AttributeDistributionRowViewModel
{
    public required string Name { get; init; }

    public required string SummaryText { get; init; }

    /// <summary>区切りごとの高さ（描画領域に対する実寸）。</summary>
    public required IReadOnlyList<double> BarHeights { get; init; }

    public required IReadOnlyList<int> Counts { get; init; }

    /// <summary>グラフの高さ。区切りが5つなので低めで足りる。</summary>
    public const double ChartHeight = 56;
}
