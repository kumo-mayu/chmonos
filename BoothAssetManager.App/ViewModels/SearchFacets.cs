using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

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
                _changedSoon.Request();
            }
        }
    }

    public string RangeText => $"{Min}〜{Max}%";

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

    /// <summary>手元に入った順。足跡が無い商品は後ろに置く</summary>
    RecentlyAdded,

    /// <summary>使った順。いまはUnityへ送った記録だけ</summary>
    RecentlyUsed,

    /// <summary>商品ページを開いた順</summary>
    RecentlyViewed,
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
