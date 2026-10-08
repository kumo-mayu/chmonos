using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// ユーザータグ（ユーザ指示 2026-09-28）。欄の候補は大分類だけで、選ぶと大分類の条件が1つ立つ。
/// 各大分類の中に小分類の追加欄を置き、小分類（と「小分類なし」）を1件ずつ足す。小分類どうしの AND／OR は大分類ごと。
///
/// 前は「大分類」と「大分類 › 小分類」を1本の候補に並べていた。小分類の多い大分類では候補が長くなり、
/// 「大分類は付けたが小分類はまだ」の商品を探す手が無かった。
/// </summary>
public sealed class UserTagModule : SearchModule
{
    private readonly List<(string Top, IReadOnlyList<string> Subs)> _tops = [];
    private bool _matchAll;
    private bool _mastersReady;
    private RelayCommand? _add;

    public UserTagModule()
        : base(SearchModuleKind.UserTag)
    {
    }

    public ObservableCollection<UserTagTopRow> Rows { get; } = [];

    /// <summary>まだ足していない大分類。</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    /// <summary>
    /// 欄を出すか。**大分類を全部足しても出したまま**にする（改変の2段と同じ・ユーザ指摘 2026-10-06：欄が消えると下の枠が上へずれる）。
    /// 大分類が1つも無いときだけ隠し、代わりに空の文を出す。
    /// </summary>
    public bool ShowsInput => _tops.Count > 0;

    /// <summary>大分類が1つも無い（足す物が無い）。足し終えて候補が尽きたときは言わない。</summary>
    public bool IsMasterEmpty => _mastersReady && _tops.Count == 0;

    /// <summary>大分類どうしを全部満たす（AND）か。前の「候補から積む」形と同じく、既定は OR。</summary>
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

    /// <summary>
    /// 2つ以上の大分類を足したときだけ押せる（1つなら結果が変わらない）。
    /// **大分類どうしの AND／OR は、0〜1件の間は出さない**（ユーザ判断 2026-10-06「大分類は0-1件では表示しない。少し画面がずれてしまうがこれは受け入れる」）。
    /// 枠の中の小分類どうしは、0〜1件でも薄く出したまま（<see cref="UserTagTopRow"/>）。
    /// </summary>
    public bool CanChooseMatchMode => Rows.Count > 1;

    /// <summary>大分類どうしの AND／OR を出すか（<see cref="CanChooseMatchMode"/> と同じ。出ている間は押せるので薄くしない）。</summary>
    public bool ShowsMatchMode => CanChooseMatchMode;

    public bool MatchModeDimmed => false;

    public string MatchModeTip => CanChooseMatchMode ? TopMatchAllHint : MatchModeText.NeedsTwoOf("大分類");

    /// <summary>大分類どうしのチェックの文。枠の中の小分類どうしのチェックと見分けられるよう、何どうしかを言う（言い方は <see cref="MatchModeText"/>）。</summary>
    public string TopMatchAllText => MatchModeText.AllOf("大分類");

