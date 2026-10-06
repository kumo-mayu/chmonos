using System.Collections.ObjectModel;

namespace Chmonos.App.ViewModels;

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

    /// <summary>押せない「属性 ▸」の吹き出し。</summary>
    internal const string NoAttributesHint = "属性の管理で属性を追加すると選べます。";

    /// <summary>
    /// 表示順のボタンが開くメニュー（ユーザ判断 2026-10-06：「属性」の中に大量の属性が入っている形）。
    /// 名前・ショップ・カテゴリ・スキ数／BOOTH価格・払った額／公開日〜取り込み日／容量／属性 ▸（子に属性の管理の並びで全部）。
    /// プルダウンは子の一覧を持てないので、メニューにした。今の項目が替わるたびに作り直す（印を付け直すため。<see cref="SortField"/> の知らせに続けて知らせる）
    /// </summary>
    public IReadOnlyList<SortMenuEntry> SortMenu => BuildSortMenu();

    private List<SortMenuEntry> BuildSortMenu()
    {
        var entries = new List<SortMenuEntry>();
        string? group = null;
        foreach (var field in SortFields.Where(field => field.Kind != SortKind.Attribute))
        {
            if (group is not null && field.Group != group)
            {
                entries.Add(SortMenuEntry.Separator());
            }

            group = field.Group;
            entries.Add(SortMenuLeaf(field, $"SearchSortField.{field.Kind}"));
        }

        var attributes = SortFields
            .Where(field => field.Kind == SortKind.Attribute)
            .Select(field => SortMenuLeaf(field, $"SearchSortField.Attribute.{field.AttributeName}"))
            .ToList();
        entries.Add(SortMenuEntry.Separator());
        entries.Add(new SortMenuEntry
        {
            Label = "属性",
            IsParent = true,
            Children = attributes,
            IsChecked = attributes.Any(attribute => attribute.IsChecked),
            // 属性が無いと子が空で、開いても何も選べない。消さずに押せなくして、作る所を言う（ui-input.md「右クリックのメニュー」と同じ）
            IsEnabled = attributes.Count > 0,
            DisabledHint = attributes.Count > 0 ? null : NoAttributesHint,
            AutomationId = "SearchSortField.Attributes",
        });
        return entries;
    }

    private SortMenuEntry SortMenuLeaf(SortField field, string automationId) => new()
    {
        Label = field.Label,
        IsChecked = ReferenceEquals(field, _sortField),
        AutomationId = automationId,
        ChooseCommand = new RelayCommand(() => SortField = field),
    };

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
        // 言い方は項目ごとに変える（日付は新しい／古い、数は多い／少ない、額は高い／安い、名前とショップは昇順／降順）。
        // **意味の近い項目を隣に置く**（2026-09-24）：買ったとき・BOOTH の値（日付と額）→ 名前で並ぶもの → 手元の量 → 「最近」の足跡 → 属性。
        // 後ろに足していくと、「払った額」と「BOOTHの価格」のような比べたい物が離れる。
        // FullLabel は検索の履歴に残る言い方なので、前からある項目の言い方は変えない
        SortFields.Clear();
        SortFields.Add(new SortField
        {
            Label = "入手日",
            Kind = SortKind.AcquiredAt,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "入手日が新しい順" : "入手日が古い順",
        });

        // 自分用の額の合計。価格の絞り込みの既定（自分が払った額）と同じ数え方で、贈った・貰ったは含めない
        SortFields.Add(new SortField
        {
            Label = "払った額",
            Kind = SortKind.SelfPaid,
            DescendingLabel = "高い順",
            AscendingLabel = "安い順",
            FullLabel = descending => descending ? "払った額が高い順" : "払った額が安い順",
        });
        SortFields.Add(new SortField
        {
            Label = "公開日",
            Kind = SortKind.PublishedAt,
            DescendingLabel = "新しい順",
            AscendingLabel = "古い順",
            FullLabel = descending => descending ? "公開日が新しい順" : "公開日が古い順",
        });

        // 絞り込みの「BOOTHの価格」と同じ呼び方にする（「今の価格」だと、自分が払った額と取り違えやすい）
        SortFields.Add(new SortField
        {
            Label = "BOOTH価格",
            Kind = SortKind.BoothPrice,
            DescendingLabel = "高い順",
            AscendingLabel = "安い順",
            FullLabel = descending => descending ? "BOOTH価格が高い順" : "BOOTH価格が安い順",
        });
        SortFields.Add(new SortField
        {
            Label = "スキ数",
            Kind = SortKind.WishList,
            DescendingLabel = "多い順",
            AscendingLabel = "少ない順",
            FullLabel = descending => descending ? "スキ数が多い順" : "スキ数が少ない順",
        });
        SortFields.Add(new SortField
        {
            Label = "名前",
            Kind = SortKind.Name,
            DescendingLabel = "降順",
            AscendingLabel = "昇順",
            DefaultDescending = false,
            FullLabel = descending => descending ? "名前の逆順" : "名前順",
        });
        SortFields.Add(new SortField
        {
            Label = "ショップ",
            Kind = SortKind.Shop,
            DescendingLabel = "降順",
            AscendingLabel = "昇順",
            DefaultDescending = false,
            FullLabel = descending => descending ? "ショップの逆順" : "ショップ順",
        });

        // 向きは「何の順か」を名乗る（昇順 でも 新しい／古い でもない。BOOTH のカテゴリの一覧と同じ並び）
        SortFields.Add(new SortField
        {
            Label = "カテゴリ",
            Kind = SortKind.Category,
            DescendingLabel = "逆順",
            AscendingLabel = "BOOTHの並び",
            DefaultDescending = false,
            FullLabel = descending => descending ? "カテゴリがBOOTHの逆順" : "カテゴリがBOOTHの並び順",
        });
        SortFields.Add(new SortField
        {
            Label = "容量",
            Kind = SortKind.Size,
            DescendingLabel = "大きい順",
            AscendingLabel = "小さい順",
            FullLabel = descending => descending ? "容量が大きい順" : "容量が小さい順",
        });

        // 「最近」の3種。足跡が無い商品は後ろにまとめる（0扱いにすると
        // 「まだ無い」が「一番古い」に化ける）。
        // 名前は「最近」の条件の記録の種類と同じ言い方（ユーザ判断 2026-10-06。前の「使った日」「見た日」は、条件の「Unityへ送った」「商品ページを開いた」と
        // 同じ物だと読めなかった）。言い方を変えたので、前の言い方で残った検索の履歴・保存した条件の表示順は既定の順で開く
        foreach (var (kind, sortKind) in new[]
        {
            (Core.Services.RecentKind.Used, SortKind.RecentlyUsed),
            (Core.Services.RecentKind.Viewed, SortKind.RecentlyViewed),
            (Core.Services.RecentKind.Added, SortKind.RecentlyAdded),
        })
        {
            var label = RecentModule.DateLabel(kind);
            SortFields.Add(new SortField
            {
                Label = label,
                Kind = sortKind,
                DescendingLabel = "新しい順",
                AscendingLabel = "古い順",
                FullLabel = descending => descending ? $"{label}が新しい順" : $"{label}が古い順",
            });
        }

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

        // 足した順ではなく、決めた並びに並べ直す（まとまりごとに区切り線を引くので、同じまとまりを隣に置く）。
        // 属性どうしは属性の管理の並びのまま（並べ直しは安定な並べ替え）
        var ordered = SortFields.OrderBy(field => SortField.OrderOf(field.Kind)).ToList();
        SortFields.Clear();
        foreach (var field in ordered)
        {
            SortFields.Add(field);
        }

        // 組み直しで参照が変わるので、同じ意味の項目に繋ぎ直す。見つからなければ既定（入手日）——並べ直したので先頭は名前になった
        _sortField = SortFields.FirstOrDefault(field =>
            field.Kind == _sort.Kind && field.AttributeName == _sort.AttributeName)
            ?? SortFields.First(field => field.Kind == SortKind.AcquiredAt);
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
