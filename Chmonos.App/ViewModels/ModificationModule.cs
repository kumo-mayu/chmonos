using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Chmonos.App.Controls;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 条件「改変」（ユーザ判断 2026-10-06）。ユーザータグ（<see cref="UserTagModule"/>）と同じ2段の形：
/// 1段目にアバター（絵付き）、2段目にそのアバターの改変（絵付き）。
///
/// - アバターだけを選ぶ＝そのアバターの改変のどれかに使った商品（前の「アバター（このアバターの改変すべて）」と同じ）。改変を選ぶとその改変に絞る。
/// - 改変どうし（アバターの枠の中）・アバターどうしは、ユーザータグと同じく AND にするかのチェック（言い方は <see cref="MatchModeText"/>）。
/// - 1段目の欄は商品ページの「改変に追加」の窓と同じ探し方（<see cref="ModificationSearchText"/>）。候補にはアバターと改変が並び、
///   **改変を選ぶと、そのアバターを足した上で、その改変を2段目で選んだ状態にする**。
/// - 画面では「大分類」「小分類」と書かない（ユーザ「大分類や小分類は例として出しただけ」）。アバターと改変の言葉で書く。
/// </summary>
public sealed class ModificationModule : SearchModule
{
    /// <summary>1段目の候補の群の見出し。</summary>
    public static IReadOnlyList<string> Headings { get; } = ["アバター", "改変"];

    private readonly Func<string, ImageSource?> _avatarIcon;
    private readonly Func<ModificationRecord, ImageSource?> _modificationIcon;

    /// <summary>1段目の候補の文字 → (アバター, 改変か null)。</summary>
    private readonly Dictionary<string, (string Avatar, string? Modification)> _entries = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly Dictionary<string, Candidate> _candidates = new(StringComparer.CurrentCultureIgnoreCase);
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _avatarNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ModificationRecord>> _byAvatar = new(StringComparer.Ordinal);
    private bool _sourcesReady;
    private bool _matchAll;
    private RelayCommand? _add;

    /// <summary>1段目の候補1件の、当てる材料（名前・アバターの名前と呼び方・プロジェクトの名前）。</summary>
    private sealed record Candidate(string Name, string AvatarText, string ProjectName, IReadOnlyList<string> Aliases, int Group);

    /// <param name="avatarIcon">アバターの商品ID → 絵（`AvatarImageSync.IconPath` と同じ決め方）。</param>
    /// <param name="modificationIcon">改変 → 絵（`ModificationIcon.PathOf` と同じ決め方）。</param>
    public ModificationModule(Func<string, ImageSource?> avatarIcon, Func<ModificationRecord, ImageSource?> modificationIcon)
        : base(SearchModuleKind.Modification)
    {
        _avatarIcon = avatarIcon;
        _modificationIcon = modificationIcon;
    }

    public ObservableCollection<ModificationAvatarRow> Rows { get; } = [];

    /// <summary>まだ足していないアバターと、その改変。</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>改変が1つも無い（足す物が無い）。</summary>
    public bool IsEmpty => _sourcesReady && _byAvatar.Count == 0;

    /// <summary>アバターどうしを全部満たす（AND）か。既定は OR（ユーザータグの大分類どうしと同じ）。</summary>
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

    public string AvatarMatchAllText => MatchModeText.AllOf("アバター");