    public string TopMatchAllHint => MatchModeText.AllHintOf("大分類");

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => AddTop(parameter as string));

    /// <summary>大分類と小分類の一覧（タグの管理の並び）を入れる。一覧から消えた大分類・小分類の条件は外す（属性と同じ）。</summary>
    public void SetMasters(IEnumerable<(string Top, IReadOnlyList<string> Subs)> tops)
    {
        _tops.Clear();
        _tops.AddRange(tops);
        _mastersReady = true;

        foreach (var row in Rows.ToList())
        {
            var known = _tops.FirstOrDefault(top => string.Equals(top.Top, row.Top, StringComparison.CurrentCultureIgnoreCase));
            if (known.Top is null)
            {
                Rows.Remove(row);
                continue;
            }

            row.SetSubs(known.Subs);
        }

        RefreshSuggestions();
    }

    /// <summary>大分類の条件を立てる。小分類は空欄で始め、この大分類が付いている商品で絞る。</summary>
    /// <param name="notify">false なら絞り直さない（状態を戻すとき・他の画面から条件を渡すとき）。一覧に無い名前もそのまま受ける（一覧は後から入る）。</param>
    public UserTagTopRow? AddTop(string? name, bool notify = true)
    {
        var trimmed = name?.Trim();
        var known = _tops.Select(top => top.Top)
            .FirstOrDefault(top => string.Equals(top, trimmed, StringComparison.CurrentCultureIgnoreCase))
            ?? (notify ? null : trimmed);
        if (string.IsNullOrEmpty(known))
        {
            return null;
        }

        if (Rows.FirstOrDefault(row => string.Equals(row.Top, known, StringComparison.CurrentCultureIgnoreCase)) is { } existing)
        {
            return existing;
        }

        var row = new UserTagTopRow(known);
        row.SetSubs(_tops.FirstOrDefault(top => string.Equals(top.Top, known, StringComparison.CurrentCultureIgnoreCase)).Subs ?? []);
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

        return row;
    }

    protected override bool HasCondition => Rows.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
    {
        EnsurePrepared(context);
        return UserTagCondition.MatchesAll(_preparedConditions, _matchAll, item.Local.UserTags);
    }

    /// <summary>
    /// 条件の並びを作るのは絞り込みの1回で1回だけ（案c）。前は商品ごとに枠の数だけ条件を作り直していた。
    /// 小分類のチップを足し外しすると枠の Changed から <see cref="SearchModule.NotifyChanged"/> が来て捨てられる。
    /// </summary>
    protected override void PrepareCore(SearchModuleContext context) => _preparedConditions = Conditions();

    private List<UserTagCondition> _preparedConditions = [];

    private List<UserTagCondition> Conditions() => Rows.Select(row => row.Condition).ToList();

    /// <summary>除くときも、大分類どうし・枠の中の小分類の AND／OR はそのまま効かせ、その反対を取る（D3・ユーザ判断 2026-10-01 の案1）。</summary>
    public override bool SupportsExclude => true;

    protected override string SummaryBody => JoinValues(Rows.Select(row => row.SummaryText), _matchAll);

    public override void Clear()
    {
        Rows.Clear();
        _matchAll = false;
        OnPropertyChanged(nameof(MatchAll));
        RefreshSuggestions();
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var row in Rows)
        {
            row.RefreshCounts(items);
        }
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with { UserTags = Conditions(), MatchAll = _matchAll };

    protected override void Read(SearchModuleState state)
    {
        Rows.Clear();
        foreach (var condition in state.UserTags)
        {
            AddTop(condition.Top, notify: false)?.Load(condition);
        }

        _matchAll = state.MatchAll;
        OnPropertyChanged(nameof(MatchAll));
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        foreach (var (top, _) in _tops.Where(top =>
            !Rows.Any(row => string.Equals(row.Top, top.Top, StringComparison.CurrentCultureIgnoreCase))))
        {
            Suggestions.Add(top);
        }

        // 枠の足し外しはどれもここを通る。用意した条件の並びを捨てる
        Unprepare();
        OnPropertyChanged(nameof(ShowsInput));
        OnPropertyChanged(nameof(IsMasterEmpty));
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(ShowsMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
    }
}

/// <summary>
/// ユーザータグの条件の中の、大分類1つ。小分類は候補付きの欄から1件ずつ足す（候補を全部並べると小分類の数だけ縦に伸びる）。
/// </summary>
public sealed class UserTagTopRow : ViewModelBase
{
    /// <summary>
    /// 「小分類なし」のチップの鍵。小分類の名前は鍵にそのまま使うので、名前に入らない字で始める
    /// （同じ文字の小分類を作っても取り違えない）。
    /// </summary>
    private const string NoSubKey = "\u001Fnosub";

    public const string NoSubText = "小分類なし";

    private readonly List<string> _subs = [];
    private bool _matchAll;
    private int _count = -1;
    private RelayCommand? _add;

    public UserTagTopRow(string top)
    {
        Top = top;
    }

    public string Top { get; }

    /// <summary>条件が変わった（モジュールが絞り直す）。</summary>
    public event Action? Changed;

    public RelayCommand? RemoveCommand { get; set; }

    public ObservableCollection<ListChip> Chips { get; } = [];

    /// <summary>まだ足していない小分類と「小分類なし」。</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    /// <summary>欄を出すか。「小分類なし」が必ず選べるので、全部選んだ後も含めていつも出す（枠の高さが変わらないように）。</summary>
    public bool ShowsInput => true;

    /// <summary>小分類どうしを全部満たす（AND）か。既定は OR（大分類どうしの既定に揃える）。</summary>
    public bool MatchAll
    {
        get => _matchAll;
        set
        {
            if (SetField(ref _matchAll, value))
            {
                RefreshConflict();
                Changed?.Invoke();
            }
        }
    }

    /// <summary>小分類が2つ以上のときだけ押せる（1つの間も隠さずに薄くする。大分類どうしと同じ）。</summary>
    public bool CanChooseMatchMode => Chips.Count > 1;

    public bool MatchModeDimmed => !CanChooseMatchMode;

    public string MatchModeTip => CanChooseMatchMode ? MatchModeText.AllHint : MatchModeText.NeedsTwoOf("小分類");

