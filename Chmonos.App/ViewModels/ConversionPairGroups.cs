using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 「追加した組」の並び（ユーザ判断 2026-11-20・open.md「変換：追加した組の行に…」③）。覚えるのは <c>ui-state.json</c> の
/// <see cref="UiState.ConversionPairSort"/>（設定の画面に出す値ではなく、前回の続きから始めるために覚えるだけの値なので）。
/// </summary>
public enum ConversionPairSort
{
    /// <summary>元の名前順。まとまりの中は先の名前順。</summary>
    Name,

    /// <summary>元の名前の逆順。まとまりの中は先の名前順のまま（判断は「元の名前の昇順・降順」で、中は「先の名前の順」）。</summary>
    NameDescending,

    /// <summary>追加が新しい順。まとまりは中のいちばん新しい組の順、中も新しい順。</summary>
    Newest,

    /// <summary>追加が古い順。まとまりは中のいちばん新しい組の古い順、中も古い順（新しい順のちょうど逆）。</summary>
    Oldest,
}

/// <summary>並びの選択肢（画面の文字と ui-state の値）。</summary>
public sealed record ConversionPairSortChoice(ConversionPairSort Sort, string Label, string Key)
{
    public static IReadOnlyList<ConversionPairSortChoice> All { get; } =
    [
        new(ConversionPairSort.Name, "元の名前順", "name"),
        new(ConversionPairSort.NameDescending, "元の名前の逆順", "nameDesc"),
        new(ConversionPairSort.Newest, "追加が新しい順", "newest"),
        new(ConversionPairSort.Oldest, "追加が古い順", "oldest"),
    ];

    /// <summary>ui-state の値から。知らない値（手で直した JSON・後の版の値）は既定（元の名前順）に戻す。</summary>
    public static ConversionPairSort FromKey(string? key)
        => All.FirstOrDefault(choice => string.Equals(choice.Key, key, StringComparison.Ordinal))?.Sort ?? ConversionPairSort.Name;

    public static string KeyOf(ConversionPairSort sort) => All.First(choice => choice.Sort == sort).Key;

    public override string ToString() => Label;
}

/// <summary>
/// 組の端（アバター・共通素体）の頭の小さな絵。アバターは「アバター」の見方の行と同じ読み方（<see cref="AvatarImageSync.IconPath"/>・
/// タイルの控え）、共通素体は共通素体の見方の行と同じ（付けた画像 → 素体の商品の1枚目）。絵が無ければ頭の字（素体の一覧の行と同じ印）。
/// 場所は見えた行で初めて探し、絵は裏で読む（アバターの行と同じ。一覧を組む時に全部を探さない・流す重さを増やさない）
/// </summary>
public sealed class ConversionEndIcon : ViewModelBase
{
    private readonly Func<string?> _pathFactory;
    private readonly Services.ThumbnailLoader? _thumbnails;
    private string? _path;
    private bool _pathLoaded;

    public ConversionEndIcon(string initialName, Func<string?> pathFactory, Services.ThumbnailLoader? thumbnails)
    {
        Initial = Core.Services.AvatarText.InitialOf(initialName);
        _pathFactory = pathFactory;
        _thumbnails = thumbnails;
    }

    /// <summary>絵が無い時の頭の字（素体は「（共通素体）」を添える前の名前から）。</summary>
    public string Initial { get; }

    private string? Path
    {
        get
        {
            if (!_pathLoaded)
            {
                _pathLoaded = true;
                _path = _pathFactory();
            }

            return _path;
        }
    }

    public bool HasIcon => Path is not null;

