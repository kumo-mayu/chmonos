using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 属性での絞り込み1つ分。上下限のレンジで指定する（検索の「属性」の条件の1行）。
///
/// 属性を条件に加えること自体が「この属性で選ぶ」という意思表示なので、
/// 未評価の商品は最初から外す。0-100のままでも通さない。
/// 未評価は「値が小さい」ではなく「値が無い」ため、レンジのどこにも当てはまらない。
/// </summary>
public sealed class AttributeFilter : ViewModelBase
{
    private int _min;
    private int _max = 100;

    /// <summary>ドラッグ中に毎回絞り直さない（値の表示はすぐ・<see cref="Debounced"/>）。</summary>
    private readonly Debounced _changedSoon;

    public AttributeFilter() => _changedSoon = new Debounced(TimeSpan.FromMilliseconds(150), () => Changed?.Invoke());

    public required string Name { get; init; }

    public event Action? Changed;

    /// <summary>この属性を条件から外す。</summary>
    public RelayCommand? RemoveCommand { get; set; }

    public int Min
    {
        get => _min;
        set
        {
            if (SetField(ref _min, Math.Clamp(value, 0, _max)))
            {
                OnPropertyChanged(nameof(RangeText));
                OnPropertyChanged(nameof(LowPosition));
                _changedSoon.Request();
            }
        }
    }

    public int Max
    {
        get => _max;
        set
        {
            if (SetField(ref _max, Math.Clamp(value, _min, 100)))
            {
                OnPropertyChanged(nameof(RangeText));
                OnPropertyChanged(nameof(HighPosition));
                _changedSoon.Request();
            }
        }
    }

    public string RangeText => $"{Min}〜{Max}%";

    /// <summary>
    /// スライダの位置（0〜100）。**1%刻み**（ユーザ判断 2026-10-06・メモ82・判断9。前は目盛の10%に吸い付けていた）。
    /// スライダはドラッグ中に端数を返すので、ここで整数に丸めて受ける（数に結ぶと、端数の変換に任せることになる）。
    /// </summary>
    // 上下は越えられない（価格と同じ）。越えようとして値が変わらなかったときも位置を知らせ、つまみを止まった値へ戻す
    public double LowPosition
    {
        get => _min;
        set
        {
            Min = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            OnPropertyChanged(nameof(LowPosition));
        }
    }

    public double HighPosition
    {
        get => _max;
        set
        {
            Max = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            OnPropertyChanged(nameof(HighPosition));
        }
    }

    /// <summary>棒の数。5%ずつ（0〜4%・5〜9%…95〜100%）。編集画面が5%刻みだった頃の値が棒の間で割れず、目盛（10%）の2本ぶんに当たる。</summary>
    public const int HistogramBuckets = 20;

    /// <summary>
    /// 分布の帯（ユーザ判断 2026-10-06・メモ82：価格と同じくヒストグラムを出す）。手元でこの属性を評価した商品の値がどこに集まっているか。
    /// 目盛は直線（0〜100%）なので、帯も等しい幅で数える。
    /// </summary>
    public IReadOnlyList<HistogramBar> Histogram { get; private set; } = [];

    public bool HasHistogram => Histogram.Count > 0;

    /// <summary>手元の値の全部から帯を描き直す。評価した商品が無ければ帯を出さない。</summary>
    public void SetValues(IEnumerable<int> values)
    {
        var counts = new int[HistogramBuckets];
        foreach (var value in values)
        {
            counts[Math.Clamp(value * HistogramBuckets / 100, 0, HistogramBuckets - 1)]++;
        }

        Histogram = RangeModule.Bars(counts);
        OnPropertyChanged(nameof(Histogram));
        OnPropertyChanged(nameof(HasHistogram));
    }

    /// <summary>評価が入っていて、かつレンジに収まるものだけを通す。</summary>
    public bool Matches(ItemRecord item)
        => item.Local.Attributes.TryGetValue(Name, out var value) && value >= Min && value <= Max;
}

public enum SortKind
{
    AcquiredAt,
    Name,
    Size,
    WishList,
    Attribute,

