using System.Collections.ObjectModel;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>属性1件。平均も出すのは、値の入り方が偏っていないか見えるようにするため。</summary>
public sealed class AttributeMasterRow : ManageMasterRow
{
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
/// 属性の管理画面。タグの管理と同じ作り（一覧＋詳細、件数から検索へ、マスタに無い参照の修復、ドラッグでの並べ替え）で、
/// 共通の所は <see cref="ManageViewModelBase{TRow, TOrphan}"/> が持つ。
///
/// 違いはitem側が名前だけでなく 0〜100 の値を持つこと。統合すると
/// 「両方に値が入っているitemでどちらを残すか」が出るので、そこだけ聞く。
/// </summary>
public sealed class AttributeManageViewModel : ManageViewModelBase<AttributeMasterRow, OrphanAttributeRow>
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    public AttributeManageViewModel(AppServiceContainer services, MainViewModel main)
        : base(services, main, "attribute", "attributes.list", ParseSort(services.UiState.AttributeSort, TagSortMode.Manual))
    {
        _services = services;
        _main = main;

        // メモは押さずに残す（タグの管理・ショップ・アバターと揃える。ユーザ指示 2026-09-19）。部品は土台が持つ
        ToggleDefaultCommand = new RelayCommand(() => ToggleDefaultAsync().Forget(), () => Selected is not null);

        // 読み終わる前から、頭（状況の文・一覧に無い属性）と下の枠を出しておく
        RebuildLines();
        ReloadAsync().Forget();
    }

    protected override string KindName => "属性";

    /// <summary>
    /// 選んでいた属性と、この属性を持つ商品を開いていたかを覚える（タグの管理と同じ。商品ページから戻ると先頭に戻ってしまう）。
    /// 画面は開くたびに作り直すので、型の側で持つ。アプリを閉じるまでの記憶でよい
    /// </summary>
    private static string? _lastSelected;

    private static bool _lastItemsExpanded;

    protected override string? LastSelectedName
    {
        get => _lastSelected;
        set => _lastSelected = value;
    }

    /// <summary>左の一覧（`attributes.json` の並び）。</summary>
    public ObservableCollection<AttributeMasterRow> Rows => VisibleRows;

    /// <summary>マスタにある全属性。マスタに無い参照の寄せ先はこちらを候補にする。</summary>
    public ObservableCollection<string> AllNames { get; } = [];

    /// <summary>
    /// 並べ方（ユーザ指示 2026-09-19：タグの管理と揃える）。選ぶと `attributes.json` の並びも同じ順に書き換える
    /// ——検索と編集の候補がこの並びをそのまま使うので、画面だけ並べ替えると食い違う
    /// </summary>
    protected override void SaveSort(string saved)
        => _main.SaveUiStateAsync(state => state with { AttributeSort = saved }).Forget();

    protected override async Task ReorderBySortAsync()
        => await _services.Commands.ExecuteAsync(new UiCommand.ReorderAttributes(
            SortNames(AllRows.Select(row => (row.Name, row.ItemCount)))));

    protected override UiCommand AddCommandFor(string name) => new UiCommand.AddAttribute(name);

    protected override UiCommand MemoCommandFor(AttributeMasterRow row, string memo) => new UiCommand.SetAttributeMemo(row.Name, memo);

    protected override UiCommand ReorderCommandFor(IReadOnlyList<string> order) => new UiCommand.ReorderAttributes(order);

    protected override string RewriteLogLabel => "属性の書き換え";

    protected override void ShowItemsOf(string name) => _main.ShowItemsWithAttribute(name);

    /// <summary>
    /// この属性を持つ商品（ユーザ指示 2026-09-19：タグの管理の小分類の中身と揃える）。
    /// 開いたときだけ作る——全部の属性で先に作ると、絵の読み込みが件数ぶん走る
    /// </summary>
    public ObservableCollection<TagItemRow> Items { get; } = [];

    /// <summary>
    /// 右側の平らな一覧（<see cref="ManageViewModelBase{TRow, TOrphan}.Lines"/>）の間には、開いた商品の段（<see cref="ManageItemLine"/>）だけを挟む。
    /// 商品を WrapPanel に全部並べていた頃は、2000件の属性を開くと約8秒固まった（2026-09-24）。
    /// 組み直しはその場で行う（タグの管理のように知らせが続かない）
    /// </summary>
    protected override void RequestLines() => RebuildLines();