    /// <summary>
    /// 「小分類なし」を小分類と AND で結んでいるか。大分類は付いているが小分類が無い商品と、その小分類を持つ商品は重ならないので、必ず0件になる。
    /// 選べなくするのは制限が強いので選べるまま、チップの色だけ変えて見て分かるようにする（ユーザ判断 2026-10-06・メモ82〜84 の判断3）
    /// </summary>
    public bool HasNoSubConflict => _matchAll && Chips.Count > 1 && Chips.Any(chip => chip.Key == NoSubKey);

    private void RefreshConflict()
    {
        var conflict = HasNoSubConflict;
        foreach (var chip in Chips)
        {
            chip.IsConflict = conflict && chip.Key == NoSubKey;
        }

        OnPropertyChanged(nameof(HasNoSubConflict));
    }

    /// <summary>この大分類の条件に、他の条件のもとで当たる件数。</summary>
    public string CountText => _count < 0 ? string.Empty : _count.ToString(CultureInfo.InvariantCulture);

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => AddSub(parameter as string));

    public UserTagCondition Condition => new()
    {
        Top = Top,
        Subs = Chips.Where(chip => chip.Key != NoSubKey).Select(chip => chip.Key).ToList(),
        NoSub = Chips.Any(chip => chip.Key == NoSubKey),
        MatchAll = _matchAll,
    };

    /// <summary>畳んだ姿と検索の履歴に出す1行の中の、この大分類の分。</summary>
    public string SummaryText
        => Chips.Count == 0
            ? Top
            : $"{Top}（{string.Join("・", Chips.Select(chip => chip.Text))}{(_matchAll && Chips.Count > 1 ? " のすべて" : string.Empty)}）";

    /// <summary>この大分類の小分類の一覧を入れる。一覧から消えた小分類のチップは外す。</summary>
    public void SetSubs(IEnumerable<string> subs)
    {
        _subs.Clear();
        _subs.AddRange(subs);

        foreach (var chip in Chips.Where(chip => chip.Key != NoSubKey
            && !_subs.Contains(chip.Key, StringComparer.CurrentCultureIgnoreCase)).ToList())
        {
            Chips.Remove(chip);
        }

        RefreshSuggestions();
    }

    /// <summary>状態を戻す。通知は出さない（呼ぶ側がまとめて絞り直す）。</summary>
    public void Load(UserTagCondition condition)
    {
        if (condition.NoSub)
        {
            AddChip(NoSubKey, NoSubText, notify: false);
        }

        foreach (var sub in condition.Subs)
        {
            AddChip(sub, sub, notify: false);
        }

        _matchAll = condition.MatchAll;
        OnPropertyChanged(nameof(MatchAll));
        RefreshConflict();
    }

    /// <summary>小分類を足す（他の画面から「この小分類の商品」を見に来たとき）。通知は出さない。</summary>
    public void AddSubQuietly(string sub) => AddChip(sub, sub, notify: false);

    public void RefreshCounts(IReadOnlyList<ItemRecord> items)
    {
        var condition = Condition;
        _count = items.Count(item => condition.Matches(item.Local.UserTags));
        OnPropertyChanged(nameof(CountText));

        foreach (var chip in Chips)
        {
            var single = new UserTagCondition
            {
                Top = Top,
                Subs = chip.Key == NoSubKey ? [] : [chip.Key],
                NoSub = chip.Key == NoSubKey,
            };
            chip.Count = items.Count(item => single.Matches(item.Local.UserTags));
        }
    }

    private void AddSub(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        if (trimmed == NoSubText)
        {
            AddChip(NoSubKey, NoSubText, notify: true);
            return;
        }

        if (_subs.FirstOrDefault(sub => string.Equals(sub, trimmed, StringComparison.CurrentCultureIgnoreCase)) is { } known)
        {
            AddChip(known, known, notify: true);
        }
    }

    private void AddChip(string key, string text, bool notify)
    {
        if (Chips.Any(chip => string.Equals(chip.Key, key, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        var chip = new ListChip(key, text);
        chip.RemoveCommand = new RelayCommand(() =>
        {
            Chips.Remove(chip);
            RefreshSuggestions();
            Changed?.Invoke();
        });

        // 「小分類なし」は小分類の前に置く（どの大分類でも同じ位置に出る）
        if (key == NoSubKey)
        {
            Chips.Insert(0, chip);
        }
        else
        {
            Chips.Add(chip);
        }

        RefreshSuggestions();
        if (notify)
        {
            Changed?.Invoke();
        }
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();

        // 「小分類なし」は先頭。小分類の多い大分類でも、打たずに開くだけで見える
        if (!Chips.Any(chip => chip.Key == NoSubKey))
        {
            Suggestions.Add(NoSubText);
        }

        foreach (var sub in _subs.Where(sub => !Chips.Any(chip => string.Equals(chip.Key, sub, StringComparison.CurrentCultureIgnoreCase))))
        {
            Suggestions.Add(sub);
        }

        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
        RefreshConflict();
    }
}