    /// <summary>取り込んだ順。足跡が無い商品は後ろに置く</summary>
    RecentlyAdded,

    /// <summary>使った順。いまはUnityへ送った記録だけ</summary>
    RecentlyUsed,

    /// <summary>商品ページを開いた順</summary>
    RecentlyViewed,

    /// <summary>自分用に払った額の合計（贈った・貰ったは含めない）</summary>
    SelfPaid,

    /// <summary>BOOTH の公開日</summary>
    PublishedAt,

    /// <summary>BOOTH の今の価格（いちばん安いバリエーション）</summary>
    BoothPrice,

    /// <summary>ショップ名の読みの順。同じショップの中は入手日の新しい順</summary>
    Shop,

    /// <summary>BOOTH のカテゴリの表の順。同じカテゴリの中は入手日の新しい順</summary>
    Category,
}

/// <summary>
/// 表示順の指定。絞り込みとは別のUIに置く（同じ場所で指定すると、
/// 「絞ったのか並べ替えただけなのか」が読み取れなくなるため）。
/// </summary>
public sealed class SortOption
{
    public required string Label { get; init; }

    public required SortKind Kind { get; init; }

    public bool Descending { get; init; }

    /// <summary><see cref="SortKind.Attribute"/> のときの属性名。</summary>
    public string? AttributeName { get; init; }

    /// <summary>読み上げと自動操作から見える名前。既定だと型名になる</summary>
    public override string ToString() => Label;
}

/// <summary>
/// 並べ替えに使う**項目**（ユーザ指示 2026-09-20・M5：使う項目と昇順・降順を分ける）。
///
/// 前は「入手日が新しい順」「入手日が古い順」のように、項目と向きを掛け合わせた選択肢を1つの
/// プルダウンに並べていた。属性が増えると選択肢も増え、同じ項目の逆向きを探すのに一覧を読み直すことになる。
///
/// **向きの言い方は項目ごとに変える**——日付は「新しい／古い」、数は「多い／少ない」、
/// 名前とショップは「昇順／降順」（2026-10-01 に「あ→わ」から変えた。名前には英字・数字・記号も混ざり、「あ→わ」ではそれらがどこに来るかが分からない）。
/// 日付と数に「昇順・降順」を使わないのは、どちらが新しいのか・多いのかが読み取れないため。
/// </summary>
public sealed class SortField
{
    public required string Label { get; init; }

    /// <summary>
    /// メニューの中のまとまり（間に区切り線を引く。ユーザ判断 2026-10-06）。属性は「属性 ▸」の子に入る
    /// （属性の数だけ一番上の並びが伸びて、ほかの項目が埋もれないように）
    /// </summary>
    public string Group => Kind switch
    {
        SortKind.Name or SortKind.Shop or SortKind.Category or SortKind.WishList => "1",
        SortKind.BoothPrice or SortKind.SelfPaid => "2",
        SortKind.PublishedAt or SortKind.AcquiredAt or SortKind.RecentlyViewed or SortKind.RecentlyUsed or SortKind.RecentlyAdded => "3",
        SortKind.Size => "4",
        _ => "属性",
    };

    /// <summary>
    /// メニューでの並び（ユーザ判断 2026-10-06）：名前・ショップ・カテゴリ・スキ数／BOOTH価格・払った額／
    /// 公開日・入手日・商品閲覧日・Unity送信日・取り込み日／容量／属性。属性は属性の管理の並びのまま後ろに続ける
    /// </summary>
    internal static int OrderOf(SortKind kind) => kind switch
    {
        SortKind.Name => 0, SortKind.Shop => 1, SortKind.Category => 2, SortKind.WishList => 3,
        SortKind.BoothPrice => 4, SortKind.SelfPaid => 5,
        SortKind.PublishedAt => 6, SortKind.AcquiredAt => 7, SortKind.RecentlyViewed => 8, SortKind.RecentlyUsed => 9, SortKind.RecentlyAdded => 10,
        SortKind.Size => 11,
        _ => 12,
    };

    public required SortKind Kind { get; init; }