    public string AvatarMatchAllHint => MatchModeText.AllHintOf("アバター");

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => Add(parameter as string));

    /// <summary>1段目の候補の頭の絵。</summary>
    public Func<string, ImageSource?> IconSelector => text => _entries.TryGetValue(text, out var entry)
        ? entry.Modification is { } id && FindRecord(entry.Avatar, id) is { } record ? _modificationIcon(record) : _avatarIcon(entry.Avatar)
        : null;

    /// <summary>1段目の候補の群（アバター → 改変）。</summary>
    public Func<string, SuggestInfo?> InfoSelector => text => _candidates.TryGetValue(text, out var candidate)
        ? new SuggestInfo(candidate.Group, [])
        : null;

    /// <summary>1段目の欄の探し方（改変に追加の窓と同じ）。</summary>
    public Func<string, string, SuggestMatch?> Matcher => (text, query) =>
    {
        if (!_candidates.TryGetValue(text, out var candidate))
        {
            return null;
        }

        var words = ModificationSearchText.Words(query);
        if (candidate.Group == 0)
        {
            // アバターの行：名前か呼び方に全部の語が入れば当たる。名前は行に出ているので札は出さない
            return words.All(word => ModificationSearchText.Contains(candidate.Name, word)
                || candidate.Aliases.Any(alias => ModificationSearchText.Contains(alias, word)))
                ? new SuggestMatch(string.Empty)
                : null;
        }

        return ModificationSearchText.Matches(candidate.Name, candidate.AvatarText, candidate.ProjectName, words, out var note, candidate.Aliases)
            ? new SuggestMatch(note)
            : null;
    };

    /// <summary>
    /// 改変の一覧を入れる。一覧から消えた改変のチップと、改変が1つも無くなったアバターの枠は外す（ユーザータグと同じ）。
    /// </summary>
    /// <param name="aliasesOf">アバターの商品ID → 名前のほかに当てる語（呼び方・正式名）。</param>
    public void SetSources(IReadOnlyList<ModificationRecord> records, Func<string, string> avatarName, Func<string, IReadOnlyList<string>> aliasesOf)
    {
        _byAvatar.Clear();
        _avatarNames.Clear();
        foreach (var record in records)
        {
            if (!_byAvatar.TryGetValue(record.AvatarItemId, out var list))
            {
                list = [];
                _byAvatar[record.AvatarItemId] = list;
                _avatarNames[record.AvatarItemId] = avatarName(record.AvatarItemId);
            }

            list.Add(record);
        }

        _entries.Clear();
        _candidates.Clear();
        _order.Clear();
        var avatars = _byAvatar.Keys.OrderBy(id => _avatarNames[id], StringComparer.CurrentCulture).ToList();
        foreach (var id in avatars)
        {
            var text = Unique(_avatarNames[id], id);
            _entries[text] = (id, null);
            _candidates[text] = new Candidate(_avatarNames[id], string.Empty, string.Empty, aliasesOf(id), 0);
            _order.Add(text);
        }

        // 同じ名前の改変は別のアバターにもありうる（「普段着」など）。重なる名前は、どれにもアバターの名前を添えて見分ける
        var shared = records.GroupBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Select(record => record.AvatarItemId).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var id in avatars)
        {
            foreach (var record in _byAvatar[id].OrderBy(record => record.Name, StringComparer.CurrentCulture))
            {
                var text = Unique(shared.Contains(record.Name) ? $"{record.Name}（{_avatarNames[id]}）" : record.Name, $"{record.Name}（{_avatarNames[id]}）");
                _entries[text] = (id, record.Id);
                _candidates[text] = new Candidate(record.Name, _avatarNames[id], ProjectNameOf(record), aliasesOf(id), 1);
                _order.Add(text);
            }
        }

        _sourcesReady = true;
        foreach (var row in Rows.ToList())
        {
            if (!_byAvatar.TryGetValue(row.Avatar, out var list))
            {
                Rows.Remove(row);
                continue;
            }

            row.SetSources(_avatarNames[row.Avatar], list);
        }

        RefreshSuggestions();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string Unique(string text, string fallback)
    {
        if (!_entries.ContainsKey(text))
        {
            return text;
        }

        var candidate = fallback;
        for (var index = 2; _entries.ContainsKey(candidate); index++)
        {
            candidate = $"{fallback}（{index}）";
        }

        return candidate;
    }

    internal static string ProjectNameOf(ModificationRecord record)
        => record.HasUnityProject ? System.IO.Path.GetFileName(record.UnityProject!.TrimEnd('\\', '/')) : string.Empty;

    private ModificationRecord? FindRecord(string avatar, string id)
        => _byAvatar.TryGetValue(avatar, out var list) ? list.FirstOrDefault(record => record.Id == id) : null;

    private void Add(string? text)
    {
        if (text is null || !_entries.TryGetValue(text.Trim(), out var entry))
        {
            return;
        }

        var row = AddAvatar(entry.Avatar);
        if (entry.Modification is { } id)
        {
            // 改変の名前で選んだら、アバターの枠を立てた上でその改変を選んだ状態にする（ユーザ判断 2026-10-06）
            row.AddModificationQuietly(id);
        }

        RefreshSuggestions();
        NotifyChanged();
    }

    /// <summary>アバターの枠を立てる（既にあればそれを返す）。通知は出さない。</summary>
    internal ModificationAvatarRow AddAvatar(string avatar)
    {
        if (Rows.FirstOrDefault(row => row.Avatar == avatar) is { } existing)
        {
            return existing;
        }

        var row = new ModificationAvatarRow(avatar, _avatarIcon, _modificationIcon);
        if (_byAvatar.TryGetValue(avatar, out var list))
        {
            row.SetSources(_avatarNames[avatar], list);
        }

        // 枠の中で改変を足し外しすると、1段目の候補（選んだ改変は出さない）も変わる
        row.Changed += () =>
        {
            RefreshSuggestions();
            NotifyChanged();
        };
        row.RemoveCommand = new RelayCommand(() =>
        {
            Rows.Remove(row);
            RefreshSuggestions();
            NotifyChanged();
        });
        Rows.Add(row);
        RefreshSuggestions();
        return row;
    }

    protected override bool HasCondition => Rows.Count > 0;

    public override bool Matches(ItemRecord item, SearchModuleContext context)
        => _matchAll
            ? Rows.All(row => row.Matches(item.Id, context.Modifications))
            : Rows.Any(row => row.Matches(item.Id, context.Modifications));

    /// <summary>除くときも、アバターどうし・枠の中の改変どうしの AND／OR はそのまま効かせ、その反対を取る（ユーザータグと同じ）。</summary>
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
            row.RefreshCounts(items, context.Modifications);
        }
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with { Modifications = Rows.Select(row => row.Condition).ToList(), MatchAll = _matchAll };

    protected override void Read(SearchModuleState state)
    {
        Rows.Clear();
        foreach (var condition in state.Modifications)
        {
            var row = AddAvatar(condition.Avatar);
            foreach (var id in condition.Modifications)
            {
                row.AddModificationQuietly(id);
            }

            row.SetMatchAllQuietly(condition.MatchAll);
        }

        _matchAll = state.MatchAll;
        OnPropertyChanged(nameof(MatchAll));
        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        // 足したアバターは候補から外す。その改変は2段目の欄から足せるので、1段目にも残す（改変の名前で探して選ぶと、その枠に足す）
        var chosen = Rows.Select(row => row.Avatar).ToHashSet(StringComparer.Ordinal);
        Suggestions.Clear();
        foreach (var text in _order)
        {
            var entry = _entries[text];
            if (entry.Modification is null && chosen.Contains(entry.Avatar))
            {
                continue;
            }

            if (entry.Modification is { } id && Rows.FirstOrDefault(row => row.Avatar == entry.Avatar) is { } row && row.Has(id))
            {
                continue;
            }

            Suggestions.Add(text);
        }

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowsMatchMode));
    }
}