    /// <summary>開き具合・表示の切り替え・向き・幅から段を組み直す。同じ中身の段は使い回す（見えている部品を作り直さない）。</summary>
    private void RebuildLines()
    {
        var target = new List<object> { this };

        if (HasSelection && _isItemsExpanded && ItemsWidth > 0 && Items.Count > 0)
        {
            var card = IsCardMode;
            var columns = ManageItemLayout.ColumnsFor(ItemsWidth, card ? Services.CardMetrics.SlotWidth : ManageItemLayout.ListSlotWidth);

            // カードは前も WrapPanel で、向きの切り替えを持たなかった（向きはリストだけ）
            var previous = ManageItemLayout.IndexByFirst(Lines);
            var used = new HashSet<ManageItemLine>(ReferenceEqualityComparer.Instance);
            foreach (var items in ManageItemLayout.Split(Items, columns, vertical: !card && ItemsFlowVertical))
            {
                target.Add(ManageItemLayout.Line(items, card, previous, used));
            }
        }

        target.Add(Foot);
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

    /// <summary>この属性を持つ商品の中を探す（<see cref="ManageViewModelBase{TRow, TOrphan}.ItemFilter"/>）。</summary>
    protected override void OnItemFilterChanged()
    {
        // 探している間は開いて見せる。空にしたら、覚えている開き方に戻す
        _isItemsExpanded = HasItemFilter || _lastItemsExpanded;
        OnPropertyChanged(nameof(IsItemsExpanded));
        RebuildItems();
    }

    private int _itemFilterHits;

    public override string ItemFilterResultText => HasItemFilter
        ? $"「{ItemFilter.Trim()}」に当たる商品 {_itemFilterHits} 件"
        : string.Empty;

    /// <summary>商品の一覧は同じ条件なら作り直さない作り（<c>_builtKey</c>）なので、鍵を捨ててから作る。</summary>
    protected override void RefilterItemsForSearchFacts()
    {
        _builtKey = null;
        RebuildItems();
    }

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

        var key = $"{row.Name}\n{ItemFilter.Trim()}\n{_itemsAscending}";
        if (key == _builtKey)
        {
            return;
        }

        _builtKey = key;

        var filter = ItemTextFilter.Create(ItemFilter, _main.Search.CreateSearchFacts());
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
            // 絵の引き方はタグの管理（改変の一覧）と同じ
            entry = CreateItemRow(item, ThumbnailPathOf(item), $"{value} %");
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

    /// <summary>編集画面で最初から並べる属性かを切り替える</summary>
    public RelayCommand ToggleDefaultCommand { get; }

    public string ToggleDefaultText => Selected?.IsDefault == true
        ? "最初から並べるのをやめる"
        : "編集画面に最初から並べる";

    protected override void ClearNoticesOfPrevious() => DefaultNotice.Clear();

    protected override void OnSelectionChanged()
    {
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
    }

    public bool SelectedIsDefault => Selected?.IsDefault == true;

    /// <summary>並べるだけで保存しないことを、切り替える前に書いておく</summary>
    public string DefaultNote => SelectedIsDefault
        ? "編集画面に最初から並びます。値を動かすまで保存されません。"
        : "編集画面に最初から並べておけます。値を動かすまで保存されません。";

    public override string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品にも付いていません。編集画面で値を入れると、ここに件数が表示されます。"
            : $"{Selected.ItemCount} 件の商品に付いています（{Selected.AverageText}）";

    public override string ShowItemsToolTip => SelectedIsUsed
        ? "この属性を持つ商品を、検索で開きます。"
        : "この属性はまだどの商品にも付いていません。編集画面で値を入れると開けます。";

    /// <summary>**探して0件と、そもそも1つも無いのを言い分ける**（タグの管理と同じ）。</summary>
    public override string EmptyText => HasFilter
        ? "探している言葉に当てはまる属性がありません。言葉を変えるか、絞り込みを消してください。"
        : "属性はまだありません。上の欄に名前を入れて追加すると、編集画面でその評価を付けられるようになります。";

    /// <summary>「最初から並べる」の切り替えの知らせ。そのボタンの下に出す（ほかの出し先は土台の <see cref="ManageViewModelBase{TRow, TOrphan}.NameNotice"/> など）。</summary>
    public AreaNotice DefaultNotice { get; } = new();

    public override async Task ReloadAsync()
    {
        // 古いファイルを読まないよう、待っているメモを書き終えてから読む
        await MemoSaver.FlushAsync();

        // 名前の変更・統合・削除の後は、行の名前や値が変わっているかもしれないので、使い回しの控えを捨てる
        _rowCache.Clear();
        _builtKey = null;

        var master = _services.Store.Attributes.Load();
        var usage = await _services.Attributes.LoadUsageAsync();
        var orphans = await _services.Attributes.LoadOrphansAsync();

        RunOnUiThread(() =>
        {
            var counts = usage.ToDictionary(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase);

            var rows = master.Attributes.Select(definition => new AttributeMasterRow
            {
                Name = definition.Name,
                Memo = definition.Memo,
                ItemCount = counts.TryGetValue(definition.Name, out var entry) ? entry.ItemCount : 0,
                Average = counts.TryGetValue(definition.Name, out var found) ? found.Average : null,
                IsDefault = definition.IsDefault,
            }).ToList();

            AllNames.Clear();
            foreach (var row in rows)
            {
                AllNames.Add(row.Name);
            }

            ShowLoaded(rows, () => orphans.Select(CreateOrphanRow));
        });
    }

    /// <summary>
    /// 左の一覧の当たり方。名前とメモに加え、**この属性を付けた（評価した）商品の名前でも当てる**（メモ57 2026-10-05）。
    /// 属性は各属性の下に商品が並ぶので、商品名で探して属性が出ても何で当たったか分かる（タグの管理は二重構造で
    /// 分かりにくかったので、名前とメモだけのまま。メモ21-② 2026-10-03）。何で当たったかは「商品 n 件」と出す
    /// </summary>
    protected override Func<AttributeMasterRow, string?> CreateMatcher(ItemTextFilter filter)
    {
        var itemHits = CountItemHitsByAttribute(filter);
        return row =>
        {
            if (filter.MatchesNameOrMemo(row.Name, null))
            {
                return string.Empty;
            }

            var reasons = new List<string>();

            // 入力中のメモも照らす（保存を待たない）。選んでいる属性の下書きは、行の Memo にまだ入っていない
            if (filter.MatchesNameOrMemo(row.Name, ReferenceEquals(row, Selected) ? MemoDraft : row.Memo))
            {
                reasons.Add("メモ");
            }

            if (itemHits.TryGetValue(row.Name, out var hits) && hits > 0)
            {
                reasons.Add($"商品 {hits} 件");
            }

            return reasons.Count > 0 ? string.Join("・", reasons) : null;
        };
    }

    /// <summary>属性ごとに、その属性を付けた商品のうち検索の語に当たる件数。1回の走査で全属性ぶん数える</summary>
    private Dictionary<string, int> CountItemHitsByAttribute(ItemTextFilter filter)
    {
        var hits = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var item in _main.Search.SnapshotItems())
        {
            if (item.Local.Attributes.Count == 0 || !filter.Matches(item))
            {
                continue;
            }

            foreach (var key in item.Local.Attributes.Keys)
            {
                hits[key] = hits.GetValueOrDefault(key) + 1;
            }
        }

        return hits;
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
    /// 統合の2回目の確認（ユーザ判断 2026-09-20・D5）。窓で影響を見て残す値を選んだ後に、もう一度だけ聞く。
    ///
    /// **統合は元に戻せない**（1つになった後は、どちらに付いていたかで分け直せない）。
    /// 名前の変更は同じ手順で戻せるので、そちらは窓だけの1回で済ませる。
    /// </summary>
    private static bool ConfirmMerge(string from, string to, AttributeMergePreview preview, AttributeMergeValue keep)
        => Confirm(
            $"「{from}」を「{to}」に統合します。\n\n"
            + (preview.ItemCount == 0
                ? "どの商品も評価していません。\n"
                : $"{preview.ItemCount} 件の商品を書き換えます。メモは「{to}」側へ追記します。\n")
            + (preview.Conflicts > 0
                ? $"両方に値が入っている {preview.Conflicts} 件は、"
                    + $"{(keep == AttributeMergeValue.KeepTarget ? $"「{to}」" : $"「{from}」")}の値を残します。\n"
                : string.Empty)
            + "\nこの操作は元に戻せません。",
            "属性を統合する");

    /// <summary>試験の口：統合の件数を数える処理を差し替える（数えている間に選び直す場面を作る）。</summary>
    internal Func<string, string, Task<AttributeMergePreview>>? PreviewMergeForTest { get; set; }

    /// <summary>試験の口：統合の窓に答える（窓を出すと答える人がいないので止まる）。true で統合する。</summary>
    internal static Func<MergeAttributeDialogViewModel, bool>? MergeDialogIntercept { get; set; }

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。
    /// 統合では両方に値が入っているitemが出るので、そのときだけどちらを残すか聞く。
    /// </summary>
    internal override async Task RenameAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is not { } source || string.IsNullOrEmpty(target)
            || string.Equals(target, source.Name, StringComparison.Ordinal))
        {
            return;
        }

