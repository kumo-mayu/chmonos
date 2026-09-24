using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 検索の絞り込みのモジュール（ユーザ案 2026-09-15・`docs/history/search-redesign.md`）。
/// 名前は ui-state.json と検索の履歴に書くので、変えると古い状態が読めなくなる（読めなかった物は黙って飛ばす）。
/// </summary>
public enum SearchModuleKind
{
    Category,
    BoothTag,
    Shop,
    WishList,
    Price,
    EndOfSale,
    PublishedAt,
    Adult,
    Owned,
    Gift,
    FreePaid,
    UserTag,
    Attribute,
    Avatar,
    Favorite,
    AcquiredAt,
    Hidden,
    Unedited,

    /// <summary>対応アバターの推定に、まだ確かめていないものがあるか（ユーザ判断 2026-09-18）。</summary>
    AvatarUnconfirmed,
    Modification,
    UnityProject,
    Path,
    Recent,
}

/// <param name="Headings">「条件を追加」のメニューのどの見出しの下に出すか。重なってよい（ユーザ案：分類の重複を許す）。</param>
public sealed record SearchModuleInfo(SearchModuleKind Kind, string Label, string Hint);

/// <param name="Groups">見出しの中の、意味のまとまり。まとまりの間に区切り線を引く。</param>
public sealed record SearchModuleMenuLayout(string Title, IReadOnlyList<IReadOnlyList<SearchModuleKind>> Groups);

/// <summary>モジュールの一覧。名前・説明・メニューの見出しと並びをここだけで決める。</summary>
public static class SearchModuleCatalog
{
    public const string BoothInfo = "BOOTHの情報";
    public const string ItemInfo = "商品の情報";
    public const string Calendar = "カレンダー";
    public const string Slider = "スライダー";
    public const string Usage = "利用状況";

    /// <summary>最初の起動で出しておく最低限の条件（ユーザ判断 2026-09-16 Q9）。</summary>
    public static IReadOnlyList<SearchModuleKind> Defaults { get; } =
        [SearchModuleKind.Owned, SearchModuleKind.UserTag, SearchModuleKind.Avatar];

    public static IReadOnlyList<SearchModuleInfo> All { get; } =
    [
        new(SearchModuleKind.Category, "カテゴリ", "BOOTHのカテゴリ（自分で入れた分類を含む）で絞ります。"),
        new(SearchModuleKind.BoothTag, "BOOTHタグ", "BOOTHのタグで絞ります。"),
        new(SearchModuleKind.Shop, "ショップ", "ショップで絞ります。ショップ画面で星を付けたお気に入りのショップもまとめて選べます。"),
        new(SearchModuleKind.WishList, "スキ数", "BOOTHのスキ数で絞ります。"),
        new(SearchModuleKind.Price, "価格", "既定は自分が払った額。切り替えると BOOTH の価格（どれかのバリエーションが範囲に入れば当たり）で絞ります。"),
        new(SearchModuleKind.EndOfSale, "販売終了", "BOOTHで販売が終わった商品で絞ります。非公開・削除された商品は、既定では表示しません。"),
        new(SearchModuleKind.PublishedAt, "公開日", "BOOTHでの公開日で絞ります。"),
        new(SearchModuleKind.Adult, "R-18", "R-18 の商品で絞ります。"),
        new(SearchModuleKind.Owned, "所持", "手元にファイルがあるかで絞ります。"),
        new(SearchModuleKind.Gift, "ギフト", "購入記録のバリエーションで絞ります。貰ったもので、自分でも買ったものは両方に表示されます。"),
        new(SearchModuleKind.FreePaid, "有料・無料", "払った額（分からなければ BOOTH の価格）で絞ります。無料と有料の両方があるものは両方に表示されます。"),
        new(SearchModuleKind.UserTag, "ユーザタグ", "自分で付けたタグで絞ります。"),
        new(SearchModuleKind.Attribute, "属性", "自分で付けた属性の値で絞ります。評価していない商品は外れます。"),
        new(SearchModuleKind.Avatar, "対応アバター", "対応しているアバター・共通素体で絞ります。"),
        new(SearchModuleKind.Favorite, "お気に入り", "カードの星で絞ります。"),
        new(SearchModuleKind.AcquiredAt, "入手日", "入手日で絞ります。入手日を入れていない商品は外れます。"),
        new(SearchModuleKind.Hidden, "非表示", "非表示にした商品を表示します。この条件が無いときは、非表示の商品は表示しません。"),
        new(SearchModuleKind.Unedited, "未編集", "ユーザタグをまだ付けていない商品で絞ります。"),
        new(SearchModuleKind.AvatarUnconfirmed, "対応アバターの確認", "説明文から読み取っただけで、まだ確かめていない対応アバターがある商品で絞ります。"),
        new(SearchModuleKind.Modification, "改変", "改変に使った商品で絞ります。アバターを選ぶと、そのアバターの改変に使った商品です。"),
        new(SearchModuleKind.UnityProject, "Unityプロジェクト", "そのプロジェクトに紐付けた改変に使った商品で絞ります。"),
        new(SearchModuleKind.Path, "ファイルの場所", "手元のファイルが置いてあるフォルダで絞ります（その下の全部を含む）。"),
        new(SearchModuleKind.Recent, "最近", "最近使った（Unityへ送った）・見た・手元に入った商品で絞ります。記録が無い商品は外れます。"),
    ];

    /// <summary>
    /// 「条件を追加」の見出しと、見出しの中の並び（ユーザ判断 2026-09-16・案1）。
    ///
    /// **意味のまとまりで並べ、まとまりの間に区切り線を引く**：何の商品か → お金と入手 → 自分の整理 → 使い方。
    /// 前は条件の一覧の順のまま全見出しに出していて、所持と入手日、改変とファイルの場所のように意味の近い物が離れていた。
    /// 並びは見出しごとに決める（全体で1つの並びだと、ある見出しで良い並びが別の見出しで崩れる）。
    /// 同じ条件が2つの見出しに出てよい（対応アバターは出品者が BOOTH に書いた物を元にするので BOOTH の情報に見え、自分で直せる商品の情報でもある）。
    /// </summary>
    public static IReadOnlyList<SearchModuleMenuLayout> Menu { get; } =
    [
        new(BoothInfo,
        [
            [SearchModuleKind.Category, SearchModuleKind.BoothTag, SearchModuleKind.Avatar, SearchModuleKind.Adult],
            [SearchModuleKind.Shop, SearchModuleKind.Price],
            [SearchModuleKind.PublishedAt, SearchModuleKind.EndOfSale, SearchModuleKind.WishList],
        ]),
        new(ItemInfo,
        [
            [SearchModuleKind.UserTag, SearchModuleKind.Attribute, SearchModuleKind.Avatar, SearchModuleKind.Adult],
            [SearchModuleKind.Owned, SearchModuleKind.Gift, SearchModuleKind.FreePaid, SearchModuleKind.AcquiredAt],
            [SearchModuleKind.Favorite, SearchModuleKind.Unedited, SearchModuleKind.AvatarUnconfirmed, SearchModuleKind.Hidden],
            [SearchModuleKind.Recent, SearchModuleKind.Modification, SearchModuleKind.UnityProject, SearchModuleKind.Path],
        ]),

        // BOOTH に出た日 → 自分が手に入れた日
        new(Calendar, [[SearchModuleKind.PublishedAt, SearchModuleKind.AcquiredAt]]),

        // BOOTH の数 → 自分の評価
        new(Slider, [[SearchModuleKind.Price, SearchModuleKind.WishList, SearchModuleKind.Attribute]]),

        // 広い → 狭い
        new(Usage, [[SearchModuleKind.Recent, SearchModuleKind.Modification, SearchModuleKind.UnityProject]]),
    ];

