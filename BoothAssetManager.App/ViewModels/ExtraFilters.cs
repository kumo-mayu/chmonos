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

    /// <summary>最近使った（Unityへ送った）もの。</summary>
    RecentlyUsed,

    /// <summary>最近見たもの。</summary>
    RecentlyViewed,

    /// <summary>最近手元に入ったもの。</summary>
    RecentlyAdded,

    /// <summary>お気に入りの星を付けたもの（#70）。</summary>
    Favorite,
}

/// <summary>
/// 「最近」の足跡を、商品IDから引ける形にまとめたもの。
///
/// 絞り込みの1回ぶんで使い回す。1商品ごとにファイルを読み直さないため。
/// </summary>
public sealed record RecentTimes(
    IReadOnlyDictionary<string, DateTimeOffset> Added,
    IReadOnlyDictionary<string, DateTimeOffset> Used,
    IReadOnlyDictionary<string, DateTimeOffset> Viewed)
{
    public static RecentTimes Empty { get; } = new(
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>(),
        new Dictionary<string, DateTimeOffset>());

    public DateTimeOffset? Of(string itemId, RecentKind kind)
    {
        var source = kind switch
        {
            RecentKind.Added => Added,
            RecentKind.Used => Used,
            _ => Viewed,
        };

        return source.TryGetValue(itemId, out var at) ? at : null;
    }
}

