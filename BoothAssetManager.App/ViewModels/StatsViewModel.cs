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

        _ = LoadAsync();
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

    public string SpentSubText
    {
        get
        {
            if (_snapshot is null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (_snapshot.GiftedCount > 0)
            {
                parts.Add($"ギフト {_snapshot.GiftedCount} 件");
            }

            if (_snapshot.FreeCount > 0)
            {
                parts.Add($"無料 {_snapshot.FreeCount} 件");
            }

            return parts.Count == 0 ? string.Empty : $"{string.Join(" / ", parts)}は別枠";
        }
    }

    /// <summary>金額に入っていない分。総額を額面どおり受け取られないように必ず出す。</summary>
    public string UnpricedNote => _snapshot is null || _snapshot.UnpricedItemCount == 0
        ? string.Empty
        : $"購入価格の記録が無い商品が {_snapshot.UnpricedItemCount:N0} 件あります。その分は総額に入っていません。";

    public bool HasUnpricedNote => UnpricedNote.Length > 0;

    public string SizeText => FormatSize(_snapshot?.PhysicalBytes ?? 0);

    public string SizeSubText => _snapshot is null || _snapshot.DuplicateBytes <= 0
        ? string.Empty
        : $"うち重複コピー {FormatSize(_snapshot.DuplicateBytes)}";

    public string ShopCountText => $"{_snapshot?.ShopCount ?? 0:N0}";

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
                nameof(UnpricedNote), nameof(HasUnpricedNote), nameof(SizeText), nameof(SizeSubText),
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
                OpenCommand = new RelayCommand(() => _ = _main.ShowShopAsync(subdomain, ("統計", _main.ShowStats))),
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
                ValueText = FormatSize(bar.Bytes),
                SubText = $"{bar.ItemCount} 件",
                Ratio = categoryMax == 0 ? 0 : bar.Bytes / (double)categoryMax,
                OpenCommand = new RelayCommand(() => ShowCategory(category)),
                Tooltip = "この分類で検索し直します。",
            });
        }

        RebuildBacklog();

        OnPropertyChanged(nameof(HasAvatars));
        OnPropertyChanged(nameof(HasBacklog));
    }

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
                Label = "BoothID が未確定",
                Count = _snapshot.Backlog.UnresolvedCount,
                Severity = "Warn",
                OpenCommand = new RelayCommand(_main.ShowResolve),
            });
        }

        if (_snapshot.Backlog.NeedsAppTagCount > 0)
        {
            Backlog.Add(new BacklogRowViewModel
            {
                Label = "appTag 未設定（要編集）",
                Count = _snapshot.Backlog.NeedsAppTagCount,
                Severity = "Plain",
                OpenCommand = new RelayCommand(() => _ = _main.ShowEditAsync()),
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

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
