using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 追加式の条件の種類。
///
/// 常設に置かないのは、どれも「たまに使う」ものだから。常設にすると全員が縦の場所を払う。
/// 追加した種類は次の起動にも残るので、よく使う人にとっては実質常設になる。
/// </summary>
public enum ExtraFilterKind
{
    PublishedAt,
    WishList,
    Price,
    Adult,
    EndOfSale,
    HasUpdate,
    BaseAvatar,
    UsedOn,
    Folder,
}

/// <summary>入力の形。種類ごとにどの欄を出すかが決まる。</summary>
public enum ExtraFilterShape
{
    /// <summary>下限と上限。片側だけでもよい。</summary>
    Range,

    /// <summary>入りか切りか。</summary>
    Check,

    /// <summary>候補から選んで積む。候補が多くて全部並べられないもの。</summary>
    Suggest,

    /// <summary>1階層ずつ降りる。フォルダだけ。</summary>
    Drill,
}

/// <summary>追加できる条件の一覧。名前と形をここだけで決める。</summary>
public static class ExtraFilterCatalog
{
    public sealed record Entry(ExtraFilterKind Kind, string Label, ExtraFilterShape Shape, string Hint);

    public static IReadOnlyList<Entry> All { get; } =
    [
        new(ExtraFilterKind.PublishedAt, "公開日", ExtraFilterShape.Range, "BOOTHでの公開日で絞ります。"),
        new(ExtraFilterKind.WishList, "スキ数", ExtraFilterShape.Range, "BOOTHのスキ数で絞ります。"),
        new(ExtraFilterKind.Price, "価格", ExtraFilterShape.Range,
            "自分が払った額で絞ります（BOOTHの現在価格ではありません）。"),
        new(ExtraFilterKind.Adult, "R-18", ExtraFilterShape.Check, "R-18の商品だけを出します。"),
        new(ExtraFilterKind.EndOfSale, "販売終了", ExtraFilterShape.Check, "BOOTHで販売が終わった商品だけを出します。"),
        new(ExtraFilterKind.HasUpdate, "更新の有無", ExtraFilterShape.Check,
            "要確認に未読の更新通知が残っている商品だけを出します。"),
        new(ExtraFilterKind.BaseAvatar, "対応素体", ExtraFilterShape.Suggest, "共通素体への対応で絞ります。"),
        new(ExtraFilterKind.UsedOn, "着せているアバター", ExtraFilterShape.Suggest,
            "自分が実際に着せた記録で絞ります（出品者の宣言とは別です）。"),
        new(ExtraFilterKind.Folder, "フォルダ", ExtraFilterShape.Drill, "ファイルの置き場所で絞ります。"),
    ];

    public static Entry Of(ExtraFilterKind kind) => All.First(entry => entry.Kind == kind);
}

/// <summary>
/// 積んだ条件1つ。
///
/// 種類だけが起動をまたいで残り、値は残らない。
/// 値まで戻すと「なぜか商品が少ない」状態で始まり、
/// 原因が畳まれた条件の中にあると気付けないため。
/// </summary>
public sealed class ExtraFilter : ViewModelBase
{
    private string _min = string.Empty;
    private string _max = string.Empty;
    private bool _isOn;

    public required ExtraFilterKind Kind { get; init; }

    public string Label => ExtraFilterCatalog.Of(Kind).Label;

    public ExtraFilterShape Shape => ExtraFilterCatalog.Of(Kind).Shape;

    public string Hint => ExtraFilterCatalog.Of(Kind).Hint;

    public bool IsRange => Shape == ExtraFilterShape.Range;

    public bool IsCheck => Shape == ExtraFilterShape.Check;

    public bool IsSuggest => Shape == ExtraFilterShape.Suggest;

    public bool IsDrill => Shape == ExtraFilterShape.Drill;

    private string? _currentPath;

    /// <summary>
    /// 今どこを見ているか。null は根。
    /// これは現在地であって条件ではない（条件は <see cref="Selected"/>）。
    /// セッション内だけ覚え、閉じたら忘れる。
    /// </summary>
    public string? CurrentPath
    {
        get => _currentPath;
        set
        {
            if (SetField(ref _currentPath, value))
            {
                Descended?.Invoke();
            }
        }
    }

    /// <summary>降りた・上がったので、出す行を作り直してほしい。</summary>
    public event Action? Descended;

    /// <summary>今いる階層の行。検索側が作って入れる。</summary>
    public System.Collections.ObjectModel.ObservableCollection<FolderRow> Rows { get; } = [];

    /// <summary>現在地を示すパンくず。押すとその階層へ戻る。</summary>
    public System.Collections.ObjectModel.ObservableCollection<FolderCrumb> Crumbs { get; } = [];

    /// <summary>
    /// 同じ名前のフォルダを2つ以上選んでいるか。
    /// そのときだけチップに親を1段足して区別できるようにする。
    /// </summary>
    public bool HasAmbiguousSelection => Selected
        .Select(LastSegmentOf)
        .GroupBy(name => name, StringComparer.CurrentCultureIgnoreCase)
        .Any(group => group.Count() > 1);

    internal static string LastSegmentOf(string path)
    {
        var index = path.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
        return index < 0 ? path : path[(index + 1)..];
    }

    public event Action? Changed;