    public System.Windows.Media.Imaging.BitmapSource? Icon => Path is { } path
        ? _thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Icon)))
        : null;

    /// <summary>
    /// 乗せた時の大きな絵（ユーザ判断 2026-11-20・①）。吹き出しが開いた時に初めてカードの大きさで読む
    /// （組の一覧を流す時には読まない。③の右の欄・組の一覧の軽さを崩さない）
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? HoverImage => Path is { } path
        ? _thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    /// <summary>乗せた時の吹き出しを出すか。絵が無い（頭の字の）時は出さない</summary>
    public bool HasHoverImage => HasIcon;

    /// <summary>
    /// 場所を探し直す。変換扱いの欄は読み直しで中身の同じ行を残すので、素体に画像を付けた後も頭の字のまま残らないように。
    /// まだ探していなければ何もしない。探し直すのは描いている行だけ（結んでいない行は値を読まない）
    /// </summary>
    internal void Refresh()
    {
        if (!_pathLoaded)
        {
            return;
        }

        _pathLoaded = false;
        OnPropertyChanged(nameof(HasIcon));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasHoverImage));
        OnPropertyChanged(nameof(HoverImage));
    }
}

/// <summary>
/// 「追加した組」の元ごとのまとまり（ユーザ判断 2026-11-20・④：組はいつも元でまとめ、元のアイコン・名前・組の数を見出しにして畳める）。
/// 見出しは**畳むだけ**で、選ぶ行にはしない（右に出す物はプロファイルの商品か組のどちらか1つで、まとまりには出す中身が無い）。
/// キーボードでは ↑↓ で行と見出しを渡り、見出しで Enter・Space で開閉、← で畳み → で開く（通知の束の見出しと同じ）
/// </summary>
public sealed class ConversionPairGroup : ViewModelBase
{
    private readonly ConversionPairEditor _owner;
    private bool _isExpanded;

    internal ConversionPairGroup(ConversionPairEditor owner, ConversionEnd from, string name, bool isExpanded)
    {
        _owner = owner;
        From = from;
        Name = name;
        _isExpanded = isExpanded;
    }

    public ConversionEnd From { get; }

    public string Name { get; }

    /// <summary>一覧の行の型を見出しに替える印（<see cref="ConversionPairRow.IsGroupHeader"/> は false）。</summary>
    public bool IsGroupHeader => true;

    public ConversionEndIcon? Icon { get; private set; }

    internal void SetIcon(ConversionEndIcon icon)
    {
        Icon = icon;
        OnPropertyChanged(nameof(Icon));
    }

    /// <summary>中の組（今の並び）。</summary>
    public IReadOnlyList<ConversionPairRow> Rows { get; private set; } = [];

    /// <summary>中の組のうち、探す欄に当たる物（絞っていなければ全部）。</summary>
    public IReadOnlyList<ConversionPairRow> ShownRows { get; private set; } = [];

    /// <summary>見出しの数。絞っている間は当たった数（見出しの下に並ぶ行の数と合わせる）。</summary>
    public string CountText => $"（{ShownRows.Count}）";

    /// <summary>開閉の三角の名前の「何の」（「アオイの組を開く」⇄「…を折りたたむ」。ExpandToggle が状態から作る）。</summary>
    public string Subject => $"{Name}の組";

    /// <summary>
    /// 開いているか（見出しの三角が見せる値）。絞っていない間は覚えた値で、人が替えたら覚える
    /// （<see cref="ConversionPairEditor"/> が ui-state へ書き、一覧の行を出し入れする）。
    /// **絞っている間は、絞りの間だけの値**（2026-11-20・open.md「変換：組を探す欄…」）：当たったまとまりは開いて見せるが、
    /// 畳んだ事の記録は書き換えない。絞りの間に替えた開閉も覚えず、絞りを消すと覚えた姿に戻る
    /// </summary>
    public bool IsExpanded
    {
        get => _owner.IsFiltering ? _shownWhileFiltering : _isExpanded;
        set
        {
            if (_owner.IsFiltering)
            {
                if (_shownWhileFiltering != value)
                {
                    _shownWhileFiltering = value;
                    OnPropertyChanged();
                    _owner.GroupToggledWhileFiltering();
                }

                return;
            }

            if (SetField(ref _isExpanded, value))
            {
                _owner.GroupToggled(this);
            }
        }
    }

    /// <summary>絞りの間だけの開閉。語を替えるたびに開いた姿から始める（当たった組を見せるため）。</summary>
    private bool _shownWhileFiltering = true;

    /// <summary>絞りの間だけ開く・閉じる（覚えた値は触らない）。絞りを消した時は、覚えた値に戻ったと知らせるのにも使う。</summary>
    internal void SetShownWhileFiltering(bool value)
    {
        _shownWhileFiltering = value;
        OnPropertyChanged(nameof(IsExpanded));
    }

