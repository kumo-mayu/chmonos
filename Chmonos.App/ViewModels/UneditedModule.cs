using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 編集状況（ユーザ判断 2026-10-01。前の名前は「未編集」で、ユーザータグが0件かだけを見ていた）。
/// 三項「未入力のみ／入力済みのみ／両方」に、どの項目を見るか（<see cref="EditField"/>）のトグルと、2つ以上のときの「すべて」を足した形。
/// 対応アバターの確認も項目の1つ（ユーザ判断 2026-10-06。前は別の条件）：未入力のみ＝確認待ちあり、入力済みのみ＝確認待ちなし。
///
/// 既定は「ユーザータグのどれかが未入力」＝前の「未編集」と同じ意味（札・ナビの「未編集」もユーザータグが0件のまま）。
/// 取り込みの③（対応アバターの検出）を待っている商品は「未入力のみ」から外す（カードに「取り込み中」と出る商品で、編集画面の順番にも出ない・D22）。
/// 三項なので「除く」は持たない（「入力済みのみ」が反対を選ぶ）。
/// </summary>
public sealed class UneditedModule : SearchModule
{
    private const string MissingKey = "unedited";
    private const string FilledKey = "edited";
    private const string NeutralKey = "both";

    private readonly Func<ItemRecord, bool> _isAwaiting;
    private ChoiceOption _selected;
    private bool _matchAll;
    private List<EditField> _fields = [.. EditFieldsMissing.Default];

    /// <summary>番号が替わったら、項目のトグルの ID も振り直す。</summary>
    protected override void OnOrdinalChanged()
    {
        foreach (var toggle in Fields)
        {
            toggle.RaiseAutomationId();
        }
    }

    /// <param name="isAwaiting">取り込みの③を待っているか。</param>
    public UneditedModule(Func<ItemRecord, bool> isAwaiting)
        : base(SearchModuleKind.Unedited)
    {
        _isAwaiting = isAwaiting;
        Options =
        [
            new ChoiceOption(MissingKey, "未入力のみ"),
            new ChoiceOption(FilledKey, "入力済みのみ"),
            new ChoiceOption(NeutralKey, "両方"),
        ];
        _selected = Options[0];
        Fields = EditFieldsMissing.All.Select(field => new EditFieldToggle(this, field)).ToList();
    }

    public IReadOnlyList<ChoiceOption> Options { get; }