    /// <summary>この条件を外す。</summary>
    public RelayCommand? RemoveCommand { get; set; }

    /// <summary>下限。空欄は「指定なし」。</summary>
    public string Min
    {
        get => _min;
        set
        {
            if (SetField(ref _min, value))
            {
                Changed?.Invoke();
            }
        }
    }

    public string Max
    {
        get => _max;
        set
        {
            if (SetField(ref _max, value))
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// チェック式の条件で、条件として効かせるか。
    /// 自分で足したときは入り、起動時の復元では切り（値は起動をまたいで残さない）。
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetField(ref _isOn, value))
            {
                Changed?.Invoke();
            }
        }
    }

    /// <summary>候補入力で積んだ値。</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Selected { get; } = [];

    public void Add(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !Selected.Contains(value, StringComparer.CurrentCultureIgnoreCase))
        {
            Selected.Add(value.Trim());
            Changed?.Invoke();
        }
    }

    public void Remove(string value)
    {
        if (Selected.Remove(value))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>この条件がitemを通すか。</summary>
    public bool Matches(ItemRecord item, IReadOnlyCollection<string> unreadItemIds) => Kind switch
    {
        ExtraFilterKind.PublishedAt => InDateRange(item.Booth.PublishedAt),
        ExtraFilterKind.WishList => InNumberRange(item.Booth.WishListsCount),
        ExtraFilterKind.Price => InNumberRange(Purchases.SelfSpendOf(item)),
        ExtraFilterKind.Adult => !IsOn || item.Booth.IsAdult,
        ExtraFilterKind.EndOfSale => !IsOn || item.Booth.IsEndOfSale,
        ExtraFilterKind.HasUpdate => !IsOn || unreadItemIds.Contains(item.Id),
        ExtraFilterKind.BaseAvatar => Selected.Count == 0
            || item.Local.AvatarBases.Any(link => !link.Rejected
                && Selected.Contains(link.BaseName, StringComparer.CurrentCultureIgnoreCase)),
        ExtraFilterKind.UsedOn => Selected.Count == 0
            || item.Local.UsedOn.Any(usage => Selected.Contains(usage.AvatarItemId, StringComparer.Ordinal)),

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる。
        // 複数選んだ場合はOR（appTagと揃える）
        ExtraFilterKind.Folder => Selected.Count == 0
            || Selected.Any(folder => FolderTree.IsUnder(item, folder)),
        _ => true,
    };

    /// <summary>今この条件が実際に絞っているか。要約に出すかの判断に使う。</summary>
    public bool IsActive => Shape switch
    {
        ExtraFilterShape.Range => Min.Trim().Length > 0 || Max.Trim().Length > 0,
        ExtraFilterShape.Check => IsOn,
        _ => Selected.Count > 0,
    };

    public string SummaryText => Shape switch
    {
        ExtraFilterShape.Range => $"{Label} {(Min.Trim().Length == 0 ? "" : Min)}〜{(Max.Trim().Length == 0 ? "" : Max)}",
        ExtraFilterShape.Check => Label,
        _ => $"{Label}（{string.Join("・", Selected)}）",
    };

    private bool InNumberRange(int value)
    {
        if (int.TryParse(Min.Trim(), out var min) && value < min)
        {
            return false;
        }

        return !int.TryParse(Max.Trim(), out var max) || value <= max;
    }

    private bool InDateRange(DateTimeOffset? value)
    {
        // 公開日が分からないものは、日付で絞った時点で外す。
        // 「値が小さい」ではなく「値が無い」ので、範囲のどこにも当てはまらない
        if (value is not { } date)
        {
            return !IsActive;
        }

        if (DateOnly.TryParse(Min.Trim(), out var min) && DateOnly.FromDateTime(date.Date) < min)
        {
            return false;
        }

        return !DateOnly.TryParse(Max.Trim(), out var max) || DateOnly.FromDateTime(date.Date) <= max;
    }
}

/// <summary>
/// ドリルダウンの1行。
/// チェックで選び、シェブロンで降りる。1行に当たり判定が2つ乗るので、
/// XAML側でシェブロンを行の右端に離してある。
/// </summary>
public sealed class FolderRow : ViewModelBase
{
    private bool _isSelected;

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required int Count { get; init; }

    /// <summary>降りられるか。商品が1件のフォルダにはシェブロンを出さない。</summary>
    public required bool CanDescend { get; init; }

    /// <summary>記録上のフォルダが今つながっていない。外付けを外したときなど。</summary>
    public required bool IsOffline { get; init; }

    public string CountText => IsOffline ? $"{Count}・今つながっていません" : Count.ToString();

    public bool IsEmpty => Count == 0;

    public RelayCommand? DescendCommand { get; set; }

    public RelayCommand? OpenCommand { get; set; }

    public RelayCommand? AddToImportCommand { get; set; }

    public bool CanAddToImport { get; init; }

    public event Action? Changed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                Changed?.Invoke();
            }
        }
    }
}

/// <summary>パンくずの1つ。現在地を示すだけで、条件ではない。</summary>
public sealed class FolderCrumb
{
    public required string Label { get; init; }

    /// <summary>null は根。</summary>
    public string? Path { get; init; }

    public RelayCommand? GoCommand { get; set; }

    public bool IsLast { get; init; }
}
