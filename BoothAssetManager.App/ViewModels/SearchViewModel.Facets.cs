using System.Collections.ObjectModel;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：結果の行・表示順・マスタからの組み直し</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// 結果を行単位で持つ。行を仮想化の単位にすることで、画面に出ている行のカードだけが実体化する。
    /// WPFには仮想化する WrapPanel が無いので、列数をこちら側で決めて行に切っている。
    /// </summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

    /// <summary>表示順の候補。属性が増えるとその軸も増える。</summary>
    public ObservableCollection<SortOption> SortOptions { get; } = [];

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

    /// <summary>
    /// マスタと全商品から、条件の候補と表示順の候補を組み直す。
    /// 条件の値は鍵（名前・ID）で持っているので、組み直しても消えない（編集画面でタグを足して戻ってきても条件が残る）。
    /// </summary>
    private void BuildFacets()
    {
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

        SortOptions.Clear();
        SortOptions.Add(DefaultSort);
        SortOptions.Add(new SortOption { Label = "入手日が古い順", Kind = SortKind.AcquiredAt });
        SortOptions.Add(new SortOption { Label = "名前順", Kind = SortKind.Name });
        SortOptions.Add(new SortOption { Label = "容量が大きい順", Kind = SortKind.Size, Descending = true });
        SortOptions.Add(new SortOption { Label = "スキ数が多い順", Kind = SortKind.WishList, Descending = true });

        // 「最近」の3種。足跡が無い商品は後ろにまとめる（0扱いにすると
        // 「まだ無い」が「一番古い」に化ける）
        SortOptions.Add(new SortOption { Label = "最近使った順", Kind = SortKind.RecentlyUsed, Descending = true });
        SortOptions.Add(new SortOption { Label = "最近見た順", Kind = SortKind.RecentlyViewed, Descending = true });
        SortOptions.Add(new SortOption { Label = "最近手元に入った順", Kind = SortKind.RecentlyAdded, Descending = true });

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

        // 組み直しで参照が変わるので、同じ意味の選択肢に繋ぎ直す
        _sort = SortOptions.FirstOrDefault(option =>
            option.Kind == _sort.Kind
            && option.Descending == _sort.Descending
            && option.AttributeName == _sort.AttributeName) ?? SortOptions[0];

        OnPropertyChanged(nameof(Sort));

        // 素体の所属はアバターの管理で変わる。索引は次の絞り込みで作り直す
        _compatibility = null;
        _moduleSourcesReady = true;
        RefreshModuleSources();
    }

    /// <summary>
    /// マスタだけが変わったときに、絞り込みの選択肢を作り直す。
    ///
    /// 並べ替えや追加は商品に触らないので、全件の読み直しまでは要らない。
    /// これを呼ばないと、タグの管理で並べ替えても検索画面が古い並びのままになる。
    /// </summary>
    public void RefreshFacets()
    {
        BuildFacets();
        ApplyFilters();
    }
}