        // **始めた時の属性に固定する**（外部の点検 2026-10-06）。統合の件数を数える間（商品を全部読む）に一覧で別の属性を
        // 選び直せるので、待った後に Selected を読み直すと、数えた属性とは別の属性を統合し、その評価値を失っていた
        var sourceName = source.Name;
        var merging = AllRows.Any(row => string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));
        var keep = AttributeMergeValue.KeepTarget;

        if (merging)
        {
            var preview = await (PreviewMergeForTest ?? ((from, to) => _services.Attributes.PreviewMergeAsync(from, to)))(sourceName, target);

            // 待った間に選び直していたら、数えた件数が今の選択と合わないので、窓を出さずにやめる（もう一度押せば今の選択で数え直す）
            if (!ReferenceEquals(Selected, source))
            {
                return;
            }

            var mergeModel = new MergeAttributeDialogViewModel(sourceName, target, preview);
            if (!(MergeDialogIntercept?.Invoke(mergeModel) ?? new Views.MergeAttributeDialog(mergeModel).ShowDialog() == true))
            {
                return;
            }

            keep = mergeModel.Keep;

            if (!ConfirmMerge(sourceName, target, preview, keep))
            {
                return;
            }
        }

        // 名前を変えただけなら名前の近く。統合は片方の行が消えるので、一覧の見出しの近く
        var notice = merging ? ListNotice : NameNotice;
        var result = await RewriteAsync(
            new UiCommand.RenameAttribute(sourceName, target, keep), "名前を変更できませんでした。", notice);

        string? done = null;
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            done = rewritten.Result.WasMerged
                ? $"「{target}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。"
                : $"「{target}」に変更し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。";
        }

        var next = target;
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = AllRows.FirstOrDefault(row =>
            string.Equals(row.Name, next, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        // 名前が変わると選び直しで名前の近くの知らせが消えるので、選び直した後に出す
        if (done is not null)
        {
            notice.Show(done);
        }

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、評価も一緒に外す。
    /// 属性はuserTagと違って「未設定」が既定なので、空になっても編集の対象には戻らない。
    /// </summary>
    protected override async Task DeleteSelectedAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどの商品も評価していません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件の商品から、この属性の評価が消えます。\n"
                + $"\nこの操作は元に戻せません。同じ名前で作り直しても、{Selected.ItemCount} 件ぶんの評価は戻りません。";

        if (!Confirm(message, "属性を削除する"))
        {
            return;
        }

        var result = await RewriteAsync(new UiCommand.DeleteAttribute(Selected.Name), "削除できませんでした。", ListNotice);
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            ListNotice.Show($"削除し、{rewritten.Result.ItemsUpdated} 件の商品から評価を外しました。");
        }

        Selected = null;
        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
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
        var said = next
            ? $"「{Selected.Name}」を編集画面に最初から並べます。値は動かしたときだけ付きます。"
            : $"「{Selected.Name}」を最初から並べるのをやめました。付けた評価はそのまま残ります。";
        await ReloadAsync();
        DefaultNotice.Show(said);
    }

    private async Task AddOrphanToMasterAsync(OrphanAttributeRow row)
    {
        // 一覧に無い名前の3つの操作も、できなかったときは一覧の見出しの近くに出す（前は何も出ず、できたと言っていた）
        var result = await RewriteAsync(new UiCommand.AddAttribute(row.Name), "一覧に追加できませんでした。", ListNotice);
        if (result is not null and not CommandResult.Failed)
        {
            ListNotice.Show($"「{row.Name}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに表示されるようになります。");
        }

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

        var result = await RewriteAsync(new UiCommand.RenameAttribute(row.Name, name, keep), "統合できませんでした。", ListNotice);
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            ListNotice.Show($"「{name}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。");
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

        var result = await RewriteAsync(new UiCommand.DeleteAttribute(row.Name), "外せませんでした。", ListNotice);
        if (result is CommandResult.AttributesRewritten rewritten)
        {
            ListNotice.Show($"「{row.Name}」を {rewritten.Result.ItemsUpdated} 件の商品から外しました。");
        }

        await ReloadAsync();
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }
}