    public static SearchModuleInfo Of(SearchModuleKind kind) => All.First(entry => entry.Kind == kind);
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
/// 改変から引いた「どの改変・アバター・プロジェクトにどの商品を使ったか」。
///
/// **絞り込みの1回ぶんで使い回す**（<see cref="RecentTimes"/> と同じ理由。1商品ごとに改変のファイルを読み直さないため）。
/// </summary>
public sealed record ModificationUsage(
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByAvatar,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByModification,
    IReadOnlyDictionary<string, IReadOnlySet<string>> ItemIdsByProject,
    IReadOnlyList<ModificationRecord> Records)
{
    public static ModificationUsage Empty { get; } = new(
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase),
        []);

    public static ModificationUsage From(IEnumerable<ModificationRecord> records)
    {
        var list = records.ToList();
        var byAvatar = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var byProject = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var byModification = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        foreach (var record in list)
        {
            // 同じ商品が2回入っていても、絞り込みに要るのは「入っているか」だけ
            var members = record.UsedMembers.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
            byModification[record.Id] = members;
            Collect(byAvatar, record.AvatarItemId, members);

            if (!string.IsNullOrWhiteSpace(record.UnityProject))
            {
                Collect(byProject, record.UnityProject!, members);
            }
        }

        return new ModificationUsage(
            byAvatar.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.Ordinal),
            byModification,
            byProject.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            list);

        static void Collect(Dictionary<string, HashSet<string>> map, string key, IEnumerable<string> members)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }

            set.UnionWith(members);
        }
    }

    public bool Used(string avatarItemId, string itemId)
        => ItemIdsByAvatar.TryGetValue(avatarItemId, out var used) && used.Contains(itemId);

    public bool InModification(string modificationId, string itemId)
        => ItemIdsByModification.TryGetValue(modificationId, out var used) && used.Contains(itemId);

    public bool InProject(string projectPath, string itemId)
        => ItemIdsByProject.TryGetValue(projectPath, out var used) && used.Contains(itemId);
}

/// <summary>絞り込み1回ぶんの材料。モジュールが商品を照らすときに使う。</summary>
public sealed class SearchModuleContext
{
    private readonly Func<AvatarCompatibilityIndex> _compatibility;
    private AvatarCompatibilityIndex? _index;

    public SearchModuleContext(
        Func<AvatarCompatibilityIndex> compatibility,
        ModificationUsage modifications,
        RecentTimes recent,
        Func<string, string>? pathMap,
        DateTimeOffset now)
    {
        _compatibility = compatibility;
        Modifications = modifications;
        Recent = recent;
        PathMap = pathMap;
        Now = now;
    }

    /// <summary>素体経由の対応の索引。対応アバターで絞るときだけ作る。</summary>
    public AvatarCompatibilityIndex Compatibility => _index ??= _compatibility();

    public ModificationUsage Modifications { get; }

    public RecentTimes Recent { get; }

    /// <summary>外付けのドライブ文字が変わった記録を今の場所に読み替える（フォルダの条件）。</summary>
    public Func<string, string>? PathMap { get; }

    public DateTimeOffset Now { get; }
}

/// <summary>
/// 絞り込みのモジュール1つ（ユーザ案 2026-09-15）。追加したまま切れる（<see cref="IsEnabled"/>）、右上の × で外す。
/// 各モジュールは一度しか追加できない（検索画面が守る）。
/// </summary>
public abstract class SearchModule : ReorderableRow
{
    /// <summary>
    /// 動かし続けている間、絞り直しを待つ時間。
    ///
    /// スライダのドラッグは1秒に数十回値が変わる。2000件での絞り直しは実測でこれより短いので、
    /// 止まってから1回で追いつく（`docs/feedback/done-2026-09.md` の計測）。
    /// 長くすると結果が遅れて見え、短くするとドラッグ中に何度も走る。
    /// </summary>
    private static readonly TimeSpan FilterWait = TimeSpan.FromMilliseconds(150);

    private bool _isEnabled = true;
    private bool _isCollapsed;
    private string? _disabledReason;
    private RelayCommand? _toggleCollapse;
    private readonly Debounced _changedSoon;

    protected SearchModule(SearchModuleKind kind)
    {
        Kind = kind;
        _changedSoon = new Debounced(FilterWait, () => Changed?.Invoke());
    }

    public SearchModuleKind Kind { get; }

    /// <summary>条件はどれも同じ並びに混ざる（種類が違っても順番を入れ替えられる）。</summary>
    public override object ReorderGroup => typeof(SearchModule);

    public SearchModuleInfo Info => SearchModuleCatalog.Of(Kind);

    public string Label => Info.Label;

    public string Hint => Info.Hint;

    /// <summary>条件が変わった（検索側が絞り直して、状態を書く）。</summary>
    public event Action? Changed;

    /// <summary>見た目だけが変わった（畳んだ・開いた）。絞り直さずに状態だけ書く。</summary>
    public event Action? ViewChanged;

    public RelayCommand? RemoveCommand { get; set; }

