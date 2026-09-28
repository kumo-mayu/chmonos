using System.Collections.ObjectModel;
using System.Windows;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>属性1件。平均も出すのは、値の入り方が偏っていないか見えるようにするため。</summary>
public sealed class AttributeMasterRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    /// <summary>保存したら書き換える。読み込み時の値のままだと、選び直したときに古いメモが出る。</summary>
    public string? Memo { get; set; }

    public required int ItemCount { get; init; }

    public double? Average { get; init; }

    public string ItemCountText => ItemCount == 0 ? "未評価" : $"{ItemCount}";

    public string AverageText => Average is null ? "評価なし" : $"平均 {Average.Value:0} %";

    public bool IsUsed => ItemCount > 0;

    /// <summary>
    /// 編集画面で最初から並べる属性か。
    ///
    /// **並べるだけで、値は保存しない。**触らなかった行は書き出されない。
    /// </summary>
    public required bool IsDefault { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>マスタに無いのにitemが参照している属性。要確認は知らせるだけで、直せるのはここ。</summary>
public sealed class OrphanAttributeRow : ViewModelBase
{
    public required string Name { get; init; }

    public required int ItemCount { get; init; }

    public string ItemCountText => $"{ItemCount} 件の商品が参照";

    public RelayCommand? AddToMasterCommand { get; set; }

    public RelayCommand? MergeCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// 属性の管理画面。タグの管理と同じ作りにしてある（一覧＋詳細、件数から検索へ、
/// マスタに無い参照の修復、ドラッグでの並べ替え）。
///
/// 違いはitem側が名前だけでなく 0〜100 の値を持つこと。統合すると
/// 「両方に値が入っているitemでどちらを残すか」が出るので、そこだけ聞く。
/// </summary>
public sealed class AttributeManageViewModel : ViewModelBase, IPendingWrites
{
    private readonly AppServiceContainer _services;

    private ManageItemView? _itemView;

    /// <summary>商品をカードで出すかリストで出すか（M4）。ほかの画面と同じ切り替え。</summary>
    private ManageItemView ItemView => _itemView ??= new ManageItemView(_services, "attribute", () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
        RebuildLines();
    });

    public bool IsCardMode => ItemView.IsCardMode;

    public bool IsListMode => ItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => ItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => ItemView.Set(true));
    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "attributes.list");
    private readonly MainViewModel _main;

    private List<AttributeMasterRow> _all = [];
    private AttributeMasterRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private string _statusText = string.Empty;

    public AttributeManageViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        AddCommand = new RelayCommand(parameter => AddAsync(parameter as string).Forget());
        AskRenameCommand = new RelayCommand(() => AskRenameAsync().Forget(), () => Selected is not null);
        DeleteCommand = new RelayCommand(() => DeleteAsync().Forget(), () => Selected is not null);

        // メモは押さずに残す（タグの管理・ショップ・アバターと揃える。ユーザ指示 2026-09-19）
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        ToggleDefaultCommand = new RelayCommand(() => ToggleDefaultAsync().Forget(), () => Selected is not null);
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());
        ShowItemsCommand = new RelayCommand(
            () => _main.ShowItemsWithAttribute(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        _sort = services.UiState.AttributeSort switch
        {
            "name" => TagSortMode.Name,
            "count" => TagSortMode.Count,
            _ => TagSortMode.Manual,
        };

        // 読み終わる前から、頭（状況の文・一覧に無い属性）と下の枠を出しておく
        RebuildLines();
        ReloadAsync().Forget();
    }

    /// <summary>
    /// 選んでいた属性と、この属性を持つ商品を開いていたかを覚える（タグの管理と同じ。商品ページから戻ると先頭に戻ってしまう）。
    /// 画面は開くたびに作り直すので、型の側で持つ。アプリを閉じるまでの記憶でよい
    /// </summary>
    private static string? _lastSelected;

    private static bool _lastItemsExpanded;

    private TagSortMode _sort;

    /// <summary>
    /// 並べ方（ユーザ指示 2026-09-19：タグの管理と揃える）。選ぶと `attributes.json` の並びも同じ順に書き換える
    /// ——検索と編集の候補がこの並びをそのまま使うので、画面だけ並べ替えると食い違う
    /// </summary>
    public TagSortMode Sort
    {
        get => _sort;
        set
        {
            if (!SetField(ref _sort, value))
            {
                return;
            }

            var saved = value switch
            {
                TagSortMode.Name => "name",
                TagSortMode.Count => "count",
                _ => "manual",
            };

            _main.SaveUiStateAsync(state => state with { AttributeSort = saved }).Forget();
            ApplySortAsync().Forget();
        }
    }

    public bool SortsByName
    {
        get => Sort == TagSortMode.Name;
        set { if (value) { Sort = TagSortMode.Name; } }
    }

    public bool SortsByCount
    {
        get => Sort == TagSortMode.Count;
        set { if (value) { Sort = TagSortMode.Count; } }
    }

    public bool SortsManually
    {
        get => Sort == TagSortMode.Manual;
        set { if (value) { Sort = TagSortMode.Manual; } }
    }

    /// <summary>今の並べ方で `attributes.json` を並べ替える。手で並べた順のときは、人が置いた順を触らない。</summary>
    private async Task ApplySortAsync()
    {
        foreach (var name in new[] { nameof(SortsByName), nameof(SortsByCount), nameof(SortsManually) })
        {
            OnPropertyChanged(name);
        }

        if (Sort == TagSortMode.Manual)
        {
            return;
        }

        var order = (Sort == TagSortMode.Count
                ? _all.OrderByDescending(row => row.ItemCount).ThenBy(row => row.Name, StringComparer.CurrentCulture)
                : _all.OrderBy(row => row.Name, StringComparer.CurrentCulture))
            .Select(row => row.Name)
            .ToList();

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderAttributes(order));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>ドラッグしたら「手で並べた順」にする（タグの管理と同じ）。</summary>
    private void SwitchToManual()
    {
        if (_sort == TagSortMode.Manual)
        {
            return;
        }

        _sort = TagSortMode.Manual;
        _main.SaveUiStateAsync(state => state with { AttributeSort = "manual" }).Forget();

        foreach (var name in new[] { nameof(Sort), nameof(SortsByName), nameof(SortsByCount), nameof(SortsManually) })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>
    /// この属性を持つ商品（ユーザ指示 2026-09-19：タグの管理の小分類の中身と揃える）。
    /// 開いたときだけ作る——全部の属性で先に作ると、絵の読み込みが件数ぶん走る
    /// </summary>
    public ObservableCollection<TagItemRow> Items { get; } = [];

    /// <summary>
    /// 右側に並べる平らな一覧（仮想化の単位）。先頭はこの画面そのもの（属性の札・商品の見出し）、
    /// 続いて開いた商品の段（<see cref="ManageItemLine"/>）、最後に下の枠（<see cref="ManageFoot"/>）。
    /// 商品を WrapPanel に全部並べていた頃は、2000件の属性を開くと約8秒固まった（2026-09-24）
    /// </summary>
    public ObservableCollection<object> Lines { get; } = [];

    private ManageFoot? _foot;

    /// <summary>中の商品を並べられる幅。0 はまだ測っていない（段に切れないので、段を並べない）。</summary>
    private double _itemsWidth;

    /// <summary>中の商品を並べられる幅を受け取る（View が一覧の幅から枠の分を引いて渡す）。段の数が変わるときだけ切り直す。</summary>
    public void SetItemsWidth(double width)
    {
        if (Math.Abs(width - _itemsWidth) < 0.5)
        {
            return;
        }

        var before = (Cards: ManageItemLayout.ColumnsFor(_itemsWidth, Services.CardMetrics.SlotWidth), List: ManageItemLayout.ColumnsFor(_itemsWidth, ManageItemLayout.ListSlotWidth));
        var measured = _itemsWidth > 0;
        _itemsWidth = width;
        var after = (Cards: ManageItemLayout.ColumnsFor(width, Services.CardMetrics.SlotWidth), List: ManageItemLayout.ColumnsFor(width, ManageItemLayout.ListSlotWidth));
        if (!measured || before != after)
        {
            RebuildLines();
        }
    }

    /// <summary>開き具合・表示の切り替え・向き・幅から段を組み直す。同じ中身の段は使い回す（見えている部品を作り直さない）。</summary>
    private void RebuildLines()
    {
        _foot ??= new ManageFoot(this);
        var target = new List<object> { this };

        if (HasSelection && _isItemsExpanded && _itemsWidth > 0 && Items.Count > 0)
        {
            var card = IsCardMode;
            var columns = ManageItemLayout.ColumnsFor(_itemsWidth, card ? Services.CardMetrics.SlotWidth : ManageItemLayout.ListSlotWidth);

            // カードは前も WrapPanel で、向きの切り替えを持たなかった（向きはリストだけ）
            var previous = ManageItemLayout.IndexByFirst(Lines);
            var used = new HashSet<ManageItemLine>(ReferenceEqualityComparer.Instance);
            foreach (var items in ManageItemLayout.Split(Items, columns, vertical: !card && ItemsFlowVertical))
            {
                target.Add(ManageItemLayout.Line(items, card, previous, used));
            }
        }

        target.Add(_foot);
        CollectionSync.Apply(Lines, target);
    }

    private bool _isItemsExpanded;

    public bool IsItemsExpanded
    {
        get => _isItemsExpanded;
        set
        {
            if (SetField(ref _isItemsExpanded, value))
            {
                if (!HasItemFilter)
                {
                    _lastItemsExpanded = value;
                }

                RebuildItems();
            }
        }
    }

    // 「評価した商品」ではなく「この属性を持つ商品」（ユーザ指示 2026-09-19）
    public string ItemsHeaderText => Selected is { ItemCount: > 0 } row
        ? $"この属性を持つ商品 {row.ItemCount} 件"
        : "この属性を持つ商品";

    public string ExpandToolTip => SelectedIsUsed
        ? "開くと、この属性を持つ商品が値の順に並びます。"
        : "この属性を持つ商品はまだありません。編集画面で値を入れると、ここに並びます。";

    /// <summary>
    /// 並べ方・向き・中を探す欄がまとめて押せなくなるので、**その理由を見える所に1行出す**
    /// （`ui-rules.md`・E11：同じ画面で理由の出るボタンと出ないボタンが混ざっていた）。
    /// 灰色の部品ひとつひとつに吹き出しを付けるより、まとめて1行の方が読まれる。
    /// </summary>
    public bool ShowsUnusedReason => HasSelection && !SelectedIsUsed;

    /// <summary>
    /// 値の高い順か低い順か（ユーザ指示 2026-09-19）。既定は高い順——その属性の「らしい」物から見たいことが多い。
    /// 低い順は、付けたけれど弱い物を見直すときに使う。属性をまたいで、アプリを閉じるまで覚える
    /// </summary>
    private static bool _itemsAscending;

    public bool ItemsDescending
    {
        get => !_itemsAscending;
        set
        {
            if (value == _itemsAscending)
            {
                _itemsAscending = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ItemsAscending));
                RebuildItems();
            }
        }
    }

    public bool ItemsAscending
    {
        get => _itemsAscending;
        set => ItemsDescending = !value;
    }

    private bool? _itemsVertical;

    /// <summary>
    /// 中の商品を上から下へ流すか（ユーザ指示 2026-09-19：横固定だと縦に読む人には並びが追いにくい）。
    /// `ui-state.json` の `attributeItemsVertical` に覚える（タグの管理は名前順なので持たない。ユーザ判断 同日）
    /// </summary>
    public bool ItemsFlowVertical
    {
        get => _itemsVertical ??= _services.UiState.AttributeItemsVertical;
        set
        {
            if (value == ItemsFlowVertical)
            {
                return;
            }

            _itemsVertical = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ItemsFlowHorizontal));
            RebuildLines();
            _main.SaveUiStateAsync(state => state with { AttributeItemsVertical = value }).Forget();
        }
    }

    public bool ItemsFlowHorizontal
    {
        get => !ItemsFlowVertical;
        set => ItemsFlowVertical = !value;
    }

    private string _itemFilter = string.Empty;

    /// <summary>この属性を持つ商品の中を探す。書き方は検索画面と同じ（ユーザ指示 2026-09-19）。</summary>
    public string ItemFilter
    {
        get => _itemFilter;
        set
        {
            if (SetField(ref _itemFilter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasItemFilter));

                // 探している間は開いて見せる。空にしたら、覚えている開き方に戻す
                _isItemsExpanded = HasItemFilter || _lastItemsExpanded;
                OnPropertyChanged(nameof(IsItemsExpanded));
                RebuildItems();
            }
        }
    }

    public bool HasItemFilter => _itemFilter.Trim().Length > 0;

    private int _itemFilterHits;

    public string ItemFilterResultText => HasItemFilter
        ? $"「{_itemFilter.Trim()}」に当たる商品 {_itemFilterHits} 件"
        : string.Empty;

    /// <summary>
    /// 今の <see cref="Items"/> が何を出しているか（属性・探す語・向き）。同じなら作り直さない。
    /// null は「まだ何も出していない」
    /// </summary>
    private string? _builtKey;

    /// <summary>
    /// 行は商品と属性ごとに1つ作って、画面を移るまで使い回す（ユーザ指摘 2026-09-19：開くたびに作り直していて、
    /// 113件で開くまでに 0.2〜0.3 秒固まっていた）。行を使い回せば、一覧の部品も並べ替え・出し入れで済む
    /// </summary>
    private readonly Dictionary<string, TagItemRow> _rowCache = new(StringComparer.Ordinal);

    private void RebuildItems()
    {
        RebuildItemsCore();
        RebuildLines();
    }

    private void RebuildItemsCore()
    {
        // 畳んでいる間は触らない。畳むたびに消すと、開き直すたびに部品を全部作り直すことになる
        if (!_isItemsExpanded || Selected is not { ItemCount: > 0 } row)
        {
            return;
        }

        var key = $"{row.Name}\n{_itemFilter.Trim()}\n{_itemsAscending}";
        if (key == _builtKey)
        {
            return;
        }

        _builtKey = key;

        var filter = ItemTextFilter.Create(_itemFilter);
        var found = _main.Search.SnapshotItems()
            .Select(item => (Item: item, Value: ValueOf(item, row.Name)))
            .Where(entry => entry.Value is not null && (filter is null || filter.Matches(entry.Item)));

        // 同じ値の中は名前順（向きを変えても、同じ値の並びは動かさない）
        var rated = (_itemsAscending
                ? found.OrderBy(entry => entry.Value)
                : found.OrderByDescending(entry => entry.Value))
            .ThenBy(entry => entry.Item.DisplayName, StringComparer.CurrentCulture)
            .Select(entry => RowFor(row.Name, entry.Item, entry.Value!.Value))
            .ToList();

        SyncItems(rated);
        _itemFilterHits = rated.Count;
        OnPropertyChanged(nameof(ItemFilterResultText));
    }

    /// <summary>
    /// <see cref="Items"/> を目当ての並びに寄せる。**消して足し直さず、要らない行を抜き・動かし・足りない行を差す**——
    /// 一覧は残った行の部品をそのまま使うので、向きを変える・探す語を足すだけなら作り直しが起きない
    /// </summary>
    private void SyncItems(IReadOnlyList<TagItemRow> target)
    {
        var keep = new HashSet<TagItemRow>(target);
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var i = 0; i < target.Count; i++)
        {
            var at = IndexFrom(target[i], i);
            if (at == i)
            {
                continue;
            }

            if (at > i)
            {
                Items.Move(at, i);
            }
            else
            {
                Items.Insert(i, target[i]);
            }
        }
    }

    private int IndexFrom(TagItemRow row, int start)
    {
        for (var i = start; i < Items.Count; i++)
        {
            if (ReferenceEquals(Items[i], row))
            {
                return i;
            }
        }

        return -1;
    }

    private TagItemRow RowFor(string attribute, ItemRecord item, int value)
    {
        // 値は属性ごとに違うので、鍵に属性も入れる
        var key = attribute + "\n" + item.Id;
        if (!_rowCache.TryGetValue(key, out var entry))
        {
            entry = CreateItemRow(item, value);
            _rowCache[key] = entry;
        }

        return entry;
    }

    /// <summary>属性の値。名前は大文字小文字を区別しない（一覧の件数の数え方と同じ）。</summary>
    private static int? ValueOf(ItemRecord item, string name)
    {
        foreach (var (key, value) in item.Local.Attributes)
        {
            if (string.Equals(key, name, StringComparison.CurrentCultureIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>中に出す商品1件。絵の引き方はタグの管理（改変の一覧）と同じ。</summary>
    private TagItemRow CreateItemRow(ItemRecord item, int value)
    {
        var builder = new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, ItemRecord>());

        var entry = new TagItemRow
        {
            ItemId = item.Id,
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            ValueText = $"{value} %",
            ThumbnailPath = builder.ItemThumbnailPath(item),
            Thumbnails = _main.Thumbnails,
            CardFactory = () => _main.Search.CardFor(item.Id),
        };

        entry.OpenCommand = new RelayCommand(() => _main.ShowItem(item));
        return entry;
    }

    public ObservableCollection<AttributeMasterRow> Rows { get; } = [];

    public ObservableCollection<OrphanAttributeRow> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherNames { get; } = [];

    /// <summary>マスタにある全属性。マスタに無い参照の寄せ先はこちらを候補にする。</summary>
    public ObservableCollection<string> AllNames { get; } = [];

    /// <summary>戻る（V2・V3）。ナビから入っても直前の画面へ戻れる。</summary>
    public MainViewModel Main => _main;

    public RelayCommand AddCommand { get; }

    /// <summary>名前を変える窓を出す（タグの管理と同じ窓。既にある名前を選ぶと統合）。</summary>
    public RelayCommand AskRenameCommand { get; }

    public RelayCommand DeleteCommand { get; }

    private readonly Debounced _saveMemo;

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => _saveMemo.RunNowAsync();

    private bool _memoPending;
    private bool _swappingMemo;
    private bool _rebuildingList;

    /// <summary>編集画面で最初から並べる属性かを切り替える</summary>
    public RelayCommand ToggleDefaultCommand { get; }

    public string ToggleDefaultText => Selected?.IsDefault == true
        ? "最初から並べるのをやめる"
        : "編集画面に最初から並べる";

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ShowItemsCommand { get; }

    public AttributeMasterRow? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || (_rebuildingList && value is null))
            {
                return;
            }

            // 待っているメモは、移る前に今の属性へ書き切る。移ってからだと、移った先の名前で書いてしまう
            FlushMemo();

            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            _selected = value;

            if (_selected is not null)
            {
                _selected.IsSelected = true;
                _lastSelected = _selected.Name;
            }

            // 選び直しで欄を入れ替えるときは、自動保存を走らせない
            _swappingMemo = true;
            MemoDraft = _selected?.Memo ?? string.Empty;
            _swappingMemo = false;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedUsageText));
            OnPropertyChanged(nameof(SelectedIsUsed));
            OnPropertyChanged(nameof(ShowItemsToolTip));
            OnPropertyChanged(nameof(ItemsHeaderText));
            OnPropertyChanged(nameof(ExpandToolTip));
            OnPropertyChanged(nameof(ShowsUnusedReason));
            OnPropertyChanged(nameof(ToggleDefaultText));

            // 開き方は属性をまたいで持つ（小分類を開いたまま見比べるのと同じ）。探している語もそのまま当てる
            _isItemsExpanded = SelectedIsUsed && (HasItemFilter || _lastItemsExpanded);
            OnPropertyChanged(nameof(IsItemsExpanded));
            RebuildItems();
            OnPropertyChanged(nameof(SelectedIsDefault));
            OnPropertyChanged(nameof(DefaultNote));
            RebuildOtherNames();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public bool SelectedIsDefault => Selected?.IsDefault == true;

    /// <summary>並べるだけで保存しないことを、切り替える前に書いておく</summary>
    public string DefaultNote => SelectedIsDefault
        ? "編集画面に最初から並びます。値を動かすまで保存されません。"
        : "編集画面に最初から並べておけます。値を動かすまで保存されません。";

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品にも付いていません。編集画面で値を入れると、ここに件数が表示されます。"
            : $"{Selected.ItemCount} 件の商品に付いています（{Selected.AverageText}）";

    public string ShowItemsToolTip => SelectedIsUsed
        ? "この属性を持つ商品を、検索で開きます。"
        : "この属性はまだどの商品にも付いていません。編集画面で値を入れると開けます。";

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                Rebuild();
            }
        }
    }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value))
            {
                OnPropertyChanged(nameof(MemoChanged));

                if (!_swappingMemo)
                {
                    _memoPending = true;
                    _saveMemo.Request();
                }
            }
        }
    }

    public bool MemoChanged => Selected is not null && MemoDraft != (Selected.Memo ?? string.Empty);

    public bool HasOrphans => Orphans.Count > 0;

    /// <summary>
    /// 左の一覧が空のとき、真っ白にせず次にやることを書く（`ui-rules.md`・E1）。
    /// **探して0件と、そもそも1つも無いのを言い分ける**（タグの管理と同じ）。
    /// </summary>
    public bool IsEmpty => Rows.Count == 0;

    public string EmptyText => HasFilter
        ? "探している言葉に当てはまる属性がありません。言葉を変えるか、絞り込みを消してください。"
        : "属性はまだありません。上の「属性を追加」に名前を入れると、編集画面でその評価を付けられるようになります。";

    public string HeaderText => $"属性 {_all.Count} 件";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;

    public async Task ReloadAsync()
    {
        // 古いファイルを読まないよう、待っているメモを書き終えてから読む
        if (_memoPending)
        {
            _saveMemo.Cancel();
            await SaveMemoAsync();
        }

        // 名前の変更・統合・削除の後は、行の名前や値が変わっているかもしれないので、使い回しの控えを捨てる
        _rowCache.Clear();
        _builtKey = null;

        var master = _services.Store.Attributes.Load();
        var usage = await _services.Attributes.LoadUsageAsync();
        var orphans = await _services.Attributes.LoadOrphansAsync();

        RunOnUiThread(() =>
        {
            var counts = usage.ToDictionary(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);
            var keep = Selected?.Name;

            _all = master.Attributes.Select(definition => new AttributeMasterRow
            {
                Name = definition.Name,
                Memo = definition.Memo,
                ItemCount = counts.TryGetValue(definition.Name, out var entry) ? entry.ItemCount : 0,
                Average = counts.TryGetValue(definition.Name, out var found) ? found.Average : null,
                IsDefault = definition.IsDefault,
            }).ToList();

            AllNames.Clear();
            foreach (var row in _all)
            {
                AllNames.Add(row.Name);
            }

            Orphans.Clear();
            foreach (var orphan in orphans)
            {
                Orphans.Add(CreateOrphanRow(orphan));
            }

            Rebuild();
            OnPropertyChanged(nameof(HeaderText));
            OnPropertyChanged(nameof(HasOrphans));

            Selected = _all.FirstOrDefault(row => row.Name == (keep ?? _lastSelected)) ?? Rows.FirstOrDefault();
        });
    }

    /// <summary>
    /// 左の一覧を絞る。属性の名前だけでなく、この属性を持つ商品でも引く（タグの管理と揃える。ユーザ指示 2026-09-19）。
    /// 書き方は検索画面と同じ
    /// </summary>
    private void Rebuild()
    {
        var filter = ItemTextFilter.Create(_filterText);
        var items = filter is null ? [] : _main.Search.SnapshotItems();

        // 作り直す間は、一覧が書き戻す「選択なし」を受けない（タグの管理と同じ。探すたびに右が空になっていた）
        _rebuildingList = true;
        try
        {
            Rows.Clear();
            foreach (var row in _all.Where(row => filter is null
                || filter.MatchesName(row.Name)
                || items.Any(item => ValueOf(item, row.Name) is not null && filter.Matches(item))))
            {
                Rows.Add(row);
            }
        }
        finally
        {
            _rebuildingList = false;
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterResultText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    public bool HasFilter => _filterText.Trim().Length > 0;

    public string FilterResultText => HasFilter
        ? $"「{_filterText.Trim()}」に当たる属性 {Rows.Count} 件"
        : string.Empty;

    private void RebuildOtherNames()
    {
        OtherNames.Clear();
        foreach (var name in _all
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherNames.Add(name);
        }
    }

    private OrphanAttributeRow CreateOrphanRow(OrphanAttribute orphan)
    {
        var row = new OrphanAttributeRow { Name = orphan.Name, ItemCount = orphan.ItemCount };

        row.AddToMasterCommand = new RelayCommand(() => AddOrphanToMasterAsync(row).Forget());
        row.MergeCommand = new RelayCommand(parameter => MergeOrphanAsync(row, parameter as string).Forget());
        row.RemoveCommand = new RelayCommand(() => RemoveOrphanAsync(row).Forget());

        return row;
    }

    /// <summary>
    /// 並べ替える。並びは検索の候補にも編集の候補にもそのまま出るので、
    /// 「よく使う順」に置けること自体が機能になる。itemの値には触らない。
    /// </summary>
    public async Task MoveAsync(AttributeMasterRow moved, AttributeMasterRow target, bool after)
    {
        var order = _all.Select(row => row.Name).ToList();

        var from = order.IndexOf(moved.Name);
        if (from < 0 || moved.Name == target.Name)
        {
            return;
        }

        order.RemoveAt(from);

        var at = order.IndexOf(target.Name);
        var to = after ? at + 1 : at;
        if (at < 0 || to == from)
        {
            return;
        }

        order.Insert(to, moved.Name);

        SwitchToManual();
        await _services.Commands.ExecuteAsync(new UiCommand.ReorderAttributes(order));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    private async Task AddAsync(string? name)
    {
        // 改行やタブは空白に寄せて1行にする（I13）
        var trimmed = NameText.Normalize(name);

        // 空のまま押したときに黙って終わらない（I1）
        if (trimmed.Length == 0)
        {
            StatusText = "属性の名前を入れてから押してください。";
            return;
        }

        if (NameText.IsTooLong(trimmed))
        {
            StatusText = NameText.TooLongMessage("属性の名前");
            return;
        }

        // 既にある名前は足されない。**足していないのに「追加しました」と言わない**（I2）
        if (_all.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            StatusText = $"「{trimmed}」は既にあります。";
            Selected = _all.FirstOrDefault(row =>
                string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(trimmed));
        StatusText = $"「{trimmed}」を追加しました。";
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _all.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
    }

    /// <summary>
    /// 統合の2回目の確認（ユーザ判断 2026-09-20・D5）。窓で影響を見て残す値を選んだ後に、もう一度だけ聞く。
    ///
    /// **統合は元に戻せない**（1つになった後は、どちらに付いていたかで分け直せない）。
    /// 名前の変更は同じ手順で戻せるので、そちらは窓だけの1回で済ませる。
    /// </summary>
    private static bool ConfirmMerge(string from, string to, AttributeMergePreview preview, AttributeMergeValue keep)
        => Confirm(
            $"「{from}」を「{to}」に統合します。\n\n"
            + (preview.ItemCount == 0
                ? "どの商品も評価していないので、商品は書き換わりません。\n"
                : $"{preview.ItemCount} 件の商品を書き換えます。メモは「{to}」側へ追記します。\n")
            + (preview.Conflicts > 0
                ? $"両方に値が入っている {preview.Conflicts} 件は、"
                    + $"{(keep == AttributeMergeValue.KeepTarget ? $"「{to}」" : $"「{from}」")}の値を残します。\n"
                : string.Empty)
            + "\nこの操作は元に戻せません。",
            "属性を統合する");

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。
    /// 統合では両方に値が入っているitemが出るので、そのときだけどちらを残すか聞く。
    /// </summary>
    private async Task RenameAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.Ordinal))
        {
            return;
        }

        var merging = _all.Any(row => string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));
        var keep = AttributeMergeValue.KeepTarget;

        if (merging)
        {
            var preview = await _services.Attributes.PreviewMergeAsync(Selected.Name, target);
            var dialog = new Views.MergeAttributeDialog(
                new MergeAttributeDialogViewModel(Selected.Name, target, preview));

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            keep = ((MergeAttributeDialogViewModel)dialog.DataContext).Keep;

            if (!ConfirmMerge(Selected.Name, target, preview, keep))
            {
                return;
            }
        }

        var result = await RewriteAttributesAsync(
            new UiCommand.RenameAttribute(Selected.Name, target, keep), "名前を変更できませんでした。");

        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = rewritten.Result.WasMerged
                ? $"「{target}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。"
                : $"「{target}」に変更し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。";
        }

        var next = target;
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _all.FirstOrDefault(row =>
            string.Equals(row.Name, next, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、評価も一緒に外す。
    /// 属性はuserTagと違って「未設定」が既定なので、空になっても編集の対象には戻らない。
    /// </summary>
    private async Task DeleteAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどの商品も評価していないので、影響はありません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件の商品から、この属性の評価が消えます。\n"
                + $"\nこの操作は元に戻せません。同じ名前で作り直しても、{Selected.ItemCount} 件ぶんの評価は戻りません。";

        if (!Confirm(message, "属性を削除する"))
        {
            return;
        }

        var result = await RewriteAttributesAsync(new UiCommand.DeleteAttribute(Selected.Name), "削除できませんでした。");
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"削除し、{rewritten.Result.ItemsUpdated} 件の商品から評価を外しました。";
        }

        Selected = null;
        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 改名・削除の命令を送り、できなかったら状態の行に出す。
    ///
    /// 命令は書けなかった例外（ファイルを掴まれた・ドライブが外れた）をそのまま投げ、入口は Forget() でログに残すだけなので、
    /// 前は押しても何も起きなかったように見えた。「できなかった」の結果も受けずに捨てていた。
    /// 例外でも読み直しは続ける——途中まで書き換えた商品があり得るので、今の数を見せる
    /// </summary>
    private async Task<CommandResult?> RewriteAttributesAsync(UiCommand command, string failedText)
    {
        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
            }

            return result;
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("属性の書き換え", exception);
            StatusText = failedText + Core.Services.FailureText.Cause(exception);
            return null;
        }
    }

    /// <summary>待っているメモを今書く。選び直す前に呼ぶ。</summary>
    private void FlushMemo()
    {
        if (!_memoPending)
        {
            return;
        }

        _saveMemo.Cancel();
        SaveMemoAsync().Forget();
    }

    private async Task SaveMemoAsync()
    {
        _memoPending = false;
        if (Selected is not { } row)
        {
            return;
        }

        // 読み直すと打っている途中の欄が戻るので読み直さず、行の値だけ書き換える（タグの管理と同じ）
        var memo = MemoDraft;
        row.Memo = memo;
        await _services.Commands.ExecuteAsync(new UiCommand.SetAttributeMemo(row.Name, memo));
        StatusText = memo.Trim().Length == 0 ? "メモを消しました。" : "メモを保存しました。";
    }

    /// <summary>名前を変える窓を出してから実行する。既にある名前を選ぶと統合になる。</summary>
    private async Task AskRenameAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var model = new RenameTagDialogViewModel("属性", Selected.Name, Selected.ItemCount, OtherNames.ToList());
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameAsync(model.Target);
    }

    /// <summary>
    /// 編集画面で最初から並べる属性かを切り替える。
    ///
    /// **既に評価してある商品には何もしない。**並べるだけで、
    /// 値は人が動かしたときにしか保存されない。
    /// </summary>
    private async Task ToggleDefaultAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var next = !Selected.IsDefault;
        await _services.Commands.ExecuteAsync(new UiCommand.SetAttributeDefault(Selected.Name, next));
        StatusText = next
            ? $"「{Selected.Name}」を編集画面に最初から並べます。値は動かしたときだけ付きます。"
            : $"「{Selected.Name}」を最初から並べるのをやめました。付けた評価はそのまま残ります。";
        await ReloadAsync();
    }

    private async Task AddOrphanToMasterAsync(OrphanAttributeRow row)
    {
        await _services.Commands.ExecuteAsync(new UiCommand.AddAttribute(row.Name));
        StatusText = $"「{row.Name}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに表示されるようになります。";
        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private async Task MergeOrphanAsync(OrphanAttributeRow row, string? target)
    {
        var name = target?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var preview = await _services.Attributes.PreviewMergeAsync(row.Name, name);
        var dialog = new Views.MergeAttributeDialog(
            new MergeAttributeDialogViewModel(row.Name, name, preview));

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var keep = ((MergeAttributeDialogViewModel)dialog.DataContext).Keep;

        // 統合はもう一度聞く（D5）
        if (!ConfirmMerge(row.Name, name, preview, keep))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.RenameAttribute(row.Name, name, keep));
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"「{name}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。";
        }

        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    private async Task RemoveOrphanAsync(OrphanAttributeRow row)
    {
        if (!Confirm(
            $"「{row.Name}」の評価を {row.ItemCount} 件の商品から外します。\n\nこの操作は元に戻せません。",
            "参照を外す"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new UiCommand.DeleteAttribute(row.Name));
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            StatusText = $"「{row.Name}」を {rewritten.Result.ItemsUpdated} 件の商品から外しました。";
        }

        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 人が押した操作の確認（アイコンは Question で揃える。`ui-rules.md`・D9）。
    /// 既定はキャンセル。Enterを押しただけで消えないようにする。
    /// </summary>
    private static bool Confirm(string message, string caption)
        => Services.Notice.Show(
            message,
            caption,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
}
