using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 条件「更新通知あり」（ユーザ判断 2026-10-06・更新のモジュールの判断。前の名前は「更新あり」）。
/// 三択に、どの種類の変化を見るかのトグル5つを足した形——**チップではなく編集状況（<see cref="UneditedModule"/>）と同じ形**で並べる。
///
/// 見るのは要確認に**未読**で片付けていない「商品の更新」の知らせだけ（カードの札「更新あり」と同じ数え方。今のまま）。
/// 種類は知らせの差の欄から見分ける（<see cref="BoothChanges.KindsOf"/>）。既定は選んだ種類のどれかの知らせがあれば当たる（OR）。
/// **種類どうしを AND にもできる**（ユーザ判断 2026-10-06「無くす理由もないので AND と OR は使えるようにしておきましょう。未編集と同様の操作感です」）：
/// 編集状況と同じ2つのラジオボタンで、言い方は <see cref="MatchModeText"/>。AND は商品ごとの未読の知らせを合わせて、選んだ種類が全部そろう商品
/// （1通の知らせの中でそろっていなくてよい。カードの札も商品ごとに1つで、使う人が見るのは商品だから）。
/// 「更新通知なしのみ」は「更新通知ありのみ」の反対（同じつなぎ方で当たる商品、以外）。
/// </summary>
public sealed class UpdateNoticeModule : SearchModule
{
    private const string UpdatedKey = "updated";
    private const string OtherKey = "other";
    private const string NeutralKey = "both";

    /// <summary>画面の並び（ユーザ判断の並び）。</summary>
    internal static IReadOnlyList<BoothChangeKind> AllKinds { get; } =
        [BoothChangeKind.Content, BoothChangeKind.Variations, BoothChangeKind.Price, BoothChangeKind.Sale, BoothChangeKind.Page];

    private readonly Func<string, BoothChangeKind> _kindsOf;
    private ChoiceOption _selected;
    private BoothChangeKind _kinds = AllMask;
    private bool _matchAll;

    private static BoothChangeKind AllMask => AllKinds.Aggregate(BoothChangeKind.None, (all, kind) => all | kind);

    /// <param name="kindsOf">商品ID → 未読の更新の知らせが含む種類（無ければ None）。</param>
    public UpdateNoticeModule(Func<string, BoothChangeKind> kindsOf)
        : base(SearchModuleKind.Updated)
    {
        _kindsOf = kindsOf;
        Options =
        [
            new ChoiceOption(UpdatedKey, "更新通知ありのみ"),
            new ChoiceOption(OtherKey, "更新通知なしのみ"),
            new ChoiceOption(NeutralKey, "両方"),
        ];
        _selected = Options[0];
        Kinds = AllKinds.Select(kind => new ChangeKindToggle(this, kind)).ToList();
    }

    public IReadOnlyList<ChoiceOption> Options { get; }