    /// <summary>追加したまま効かせるか（ユーザ案：トグルで追加状態を保ったまま無効化できる）。</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetField(ref _isEnabled, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// 畳んでいるか（ユーザ指示 2026-09-16）。条件が増えるとパネルが縦に伸びるので、決め終えた条件は畳めるようにする。
    /// **畳んでも条件は効いたまま**なので、畳んだ姿に効いている中身を1行で出す。
    /// </summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            if (SetField(ref _isCollapsed, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
                ViewChanged?.Invoke();
            }
        }
    }

    public bool IsExpanded => !_isCollapsed;

    public RelayCommand ToggleCollapseCommand => _toggleCollapse ??= new RelayCommand(() => IsCollapsed = !IsCollapsed);

    /// <summary>畳んだ姿に出す1行。効かせていないときは、絞っていないことを言う。</summary>
    public string CollapsedSummary => IsActive ? SummaryText : "絞っていません";

    /// <summary>効かせられない理由（R-18 を設定で隠しているとき）。あれば条件として使わない。</summary>
    public string? DisabledReason
    {
        get => _disabledReason;
        set
        {
            if (SetField(ref _disabledReason, value))
            {
                OnPropertyChanged(nameof(HasDisabledReason));
                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    public bool HasDisabledReason => _disabledReason is not null;

    /// <summary>実際に絞っているか（効かせていて、理由が無く、何も絞らない値でない）。</summary>
    public bool IsActive => IsEnabled && !HasDisabledReason && HasCondition;

    protected abstract bool HasCondition { get; }

    public abstract bool Matches(ItemRecord item, SearchModuleContext context);

    /// <summary>効いている条件の1行（結果の上と、畳んだパネルと、検索の履歴に出す）。</summary>
    public abstract string SummaryText { get; }

    /// <summary>何も絞らない値に戻す（「条件をクリア」）。通知だけ出し、絞り直しは呼ぶ側がまとめて行う。</summary>
    public abstract void Clear();

        /// <summary>選択肢の横に出す件数を数え直す。<paramref name="items"/> はこのモジュールを除いた他の条件を当てた後の商品。</summary>
    public virtual void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
    }

    public SearchModuleState Save()
        => Write(new SearchModuleState
        {
            Kind = Kind.ToString(),
            Enabled = IsEnabled,
            Collapsed = IsCollapsed,
            Summary = IsActive ? SummaryText : null,
        });

    public void Load(SearchModuleState state)
    {
        _isEnabled = state.Enabled;
        _isCollapsed = state.Collapsed;
        Read(state);
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsCollapsed));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected abstract SearchModuleState Write(SearchModuleState state);

    protected abstract void Read(SearchModuleState state);

    /// <summary>通知だけ出して切り替える。「条件をクリア」や他の画面からの条件で、1つずつ絞り直さない（呼ぶ側がまとめて1回）。</summary>
    public void SetEnabledQuietly(bool value)
    {
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected void NotifyChanged()
    {
        _changedSoon.Cancel();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
        Changed?.Invoke();
    }

    /// <summary>
    /// 値が変わった。**表示はすぐ、絞り直しは止まってから1回。**
    /// スライダを動かしている間に毎回絞り直すと、件数に比例した走査が追いつかない。
    /// </summary>
    protected void NotifyChangedSoon()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
        _changedSoon.Request();
    }

    /// <summary>全角の数字・カンマ・円記号が混ざっていても数として読む。読めなければ null。</summary>
    protected static int? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var folded = text.Normalize(NormalizationForm.FormKC).Replace(",", string.Empty).Replace("¥", string.Empty).Trim();
        return int.TryParse(folded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    }
}

/// <summary>分布の帯の棒1本。高さは帯（<see cref="RangeModule.HistogramHeight"/>）に収めた px。</summary>
public sealed record HistogramBar(double Height);

/// <summary>三択などの選択肢1つ。件数は「選んだら何件になるか」。</summary>
public sealed class ChoiceOption : ViewModelBase
{
    private int _count = -1;

    public ChoiceOption(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    public string Display => _count < 0 ? Label : $"{Label}（{_count}）";

    public override string ToString() => Display;
}

/// <summary>
/// プルダウンで選ぶ条件（ユーザ案「三項」）。
///
/// 何も絞らない選択肢（「両方」）を持つ物は、その選択肢で条件を残したまま無効にできる（トグル拡張）。
/// 持たない物（ギフト）は、条件自体の切り替えで無効にする（純三項）。
/// </summary>
public sealed class ChoiceModule : SearchModule
{
    private readonly Func<ItemRecord, string, bool, bool> _matches;
    private readonly string? _neutralKey;
    private ChoiceOption _selected;
    private bool _flag;

    /// <param name="options">先頭が追加したときの既定（ユーザ案の def）。</param>
    /// <param name="neutralKey">何も絞らない選択肢の鍵。純三項は null。</param>
    /// <param name="matches">商品・選んだ鍵・補助の切り替え → 通すか。</param>
    public ChoiceModule(
        SearchModuleKind kind,
        IReadOnlyList<ChoiceOption> options,
        string? neutralKey,
        Func<ItemRecord, string, bool, bool> matches,
        string? flagLabel = null)
        : base(kind)
    {
        Options = options;
        _selected = options[0];
        _neutralKey = neutralKey;
        _matches = matches;
        FlagLabel = flagLabel;
    }

    public IReadOnlyList<ChoiceOption> Options { get; }

    public ChoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetField(ref _selected, value))
            {
                NotifyChanged();
            }
        }
    }

    public string SelectedKey => _selected.Key;

    /// <summary>補助の切り替え（販売終了の「非公開・削除された商品も表示する」）。</summary>
    public string? FlagLabel { get; }

    public bool HasFlag => FlagLabel is not null;

    public bool Flag
    {
        get => _flag;
        set
        {
            if (SetField(ref _flag, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>補助の切り替えを切っていると隠す物がある（販売終了では非公開の物を隠す）ので、それも条件とみなす。</summary>
    protected override bool HasCondition => _selected.Key != _neutralKey || (HasFlag && !_flag);

    public override bool Matches(ItemRecord item, SearchModuleContext context) => _matches(item, _selected.Key, _flag);

    public override string SummaryText
        => $"{Label}：{_selected.Label}" + (HasFlag && _flag ? $"・{FlagLabel}" : string.Empty);

    public override void Clear()
    {
        if (_neutralKey is null)
        {
            // 純三項は「何も絞らない」選択肢を持たないので、条件ごと切る
            SetEnabledQuietly(false);
            return;
        }

        _selected = Options.First(option => option.Key == _neutralKey);
        _flag = true;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Flag));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var option in Options)
        {
            option.Count = items.Count(item => _matches(item, option.Key, _flag));
        }
    }

    protected override SearchModuleState Write(SearchModuleState state) => state with { Choice = _selected.Key, Flag = _flag };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        _flag = state.Flag;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(Flag));
    }

    /// <summary>選ぶ（他の画面から条件を渡すとき）。通知だけ出し、絞り直しは呼ぶ側。</summary>
    public void Select(string key)
    {
        _selected = Options.FirstOrDefault(option => option.Key == key) ?? _selected;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }
}