/// <summary>
/// 改変から引いた「どのアバターにどの商品を使ったか」。
///
/// **絞り込みの1回ぶんで使い回す**（<see cref="RecentTimes"/> と同じ理由。
/// 1商品ごとに改変のファイルを読み直さないため）。
/// </summary>
public sealed record ModificationUsage(
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByAvatar)
{
    public static ModificationUsage Empty { get; } =
        new(new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal));

    public static ModificationUsage From(IEnumerable<Core.Models.ModificationRecord> records)
    {
        var found = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (!found.TryGetValue(record.AvatarItemId, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                found[record.AvatarItemId] = used;
            }

            // 同じ商品が2回入っていても、絞り込みに要るのは「入っているか」だけ
            foreach (var member in record.Members)
            {
                used.Add(member.ItemId);
            }
        }

        return new ModificationUsage(found.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<string>)pair.Value,
            StringComparer.Ordinal));
    }

    public bool Used(string avatarItemId, string itemId)
        => ItemIdsByAvatar.TryGetValue(avatarItemId, out var used) && used.Contains(itemId);
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

    /// <summary>
    /// 日数を1つ。「最近」の3種で使う。
    ///
    /// 上下限にしないのは、聞きたいことが「何日以内か」の片側しかないから。
    /// 「30〜」と出る欄に30を入れさせると、どちら側の意味か読めない。
    /// </summary>
    Days,
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
        new(ExtraFilterKind.Favorite, "お気に入り", ExtraFilterShape.Check, "カードの星を付けた商品だけを出します。"),
        new(ExtraFilterKind.Adult, "R-18", ExtraFilterShape.Check, "R-18の商品だけを出します。"),
        new(ExtraFilterKind.EndOfSale, "販売終了", ExtraFilterShape.Check, "BOOTHで販売が終わった商品だけを出します。"),
        new(ExtraFilterKind.HasUpdate, "更新の有無", ExtraFilterShape.Check,
            "要確認に未読の更新通知が残っている商品だけを出します。"),
        new(ExtraFilterKind.BaseAvatar, "対応素体", ExtraFilterShape.Suggest, "共通素体への対応で絞ります。"),
        // **名前と候補はそのまま、中身だけ改変経由にした。**
        // 「くうたに着せた衣装を探す」という用途は消えていない
        new(ExtraFilterKind.UsedOn, "着せているアバター", ExtraFilterShape.Suggest,
            "そのアバターの改変に入れた商品で絞ります（出品者の宣言とは別です）。"),
        new(ExtraFilterKind.Folder, "フォルダ", ExtraFilterShape.Drill, "ファイルの置き場所で絞ります。"),

        // 記録が無い商品は、日数を入れた時点で外れる。「値が小さい」ではなく
        // 「値が無い」ので、何日以内にも当てはまらない
        new(ExtraFilterKind.RecentlyUsed, "最近使った", ExtraFilterShape.Days,
            "Unityへ送った記録で絞ります。送ったことが無い商品は外れます。"),
        new(ExtraFilterKind.RecentlyViewed, "最近見た", ExtraFilterShape.Days,
            "商品ページを開いた記録で絞ります。開いたことが無い商品は外れます。"),
        new(ExtraFilterKind.RecentlyAdded, "最近手元に入った", ExtraFilterShape.Days,
            "取り込んだ記録で絞ります。この機能より前に取り込んだ商品には記録が無いので外れます。"),
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

    public bool IsDays => Shape == ExtraFilterShape.Days;

    private string _days = string.Empty;

    /// <summary>何日以内か。空なら絞っていない。</summary>
    public string Days
    {
        get => _days;
        set
        {
            if (SetField(ref _days, value))
            {
                Changed?.Invoke();
            }
        }
    }

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

    /// <summary>
    /// フォルダの条件で、記録のパスを今の場所に読み替える（外付けのドライブ文字が変わったとき・ユーザ指示 2026-09-14）。
    /// 木も選んだフォルダも今の場所で持つので、照らし合わせる商品の側も同じ読み替えを通す。検索側が入れる
    /// </summary>
    public Func<string, string>? PathMap { get; set; }

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

    /// <summary>
    /// 候補入力に出す一覧。検索側が入れる。
    ///
    /// **持ち主を検索側にする。**候補はアバターの登録簿や素体の名前から作るもので、
    /// 条件そのものではない（起動をまたいで残す値でもない）。
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>候補を入れ替えたことを画面に知らせる。入れる側から呼ぶ。</summary>
    public void NoteSuggestionsChanged() => OnPropertyChanged(nameof(HasSuggestions));

    /// <summary>
    /// 候補が空のときに、なぜ選べないかを言う。
    ///
    /// **候補が何なのかを言う。**「着せているアバター」で選ぶのはアバターで、
    /// 改変ではない（改変が無いだけなら候補は出るが、結果が0件になる）。
    /// </summary>
    public string SuggestEmptyText => Kind switch
    {
        ExtraFilterKind.UsedOn => "アバターがまだ登録されていません。アバターの管理から登録できます。",
        ExtraFilterKind.BaseAvatar => "共通素体がまだ登録されていません。アバターの管理から設定できます。",
        _ => "候補がありません。",
    };

    public string SuggestPlaceholder => Kind switch
    {
        ExtraFilterKind.UsedOn => "アバター名か商品IDで絞り込む",
        ExtraFilterKind.BaseAvatar => "共通素体の名前で絞り込む",
        _ => "候補から選ぶ",
    };

    public RelayCommand AddSelectedCommand => _addSelected ??=
        new RelayCommand(parameter => Add(parameter as string ?? string.Empty));

    private RelayCommand? _addSelected;

    public RelayCommand RemoveSelectedCommand => _removeSelected ??=
        new RelayCommand(
            parameter => Remove(parameter as string ?? string.Empty),
            parameter => parameter is string);

    private RelayCommand? _removeSelected;

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
    public bool Matches(
        ItemRecord item,
        IReadOnlyCollection<string> unreadItemIds,
        RecentTimes? recent = null,
        ModificationUsage? modifications = null) => Kind switch
    {
        ExtraFilterKind.RecentlyUsed => WithinDays(recent, item.Id, RecentKind.Used),
        ExtraFilterKind.RecentlyViewed => WithinDays(recent, item.Id, RecentKind.Viewed),
        ExtraFilterKind.RecentlyAdded => WithinDays(recent, item.Id, RecentKind.Added),

        ExtraFilterKind.PublishedAt => InDateRange(item.Booth.PublishedAt),
        ExtraFilterKind.WishList => InNumberRange(item.Booth.WishListsCount),
        ExtraFilterKind.Price => InNumberRange(Purchases.SelfSpendOf(item)),
        ExtraFilterKind.Favorite => !IsOn || item.Local.IsFavorite,
        ExtraFilterKind.Adult => !IsOn || item.Booth.IsAdult,
        ExtraFilterKind.EndOfSale => !IsOn || item.Booth.IsEndOfSale,
        ExtraFilterKind.HasUpdate => !IsOn || unreadItemIds.Contains(item.Id),
        ExtraFilterKind.BaseAvatar => Selected.Count == 0
            || item.Local.AvatarBases.Any(link => !link.Rejected
                && Selected.Contains(link.BaseName, StringComparer.CurrentCultureIgnoreCase)),
        // 候補は「名前（ID）」の形。**IDで照合する**——名前は変わるが記録はIDで持つ
        ExtraFilterKind.UsedOn => Selected.Count == 0
            || Selected.Any(entry => AvatarSuggestionText.IdOf(entry) is { } avatarItemId
                && (modifications ?? ModificationUsage.Empty).Used(avatarItemId, item.Id)),

        // 選んだフォルダの子孫を全部含む。含まないと、通過点を選んだとき0件になる。
        // 複数選んだ場合はOR（userTagと揃える）
        ExtraFilterKind.Folder => Selected.Count == 0
            || Selected.Any(folder => FolderTree.IsUnder(item, folder, PathMap)),
        _ => true,
    };

    /// <summary>今この条件が実際に絞っているか。要約に出すかの判断に使う。</summary>
    public bool IsActive => Shape switch
    {
        ExtraFilterShape.Range => Min.Trim().Length > 0 || Max.Trim().Length > 0,
        ExtraFilterShape.Check => IsOn,
        ExtraFilterShape.Days => DayCount is > 0,
        _ => Selected.Count > 0,
    };

    public string SummaryText => Shape switch
    {
        ExtraFilterShape.Range => $"{Label} {(Min.Trim().Length == 0 ? "" : Min)}〜{(Max.Trim().Length == 0 ? "" : Max)}",
        ExtraFilterShape.Check => Label,
        ExtraFilterShape.Days => $"{Label} {DayCount}日以内",
        _ => $"{Label}（{string.Join("・", Selected)}）",
    };

    /// <summary>入力した日数。読めなければ null（絞っていない扱い）。</summary>
    private int? DayCount => int.TryParse(Days.Trim(), out var days) && days > 0 ? days : null;

    /// <summary>
    /// その足跡が指定の日数以内にあるか。
    ///
    /// **記録が無い商品は、日数を入れた時点で外す。**
    /// 「値が小さい」ではなく「値が無い」ので、何日以内にも当てはまらない
    /// （公開日の範囲指定と同じ考え方）。
    /// </summary>
    private bool WithinDays(RecentTimes? recent, string itemId, RecentKind kind)
        => RecentActivity.IsWithin(
            (recent ?? RecentTimes.Empty).Of(itemId, kind),
            DayCount ?? 0,
            DateTimeOffset.Now);

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

    private bool _isOffline;

    /// <summary>
    /// 記録上のフォルダが今つながっていない。外付けを外したときなど。
    /// **行を出した後で、画面のスレッドの外で確かめて付ける**（技術的負債 4-2）。
    /// </summary>
    public bool IsOffline
    {
        get => _isOffline;
        set
        {
            if (SetField(ref _isOffline, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => IsOffline ? $"{Count}・今つながっていません" : Count.ToString();

    /// <summary>この下の物を記録したときのドライブ文字（外付けの文字が変わって、今の文字に読み替えた物だけ）。</summary>
    public IReadOnlyList<string> RecordedLetters { get; init; } = [];

    /// <summary>読み替えた結果だと分かるようにする（ユーザ指示 2026-09-14）。</summary>
    public bool IsRemapped => RecordedLetters.Count > 0;

    public string RemapText => IsRemapped ? $"記録では {string.Join("・", RecordedLetters)}" : string.Empty;

    public string RemapTip => IsRemapped
        ? $"外付けのドライブ文字が変わったので、{string.Join("・", RecordedLetters)} として記録した物を、今つながっている {Path[..2]} の場所で出しています。"
        : string.Empty;

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