    public ChoiceOption Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetField(ref _selected, value))
            {
                OnPropertyChanged(nameof(CanEditKinds));
                OnPropertyChanged(nameof(MatchModeDimmed));
                NotifyChanged();
            }
        }
    }

    public string SelectedKey => _selected.Key;

    /// <summary>種類のチェックを押せるか。「両方」は何も絞らないので押せず薄くする（編集状況と同じ。値は残す）。</summary>
    public bool CanEditKinds => _selected.Key != NeutralKey;

    public IReadOnlyList<ChangeKindToggle> Kinds { get; }

    /// <summary>入れている種類。</summary>
    public BoothChangeKind SelectedKinds => _kinds;

    protected override bool HasCondition => _selected.Key != NeutralKey;

    public override bool Matches(ItemRecord item, SearchModuleContext context) => MatchesKey(item, _selected.Key);

    private bool MatchesKey(ItemRecord item, string key) => key switch
    {
        UpdatedKey => HasSelected(item),
        OtherKey => !HasSelected(item),
        _ => true,
    };

    private bool HasSelected(ItemRecord item)
        => _matchAll
            ? (_kindsOf(item.Id) & _kinds) == _kinds
            : (_kindsOf(item.Id) & _kinds) != BoothChangeKind.None;

    /// <summary>種類どうしを全部そろえる（AND）か。既定はどれか（OR）。「両方」の間は受けない（編集状況と同じ）。</summary>
    public bool MatchAll
    {
        get => _matchAll;
        set
        {
            if (!CanEditKinds)
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

    /// <summary>つなぎ方のラジオボタンの「どれか」の側（編集状況の <see cref="UneditedModule.MatchAny"/> と同じ受け方）。</summary>
    public bool MatchAny
    {
        get => !_matchAll;
        set
        {
            // 片方を選ぶと、もう片方のラジオボタンが偽を書きに来る。偽は受けない
            if (value)
            {
                MatchAll = false;
            }
        }
    }

    /// <summary>
    /// 種類を2つ以上入れているときだけ押せる（1つなら結果が変わらない）。1つの間も隠さずに薄くする（ユーザ判断 2026-10-06：出たり消えたりすると下の欄が縦に揺れる）。
    /// 「両方」の間も出したまま押せなくする（<see cref="CanEditKinds"/>）。
    /// </summary>
    public bool CanChooseMatchMode => AllKinds.Count(kind => (_kinds & kind) != 0) > 1;

    /// <summary>つなぎ方だけを薄くするか。「両方」で外の欄ごと薄いときは重ねて薄くしない。</summary>
    public bool MatchModeDimmed => CanEditKinds && !CanChooseMatchMode;

    /// <summary>押せないときだけ理由を言う（押せるときは選択肢の文で足りる）。</summary>
    public string? MatchModeTip => CanChooseMatchMode ? null : MatchModeText.NeedsTwoOn("見る種類");

    public string MatchAnyLabel => MatchModeText.Any;

    public string MatchAllLabel => MatchModeText.All;

    protected override string SummaryBody
    {
        get
        {
            var all = _matchAll && CanChooseMatchMode;
            if (_selected.Key == NeutralKey || (_kinds == AllMask && !all))
            {
                return _selected.Label;
            }

            var names = string.Join("・", AllKinds.Where(kind => (_kinds & kind) != 0).Select(ChangeKindToggle.LabelOf));
            return (_selected.Key == UpdatedKey, all) switch
            {
                (true, false) => $"{names}の更新通知あり",
                (true, true) => $"{names}のすべての更新通知あり",
                (false, false) => $"{names}の更新通知なし",
                (false, true) => $"{names}の更新通知がすべてはそろわない",
            };
        }
    }

    public override void Clear()
    {
        _selected = Options.First(option => option.Key == NeutralKey);
        _matchAll = false;
        SetKinds(AllMask);
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(MatchAny));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanEditKinds));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CollapsedSummary));
    }

    public override void RefreshCounts(IReadOnlyList<ItemRecord> items, SearchModuleContext context)
    {
        foreach (var option in Options)
        {
            option.Count = items.Count(item => MatchesKey(item, option.Key));
        }

        // 種類の横の件数は、他の条件のもとでその種類の未読の知らせがある商品の数
        foreach (var toggle in Kinds)
        {
            toggle.Count = items.Count(item => (_kindsOf(item.Id) & toggle.Kind) != 0);
        }
    }

    protected override SearchModuleState Write(SearchModuleState state)
        => state with
        {
            Choice = _selected.Key,
            Fields = AllKinds.Where(kind => (_kinds & kind) != 0).Select(KeyOf).ToList(),
            MatchAll = _matchAll,
        };

    protected override void Read(SearchModuleState state)
    {
        _selected = Options.FirstOrDefault(option => option.Key == state.Choice) ?? Options[0];
        _matchAll = state.MatchAll;
        OnPropertyChanged(nameof(MatchAll));
        OnPropertyChanged(nameof(MatchAny));

        // 知らない名前は飛ばし、残らなければ全部（編集状況の項目と同じ読み方）
        var kinds = state.Fields
            .Select(name => AllKinds.FirstOrDefault(kind => KeyOf(kind) == name))
            .Aggregate(BoothChangeKind.None, (all, kind) => all | kind);
        SetKinds(kinds == BoothChangeKind.None ? AllMask : kinds);
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanEditKinds));
        OnPropertyChanged(nameof(MatchModeDimmed));
    }

    /// <summary>状態に書く名前。</summary>
    internal static string KeyOf(BoothChangeKind kind) => kind switch
    {
        BoothChangeKind.Content => "content",
        BoothChangeKind.Variations => "variations",
        BoothChangeKind.Price => "price",
        BoothChangeKind.Sale => "sale",
        _ => "page",
    };

    /// <summary>種類のトグルを入れ・切る。**最後の1つは外させない**（編集状況の項目と同じ）。</summary>
    internal void Toggle(BoothChangeKind kind, bool on)
    {
        var has = (_kinds & kind) != 0;
        var next = on ? _kinds | kind : _kinds & ~kind;
        if (on == has || next == BoothChangeKind.None || !CanEditKinds)
        {
            Kinds.First(toggle => toggle.Kind == kind).RaiseIsOn();
            return;
        }

        SetKinds(next);
        NotifyChanged();
    }

    private void SetKinds(BoothChangeKind kinds)
    {
        _kinds = kinds;
        foreach (var toggle in Kinds)
        {
            toggle.RaiseIsOn();
        }

        OnPropertyChanged(nameof(SelectedKinds));
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
    }

}

/// <summary>「更新通知あり」の種類1つのトグル。</summary>
public sealed class ChangeKindToggle : ViewModelBase
{
    private readonly UpdateNoticeModule _owner;
    private int _count = -1;

    public ChangeKindToggle(UpdateNoticeModule owner, BoothChangeKind kind)
    {
        _owner = owner;
        Kind = kind;
    }

    public BoothChangeKind Kind { get; }

    public string Label => LabelOf(Kind);

    /// <summary>名前で分からない2つだけ、何が変わった物かを吹き出しで言う。</summary>
    public string? Hint => Kind switch
    {
        BoothChangeKind.Content => "説明文の更新履歴が変わった商品です。",
        BoothChangeKind.Page => "商品名・画像・説明文が変わった商品です。更新履歴は除きます。",
        _ => null,
    };

    public static string LabelOf(BoothChangeKind kind) => kind switch
    {
        BoothChangeKind.Content => "中身の更新",
        BoothChangeKind.Variations => "バリエーション",
        BoothChangeKind.Price => "価格",
        BoothChangeKind.Sale => "販売の状態",
        _ => "ページ内容の変更",
    };

    public bool IsOn
    {
        get => (_owner.SelectedKinds & Kind) != 0;
        set => _owner.Toggle(Kind, value);
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

    /// <summary>UI Automation の ID（更新通知ありは1つまでの条件なので、番号は付かない）。</summary>
    public string AutomationId => $"SearchModule.Updated.Kind.{UpdateNoticeModule.KeyOf(Kind)}";

    internal void RaiseIsOn() => OnPropertyChanged(nameof(IsOn));
}