/// <summary>積んだ値1つ（チップ）。件数は「他の条件のもとで、この値に当てはまる件数」。</summary>
public sealed class ListChip : ViewModelBase
{
    private int _count = -1;
    private string _text;

    public ListChip(string key, string text)
    {
        Key = key;
        _text = text;
    }

    public string Key { get; }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public int Count
    {
        get => _count;
        set
        {
            if (SetField(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
            }
        }
    }

    public string CountText => _count < 0 ? string.Empty : _count.ToString(CultureInfo.InvariantCulture);

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// 候補から選んで積む条件（ユーザ案「list」）。全部を満たす（AND）か、いずれか（OR）かを切り替えられる。
/// 候補は必ず出す（ユーザ案「候補を出せるものに関しては必ず候補を出す」）。
/// </summary>
public sealed class ListModule : SearchModule
{
    private readonly Func<ItemRecord, SearchModuleContext, string, bool, bool> _matches;
    private readonly Func<ItemRecord, SearchModuleContext, bool>? _isUnspecified;
    private readonly List<(string Text, string Key)> _entries = [];
    private readonly Dictionary<string, string> _keyOfText = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, string> _textOfKey = new(StringComparer.Ordinal);
    private readonly bool _flagDefault;
    private bool _matchAll;
    private bool _flag;
    private bool _showMatched = true;
    private bool _showUnspecified;
    private int _matchedCount = -1;
    private int _unspecifiedCount = -1;
    private readonly Func<ItemRecord, SearchModuleContext, bool>? _includeMatches;
    private readonly string? _includeLabel;
    private bool _includeOn;
    private int _includeCount = -1;
    private RelayCommand? _add;

    /// <param name="allowsAnd">AND を選べるか（ショップは1商品に1つなので OR だけ）。</param>
    /// <param name="matches">商品・材料・積んだ値の鍵・補助の切り替え → 当てはまるか。</param>
    /// <param name="isUnspecified">
    /// 商品が「指定が無い」かたまりに入るか（対応アバターだけ）。渡すと「出すもの」の2つのチェックが出る。
    /// </param>
    /// <param name="includeLabel">選んだ値に加えて当てるかたまりの名前（ショップの「お気に入りのショップの商品」）。</param>
    /// <param name="includeMatches">そのかたまりに入る商品か。チェックを入れると、選んだ値のどれかと同じ扱いで当てる（OR）。</param>
    public ListModule(
        SearchModuleKind kind,
        bool allowsAnd,
        string placeholder,
        string emptyText,
        Func<ItemRecord, SearchModuleContext, string, bool, bool> matches,
        string? flagLabel = null,
        bool flagDefault = false,
        Func<ItemRecord, SearchModuleContext, bool>? isUnspecified = null,
        string? includeLabel = null,
        Func<ItemRecord, SearchModuleContext, bool>? includeMatches = null)
        : base(kind)
    {
        _includeLabel = includeLabel;
        _includeMatches = includeMatches;
        AllowsAnd = allowsAnd;
        Placeholder = placeholder;
        EmptyText = emptyText;
        _matches = matches;
        FlagLabel = flagLabel;
        _flagDefault = flagDefault;
        _flag = flagDefault;
        _isUnspecified = isUnspecified;
    }

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0 || Chips.Count > 0 || _entries.Count > 0;

    public ObservableCollection<ListChip> Chips { get; } = [];

    public bool AllowsAnd { get; }

    /// <summary>全部を満たす（AND）か。切っていればいずれか（OR）。</summary>
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

    /// <summary>2つ以上積んだときだけ AND／OR を出す（1つなら結果が変わらない）。</summary>
    public bool ShowsMatchMode => AllowsAnd && Chips.Count > 1;

    public string Placeholder { get; }

    public string EmptyText { get; }

    /// <summary>補助の切り替え（対応アバターの「素体経由の対応も含める」）。</summary>
    public string? FlagLabel { get; }

    public bool HasFlag => FlagLabel is not null && Chips.Count > 0;

    public bool Flag
    {
        get => _flag;
        set
        {
            if (SetField(ref _flag, value))
            {
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// 「出すもの」の2つのチェックを出すか（対応アバターだけ）。
    ///
    /// 商品を「対応が書いてある」と「対応の指定が無い」の2つのかたまりに分け、どちらを出すかを選ぶ
    /// （ユーザ判断 2026-09-16・案C）。BOOTH には対応アバターを書かずに「どのアバターでも使える」商品があり、
    /// 「含める」も「指定が無いものだけ見る」も要る。3つの意味がそのまま印の付け方になり、言い換えが要らない。
    /// </summary>
    public bool HasGroups => _isUnspecified is not null;

    /// <summary>選んだアバターに対応している商品を出すか。切ると、アバターの欄は意味を持たない（薄くする）。</summary>
    public bool ShowMatched
    {
        get => _showMatched;
        set => SetGroup(ref _showMatched, value, other: _showUnspecified);
    }

    /// <summary>対応の指定が無い商品（どのアバターにも使える扱い）を出すか。</summary>
    public bool ShowUnspecified
    {
        get => _showUnspecified;
        set => SetGroup(ref _showUnspecified, value, other: _showMatched);
    }

    /// <summary>
    /// 選んだ値に加えて当てるかたまりのチェックを出すか（ショップの「お気に入りのショップの商品」）。
    /// **別の条件にせず、ショップの条件の中に持つ**（ユーザ指示 2026-09-16）。ショップの条件は「選んだどれか」なので、
    /// 入れるとお気に入りのショップを全部選んだのと同じに働く（選んだショップと合わせてどれか。選んでいなければお気に入りだけ）。
    /// </summary>
    public bool HasInclude => _includeMatches is not null;

    public bool IncludeOn
    {
        get => _includeOn;
        set
        {
            if (SetField(ref _includeOn, value))
            {
                NotifyChanged();
            }
        }
    }

    public string IncludeText => _includeCount < 0 ? _includeLabel ?? string.Empty : $"{_includeLabel}（{_includeCount}）";

    public string MatchedLabel => _matchedCount < 0 ? "対応している商品" : $"対応している商品（{_matchedCount}）";

    public string UnspecifiedLabel => _unspecifiedCount < 0 ? "対応の指定が無い商品" : $"対応の指定が無い商品（{_unspecifiedCount}）";

    /// <summary>
    /// **最後の1つは外させない。**両方を切ると何も出ない（数の範囲の「越えられない」と同じ考え方）。
    /// 切ろうとした印は、画面に戻すために通知だけ出す。
    /// </summary>
    private void SetGroup(ref bool field, bool value, bool other)
    {
        if (field == value)
        {
            return;
        }

        if (value || other)
        {
            field = value;
        }

        OnPropertyChanged(nameof(ShowMatched));
        OnPropertyChanged(nameof(ShowUnspecified));
        if (field == value)
        {
            NotifyChanged();
        }
    }

    /// <summary>候補の頭に出す絵（対応アバター）。</summary>
    public Func<string, ImageSource?>? IconSelector { get; set; }

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => Add(parameter as string));

    /// <summary>候補を入れ替える。積んだ値の見せ方も新しい候補に合わせる（名前が変わっていても鍵で繋ぐ）。</summary>
    public void SetSuggestions(IEnumerable<(string Text, string Key)> entries)
    {
        _entries.Clear();
        _keyOfText.Clear();
        _textOfKey.Clear();

        foreach (var (text, key) in entries)
        {
            if (_keyOfText.TryAdd(text, key))
            {
                _entries.Add((text, key));
                _textOfKey.TryAdd(key, text);
            }
        }

        foreach (var chip in Chips)
        {
            if (_textOfKey.TryGetValue(chip.Key, out var text))
            {
                chip.Text = text;
            }
        }

        RefreshSuggestions();
    }

    public void Add(string? text)
    {
        if (text is null || !_keyOfText.TryGetValue(text.Trim(), out var key))
        {
            return;
        }

        AddKey(key);
    }

    /// <summary>通知だけ出して補助の切り替えを変える（他の画面から条件を渡すとき。絞り直しは呼ぶ側）。</summary>
    public void SetFlagQuietly(bool value)
    {
        _flag = value;
        OnPropertyChanged(nameof(Flag));
    }

    /// <summary>鍵で積む（他の画面から条件を渡すとき・状態を戻すとき）。</summary>
    /// <param name="text">候補にまだ無いときの見せ方（候補を読む前に渡されたとき）。候補が入ると候補の文字に揃う。</param>
    public void AddKey(string key, bool notify = true, string? text = null)
    {
        if (Chips.Any(chip => chip.Key == key))
        {
            return;
        }

        var chip = new ListChip(key, _textOfKey.TryGetValue(key, out var known) ? known : text ?? key);
        chip.RemoveCommand = new RelayCommand(() =>
        {
            Chips.Remove(chip);
            RefreshSuggestions();
            NotifyChanged();
        });

        Chips.Add(chip);
        RefreshSuggestions();
        if (notify)
        {
            NotifyChanged();
        }
    }

    /// <summary>
    /// 何か絞っているか。
    /// 「指定が無い商品だけ」はアバターを選んでいなくても絞る。それ以外は、アバターを選んで初めて絞る
    /// （足した直後に、何も選んでいないのに「指定が無い商品」が消えるのを避ける）。
    /// </summary>
    protected override bool HasCondition
        => Chips.Count > 0 || (HasGroups && !_showMatched && _showUnspecified) || (HasInclude && _includeOn);

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (_isUnspecified is not null && _isUnspecified(item, context))
        {
            // 指定が無い商品は、選んだアバターと照らさない。出すかどうかだけ
            return _showUnspecified;
        }

        if (HasGroups && !_showMatched)
        {
            return false;
        }

        if (HasInclude && _includeOn)
        {
            // 選んだ値のどれかと同じ扱い（OR）。何も選んでいなければ、このかたまりだけ
            return _includeMatches!(item, context) || (Chips.Count > 0 && MatchesChips(item, context));
        }

        return MatchesChips(item, context);
    }

    private bool MatchesChips(ItemRecord item, SearchModuleContext context)
        => Chips.Count == 0
            || (_matchAll && AllowsAnd
                ? Chips.All(chip => _matches(item, context, chip.Key, _flag))
                : Chips.Any(chip => _matches(item, context, chip.Key, _flag)));

    public override string SummaryText
    {
        get
        {
            if (HasGroups && !_showMatched)
            {
                return $"{Label}：対応の指定が無い商品だけ";
            }

            var values = Chips.Select(chip => chip.Text)
                .Concat(HasInclude && _includeOn ? [_includeLabel!] : Array.Empty<string>());
            return $"{Label}：{string.Join(_matchAll && AllowsAnd ? " かつ " : "・", values)}"
                + (HasFlag && _flag != _flagDefault ? $"（{(_flag ? FlagLabel : FlagLabel + "を除く")}）" : string.Empty)
                + (HasGroups && _showUnspecified ? "（対応の指定が無い商品も含める）" : string.Empty);
        }
    }

    public override void Clear()
    {
        Chips.Clear();
        _matchAll = false;
        _flag = _flagDefault;
        _showMatched = true;
        _showUnspecified = false;
        _includeOn = false;
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(Flag));
        OnPropertyChanged(nameof(ShowMatched));
        OnPropertyChanged(nameof(ShowUnspecified));
        OnPropertyChanged(nameof(IncludeOn));
        RefreshSuggestions();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var chip in Chips)
        {
            chip.Count = items.Count(item => (_isUnspecified is null || !_isUnspecified(item, context))
                && _matches(item, context, chip.Key, _flag));
        }

        if (_includeMatches is not null)
        {
            _includeCount = items.Count(item => _includeMatches(item, context));
            OnPropertyChanged(nameof(IncludeText));
        }

        if (_isUnspecified is null)
        {
            return;
        }

        // 各かたまりが「他の条件を当てたうえで」何件か。どちらに印を付けるかの判断に使う
        _unspecifiedCount = items.Count(item => _isUnspecified(item, context));
        _matchedCount = items.Count(item => !_isUnspecified(item, context) && MatchesChips(item, context));
        OnPropertyChanged(nameof(MatchedLabel));
        OnPropertyChanged(nameof(UnspecifiedLabel));
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Items = Chips.Select(chip => chip.Key).ToList(),
            MatchAll = _matchAll,
            Flag = _flag,
            ShowMatched = _showMatched,
            ShowUnspecified = _showUnspecified,
            IncludeFavorites = _includeOn,
        };

    protected override void Read(SearchModuleState state)
    {
        Chips.Clear();
        foreach (var key in state.Items)
        {
            AddKey(key, notify: false);
        }

        _matchAll = state.MatchAll;
        _flag = FlagLabel is null ? _flagDefault : state.Flag;

        // 両方切った状態は作らない（手で書き換えた状態でも、対応している商品を出す側に戻す）
        _showUnspecified = HasGroups && state.ShowUnspecified;
        _showMatched = !HasGroups || state.ShowMatched || !_showUnspecified;
        _includeOn = HasInclude && state.IncludeFavorites;
        OnPropertyChanged(nameof(IncludeOn));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(Flag));
        OnPropertyChanged(nameof(ShowMatched));
        OnPropertyChanged(nameof(ShowUnspecified));
    }