    /// <summary>覚えずに開く（選んだ組が畳んだまとまりの中にある時。書くのは呼ぶ側）。</summary>
    internal void SetExpandedQuietly(bool value)
    {
        if (_isExpanded != value)
        {
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
        }
    }

    internal void SetRows(IReadOnlyList<ConversionPairRow> rows, IReadOnlyList<ConversionPairRow> shown)
    {
        Rows = rows;
        ShownRows = shown;
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(ShownRows));
        OnPropertyChanged(nameof(CountText));
    }
}

/// <summary>組の並べ方（画面に依らない計算。試験で並びを確かめる）。</summary>
public static class ConversionPairOrder
{
    /// <summary>
    /// まとまりと中の組を並べる。<paramref name="added"/> は組の追加した順の位置（<c>conversion-pairs.json</c> の配列の位置。
    /// 足すと末尾に付き、外す・メモ・読み替えでは順を保つので、時刻を持たなくても追加した順が分かる）
    /// </summary>
    public static IReadOnlyList<(TGroup Group, IReadOnlyList<TRow> Rows)> Arrange<TGroup, TRow>(
        IEnumerable<(TGroup Group, IReadOnlyList<TRow> Rows)> groups,
        Func<TGroup, string> groupName,
        Func<TRow, string> rowName,
        Func<TRow, int> added,
        ConversionPairSort sort)
    {
        var names = StringComparer.CurrentCulture;
        return sort switch
        {
            ConversionPairSort.Name => [.. groups
                .Select(group => (group.Group, ByName(group.Rows)))
                .OrderBy(group => groupName(group.Group), names)
                .ThenBy(group => group.Item2.Min(added))],
            ConversionPairSort.NameDescending => [.. groups
                .Select(group => (group.Group, ByName(group.Rows)))
                .OrderByDescending(group => groupName(group.Group), names)
                .ThenBy(group => group.Item2.Min(added))],
            ConversionPairSort.Newest => [.. groups
                .Select(group => (group.Group, (IReadOnlyList<TRow>)[.. group.Rows.OrderByDescending(added)]))
                .OrderByDescending(group => group.Item2.Max(added))],
            _ => [.. groups
                .Select(group => (group.Group, (IReadOnlyList<TRow>)[.. group.Rows.OrderBy(added)]))
                .OrderBy(group => group.Item2.Max(added))],
        };

        IReadOnlyList<TRow> ByName(IReadOnlyList<TRow> rows) => [.. rows.OrderBy(rowName, names).ThenBy(added)];
    }
}

/// <summary>組を探す欄の、語を当てる端（2026-11-20・ユーザ「オプションに元と先、両方を指定できる形」。既定は両方）。</summary>
public enum ConversionPairSearchScope
{
    Both,
    From,
    To,
}

/// <summary>
/// 組を探す欄の照らし方。アバターの端は「アバター」の見方の探す欄と同じ（<see cref="Core.Services.AvatarSearch.Matches"/>：名前・商品ID・BOOTHでの正式名・呼び方）、
/// 共通素体の端は素体の名前で当てる。素体は画面に出す「（共通素体）」を添えた字では当てない（「素体」と打つだけで素体の組が全部当たる）
/// </summary>
public static class ConversionPairSearch
{
    public static bool Matches(ConversionPairRow row, string query, ConversionPairSearchScope scope)
        => query.Length == 0
            || (scope != ConversionPairSearchScope.To && EndMatches(row.Pair.From, row.FromName, row.FromEntry, query))
            || (scope != ConversionPairSearchScope.From && EndMatches(row.Pair.To, row.ToName, row.ToEntry, query));

    /// <summary>端1つ。登録簿から消えたアバター（名前の代わりに ID を出している端）は、出している字で当てる。</summary>
    public static bool EndMatches(ConversionEnd end, string shownName, AvatarRegistryEntry? entry, string query)
        => end.BaseName is { } baseName ? baseName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        : entry is not null ? Core.Services.AvatarSearch.Matches(entry, shownName, query, out _)
        : shownName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