    public ChoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(MatchAllLabel));
                OnPropertyChanged(nameof(MatchAnyLabel));
                OnPropertyChanged(nameof(CanChooseMatchMode));
                OnPropertyChanged(nameof(MatchModeDimmed));
                OnPropertyChanged(nameof(MatchModeTip));
                OnPropertyChanged(nameof(CanEditFields));
                OnPropertyChanged(nameof(MatchModeDimmed));
                NotifyChanged();
            }
        }
    }

    public string SelectedKey => _selected.Key;

    /// <summary>
    /// 項目のチェックとつなぎ方を押せるか。「両方」は何も絞らないので、項目を変えても結果が変わらない（メモ2-④ 2026-10-02）。
    /// 押せなくするだけで値は残す（未入力のみ・入力済みのみに戻すと、そのまま効く）。
    /// </summary>
    public bool CanEditFields => _selected.Key != NeutralKey;

    /// <summary>見る項目のトグル（画面の並び）。</summary>
    public IReadOnlyList<EditFieldToggle> Fields { get; }

    /// <summary>入れている項目（画面の並び）。</summary>
    public IReadOnlyList<EditField> SelectedFields => _fields;

    /// <summary>
    /// 「すべて」で結ぶか。既定はどれか（片付けの一覧として、まだ埋めていない所がある商品を出す・D20）。
    /// 「入力済みのみ」は「未入力のみ」の反対なので、同じ値が「どれかが入力済み」を表す。
    /// </summary>
    public bool MatchAll
    {
        get => _matchAll;
        set
        {
            if (!CanEditFields)
            {
                // 押せない間に来た値は受けず、画面の印を今の値に戻す
                OnPropertyChanged(nameof(MatchAll));
                OnPropertyChanged(nameof(MatchAny));
                return;
            }

            if (SetField(ref _matchAll, value))
            {
                OnPropertyChanged(nameof(MatchAny));
                NotifyChanged();
            }
        }
    }

    /// <summary>
    /// つなぎ方の2つの選択肢のうち「すべて」で結ばない方（<see cref="MatchAll"/> が偽）。ラジオボタンの片方が結ぶ。
    /// 前は1つのチェックで、未入力のみでは絞る向き・入力済みのみでは広げる向きと、同じ印の意味が入れ替わって分かりにくかった
    /// （ユーザ判断 2026-10-01：案A）。今は両方の選択肢を、選んだときに何が出るかの文で並べる
    /// </summary>
    public bool MatchAny
    {
        get => !_matchAll;
        set
        {
            // 片方を選ぶと、もう片方のラジオボタンが偽を書きに来る。偽は「もう片方が選ばれた」だけなので受けない
            if (value)
            {
                MatchAll = false;
            }
        }
    }

    /// <summary>
    /// 2つ以上入れているときだけ押せる（1つなら結果が変わらない）。1つの間も隠さずに薄くする（ユーザ判断 2026-10-06：出たり消えたりすると下の欄が縦に揺れる）。
    /// 「両方」の間も出したまま押せなくする（<see cref="CanEditFields"/>）。
    /// </summary>
    public bool CanChooseMatchMode => _fields.Count > 1;

    /// <summary>つなぎ方だけを薄くするか。「両方」で外の欄ごと薄いときは重ねて薄くしない。</summary>
    public bool MatchModeDimmed => CanEditFields && !CanChooseMatchMode;

    /// <summary>押せないときだけ理由を言う（押せるときは選択肢の文で足りる）。</summary>
    public string? MatchModeTip => CanChooseMatchMode ? null : MatchModeText.NeedsTwoOn("見る項目");

    /// <summary>「すべて」で結ばない方の文。未入力のみ＝どれかが未入力、入力済みのみ＝その反対なので、すべて入力済み。</summary>
    public string MatchAnyLabel => _selected.Key == FilledKey ? "すべて入力済み" : "どれかが未入力";

    /// <summary>「すべて」で結ぶ方の文。未入力のみ＝すべてが未入力、入力済みのみ＝その反対なので、どれかが入力済み。</summary>
    public string MatchAllLabel => _selected.Key == FilledKey ? "どれかが入力済み" : "すべてが未入力";

    protected override bool HasCondition => _selected.Key != NeutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => MatchesKey(item, _selected.Key);

    private bool MatchesKey(ItemRecord item, string key) => key switch
    {
        MissingKey => !_isAwaiting(item) && Missing(item),
        FilledKey => !Missing(item),
        _ => true,
    };

    private bool Missing(ItemRecord item)
        => _matchAll
            ? _fields.All(field => EditFieldsMissing.IsMissing(item, field))
            : _fields.Any(field => EditFieldsMissing.IsMissing(item, field));

    protected override string SummaryBody
    {
        get
        {
            var names = string.Join("・", _fields.Select(EditFieldToggle.LabelOf));
            var many = _fields.Count > 1;
            return _selected.Key switch
            {
                MissingKey when !many => $"{names}が未入力",
                MissingKey => _matchAll ? $"{names}がすべて未入力" : $"{names}のどれかが未入力",
                FilledKey when !many => $"{names}が入力済み",
                FilledKey => _matchAll ? $"{names}のどれかが入力済み" : $"{names}がすべて入力済み",
                _ => _selected.Label,
            };
        }
    }

    /// <summary>足したときの姿に戻す（両方・ユーザータグだけ・どれか）。</summary>
    public override void Clear()
    {
        _selected = Options.First(option => option.Key == NeutralKey);
        _matchAll = false;
        SetFields(EditFieldsMissing.Default);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(MatchAllLabel));
        OnPropertyChanged(nameof(MatchAnyLabel));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    /// <summary>選ぶ（他の画面から条件を渡すとき）。通知だけ出し、絞り直しは呼ぶ側。</summary>
    public void Select(string key)
    {
        _selected = Options.FirstOrDefault(option => option.Key == key) ?? _selected;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(MatchAllLabel));
        OnPropertyChanged(nameof(MatchAnyLabel));
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    /// <summary>
    /// 選択肢と項目の横の件数。項目の件数は、他の条件のもとでその項目が未入力の商品の数（取り込みの③待ちは数えない。「未入力のみ」と揃える）。
    /// </summary>
    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var option in Options)
        {
            option.Count = items.Count(item => MatchesKey(item, option.Key));
        }

        foreach (var toggle in Fields)
        {
            toggle.Count = items.Count(item => !_isAwaiting(item) && EditFieldsMissing.IsMissing(item, toggle.Field));
        }
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with { Choice = _selected.Key, Fields = _fields.Select(EditFieldsMissing.KeyOf).ToList(), MatchAll = _matchAll };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        _matchAll = state.MatchAll;
        SetFields(EditFieldsMissing.Parse(state.Fields));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(MatchAllLabel));
        OnPropertyChanged(nameof(MatchAnyLabel));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(MatchModeDimmed));
    }

    /// <summary>
    /// 項目のトグルを入れ・切る。**最後の1つは外させない**（全部切ると何も選ばない条件になる。対応アバターの2つのチェックと同じ・D21）。
    /// 外せなかった印は、画面に戻すために通知だけ出す。
    /// </summary>
    internal void Toggle(EditField field, bool on)
    {
        // 「両方」の間は押せない（CanEditFields）。画面は押せなくしているので、ここへ来るのは読み上げ・自動操作から
        if (on == _fields.Contains(field) || (!on && _fields.Count == 1) || !CanEditFields)
        {
            Fields.First(toggle => toggle.Field == field).RaiseIsOn();
            return;
        }

        SetFields(on ? [.. _fields, field] : _fields.Where(entry => entry != field).ToList());
        NotifyChanged();
    }

    private void SetFields(IEnumerable<EditField> fields)
    {
        var wanted = fields.ToHashSet();
        _fields = EditFieldsMissing.All.Where(wanted.Contains).ToList();
        foreach (var toggle in Fields)
        {
            toggle.RaiseIsOn();
        }

        OnPropertyChanged(nameof(SelectedFields));
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
    }
}