    /// <summary>候補から、積んだ物を除いて並べ直す（件数に比例して縦に伸びないよう、候補付きの欄から1件ずつ積む）。</summary>
    private void RefreshSuggestions()
    {
        var chosen = Chips.Select(chip => chip.Key).ToHashSet(StringComparer.Ordinal);
        Suggestions.Clear();
        foreach (var (text, key) in _entries)
        {
            if (!chosen.Contains(key))
            {
                Suggestions.Add(text);
            }
        }

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowsMatchMode));
        OnPropertyChanged(nameof(HasFlag));
    }
}

/// <summary>
/// 数の範囲（ユーザ案「上下指定・数値・単品」）。スライダ2本と数の欄。指定していない側は制限しない。
/// </summary>
public sealed class RangeModule : SearchModule
{
    private readonly Func<ItemRecord, string?, IReadOnlyList<int>> _values;
    /// <summary>分布の帯の高さ（px）。棒の高さをここに収める。</summary>
    public const double HistogramHeight = 22;

    /// <summary>分布の帯の棒の数。多くしても細くて読めないので、目盛（10刻み）と噛み合う数にする。</summary>
    private const int HistogramBuckets = 40;

    private string _minText = string.Empty;
    private string _maxText = string.Empty;
    private bool _minEnabled = true;
    private bool _maxEnabled = true;
    private bool _ignoreOutliers = true;
    private int? _outlierFence;
    private int _outlierCount;
    private bool _valuesFromState;
    private bool _defaultsApplied;
    private ChoiceOption? _source;
    private double _sliderMaximum = 100;

