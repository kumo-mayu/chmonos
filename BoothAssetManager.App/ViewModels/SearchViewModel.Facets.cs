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

    /// <summary>表示順に使う項目。属性が増えるとその軸も増える（向きは別に持つ・M5）。</summary>
    public ObservableCollection<SortField> SortFields { get; } = [];

    public static SortField DefaultSortField => new()
    {
        Label = "入手日",
        Kind = SortKind.AcquiredAt,
        DescendingLabel = "新しい順",
        AscendingLabel = "古い順",
        FullLabel = descending => descending ? "入手日が新しい順" : "入手日が古い順",
    };

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
    /// 並べ替えに使う項目（ユーザ指示 2026-09-20・M5）。項目を変えると、その項目の既定の向きに戻す
    /// ——「名前」を選んだのに「新しい順」のままだと、何順なのか読めない
    /// </summary>
    public SortField SortField
    {
        get => _sortField;
        set
        {
            if (value is null || ReferenceEquals(value, _sortField))
            {
                return;
            }

            _sortField = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AscendingLabel));
            OnPropertyChanged(nameof(DescendingLabel));
            OnPropertyChanged(nameof(SortsDescending));
            OnPropertyChanged(nameof(SortsAscending));
            Sort = _sortField.ToOption(_sortField.DefaultDescending);
        }
    }

    /// <summary>向きのボタンの言い方。項目で変わる（日付は「新しい順／古い順」、数は「多い順／少ない順」）。</summary>
    public string DescendingLabel => _sortField.DescendingLabel;

    public string AscendingLabel => _sortField.AscendingLabel;

    /// <summary>向き。2つしかないので、繋がったボタンで出す（`docs/spec/ui-rules.md` の並べ替えの決め事）。</summary>
    public bool SortsDescending
    {
        get => _sort.Descending;
        set
        {
            if (value && !_sort.Descending)
            {
                Sort = _sortField.ToOption(descending: true);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SortsAscending));
            }
        }
    }

    public bool SortsAscending
    {
        get => !_sort.Descending;
        set
        {
            if (value && _sort.Descending)
            {
                Sort = _sortField.ToOption(descending: false);
                OnPropertyChanged();
                OnPropertyChanged(nameof(SortsDescending));
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

        // 並べ替えは「項目」と「向き」に分ける（ユーザ指示 2026-09-20・M5）。
        // 言い方は項目ごとに変える（日付は新しい／古い、数は多い／少ない）
        SortFields.Clear();
        SortFields.Add(new SortField
        {
            Label = "入手日",
            Kind = SortKind.AcquiredAt,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "入手日が新しい順" : "入手日が古い順",
        });
        SortFields.Add(new SortField
        {
            Label = "名前",
            Kind = SortKind.Name,
            DescendingLabel = "わ→あ",
            AscendingLabel = "あ→わ",
            DefaultDescending = false,
            FullLabel = descending => descending ? "名前の逆順" : "名前順",
        });
        SortFields.Add(new SortField
        {
            Label = "容量",
            Kind = SortKind.Size,
            DescendingLabel = "大きい順",
            AscendingLabel = "小さい順",
            FullLabel = descending => descending ? "容量が大きい順" : "容量が小さい順",
        });
        SortFields.Add(new SortField
        {
            Label = "スキ数",
            Kind = SortKind.WishList,
            DescendingLabel = "多い順",
            AscendingLabel = "少ない順",
            FullLabel = descending => descending ? "スキ数が多い順" : "スキ数が少ない順",
        });

        // 「最近」の3種。足跡が無い商品は後ろにまとめる（0扱いにすると
        // 「まだ無い」が「一番古い」に化ける）
        SortFields.Add(new SortField
        {
            Label = "使った日",
            Kind = SortKind.RecentlyUsed,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "最近使った順" : "使ったのが古い順",
        });
        SortFields.Add(new SortField
        {
            Label = "見た日",
            Kind = SortKind.RecentlyViewed,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "最近見た順" : "見たのが古い順",
        });
        SortFields.Add(new SortField
        {
            Label = "手元に入った日",
            Kind = SortKind.RecentlyAdded,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "最近手元に入った順" : "手元に入ったのが古い順",
        });

        foreach (var name in _attributeNames)
        {
            SortFields.Add(new SortField
            {
                Label = name,
                Kind = SortKind.Attribute,
                AttributeName = name,
                DescendingLabel = "高い順",
                AscendingLabel = "低い順",
                FullLabel = descending => descending ? $"{name} が高い順" : $"{name} が低い順",
            });
        }

        // 組み直しで参照が変わるので、同じ意味の項目に繋ぎ直す
        _sortField = SortFields.FirstOrDefault(field =>
            field.Kind == _sort.Kind && field.AttributeName == _sort.AttributeName) ?? SortFields[0];
        _sort = _sortField.ToOption(_sort.Descending);

        OnPropertyChanged(nameof(Sort));
        OnPropertyChanged(nameof(SortField));
        OnPropertyChanged(nameof(AscendingLabel));
        OnPropertyChanged(nameof(DescendingLabel));

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