/// <summary>改変の条件の中の、アバター1体の枠。改変は候補付きの欄から1件ずつ足す。</summary>
public sealed class ModificationAvatarRow : ViewModelBase
{
    private readonly Func<string, ImageSource?> _avatarIcon;
    private readonly Func<ModificationRecord, ImageSource?> _modificationIcon;
    private readonly List<ModificationRecord> _records = [];
    private readonly Dictionary<string, string> _textOfId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idOfText = new(StringComparer.CurrentCultureIgnoreCase);
    private bool _matchAll;
    private int _count = -1;
    private ImageSource? _icon;
    private bool _iconRead;
    private RelayCommand? _add;

    public ModificationAvatarRow(string avatar, Func<string, ImageSource?> avatarIcon, Func<ModificationRecord, ImageSource?> modificationIcon)
    {
        Avatar = avatar;
        AvatarName = avatar;
        _avatarIcon = avatarIcon;
        _modificationIcon = modificationIcon;
    }

    /// <summary>アバターの商品ID。</summary>
    public string Avatar { get; }

    public string AvatarName { get; private set; }

    /// <summary>枠の頭の絵（アバター）。見えたときに初めて読む。</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRead)
            {
                _iconRead = true;
                _icon = _avatarIcon(Avatar);
            }

            return _icon;
        }
    }

    /// <summary>絵の無いときの頭文字（候補の欄・改変の一覧と同じ出し方）。</summary>
    public string Initial => Core.Services.AvatarText.InitialOf(AvatarName);

    public event Action? Changed;

    public RelayCommand? RemoveCommand { get; set; }

    public ObservableCollection<ListChip> Chips { get; } = [];

    /// <summary>このアバターの、まだ足していない改変。</summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>改変を選んでいない（このアバターのどの改変でもよい）。枠の下に、そう読めるように一言出す。</summary>
    public bool HasNoModification => Chips.Count == 0;

    /// <summary>改変どうしを全部満たす（AND）か。既定は OR。</summary>
    public bool MatchAll
    {
        get => _matchAll;
        set
        {
            if (SetField(ref _matchAll, value))
            {
                Changed?.Invoke();
            }
        }
    }

    public bool ShowsMatchMode => Chips.Count > 1;

    public string CountText => _count < 0 ? string.Empty : _count.ToString(CultureInfo.InvariantCulture);

    public RelayCommand AddCommand => _add ??= new RelayCommand(parameter => Add(parameter as string));

    /// <summary>2段目の候補の頭の絵（改変の絵）。</summary>
    public Func<string, ImageSource?> IconSelector => text => _idOfText.TryGetValue(text, out var id) && Find(id) is { } record
        ? _modificationIcon(record)
        : null;

    public ModificationCondition Condition => new()
    {
        Avatar = Avatar,
        Modifications = Chips.Select(chip => chip.Key).ToList(),
        MatchAll = _matchAll,
    };

    public string SummaryText
        => Chips.Count == 0
            ? AvatarName
            : $"{AvatarName}（{string.Join("・", Chips.Select(chip => chip.Text))}{(_matchAll && Chips.Count > 1 ? " のすべて" : string.Empty)}）";

    internal bool Has(string id) => Chips.Any(chip => chip.Key == id);

    private ModificationRecord? Find(string id) => _records.FirstOrDefault(record => record.Id == id);

    /// <summary>このアバターの改変の一覧を入れる。一覧から消えた改変のチップは外す。</summary>
    internal void SetSources(string avatarName, IEnumerable<ModificationRecord> records)
    {
        AvatarName = avatarName;
        OnPropertyChanged(nameof(AvatarName));
        OnPropertyChanged(nameof(Initial));
        _records.Clear();
        _records.AddRange(records.OrderBy(record => record.Name, StringComparer.CurrentCulture));
        _textOfId.Clear();
        _idOfText.Clear();
        foreach (var record in _records)
        {
            var text = record.Name;
            for (var index = 2; _idOfText.ContainsKey(text); index++)
            {
                text = $"{record.Name}（{index}）";
            }

            _textOfId[record.Id] = text;
            _idOfText[text] = record.Id;
        }

        foreach (var chip in Chips.ToList())
        {
            if (_textOfId.TryGetValue(chip.Key, out var text))
            {
                chip.Text = text;
            }
            else
            {
                Chips.Remove(chip);
            }
        }

        RefreshSuggestions();
    }

    internal void SetMatchAllQuietly(bool value)
    {
        _matchAll = value;
        OnPropertyChanged(nameof(MatchAll));
    }

    /// <summary>改変を足す（状態を戻すとき・1段目で改変を選んだとき）。一覧に無いIDもそのまま受ける（一覧は後から入る）。通知は出さない。</summary>
    internal void AddModificationQuietly(string id)
    {
        if (Has(id))
        {
            return;
        }

        var chip = new ListChip(id, _textOfId.TryGetValue(id, out var text) ? text : id);
        chip.IconSource = () => Find(id) is { } record ? _modificationIcon(record) : null;
        chip.RemoveCommand = new RelayCommand(() =>
        {
            Chips.Remove(chip);
            RefreshSuggestions();
            Changed?.Invoke();
        });
        Chips.Add(chip);
        RefreshSuggestions();
    }

    private void Add(string? text)
    {
        if (text is not null && _idOfText.TryGetValue(text.Trim(), out var id))
        {
            AddModificationQuietly(id);
            Changed?.Invoke();
        }
    }

    /// <summary>改変を選んでいなければこのアバターの改変のどれか、選んでいれば選んだ改変のどれか（AND ならすべて）に使った商品。</summary>
    internal bool Matches(string itemId, ModificationUsage usage)
        => Chips.Count == 0
            ? usage.Used(Avatar, itemId)
            : _matchAll
                ? Chips.All(chip => usage.InModification(chip.Key, itemId))
                : Chips.Any(chip => usage.InModification(chip.Key, itemId));

    internal void RefreshCounts(IReadOnlyList<ItemRecord> items, ModificationUsage usage)
    {
        _count = items.Count(item => Matches(item.Id, usage));
        OnPropertyChanged(nameof(CountText));
        foreach (var chip in Chips)
        {
            chip.Count = items.Count(item => usage.InModification(chip.Key, item.Id));
        }
    }

    private void RefreshSuggestions()
    {
        Suggestions.Clear();
        foreach (var record in _records.Where(record => !Has(record.Id)))
        {
            Suggestions.Add(_textOfId[record.Id]);
        }

        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(ShowsMatchMode));
        OnPropertyChanged(nameof(HasNoModification));
    }
}