    /// <param name="values">商品と元（価格の「購入額／BOOTHの価格」）→ 照らす数（どれか1つでも範囲に入れば当たり）。</param>
    /// <param name="sources">数の元の選択肢。先頭が既定。</param>
    public RangeModule(
        SearchModuleKind kind,
        Func<ItemRecord, string?, IReadOnlyList<int>> values,
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
                RefreshBounds();
                NotifyChanged();
            }
        }
    }

    /// <summary>元ごとの、手元の商品の数の全部。両端・分布の帯をここから出す。検索側が入れる。</summary>
    public Func<string?, IEnumerable<int>>? AllValuesOf { get; set; }

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
            var wasAtEnd = ParseNumber(_maxText) is not { } oldMax || oldMax >= (int)SliderMaximum;
            RefreshBounds();

            var end = (int)SliderMaximum;
            if (wasAtEnd || ParseNumber(_maxText) > end)
            {
                _maxText = end.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(MaxText));
            }

            if (ParseNumber(_minText) > end)
            {
                _minText = end.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(MinText));
            }

            OnPropertyChanged(nameof(LowPosition));
            OnPropertyChanged(nameof(HighPosition));
            NotifyChanged();
        }
    }

    /// <summary>何を外しているかを数で言う（境の数と、外れ値の数）。</summary>
    public string OutlierLabel => _outlierFence is { } fence && _outlierCount > 0
        ? $"外れ値を無視（{fence.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以上の{_outlierCount}個）"
        : "外れ値を無視（今は該当なし）";

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

        var min = ParseNumber(_minText) ?? (int)SliderMinimum;
        var max = ParseNumber(_maxText) ?? (int)SliderMaximum;
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
    public string MinHint => MinEnabled ? ScaleHint : "左の印を付けると、下限を使えます（今は下限を見ていません）。";

    public string MaxHint => MaxEnabled ? ScaleHint : "左の印を付けると、上限を使えます（今は上限を見ていません）。";

    public string ScaleHint => UsesFloor
        ? $"左の1目盛が 0〜{Floor}{Unit}、その先は対数（多いところを広く）です。"
        : "目盛は対数です（数の小さいところを広く取っています）。";

    public string MaximumLabel => ((int)SliderMaximum).ToString("N0", CultureInfo.CurrentCulture) + Unit;

    /// <summary>効いている下限。切っていれば null（制限しない）。欄が空なら左端を下限とする。</summary>
    public int? Min => _minEnabled ? ParseNumber(_minText) ?? (int)SliderMinimum : null;

    /// <summary>効いている上限。切っていれば null。欄が空なら右端を上限とする。</summary>
    public int? Max => _maxEnabled ? ParseNumber(_maxText) ?? (int)SliderMaximum : null;

    /// <summary>
    /// 左のスライダの位置（0〜100）。**端も値として受ける**（左端＝手元の最小値以上）。切るのは左のトグル。
    ///
    /// スライダは位置で持ち、数には割り戻す。スライダの右端を数に結ぶと、
    /// 右端が決まる前に値が既定の右端（10）へ丸められ、それが上限として書き戻されうる。
    /// </summary>
    public double LowPosition
    {
        get => ToPosition(ParseNumber(_minText) ?? (int)SliderMinimum);
        set => MinText = ToNumber(value).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>右のスライダの位置（0〜100）。**端も値として受ける**（右端＝手元の最大値以下）。</summary>
    public double HighPosition
    {
        get => ToPosition(ParseNumber(_maxText) ?? (int)SliderMaximum);
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
    private int ToNumber(double position)
    {
        var at = Math.Clamp(position, 0, 100);

        if (UsesFloor)
        {
            // 左端の1目盛は 0〜Floor を直線で（0も50円も指せる）。そこから先は Floor〜上限の対数
            if (at <= FloorBand)
            {
                return (int)Math.Round(at / FloorBand * Floor);
            }

            var ratio = (at - FloorBand) / (100 - FloorBand);
            var scaled = Math.Exp(Math.Log(Floor) + (ratio * (Math.Log(SliderMaximum) - Math.Log(Floor))));
            return (int)Math.Round(Math.Clamp(scaled, Floor, SliderMaximum));
        }

        var value = SliderMinimum + Math.Exp(at / 100.0 * Math.Log(1 + Span)) - 1;
        return (int)Math.Round(Math.Clamp(value, SliderMinimum, SliderMaximum));
    }

    private double ToPosition(int value)
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
        var all = (AllValuesOf?.Invoke(_source?.Key) ?? []).ToList();

        // 外れ値の境は、外す前の数の全部から決める（元を変えれば取り直す）
        _outlierFence = SupportsOutliers ? Outliers.UpperFence(all) : null;
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
            _maxText = ((int)SliderMaximum).ToString(CultureInfo.InvariantCulture);

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

    private void RefreshHistogram(IReadOnlyList<int> values)
    {
        var counts = new int[HistogramBuckets];
        foreach (var value in values)
        {
            var index = Math.Clamp((int)(ToPosition(value) / 100 * HistogramBuckets), 0, HistogramBuckets - 1);
            counts[index]++;
        }

        var peak = counts.Max();

        // 1件しかない所も見えるように、最低の高さを持たせる（0件の所は出さない）
        Histogram = peak == 0
            ? []
            : counts.Select(count => new HistogramBar(count == 0 ? 0 : Math.Max(2, count * HistogramHeight / peak))).ToList();

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

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        if (!HasCondition)
        {
            return true;
        }

        var (min, max) = (Min, Max);
        var values = _values(item, _source?.Key);
        if (IgnoresOutliersNow)
        {
            // 外れ値の数だけを外す。商品は他の種類の価格で照らす
            var fence = _outlierFence!.Value;
            values = values.Where(value => value < fence).ToList();
        }

        return values.Any(value => (min is null || value >= min) && (max is null || value <= max));
    }

    public override string SummaryText
    {
        get
        {
            var head = $"{Label}{(HasSources ? $"（{_source?.Label}）" : string.Empty)}";
            var parts = new[]
            {
                Min is { } min ? $"{min.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以上" : null,
                Max is { } max ? $"{max.ToString("N0", CultureInfo.CurrentCulture)}{Unit}以下" : null,
            }.OfType<string>().ToList();

            var outliers = IgnoresOutliersNow && _outlierCount > 0 ? "（外れ値を除く）" : string.Empty;
            return parts.Count == 0 ? head + outliers : $"{head} {string.Join(" ", parts)}{outliers}";
        }
    }

    /// <summary>足したときの姿に戻す（上下とも効かせ、幅は端から端まで）。</summary>
    public override void Clear()
    {
        _minEnabled = true;
        _maxEnabled = true;
        _ignoreOutliers = true;
        _valuesFromState = false;
        _defaultsApplied = false;
        OnPropertyChanged(nameof(MinEnabled));
        OnPropertyChanged(nameof(MaxEnabled));
        OnPropertyChanged(nameof(IgnoreOutliers));
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
            Choice = _source?.Key,
        };

    protected override void Read(SearchModuleState state)
    {
        _minText = state.Min ?? string.Empty;
        _maxText = state.Max ?? string.Empty;
        _minEnabled = state.MinEnabled;
        _maxEnabled = state.MaxEnabled;
        _ignoreOutliers = state.IgnoreOutliers;
        OnPropertyChanged(nameof(IgnoreOutliers));

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
        RefreshBounds();
    }
}

/// <summary>
/// 日付の範囲（ユーザ案「カレンダー」）。欄に打つか、右のカレンダーで選ぶ。指定した日をまるまる含む。
/// 打った文字の読み方は <see cref="DateText"/>（年の無い日付は今日以前で最も近い日、月だけなら月初め／月末）。
/// </summary>
public sealed class DateModule : SearchModule
{
    private readonly Func<ItemRecord, DateOnly?> _value;
    private string _sinceText = string.Empty;
    private string _tillText = string.Empty;
    private bool _sinceEnabled = true;
    private bool _tillEnabled = true;
    private bool _valuesFromState;
    private bool _defaultsApplied;
    private DateOnly? _dataSince;
    private DateOnly? _dataTill;

    public DateModule(SearchModuleKind kind, Func<ItemRecord, DateOnly?> value)
        : base(kind)
        => _value = value;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>手元の商品の日付の全部。両端（足したときの既定）をここから出す。検索側が入れる。</summary>
    public Func<IEnumerable<DateOnly>>? AllDatesOf { get; set; }

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
        var dates = (AllDatesOf?.Invoke() ?? []).ToList();
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
        if (!HasCondition)
        {
            return true;
        }

        // 日付が分からない商品は、日付で絞った時点で外す。「値が小さい」ではなく「値が無い」ので、範囲のどこにも当てはまらない
        if (_value(item) is not { } date)
        {
            return false;
        }

        return (Since is not { } since || date >= since) && (Till is not { } till || date <= till);
    }

    public override string SummaryText
    {
        get
        {
            var parts = new[]
            {
                Since is { } since ? $"{since:yyyy-MM-dd}から" : null,
                Till is { } till ? $"{till:yyyy-MM-dd}まで" : null,
            }.OfType<string>().ToList();

            return parts.Count == 0 ? Label : $"{Label} {string.Join(" ", parts)}";
        }
    }

    /// <summary>足したときの姿に戻す（両方効かせ、一番古い日〜一番新しい日）。</summary>
    public override void Clear()
    {
        _sinceEnabled = true;
        _tillEnabled = true;
        _valuesFromState = false;
        _defaultsApplied = false;
        OnPropertyChanged(nameof(SinceEnabled));
        OnPropertyChanged(nameof(TillEnabled));
        RefreshBounds();
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Min = _sinceText,
            Max = _tillText,
            MinEnabled = _sinceEnabled,
            MaxEnabled = _tillEnabled,
        };

    protected override void Read(SearchModuleState state)
    {
        _sinceText = state.Min ?? string.Empty;
        _tillText = state.Max ?? string.Empty;
        _sinceEnabled = state.MinEnabled;
        _tillEnabled = state.MaxEnabled;

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
            return $"{side}は見ていません（切っています）";
        }

        if (text.Trim().Length == 0)
        {
            return fallback is { } edge ? $"{edge:yyyy年M月d日}{suffix}（手元の端）" : string.Empty;
        }

        return DateText.Parse(text, isEnd, Today) is { } date
            ? $"{date:yyyy年M月d日}{suffix}"
            : "日付として読めません（例：2026/9/1・9/1・2026/9）";
    }

    /// <summary>効かせていない側に、使えない理由を出す（`ui-rules.md`・E11）。</summary>
    public string SinceHint => SinceEnabled
        ? "日付は「2026-09-01」「9/1」のように入れられます。"
        : "左の印を付けると、始まりの日を使えます（今は始まりを見ていません）。";

    public string TillHint => TillEnabled
        ? "日付は「2026-09-01」「9/1」のように入れられます。"
        : "左の印を付けると、終わりの日を使えます（今は終わりを見ていません）。";

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

