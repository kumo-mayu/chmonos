using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

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

    /// <summary>照らすときの鍵（条件が畳んだ形を覚える。<see cref="ListModule"/> の matchKey）。</summary>
    internal string? MatchKey { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (SetField(ref _text, value))
            {
                OnPropertyChanged(nameof(ToolTipText));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(Initial));
            }
        }
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

    private bool _isConflict;

    /// <summary>ほかのチップと矛盾していて、必ず0件になる（ユーザータグの AND の中の「小分類なし」）。チップを警告の色にする。</summary>
    public bool IsConflict
    {
        get => _isConflict;
        set
        {
            if (SetField(ref _isConflict, value))
            {
                OnPropertyChanged(nameof(ToolTipText));
            }
        }
    }

    /// <summary>乗せたときの文。矛盾しているときは、0件になることを言う（色だけでは何が起きているか分からない）。</summary>
    public string ToolTipText => _isConflict ? $"{_text}：AND では、ほかの小分類と同時に満たす商品はありません。" : _text;

    /// <summary>
    /// 吹き出しに添える絵（対応アバターのサムネイル。ユーザ判断 2026-10-06・メモ82）。乗せたときに初めて読む——
    /// 吹き出しの中身は開くまで結ばれないので、チップを積んだだけでは絵を読まない。
    /// </summary>
    internal Func<ImageSource?>? IconSource { get; set; }

    private ImageSource? _icon;
    private bool _iconRead;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRead && IconSource is { } source)
            {
                _iconRead = true;
                _icon = source();
            }

            return _icon;
        }
    }

    public bool HasIconSource => IconSource is not null;

    /// <summary>
    /// 札の頭に小さな絵を出すか（改変の札。絵が無ければ頭文字）。対応アバターの札は吹き出しにだけ絵を出す今の形のまま
    /// （ユーザ判断 2026-10-06「アバターのアイコンは良いと思います。このままにしましょう」）。
    /// </summary>
    public bool ShowsIcon { get; internal set; }

    /// <summary>絵の無い札の頭文字（候補の欄・改変の一覧と同じ出し方）。</summary>
    public string Initial => Core.Services.AvatarText.InitialOf(_text);

    /// <summary>長い文の間を省くか（パスの札。<see cref="ListModule.TrimsMiddle"/>）。</summary>
    public bool TrimsMiddle
    {
        get => _trimsMiddle;
        internal set
        {
            _trimsMiddle = value;
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    private bool _trimsMiddle;

    /// <summary>
    /// 札に出す文。パスの札は**字数で**間を「…」にし、頭（ドライブ）と最後のフォルダ名を残す。
    /// 札は中身の幅で決まる置き方なので、幅で省く部品（PathLine）は縮んだまま戻らない（`docs/dev/wpf.md`）。字数で決めて、残りは末尾の「…」に任せる
    /// </summary>
    public string DisplayText => _trimsMiddle ? MiddleTrim(_text, MiddleTrimLength) : _text;

    /// <summary>札の幅（200px）に11ptの全角の字が入る数の目安（26字では最後のフォルダ名がまた末尾で切れた。撮って確かめた）。</summary>
    private const int MiddleTrimLength = 18;

    internal static string MiddleTrim(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        // 最後の区切り（\ か /）の後ろを残す。長すぎれば後ろの方だけ
        var cut = text.TrimEnd('\\', '/').LastIndexOfAny(['\\', '/']);
        var tail = cut >= 0 ? text[(cut + 1)..] : text[^(max / 2)..];
        if (tail.Length > max - 4)
        {
            tail = tail[^(max - 4)..];
        }

        var head = text[..Math.Max(1, max - tail.Length - 1)];
        return $"{head}…{tail}";
    }
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
        Func<ItemRecord, SearchModuleContext, bool>? includeMatches = null,
        Func<string, string>? matchKey = null)
        : base(kind)
    {
        _matchKey = matchKey;
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

    /// <summary>
    /// 2つ以上積んだときだけ AND／OR を押せる（1つなら結果が変わらない）。出すかは <see cref="AllowsAnd"/> だけで決め、
    /// 1つの間も隠さずに薄くする（ユーザ判断 2026-10-06：出たり消えたりすると下の欄が縦に揺れる）。
    /// </summary>
    public bool CanChooseMatchMode => AllowsAnd && Chips.Count > 1;

    /// <summary>AND／OR だけを薄くするか。「指定が無い商品だけ」で外の欄ごと薄いときは重ねて薄くしない。</summary>
    public bool MatchModeDimmed => ShowMatched && !CanChooseMatchMode;

    public string MatchModeTip => CanChooseMatchMode ? MatchModeText.AllHint : MatchModeText.NeedsTwo;

    public string Placeholder { get; }

    /// <summary>
    /// 入力欄の読み上げの名前。2つ目からは番号を入れる（「カテゴリ（2つ目）で絞り込む」・D7）。
    /// 案内の文が条件名で始まらない物（対応アバター・改変など）は、後ろに番号を付ける。
    /// </summary>
    public string InputName => Ordinal <= 1
        ? Placeholder
        : Placeholder.StartsWith(Label, StringComparison.Ordinal)
            ? SpokenLabel + Placeholder[Label.Length..]
            : Placeholder + NameSuffix;

    protected override void OnOrdinalChanged() => OnPropertyChanged(nameof(InputName));

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
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(ShowUnspecified));
        if (field == value)
        {
            NotifyChanged();
        }
    }

    /// <summary>候補の頭に出す絵（対応アバター）。</summary>
    public Func<string, ImageSource?>? IconSelector { get; set; }

    /// <summary>
    /// 候補と札の長い文を、間を「…」にして末尾を残すか（ファイルの場所・Unityプロジェクト。メモ83・メモ84：長いパスが切れて見えなかった）。
    /// パスは末尾の名前がいちばん要るので、ほかの画面の長いパスと同じ省き方にする（`Controls/PathLine`）。省いたら吹き出しで全文
    /// </summary>
    public bool TrimsMiddle { get; init; }

    /// <summary>候補の語から、群と名前以外で当たる語を引く（対応アバター。メモ48・メモ58）。</summary>
    public Func<string, Controls.SuggestInfo?>? InfoSelector { get; set; }

    /// <summary>群の見出し。無ければ見出しは出ない。</summary>
    public IReadOnlyList<string>? GroupHeadings { get; set; }

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
        chip.TrimsMiddle = TrimsMiddle;
        if (IconSelector is { } icon)
        {
            // 候補の頭に出す絵と同じ物を、チップの吹き出しにも添える（対応アバター。メモ82）。候補の文字から引くので、乗せた時点の文字で引く
            chip.IconSource = () => icon(chip.Text);
        }

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

    /// <summary>
    /// 照らすときに <c>matches</c> へ渡す鍵。畳む決まり（<c>matchKey</c>）があれば、チップごとに1回だけ畳んで覚える（案c）。
    /// 鍵はチップを作ったときから変わらないので、覚えた物が古くなることはない。
    /// </summary>
    private string MatchKey(ListChip chip) => chip.MatchKey ??= _matchKey?.Invoke(chip.Key) ?? chip.Key;

    private readonly Func<string, string>? _matchKey;

    private bool MatchesChips(ItemRecord item, SearchModuleContext context)
        => Chips.Count == 0
            || (_matchAll && AllowsAnd
                ? Chips.All(chip => _matches(item, context, MatchKey(chip), _flag))
                : Chips.Any(chip => _matches(item, context, MatchKey(chip), _flag)));

    public override bool SupportsExclude => true;

    protected override string SummaryBody
    {
        get
        {
            if (HasGroups && !_showMatched)
            {
                return "対応の指定が無い商品だけ";
            }

            var values = Chips.Select(chip => chip.Text)
                .Concat(HasInclude && _includeOn ? [_includeLabel!] : Array.Empty<string>());
            return JoinValues(values, _matchAll && AllowsAnd)
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
        OnPropertyChanged(nameof(MatchModeDimmed));
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
                && _matches(item, context, MatchKey(chip), _flag));
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
            MatchAll = AllowsAnd && _matchAll,
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

        // AND を持たない種類（ショップ・カテゴリ・BOOTHタグ）に書かれた AND は読まない（照らすときも見ないが、書き戻すと残り続ける）
        _matchAll = AllowsAnd && state.MatchAll;
        _flag = FlagLabel is null ? _flagDefault : state.Flag;

        // 両方切った状態は作らない（手で書き換えた状態でも、対応している商品を出す側に戻す）
        _showUnspecified = HasGroups && state.ShowUnspecified;
        _showMatched = !HasGroups || state.ShowMatched || !_showUnspecified;
        _includeOn = HasInclude && state.IncludeFavorites;
        OnPropertyChanged(nameof(IncludeOn));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(Flag));
        OnPropertyChanged(nameof(ShowMatched));
        OnPropertyChanged(nameof(MatchModeDimmed));
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
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
        OnPropertyChanged(nameof(HasFlag));
    }
}
