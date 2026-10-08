using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

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

    /// <summary>属性の名前 → 手元でその属性を評価した商品の値の全部。行ごとの分布の帯をここから出す。検索側が入れる。</summary>
    public Func<string, IEnumerable<int>>? AllValuesOf { get; set; }

    /// <summary>分布の帯を手元の商品から描き直す（読み込み・マスタの変更のあと）。</summary>
    public void RefreshHistograms()
    {
        foreach (var row in Rows)
        {
            row.SetValues(AllValuesOf?.Invoke(row.Name) ?? []);
        }
    }

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

    /// <summary>属性が2つ以上のときだけ押せる。1つの間も隠さずに薄くする（ユーザ判断 2026-10-06：出たり消えたりすると下の欄が縦に揺れる）。</summary>
    public bool CanChooseMatchMode => Rows.Count > 1;

    public bool MatchModeDimmed => !CanChooseMatchMode;

    public string MatchModeTip => CanChooseMatchMode ? MatchModeText.AllHint : MatchModeText.NeedsTwoOf("属性");

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
        row.SetValues(AllValuesOf?.Invoke(known) ?? []);
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

    public override bool SupportsExclude => true;

    public override string ExcludedHint => "当てはまる商品と、評価していない商品を除いています。押すと除くのをやめます。";

    /// <summary>
    /// 選んだ属性が**全部**評価済みで、当てはまらない商品（D2）。1行のときの「値が分かっていて範囲の外」を、そのまま全部の行に広げた形。
    /// 評価していない属性が1つでもあれば、除くときも外す。
    /// </summary>
    protected override bool MatchesExcluded(ItemRecord item, SearchModuleContext context)
        => Rows.Count == 0 || (Rows.All(row => item.Local.Attributes.ContainsKey(row.Name)) && !Matches(item, context));

    protected override string SummaryBody => JoinValues(Rows.Select(row => $"{row.Name} {row.Min}〜{row.Max}"), _matchAll);

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
        OnPropertyChanged(nameof(CanChooseMatchMode));
        OnPropertyChanged(nameof(MatchModeDimmed));
        OnPropertyChanged(nameof(MatchModeTip));
    }
}