/// <summary>
/// 属性の値（ユーザ案「上下指定・数値・AND OR」）。属性ごとにスライダ2本。複数の属性は AND（全部）か OR（どれか）。
/// 属性を足すこと自体が「この属性で選ぶ」という意思表示なので、評価していない商品は 0〜100 のままでも外す。
/// </summary>
public sealed class AttributeModule : SearchModule
{
    private readonly List<string> _names = [];
    private bool _matchAll = true;
    private RelayCommand? _add;

    public AttributeModule()
        : base(SearchModuleKind.Attribute)
    {
    }

    public ObservableCollection<AttributeFilter> Rows { get; } = [];

    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => _names.Count > 0;

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

    public bool ShowsMatchMode => Rows.Count > 1;

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => AddRow(parameter as string));

    /// <summary>属性の名前（マスタ）を入れる。消えた属性の行は外す。</summary>
    public void SetNames(IEnumerable<string> names)
    {
        _names.Clear();
        _names.AddRange(names);

        foreach (var row in Rows.Where(row => !_names.Contains(row.Name, StringComparer.CurrentCultureIgnoreCase)).ToList())
        {
            Rows.Remove(row);
        }

        RefreshSuggestions();
    }

    public void AddRow(string? name, int min = 0, int max = 100, bool notify = true)
    {
        var known = _names.FirstOrDefault(entry => string.Equals(entry, name?.Trim(), StringComparison.CurrentCultureIgnoreCase))
            ?? (notify ? null : name?.Trim());
        if (string.IsNullOrEmpty(known) || Rows.Any(row => string.Equals(row.Name, known, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        var row = new AttributeFilter { Name = known };
        row.Min = min;
        row.Max = max;
        row.Changed += NotifyChanged;
        row.RemoveCommand = new RelayCommand(() =>
        {
            Rows.Remove(row);
            RefreshSuggestions();
            NotifyChanged();
        });

        Rows.Add(row);
        RefreshSuggestions();
        if (notify)
        {
            NotifyChanged();
        }
    }

    protected override bool HasCondition => Rows.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => Rows.Count == 0 || (_matchAll ? Rows.All(row => row.Matches(item)) : Rows.Any(row => row.Matches(item)));

    public override string SummaryText
        => $"{Label}：{string.Join(_matchAll ? " かつ " : "・", Rows.Select(row => $"{row.Name} {row.Min}〜{row.Max}"))}";

    public override void Clear()
    {
        Rows.Clear();
        _matchAll = true;
        OnPropertyChanged(nameof(MatchAll));
        RefreshSuggestions();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with { Ranges = Rows.Select(row => new AttributeRange(row.Name, row.Min, row.Max)).ToList(), MatchAll = _matchAll };

    protected override void Read(SearchModuleState state)
    {
        Rows.Clear();
        foreach (var range in state.Ranges)
        {
            AddRow(range.Name, range.Min, range.Max, notify: false);
        }

        _matchAll = state.MatchAll || state.Ranges.Count == 0;
        OnPropertyChanged(nameof(MatchAll));
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        foreach (var name in _names.Where(name =>
            !Rows.Any(row => string.Equals(row.Name, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            Suggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowsMatchMode));
    }
}

/// <summary>
/// 最近（ユーザ判断 2026-09-16 Q1：プルダウンと日数）。使った（Unityへ送った）・見た（商品ページを開いた）・手元に入った。
/// 記録が無い商品は、日数を入れた時点で外す（「値が小さい」ではなく「値が無い」）。
/// </summary>
public sealed class RecentModule : SearchModule
{
    private ChoiceOption _selected;
    private string _daysText = string.Empty;

    public RecentModule()
        : base(SearchModuleKind.Recent)
    {
        Options =
        [
            new ChoiceOption("used", "使った（Unityへ送った）"),
            new ChoiceOption("viewed", "見た（商品ページを開いた）"),
            new ChoiceOption("added", "手元に入った"),
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
                NotifyChanged();
            }
        }
    }

    public string DaysText
    {
        get => _daysText;
        set
        {
            if (SetField(ref _daysText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasDays));
                NotifyChanged();
            }
        }
    }

    /// <summary>入れた日数を消す手段を出すか（ユーザ指示 2026-09-16）。</summary>
    public bool HasDays => _daysText.Length > 0;

    public RelayCommand ClearDaysCommand => _clearDays ??= new RelayCommand(() => DaysText = string.Empty);

    private RelayCommand? _clearDays;

    private int? Days => ParseNumber(_daysText) is > 0 and var days ? days : null;

    public RecentKind SelectedKind => _selected.Key switch
    {
        "viewed" => RecentKind.Viewed,
        "added" => RecentKind.Added,
        _ => RecentKind.Used,
    };

    protected override bool HasCondition => Days is not null;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => !HasCondition || RecentActivity.IsWithin(context.Recent.Of(item.Id, SelectedKind), Days ?? 0, context.Now);

    public override string SummaryText => $"最近{_selected.Label.Split('（')[0]} {Days}日以内";

    public override void Clear()
    {
        _daysText = string.Empty;
        OnPropertyChanged(nameof(DaysText));
        OnPropertyChanged(nameof(HasDays));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    protected override SearchModuleState Write(SearchModuleState state) => state with { Choice = _selected.Key, Min = _daysText };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        _daysText = state.Min ?? string.Empty;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(DaysText));
    }
}

/// <summary>「条件を追加」のメニューの1行。追加済みはグレー（ユーザ案：一度しか追加できない）。</summary>
public sealed class SearchModuleMenuEntry : ViewModelBase
{
    private bool _isAvailable = true;

    public SearchModuleMenuEntry(SearchModuleInfo info, Action<SearchModuleKind> add)
    {
        Kind = info.Kind;
        Label = info.Label;
        Hint = info.Hint;
        AddCommand = new RelayCommand(() => add(Kind), () => IsAvailable);
    }

    public SearchModuleKind Kind { get; }

    public string Label { get; }

    public string Hint { get; }

    /// <summary>区切り線ではない（区切り線と同じ一覧に並ぶので、見た目を分ける印を揃えて持つ）。</summary>
    public bool IsSeparator => false;

    public bool IsAvailable
    {
        get => _isAvailable;
        set
        {
            if (SetField(ref _isAvailable, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand AddCommand { get; }
}

/// <summary>
/// 「条件を追加」のメニューの見出し（ユーザ案：BOOTHの情報・商品の情報・カレンダー・スライダー・利用状況）。
/// <paramref name="Entries"/> は <see cref="SearchModuleMenuEntry"/> と <see cref="SearchModuleMenuSeparator"/> が並ぶ。
/// </summary>
public sealed record SearchModuleMenuHeading(string Title, IReadOnlyList<object> Entries);

/// <summary>
/// 見出しの中の区切り線（意味のまとまりの間）。**区切りごとに別の物を作る**——同じ物を1つの一覧に何度も入れると、
/// WPF の一覧は項目と部品の対応を取り違える。
/// </summary>
public sealed class SearchModuleMenuSeparator
{
    /// <summary>メニューの項目の見た目を、線に差し替える印。</summary>
    public bool IsSeparator => true;
}
