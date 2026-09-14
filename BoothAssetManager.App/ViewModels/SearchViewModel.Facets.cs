using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：分類・タグ・属性の絞り込みの選択肢（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 結果を行単位で持つ。行を仮想化の単位にすることで、画面に出ている行のカードだけが実体化する。
    /// WPFには仮想化する WrapPanel が無いので、列数をこちら側で決めて行に切っている。
    /// </summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>カテゴリの選択肢。件数を出すために文字列ではなく型で持つ。</summary>
    public ObservableCollection<CategoryOption> Categories { get; } = [];

    /// <summary>userTagでの絞り込み。マスタのトップをそのまま並べる。</summary>
    public ObservableCollection<UserTagFilter> TagFilters { get; } = [];

    /// <summary>
    /// 属性でのレンジ絞り込み。使う軸だけを候補から選んで積む。
    /// マスタ全部を常に並べると、評価していない属性の欄まで居座って画面が伸びる。
    /// </summary>
    /// <summary>積んだBOOTHタグ。軸ごとにANDで積む（属性と同じ扱い）。</summary>
    public ObservableCollection<BoothTagFilter> BoothTagFilters { get; } = [];

    /// <summary>まだ積んでいないBOOTHタグ。候補として出す。</summary>
    public ObservableCollection<string> BoothTagSuggestions { get; } = [];

    public RelayCommand AddBoothTagFilterCommand { get; }

    public bool HasBoothTagFilters => BoothTagFilters.Count > 0;

    public bool HasBoothTagSuggestions => BoothTagSuggestions.Count > 0;

    private void AddBoothTagFilter(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || BoothTagFilters.Any(filter => string.Equals(filter.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        var filter = new BoothTagFilter { Name = trimmed };
        filter.RemoveCommand = new RelayCommand(() =>
        {
            BoothTagFilters.Remove(filter);
            RefreshBoothTagSuggestions();
            ApplyFilters();
        });

        BoothTagFilters.Add(filter);
        RefreshBoothTagSuggestions();
        ApplyFilters();
    }

    /// <summary>候補から、既に積んだものを除く。</summary>
    private void RefreshBoothTagSuggestions()
    {
        BoothTagSuggestions.Clear();

        foreach (var name in _boothTagNames.Where(name =>
            !BoothTagFilters.Any(filter => string.Equals(filter.Name, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            BoothTagSuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasBoothTagFilters));
        OnPropertyChanged(nameof(HasBoothTagSuggestions));
    }

    public ObservableCollection<AttributeFilter> AttributeFilters { get; } = [];

    /// <summary>まだ条件に入れていない属性。候補として出す。</summary>
    public ObservableCollection<string> AttributeSuggestions { get; } = [];

    public RelayCommand AddAttributeFilterCommand { get; }

    /// <summary>表示順の候補。属性が増えるとその軸も増える。</summary>
    public ObservableCollection<SortOption> SortOptions { get; } = [];

    public bool HasTagFilters => TagFilters.Count > 0;

    public bool HasAttributeFilters => AttributeFilters.Count > 0;

    public static SortOption DefaultSort => new()
    {
        Label = "入手日が新しい順",
        Kind = SortKind.AcquiredAt,
        Descending = true,
    };

    /// <summary>
    /// 表示順。絞り込みとは別に持つ。
    /// 「どれを見せるか」と「どの順で見せるか」は別の判断なので、指定する場所も分けている。
    /// </summary>
    public SortOption Sort
    {
        get => _sort;
        set
        {
            if (value is not null && SetField(ref _sort, value))
            {
                ApplyFilters();
            }
        }
    }

    public const string AllCategories = "すべて";

    /// <summary>
    /// マスタから絞り込みの軸と表示順の候補を組み直す。
    /// 選択状態は名前で引き継ぐ（編集画面でタグを足して戻ってきても条件が消えないように）。
    /// </summary>
    private void BuildFacets()
    {
        var selectedTops = TagFilters
            .Where(filter => filter.IsSelected)
            .ToDictionary(
                filter => filter.Name,
                filter => filter.SelectedSubs.ToList(),
                StringComparer.CurrentCultureIgnoreCase);

        _attributeNames = _services.Store.Attributes.Load().Attributes
            .Select(definition => definition.Name)
            .ToList();

        // BOOTHタグはマスタを持たない（商品に付いているものが全て）。
        // 候補は名前順に出す。付いている数の順にしないのは、上位が汎用語で
        // 埋まって絞り込みの役に立たないため（実データで138種の80%が1商品のみ）
        _boothTagNames = _allItems
            .SelectMany(item => item.Booth.Tags)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        // ライブラリから消えたタグを積んだままにしない
        foreach (var filter in BoothTagFilters.ToList())
        {
            if (!_boothTagNames.Contains(filter.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                BoothTagFilters.Remove(filter);
            }
        }

        RefreshBoothTagSuggestions();
        RefreshAvatarSuggestions();

        TagFilters.Clear();
        foreach (var top in _services.Store.UserTags.Load().Tops)
        {
            var filter = new UserTagFilter { Name = top.Name };
            foreach (var sub in top.Subs)
            {
                filter.Subs.Add(new UserTagSubFilter { Name = sub.Name });
            }

            filter.Attach();
            filter.Changed += ApplyFilters;

            if (selectedTops.TryGetValue(top.Name, out var subs))
            {
                filter.IsSelected = true;
                foreach (var sub in filter.Subs.Where(sub => subs.Contains(sub.Name, StringComparer.CurrentCultureIgnoreCase)))
                {
                    sub.SetSilently(true);
                }
            }

            TagFilters.Add(filter);
        }

        // 条件に入れている軸は保つ。マスタが増えても勝手に条件は増やさない
        foreach (var filter in AttributeFilters.ToList())
        {
            if (!_attributeNames.Contains(filter.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                AttributeFilters.Remove(filter);
            }
        }

        SortOptions.Clear();
        SortOptions.Add(DefaultSort);
        SortOptions.Add(new SortOption { Label = "入手日が古い順", Kind = SortKind.AcquiredAt });
        SortOptions.Add(new SortOption { Label = "名前順", Kind = SortKind.Name });
        SortOptions.Add(new SortOption { Label = "容量が大きい順", Kind = SortKind.Size, Descending = true });
        SortOptions.Add(new SortOption { Label = "スキ数が多い順", Kind = SortKind.WishList, Descending = true });

        // 「最近」の3種。足跡が無い商品は後ろにまとめる（0扱いにすると
        // 「まだ無い」が「一番古い」に化ける）
        SortOptions.Add(new SortOption
        {
            Label = "最近使った順",
            Kind = SortKind.RecentlyUsed,
            Descending = true,
        });
        SortOptions.Add(new SortOption
        {
            Label = "最近見た順",
            Kind = SortKind.RecentlyViewed,
            Descending = true,
        });
        SortOptions.Add(new SortOption
        {
            Label = "最近手元に入った順",
            Kind = SortKind.RecentlyAdded,
            Descending = true,
        });

        foreach (var name in _attributeNames)
        {
            SortOptions.Add(new SortOption
            {
                Label = $"{name} が高い順",
                Kind = SortKind.Attribute,
                AttributeName = name,
                Descending = true,
            });
        }

        RefreshAttributeSuggestions();

        // 組み直しで参照が変わるので、同じ意味の選択肢に繋ぎ直す
        _sort = SortOptions.FirstOrDefault(option =>
            option.Kind == _sort.Kind
            && option.Descending == _sort.Descending
            && option.AttributeName == _sort.AttributeName) ?? SortOptions[0];

        OnPropertyChanged(nameof(Sort));
        OnPropertyChanged(nameof(HasTagFilters));
        OnPropertyChanged(nameof(HasAttributeFilters));
    }

    /// <summary>まだ条件に入れていない属性だけを候補に出す。</summary>
    private void RefreshAttributeSuggestions()
    {
        AttributeSuggestions.Clear();
        foreach (var name in _attributeNames.Where(name =>
            !AttributeFilters.Any(filter => string.Equals(filter.Name, name, StringComparison.CurrentCultureIgnoreCase))))
        {
            AttributeSuggestions.Add(name);
        }

        OnPropertyChanged(nameof(HasAttributeSuggestions));
        OnPropertyChanged(nameof(HasAttributeFilters));
    }

    public bool HasAttributeSuggestions => AttributeSuggestions.Count > 0;

    /// <summary>属性を条件に追加する。追加直後は0-100で、全件を通す（未評価も含む）。</summary>
    private void AddAttributeFilter(string? name)
    {
        var attribute = _attributeNames.FirstOrDefault(entry =>
            string.Equals(entry, name?.Trim(), StringComparison.CurrentCultureIgnoreCase));

        if (attribute is null
            || AttributeFilters.Any(filter => string.Equals(filter.Name, attribute, StringComparison.CurrentCultureIgnoreCase)))
        {
            return;
        }

        var filter = new AttributeFilter { Name = attribute };
        filter.Changed += ApplyFilters;
        filter.RemoveCommand = new RelayCommand(() =>
        {
            AttributeFilters.Remove(filter);
            RefreshAttributeSuggestions();
            ApplyFilters();
        });

        AttributeFilters.Add(filter);
        RefreshAttributeSuggestions();
        ApplyFilters();
    }

    /// <summary>
    /// マスタだけが変わったときに、絞り込みの選択肢を作り直す。
    ///
    /// 並べ替えや追加はitemに触らないので、全件の読み直しまでは要らない。
    /// これを呼ばないと、タグの管理で並べ替えても検索画面が古い並びのままになる。
    /// </summary>
    public void RefreshFacets()
    {
        BuildFacets();
        ApplyFilters();
    }
}