    /// <summary><see cref="SortKind.Attribute"/> のときの属性名。</summary>
    public string? AttributeName { get; init; }

    /// <summary>大きい方から並べるときの言い方（「新しい順」「多い順」）。</summary>
    public required string DescendingLabel { get; init; }

    /// <summary>小さい方から並べるときの言い方（「古い順」「少ない順」）。</summary>
    public required string AscendingLabel { get; init; }

    /// <summary>この項目を選んだときの既定の向き。日付や数は大きい方から見たいことが多い。</summary>
    public bool DefaultDescending { get; init; } = true;

    /// <summary>
    /// 選んだ項目と向きを合わせた言い方（「入手日が新しい順」）。
    /// **検索の履歴はこの言い方で残る**ので、前からある言い方を変えない
    /// （変えると、前に残した履歴から並び順を戻せなくなる）。
    /// </summary>
    public required Func<bool, string> FullLabel { get; init; }

    public SortOption ToOption(bool descending) => new()
    {
        Label = FullLabel(descending),
        Kind = Kind,
        AttributeName = AttributeName,
        Descending = descending,
    };

    public override string ToString() => Label;
}

/// <summary>
/// 表示順のメニューの1行（ユーザ判断 2026-10-06：「属性」の中に大量の属性が入っている形）。
/// 項目・区切り線・「属性 ▸」（子に属性）のどれか。開くたびに今の選びから作り直すので、値は作ったときのまま変えない。
/// **区切りごとに別の物を作る**——同じ物を1つの一覧に何度も入れると、WPF の一覧は項目と部品の対応を取り違える
/// </summary>
public sealed class SortMenuEntry
{
    public string Label { get; init; } = "";

    /// <summary>メニューの項目の見た目を線に差し替える印。</summary>
    public bool IsSeparator { get; init; }

    /// <summary>子を持つ「属性 ▸」。属性が0個でも子を開く見た目（▸）で出し、押せなくする。</summary>
    public bool IsParent { get; init; }

    public IReadOnlyList<SortMenuEntry> Children { get; init; } = [];

    /// <summary>今この項目で並べているか。「属性 ▸」は子のどれかで並べているとき。</summary>
    public bool IsChecked { get; init; }

    public bool IsEnabled { get; init; } = true;

    /// <summary>押せないときの理由（吹き出し）。</summary>
    public string? DisabledHint { get; init; }

    public string AutomationId { get; init; } = "";

    /// <summary>この項目で並べる。区切りと「属性 ▸」は持たない。</summary>
    public System.Windows.Input.ICommand? ChooseCommand { get; init; }

    public static SortMenuEntry Separator() => new() { IsSeparator = true, IsEnabled = false, Label = "区切り" };

    public override string ToString() => Label;
}

/// <summary>
/// 検索の履歴1件ぶんのスロット。
///
/// 検索欄の上に横に並ぶ。押すとその条件に戻る。
/// **条件を思い出せる形で出す**（<see cref="SearchHistoryEntry.Summary"/>）——
/// 「衣装」だけでは、そのとき何で絞っていたか分からない。
/// </summary>
public sealed class SearchHistorySlot
{
    public SearchHistorySlot(
        SearchHistoryEntry entry,
        Action<SearchHistoryEntry> apply,
        Func<SearchHistoryEntry, Task> remove)
    {
        Entry = entry;
        ApplyCommand = new RelayCommand(() => apply(entry));
        RemoveCommand = new RelayCommand(() => _ = remove(entry));
    }

    public SearchHistoryEntry Entry { get; }

    public string Summary => Entry.Summary;

    /// <summary>名前を付けたものは落とさないので、印を出して区別する。</summary>
    public bool IsNamed => Entry.IsNamed;

    /// <summary>いつ使ったか。同じ見え方の条件が並んだときの手掛かり。</summary>
    public string UsedText => Entry.UsedAt == default ? string.Empty : Entry.UsedAt.ToString("MM-dd HH:mm");

    public RelayCommand ApplyCommand { get; }

    public RelayCommand RemoveCommand { get; }
}