/// <summary>編集状況の条件の、項目1つのトグル。件数は他の条件のもとでその項目が未入力の商品の数。</summary>
public sealed class EditFieldToggle : ViewModelBase
{
    private readonly UneditedModule _owner;
    private int _count = -1;

    public EditFieldToggle(UneditedModule owner, EditField field)
    {
        _owner = owner;
        Field = field;
    }

    public EditField Field { get; }

    public string Label => LabelOf(Field);

    /// <summary>編集画面の欄の名前に揃える。</summary>
    public static string LabelOf(EditField field) => field switch
    {
        EditField.UserTags => "ユーザータグ",
        EditField.Attributes => "属性",
        EditField.Purchases => "購入したバリエーション",
        EditField.AcquiredAt => "入手日",

        // 前の条件の名前（「対応アバターの確認」）と、商品ページの「確認待ち」の作業の名前に揃える
        EditField.AvatarConfirmation => "対応アバターの確認",
        _ => "メモ",
    };

    public bool IsOn
    {
        get => _owner.SelectedFields.Contains(Field);
        set => _owner.Toggle(Field, value);
    }

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

    /// <summary>UI Automation の ID。編集状況は複数置けるので、条件の番号を入れる（2つ目から `Unedited-2`。メモ83）。</summary>
    public string AutomationId => $"SearchModule.{_owner.IdKey}.Field.{Field}";

    internal void RaiseIsOn() => OnPropertyChanged(nameof(IsOn));

    internal void RaiseAutomationId() => OnPropertyChanged(nameof(AutomationId));
}
