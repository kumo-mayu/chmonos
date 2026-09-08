using System.Collections.ObjectModel;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// appTagでの絞り込み1つ分。トップを選ぶと候補に入り、サブを選ぶとその中で更に絞る。
///
/// 別々のトップを複数選んだ場合はORで扱う（「衣装かギミック」）。
/// 同じトップの中でサブを選んだ場合は、そのトップかつそのサブのいずれか、になる。
/// ANDで積むと選ぶほど0件に近づき、探す道具として使えなくなるため。
/// </summary>
public sealed class AppTagFilter : ViewModelBase
{
    private bool _isSelected;

    public required string Name { get; init; }

    public ObservableCollection<AppTagSubFilter> Subs { get; } = [];

    /// <summary>絞り込み条件が変わったことを検索側へ伝える。</summary>
    public event Action? Changed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetField(ref _isSelected, value))
            {
                return;
            }

            if (!value)
            {
                foreach (var sub in Subs)
                {
                    sub.SetSilently(false);
                }

                OnPropertyChanged(nameof(HasSelectedSubs));
            }

            Changed?.Invoke();
        }
    }

    public bool HasSubs => Subs.Count > 0;

    public bool HasSelectedSubs => Subs.Any(sub => sub.IsSelected);

    public IEnumerable<string> SelectedSubs => Subs.Where(sub => sub.IsSelected).Select(sub => sub.Name);

    public void Attach()
    {
        foreach (var sub in Subs)
        {
            sub.Changed += () =>
            {
                OnPropertyChanged(nameof(HasSelectedSubs));
                Changed?.Invoke();
            };
        }
    }

    public void Reset()
    {
        foreach (var sub in Subs)
        {
            sub.SetSilently(false);
        }

        SetField(ref _isSelected, false, nameof(IsSelected));
        OnPropertyChanged(nameof(HasSelectedSubs));
    }

    /// <summary>このitemが条件に合うか。</summary>
    public bool Matches(ItemRecord item)
    {
        var assignment = item.Local.AppTags.FirstOrDefault(entry =>
            string.Equals(entry.Top, Name, StringComparison.CurrentCultureIgnoreCase));

        if (assignment is null)
        {
            return false;
        }

        var selectedSubs = SelectedSubs.ToList();
        return selectedSubs.Count == 0
            || assignment.Subs.Any(sub => selectedSubs.Contains(sub, StringComparer.CurrentCultureIgnoreCase));
    }
}

public sealed class AppTagSubFilter : ViewModelBase
{
    private bool _isSelected;

    public required string Name { get; init; }

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

    /// <summary>通知だけ出して、絞り込みの再計算は呼ばない（親がまとめて出す）。</summary>
    public void SetSilently(bool value)
    {
        if (_isSelected != value)
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }
}

/// <summary>
/// 属性での絞り込み1つ分。上下限のレンジで指定する。
///
/// 軸を条件に加えること自体が「この属性で選ぶ」という意思表示なので、
/// 未評価のitemは最初から外す。0-100のままでも通さない。
/// 未評価は「値が小さい」ではなく「値が無い」ため、レンジのどこにも当てはまらない。
/// （以前は軸が常時並んでいたので0-100を「指定なし」と読む必要があったが、
/// 使う軸だけを選んで積む形にしたことでその含みは不要になった。）
/// </summary>
public sealed class AttributeFilter : ViewModelBase
{
    private int _min;
    private int _max = 100;

    public required string Name { get; init; }

    public event Action? Changed;

    /// <summary>この軸を条件から外す。</summary>
    public RelayCommand? RemoveCommand { get; set; }

    public int Min
    {
        get => _min;
        set
        {
            if (SetField(ref _min, Math.Clamp(value, 0, _max)))
            {
                OnPropertyChanged(nameof(RangeText));
                Changed?.Invoke();
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
                Changed?.Invoke();
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
}
