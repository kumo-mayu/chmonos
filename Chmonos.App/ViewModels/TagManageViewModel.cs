using System.Collections.ObjectModel;
using System.Windows;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 並べ替え中に、どこへ落ちるかを示す線。行の上か下かだけを持つ。
/// Adornerを使わないのは、行のテンプレートに1本足すだけで済むため。
/// </summary>
/// <summary>タグの管理の並べ方（ユーザ指示 2026-09-18）。</summary>
public enum TagSortMode
{
    /// <summary>名前順（既定）。</summary>
    Name,

    /// <summary>付いている商品の多い順。</summary>
    Count,

    /// <summary>手で並べた順（ドラッグで置いた並び＝ファイルの並びそのもの）。</summary>
    Manual,
}

public abstract class ReorderableRow : ViewModelBase
{
    private bool _dropBefore;
    private bool _dropAfter;

    public bool DropBefore
    {
        get => _dropBefore;
        set => SetField(ref _dropBefore, value);
    }

    public bool DropAfter
    {
        get => _dropAfter;
        set => SetField(ref _dropAfter, value);
    }

    public void ClearDropIndicator()
    {
        DropBefore = false;
        DropAfter = false;
    }

    private bool _isNameMatch;

    /// <summary>
    /// 左の欄に打った名前と同じ名前の行（前後の空白・大文字小文字は問わない）。一覧で強調する。
    /// 同じ名前があるとき、「もうある」と一覧の側でも分かるようにする
    /// </summary>
    public bool IsNameMatch
    {
        get => _isNameMatch;
        set => SetField(ref _isNameMatch, value);
    }

    private string _matchReason = string.Empty;

    /// <summary>
    /// 左の欄で探しているとき、名前以外で当たった理由（「メモ」「小分類「〇〇」」）。名前で当たった行は空。
    /// 名前の下の1行（小分類の件数）に続けて出す。行の数は増やさない（打つたびに一覧が伸び縮みして揺れないように）
    /// </summary>
    public string MatchReason
    {
        get => _matchReason;
        set
        {
            if (SetField(ref _matchReason, value))
            {
                OnPropertyChanged(nameof(MatchReasonText));
            }
        }
    }

    /// <summary>件数の文の後ろに続ける形（「・メモ」）。理由が無ければ空。</summary>
    public string MatchReasonText => _matchReason.Length == 0 ? string.Empty : "・" + _matchReason;

    /// <summary>
    /// 一緒に並べ替えられる相手。既定は自分と同じ型（タグのトップとサブは別の並びなので、混ざらない）。
    /// 型が違っても1つの並びに混ざるもの（検索の絞り込みの条件）は、これを揃える。
    /// </summary>
    public virtual object ReorderGroup => GetType();
}

/// <summary>トップレベル1件。件数を出すのは、消す前に影響が見えるようにするため。</summary>
public sealed class TagTopRow : ReorderableRow
{
    private bool _isSelected;

    public required string Name { get; init; }

    /// <summary>保存したら書き換える。読み込み時の値のままだと、選び直したときに古いメモが出る。</summary>
    public string? Memo { get; set; }

    public required int SubCount { get; init; }

    public required int ItemCount { get; init; }

    // 画面の言葉は「大分類／小分類」に揃える（ユーザ指示 2026-09-19：「サブ」「トップ」と混ざっていた）
    public string SubCountText => SubCount == 0 ? "小分類なし" : $"小分類 {SubCount} 件";

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>サブレベル1件。</summary>
public sealed class TagSubRow : ReorderableRow
{
    public required string Name { get; init; }

    public required string Top { get; init; }

    private bool _isFilterMatch;

    /// <summary>左の欄で探している語に、この小分類の名前かメモが当たった。開いた大分類の中で強調する（何で当たったか分かるように）。</summary>
    public bool IsFilterMatch
    {
        get => _isFilterMatch;
        set => SetField(ref _isFilterMatch, value);
    }

    /// <summary>保存したら書き換える（次に保存するとき、変わった行だけを書くため）。</summary>
    public string? Memo { get; set; }

    public required int ItemCount { get; init; }

    private string _memoDraft = string.Empty;
    private bool _isExpanded;

    /// <summary>
    /// メモの入力欄（ユーザ指示 2026-09-18）。以前は「メモなし」と書いてあるだけで、
    /// **書く場所がどこにも無かった**。何を書くかは、空のときに欄の中で言う
    /// </summary>
    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value ?? string.Empty))
            {
                MemoEdited?.Invoke(this);
            }
        }
    }

    public event Action<TagSubRow>? MemoEdited;

    /// <summary>
    /// 中の商品を見られるように畳む（ユーザ指示 2026-09-18：改変とitemの関係と同じ形）。
    /// 件数だけでは、何が入っているのか分からない
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetField(ref _isExpanded, value) && value)
            {
                ExpandRequested?.Invoke(this);
            }
        }
    }

    public event Action<TagSubRow>? ExpandRequested;

    /// <summary>中に入っている商品。開いたときに詰める（全部の小分類で先に読むと重い）。</summary>
    public ObservableCollection<TagItemRow> Items { get; } = [];

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";

    public bool IsUsed => ItemCount > 0;

    // 押せないときは理由を言う（押しても何も起きないように見える物を作らない）
    public string ExpandToolTip => IsUsed
        ? "開くと、この小分類が付いている商品が並びます。"
        : "この小分類はまだどの商品にも付いていません。";

    public string ShowItemsToolTip => IsUsed
        ? "この小分類が付いている商品を、検索で開きます。"
        : "この小分類はまだどの商品にも付いていません。編集画面で付けると開けます。";

    private bool _isHidden;

    /// <summary>商品名で絞っているとき、当たりが無い小分類は出さない（ユーザ指示 2026-09-18）。</summary>
    public bool IsHidden
    {
        get => _isHidden;
        set => SetField(ref _isHidden, value);
    }

    /// <summary>寄せ先の候補。自分自身は外す（自分に改名しても何も起きない）。</summary>
    public IReadOnlyList<string> OtherNames { get; set; } = [];

    /// <summary>移動先のトップ候補。自分が属するトップは外す。</summary>
    public IReadOnlyList<string> MoveTargets { get; set; } = [];

    public RelayCommand? ShowItemsCommand { get; set; }

    public RelayCommand? RenameCommand { get; set; }

    public RelayCommand? MoveCommand { get; set; }

    public RelayCommand? DeleteCommand { get; set; }
}

/// <summary>
/// 右側の平らな一覧の1行：畳んだ小分類のひと続き。前と同じ ColumnsPanel で列に並べる
/// （開いた小分類は幅いっぱいの行になり、そこで列の並びが切れていたので、切れ目ごとにまとまりを分けても並びは同じ）。
/// </summary>
public sealed class TagSubRunLine
{
    public required IReadOnlyList<TagSubRow> Subs { get; init; }
}

/// <summary>右側の平らな一覧の1行：開いた小分類の下の余白（並べ替えのドラッグで「この後ろ」の線もここに出す）。</summary>
public sealed class TagSubFootLine(TagSubRow sub)
{
    public TagSubRow Sub { get; } = sub;
}

/// <summary>
/// 小分類の中に入っている商品1件（ユーザ指示 2026-09-18）。
/// 改変の一覧と同じ形（絵・名前）で出し、押すと商品ページへ。
/// </summary>
public sealed class TagItemRow : ViewModelBase
{
    public required string ItemId { get; init; }

    public required string Name { get; init; }

    public required string ShopName { get; init; }

    /// <summary>名前の右に出す札（属性の管理の「85 %」）。タグの管理では出さない。</summary>
    public string? ValueText { get; init; }

    public bool HasValue => ValueText is not null;

    private string? _thumbnailPath;

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        init => _thumbnailPath = value;
    }

    public Chmonos.App.Services.ThumbnailLoader? Thumbnails { get; init; }

    /// <summary>
    /// 裏の取得がこの商品の画像を置いた。行の絵とカードを引き直す（洗い出し 6。作ったときに一度だけ場所を探すので、
    /// 取り込みの④⑤の最中に開くと、知らせないと絵の無いまま残る）
    /// </summary>
    public void RefreshImages(string? thumbnailPath)
    {
        _thumbnailPath = thumbnailPath;
        OnPropertyChanged(nameof(ThumbnailPath));
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(HoverImage));
        OnPropertyChanged(nameof(HasHoverImage));
        _card?.RefreshImages();
    }

    /// <summary>
    /// 裏で読み、届いたら描き直す（改変の一覧・アバターの管理と同じ扱い）。出すのは30DIPの枠だけなので、
    /// 頭の絵の大きさで読む（<see cref="Chmonos.App.Services.ThumbnailLoader.IconShortEdgeDip"/>。96DIPで読んでいた）
    /// </summary>
    public System.Windows.Media.Imaging.BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForIcon(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    /// <summary>ホバーで出す大きめの絵（ユーザ指示 2026-09-18。ほかの一覧と同じ）。窓が開いたときに初めて読む。</summary>
    public System.Windows.Media.Imaging.BitmapSource? HoverImage => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => ThumbnailPath is not null;

    public string Initial => AvatarText.InitialOf(Name);

    public RelayCommand? OpenCommand { get; set; }

    /// <summary>カード表示（ユーザ指示 2026-09-20・M4）で使う、検索と同じカード。作るのに要る物をまとめて受け取る。</summary>
    public Func<ItemCardViewModel?>? CardFactory { get; init; }

    private ItemCardViewModel? _card;

    /// <summary>**カード表示に切り替えたときに初めて作る**（1つの分類に数百件入ることがある）。</summary>
    public ItemCardViewModel? Card => _card ??= CardFactory?.Invoke();
}

/// <summary>マスタに無いのにitemが参照している名前。要確認は知らせるだけで、直せるのはここ。</summary>
public sealed class OrphanTagRow : ViewModelBase
{
    public required string Top { get; init; }

    /// <summary>null ならトップレベル、入っていればその配下のサブ。</summary>
    public string? Sub { get; init; }

    public required int ItemCount { get; init; }

    public bool IsSub => Sub is not null;

    public string Name => Sub ?? Top;

    /// <summary>サブは、どのトップの配下なのかが分からないと直しようがない。</summary>
    public string DisplayName => IsSub ? $"{Top}／{Sub}" : Top;

    public string KindText => IsSub ? "小分類" : "大分類";

    public string ItemCountText => $"{ItemCount} 件の商品が参照";

    public string MergePlaceholder => IsSub
        ? $"「{Top}」の既にある小分類へ統合する"
        : "既にある大分類へ統合する";

    /// <summary>寄せ先の候補。トップならトップ一覧、サブなら同じトップの既存サブ。</summary>
    public IReadOnlyList<string> MergeCandidates { get; set; } = [];

    public RelayCommand? AddToMasterCommand { get; set; }

    public RelayCommand? MergeCommand { get; set; }

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// タグの管理画面。userTagマスタ（トップ／サブの2階層）を編集する。
///
/// item側は名前で参照しているので、改名も削除も全itemの書き換えを伴う。
/// 戻せない操作なので、実行前に「何件が書き換わるか」を必ず数えて見せる。
/// マスタに無い名前をitemが参照したままの状態もここに出す。
/// 要確認はそれを知らせるだけで、直せる場所はこの画面しかないため。
/// </summary>
public sealed class TagManageViewModel : ViewModelBase, IPendingWrites, IItemCardHost, IItemImagesListener
{
    private readonly AppServiceContainer _services;

    private ManageItemView? _itemView;

    /// <summary>商品をカードで出すかリストで出すか（M4）。ほかの画面と同じ切り替え。</summary>
    private ManageItemView ItemView => _itemView ??= new ManageItemView(_services, "tag", () =>
    {
        OnPropertyChanged(nameof(IsCardMode));
        OnPropertyChanged(nameof(IsListMode));
        RequestLines();
    });

    /// <summary>
    /// 右側に並べる平らな一覧（仮想化の単位）。先頭はこの画面そのもの（大分類の札・小分類の見出し）、
    /// 続いて畳んだ小分類のまとまり（<see cref="TagSubRunLine"/>）と、開いた小分類（<see cref="TagSubRow"/>・商品の段・
    /// <see cref="TagSubFootLine"/>）、最後に下の枠（<see cref="ManageFoot"/>）。
    /// 小分類の中の商品を WrapPanel に全部並べていた頃は、すべて開くとカード約560枚を全部作り、メモリが約300MB増えた（2026-09-24）
    /// </summary>
    public ObservableCollection<object> Lines { get; } = [];

    private ManageFoot? _foot;
    private readonly Dictionary<TagSubRow, TagSubFootLine> _subFeet = new(ReferenceEqualityComparer.Instance);
    private bool _linesPending;

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
            RequestLines();
        }
    }

    /// <summary>
    /// 組み直しを1回にまとめる。「すべて開く」は小分類の数だけ、開いた小分類の中身は商品の数だけ知らせが続くので、
    /// そのたびに組むと同じ組み直しを何百回もすることになる
    /// </summary>
    private void RequestLines()
    {
        if (_linesPending)
        {
            return;
        }

        _linesPending = true;
        Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, () =>
        {
            _linesPending = false;
            RebuildLines();
        });
    }

    private void RebuildLines()
    {
        _foot ??= new ManageFoot(this);
        var target = new List<object> { this };

        if (HasSelection)
        {
            var card = IsCardMode;
            var columns = ManageItemLayout.ColumnsFor(_itemsWidth, card ? Services.CardMetrics.SlotWidth : ManageItemLayout.ListSlotWidth);
            var previous = ManageItemLayout.IndexByFirst(Lines);
            var used = new HashSet<ManageItemLine>(ReferenceEqualityComparer.Instance);
            var previousRuns = Lines.OfType<TagSubRunLine>().ToList();
            var run = new List<TagSubRow>();

            void FlushRun()
            {
                if (run.Count == 0)
                {
                    return;
                }

                // 同じ小分類のまとまりは同じ行を使う（中の小分類の部品を作り直すと、打ちかけのメモ欄から入力の位置が外れる）
                var same = previousRuns.FirstOrDefault(line => line.Subs.SequenceEqual(run));
                target.Add(same ?? new TagSubRunLine { Subs = run });
                run = [];
            }

            foreach (var sub in Subs)
            {
                // 隠した小分類は、前も列の場所だけは取っていた（ColumnsPanel は畳んだ子にも列を数える）ので、まとまりに残す
                if (!sub.IsExpanded || sub.IsHidden)
                {
                    run.Add(sub);
                    continue;
                }

                FlushRun();
                target.Add(sub);
                if (_itemsWidth > 0)
                {
                    foreach (var items in ManageItemLayout.Split(sub.Items, columns, vertical: false))
                    {
                        target.Add(ManageItemLayout.Line(items, card, previous, used));
                    }
                }

                if (!_subFeet.TryGetValue(sub, out var foot))
                {
                    foot = new TagSubFootLine(sub);
                    _subFeet[sub] = foot;
                }

                target.Add(foot);
            }

            FlushRun();
        }

        // 消えた小分類の下の余白は持ち続けない
        foreach (var gone in _subFeet.Keys.Where(sub => !Subs.Contains(sub)).ToList())
        {
            _subFeet.Remove(gone);
        }

        target.Add(_foot);
        CollectionSync.Apply(Lines, target);
    }

    public bool IsCardMode => ItemView.IsCardMode;

    public bool IsListMode => ItemView.IsListMode;

    private RelayCommand? _showCards;
    private RelayCommand? _showList;

    public RelayCommand ShowCardsCommand => _showCards ??= new RelayCommand(() => ItemView.Set(false));

    public RelayCommand ShowListCommand => _showList ??= new RelayCommand(() => ItemView.Set(true));
    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "tags.list");
    private readonly MainViewModel _main;

    /// <summary>
    /// 選んでいた大分類と、開いていた小分類を覚える（ユーザ指示 2026-09-18：商品ページから戻ると
    /// 先頭に戻ってしまい、どこを見ていたか分からなくなる）。画面は開くたびに作り直すので、型の側で持つ。
    /// アプリを閉じるまでの記憶でよい（次の起動で開き直すほどの話ではない）
    /// </summary>
    private static string? _lastSelectedTop;

    private static readonly HashSet<string> ExpandedSubs = new(StringComparer.Ordinal);

    /// <summary>大分類と小分類の組を1つの鍵にする。名前には入らない改行で区切る。</summary>
    private static string SubKey(TagSubRow row) => row.Top + "\n" + row.Name;

    private List<TagTopRow> _allTops = [];
    private TagTopRow? _selected;
    private string _filterText = string.Empty;
    private string _memoDraft = string.Empty;
    private bool _isBusy;
    private bool _swappingMemo;
    private bool _rebuildingList;

    public TagManageViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        // 小分類の入れ替え（大分類を選び直す・読み直す）で、右の平らな一覧を組み直す。頭と下の枠は読み終わる前から出しておく
        Subs.CollectionChanged += (_, _) => RequestLines();
        RequestLines();

        AddTopCommand = new RelayCommand(() => AddTopAsync(FilterText).Forget());
        SubmitFilterCommand = new RelayCommand(() => SubmitFilterAsync().Forget());
        AddSubCommand = new RelayCommand(() => AddSubAsync(NewSubText).Forget(), () => Selected is not null);
        RenameTopCommand = new RelayCommand(() => AskRenameTopAsync().Forget(), () => Selected is not null);
        DeleteTopCommand = new RelayCommand(() => DeleteTopAsync().Forget(), () => Selected is not null);
        NestTopCommand = new RelayCommand(() => NestTopAsync().Forget());
        RefreshCommand = new RelayCommand(() => ReloadAsync().Forget());
        ToggleAllCommand = new RelayCommand(ToggleAll);
        ShowItemsCommand = new RelayCommand(
            () => _main.ShowItemsWithTag(Selected!.Name),
            () => Selected is { ItemCount: > 0 });

        // メモは押さずに残す（ユーザ指示 2026-09-18。ショップ・アバターと同じ）
        _saveMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveMemoAsync);
        _saveSubMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveSubMemosAsync);

        _sort = services.UiState.TagSort switch
        {
            "count" => TagSortMode.Count,
            "manual" => TagSortMode.Manual,
            _ => TagSortMode.Name,
        };

        ReloadAsync().Forget();
    }

    private readonly Debounced _saveMemo;
    private readonly Debounced _saveSubMemo;

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public Task FlushPendingWritesAsync() => Task.WhenAll(_saveMemo.RunNowAsync(), _saveSubMemo.RunNowAsync());

    /// <summary>
    /// 並べ方（ユーザ指示 2026-09-18）。既定は名前順。
    /// 選ぶと `userTags.json` の並びも同じ順に書き換える——検索の候補もこの並びをそのまま使うので、
    /// 画面だけ並べ替えると「画面と候補で順番が違う」状態になる
    /// </summary>
    private TagSortMode _sort;

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
                TagSortMode.Count => "count",
                TagSortMode.Manual => "manual",
                _ => "name",
            };

            _main.SaveUiStateAsync(state => state with { TagSort = saved }).Forget();
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

    /// <summary>
    /// 今の並べ方で `userTags.json` を並べ替える。手で並べた順のときは何もしない
    /// （人が置いた順がその並びそのものなので、触ると意味が変わる）
    /// </summary>
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

        var tops = SortNames(_allTops.Select(row => (row.Name, row.ItemCount)));
        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(tops));

        // 小分類も同じ並べ方に揃える。大分類だけ並べても、開いた先がばらばらでは読めない
        var master = _services.Store.UserTags.Load();
        foreach (var top in master.Tops)
        {
            if (top.Subs.Count < 2)
            {
                continue;
            }

            _subCounts.TryGetValue(top.Name, out var usage);
            var subs = SortNames(top.Subs.Select(sub =>
                (sub.Name, usage?.SubCounts.GetValueOrDefault(sub.Name) ?? 0)));

            await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(subs, top.Name));
        }

        await ReloadAsync();
        _main.RefreshMasters();
    }

    private IReadOnlyList<string> SortNames(IEnumerable<(string Name, int Count)> entries) => Sort switch
    {
        TagSortMode.Count => entries
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCulture)
            .Select(entry => entry.Name)
            .ToList(),
        _ => entries
            .OrderBy(entry => entry.Name, StringComparer.CurrentCulture)
            .Select(entry => entry.Name)
            .ToList(),
    };

    public ObservableCollection<TagTopRow> Tops { get; } = [];

    public ObservableCollection<TagSubRow> Subs { get; } = [];

    public ObservableCollection<OrphanTagRow> Orphans { get; } = [];

    /// <summary>改名の寄せ先候補。既存を選べば統合、無い語を入れれば単なる改名になる。</summary>
    public ObservableCollection<string> OtherTopNames { get; } = [];

    /// <summary>サブレベルの改名の寄せ先候補。同じトップの中だけを候補にする。</summary>
    public ObservableCollection<string> SubNames { get; } = [];

    /// <summary>
    /// 戻る（ユーザ判断 2026-09-20・V2・V3）。**ナビから入っても直前の画面へ戻れる。**
    /// 出し方・見た目はどの画面でも同じにする（履歴が無いときだけ出さない）。
    /// </summary>
    public MainViewModel Main => _main;

    public RelayCommand AddTopCommand { get; }

    /// <summary>
    /// 欄で Enter。打った名前の大分類が無ければ足し、あればそれを選ぶ。
    /// 左の欄は検索と追加の1本（ユーザ指示 2026-10-02）。候補は並べず、足せるときだけ欄の下に「追加」の1行を出す
    /// </summary>
    public RelayCommand SubmitFilterCommand { get; }

    public RelayCommand AddSubCommand { get; }

    public RelayCommand RenameTopCommand { get; }

    public RelayCommand DeleteTopCommand { get; }

    /// <summary>大分類を別の大分類の小分類にする。押せるかは <see cref="CanNestTop"/> を見た目（IsEnabled）で出す（wpf.md：CanExecute は止まることがある）</summary>
    public RelayCommand NestTopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>この分類が付いているitemを検索で見せる。消す・統合するの判断は中身を見ないとできない。</summary>
    public RelayCommand ShowItemsCommand { get; }

    public bool SelectedIsUsed => Selected is { ItemCount: > 0 };

    public TagTopRow? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value || (_rebuildingList && value is null))
            {
                return;
            }

            // 待っているメモは、移る前に今の対象へ書き切る。移ってからだと、移った先の欄の文と名前で書いてしまう
            FlushMemos();

            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            var previousName = _selected?.Name;
            _selected = value;

            // 読み直しで同じ名前の行に選び直しただけなら、今出している知らせは残す（名前の変更・移した結果が消えてしまう）。
            // 別の大分類へ移ったときは、前の大分類の話なので消す
            if (!string.Equals(previousName, value?.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                NameNotice.Clear();
                SubNotice.Clear();
            }

            if (_selected is not null)
            {
                _selected.IsSelected = true;
                _lastSelectedTop = _selected.Name;
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
            OnPropertyChanged(nameof(CanNestTop));
            OnPropertyChanged(nameof(NestTopToolTip));
            RebuildSubs();
            RebuildOtherNames();
            AddSubNotice.Clear();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelection => Selected is not null;

    public string SelectedName => Selected?.Name ?? string.Empty;

    public string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品にも付いていません。編集画面で付けると、ここに件数が表示されます。"
            : $"{Selected.ItemCount} 件の商品に付いています";

    public string ShowItemsToolTip => SelectedIsUsed
        ? "この大分類が付いている商品を、検索で開きます。"
        : "この大分類はまだどの商品にも付いていません。編集画面で付けると開けます。";

    /// <summary>
    /// 大分類を別の大分類の小分類にできるか。小分類を持つ大分類ではできない（ユーザ要望 2026-09-29：
    /// 小分類の下にもう1段は作れず、持っている小分類の行き場が無い）。入れ先が無い（大分類が1つだけ）ときもできない。
    /// ボタンは消さずに押せない形で置き、理由を吹き出しで言う（タグの管理の決め事）
    /// </summary>
    public bool CanNestTop => Selected is { SubCount: 0 } && _allTops.Count > 1;

    public string NestTopToolTip => Selected is { SubCount: > 0 }
        ? "小分類がある大分類は、小分類にできません。"
        : _allTops.Count > 1
            ? "この大分類を、別の大分類の小分類にします。"
            : "入れ先にする大分類が、ほかにありません。";

    /// <summary>
    /// 大分類を別の大分類の小分類にする。窓（小分類を移す窓と同じ物）で入れ先を選び、
    /// 戻せない操作なので閉じた後に件数を書いてもう一度確かめる（大分類の統合と同じ作法）。
    /// </summary>
    private async Task NestTopAsync()
    {
        if (Selected is not { } source || !CanNestTop)
        {
            return;
        }

        var targets = _allTops
            .Where(entry => !string.Equals(entry.Name, source.Name, StringComparison.CurrentCultureIgnoreCase))
            .Select(entry => entry.Name)
            .ToList();

        var model = MoveSubDialogViewModel.ForNestingTop(_services.UserTags, source.Name, targets);
        if (new Views.MoveSubDialog(model).ShowDialog() != true
            || model is not { Target: { } into, NestPreview: { } preview })
        {
            return;
        }

        // 一覧に無い小分類が商品の側に付いていると、一覧の数だけでは分からない。下見で分かったらここで止める
        if (preview.HasSubs)
        {
            ListNotice.Warn($"「{source.Name}」には小分類が付いた商品があるため、小分類にできませんでした。");
            return;
        }

        var impact = preview.ItemCount == 0
            ? "どの商品にも付いていません。\n"
            : $"{preview.ItemCount} 件の商品を「{into}」の「{source.Name}」に書き換えます。\n";

        if (!Confirm(
                $"「{source.Name}」を「{into}」の小分類にします。\n\n"
                + impact
                + (preview.IsMerge
                    ? $"「{into}」の同じ名前の小分類と統合します。\n" + MemoNotice(source.Memo, $"{into}／{source.Name}")
                    : string.Empty)
                + "この操作は元に戻せません。",
                "別の大分類の小分類にする"))
        {
            return;
        }

        var result = await RewriteTagsAsync(new UiCommand.NestUserTagTop(source.Name, into), "小分類にできませんでした。", ListNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            ListNotice.Show($"「{source.Name}」を「{into}」の小分類にし、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。");
        }

        await ReloadAsync();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, into, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value ?? string.Empty))
            {
                // 打ち直したら前の「既にあります」は古い（消えるだけで、欄の下の場所は空けたまま）
                AddNoticeText = string.Empty;
                RebuildTops();
            }
        }
    }

    /// <summary>
    /// 欄に打った名前の大分類が既にあれば、その行。空白と大文字小文字は問わない（足すときの「既にあります」と同じ照らし方）
    /// </summary>
    private TagTopRow? TypedExisting
    {
        get
        {
            var typed = NameText.Normalize(_filterText);
            return typed.Length == 0
                ? null
                : _allTops.FirstOrDefault(row => string.Equals(row.Name, typed, StringComparison.CurrentCultureIgnoreCase));
        }
    }

    private string _addNoticeText = string.Empty;
    private bool _addNoticeIsWarning = true;

    /// <summary>
    /// 欄のすぐ下の1行。名前が空・長すぎる・既にある（打ち直しが要る）と、足せた（済んだこと）を言う。
    /// 欄の側が1行分の場所を常に取ってあるので、出ても消えても下の一覧は動かない
    /// </summary>
    public string AddNoticeText
    {
        get => _addNoticeText;
        private set => SetField(ref _addNoticeText, value);
    }

    /// <summary>欄の下の知らせが、打ち直しや別の操作の要る物か（要る物は警告の色、済んだことはふつうの色）。</summary>
    public bool AddNoticeIsWarning
    {
        get => _addNoticeIsWarning;
        private set => SetField(ref _addNoticeIsWarning, value);
    }

    private void SayAdd(string text, bool warning)
    {
        AddNoticeIsWarning = warning;
        AddNoticeText = text;
    }

    public string MemoDraft
    {
        get => _memoDraft;
        set
        {
            if (SetField(ref _memoDraft, value))
            {
                OnPropertyChanged(nameof(MemoChanged));

                // 押さずに残す（ユーザ指示 2026-09-18）。選び直しで欄を入れ替えたときは書かない
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
    /// **探して0件と、そもそも1つも無いのを言い分ける**（検索・フォルダ・改変と同じ書き分け）。
    /// </summary>
    public bool IsEmpty => Tops.Count == 0;

    public string EmptyText => HasFilter
        ? "探している言葉に当てはまる大分類がありません。言葉を変えるか、絞り込みを消してください。"
        : "大分類はまだありません。上の欄に名前を入れて追加するか、商品の編集画面でユーザータグを付けると、ここに並びます。";

    public int TopCount => _allTops.Count;

    public string HeaderText => $"大分類 {TopCount} 件";

    /// <summary>
    /// 操作の結果の知らせは、押した所の近くに出す（`notice-placement-2026-10-03.md`）。右の欄の先頭の1行にまとめて出していたのをやめた。
    /// 欄の下（足す欄の誤りと「既にあります」）は AddNoticeText・<see cref="AddSubNotice"/>、
    /// 名前の近くは <see cref="NameNotice"/>、小分類の見出しの近くは <see cref="SubNotice"/>、
    /// 一覧の見出しの近く（行ごと消える操作と、一覧に無いタグの直し）は <see cref="ListNotice"/>。
    /// メモの自動保存の成功は出さない（欄が残るので足りる。設定と同じ）
    /// </summary>
    public AreaNotice NameNotice { get; } = new();

    public AreaNotice SubNotice { get; } = new();

    public AreaNotice ListNotice { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task ReloadAsync()
    {
        await FlushMemosAsync();

        IsBusy = true;
        try
        {
            var master = _services.Store.UserTags.Load();
            var usage = await _services.UserTags.LoadUsageAsync();
            var orphans = await _services.UserTags.LoadOrphansAsync();

            RunOnUiThread(() =>
            {
                _filterMaster = master;

                var counts = usage.ToDictionary(entry => entry.Top, StringComparer.CurrentCultureIgnoreCase);
                var keep = Selected?.Name;

                _allTops = master.Tops.Select(top => new TagTopRow
                {
                    Name = top.Name,
                    Memo = top.Memo,
                    SubCount = top.Subs.Count,
                    ItemCount = counts.TryGetValue(top.Name, out var entry) ? entry.ItemCount : 0,
                }).ToList();

                _subCounts = counts;

                Orphans.Clear();
                foreach (var orphan in orphans)
                {
                    Orphans.Add(CreateOrphanRow(orphan));
                }

                RebuildTops();
                OnPropertyChanged(nameof(TopCount));
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(HasOrphans));

                // 選び直す。改名した直後は名前が変わっているので、無ければ先頭に落とす。
                // 開き直したとき（keep が無いとき）は、前に見ていた大分類に戻る
                Selected = _allTops.FirstOrDefault(row => row.Name == (keep ?? _lastSelectedTop))
                    ?? Tops.FirstOrDefault();
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 探すときに小分類の名前を引く一覧（<c>user-tags.json</c>）。画面の一覧を組んだときに読んだ物を控える。
    /// 前は探す欄の1文字ごとに画面のスレッドでファイルを読み直していた。一覧を変える操作は済んだ後に読み直す（<see cref="ReloadAsync"/>）ので、ここも一緒に新しくなる
    /// </summary>
    private Core.Models.UserTagMaster? _filterMaster;

    private IReadOnlyDictionary<string, UserTagUsage> _subCounts =
        new Dictionary<string, UserTagUsage>(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>
    /// 探すのは大分類・小分類の**名前とメモだけ**。付けた商品の名前では当てない（メモ21-② 2026-10-03：
    /// 商品で当たる道があると、分類の名前を探したつもりが「なぜこの大分類が出たか」が分からなかった）。
    /// 名前以外で当たった行には、何で当たったかを名前の下に出す（<see cref="TagTopRow.MatchReason"/>）
    /// </summary>
    private void RebuildTops()
    {
        // 書き方は検索画面と同じ（ユーザ指示 2026-09-19）。大分類・小分類の名前にも同じ式を当てる
        var filter = ItemTextFilter.Create(_filterText, _main.Search.CreateSearchFacts());
        var master = filter is null ? null : _filterMaster ?? _services.Store.UserTags.Load();

        // 作り直す間は、一覧が書き戻す「選択なし」を受けない。受けると、探すたびに右が空になっていた。
        // 当たりから外れても右は今見ている物のまま残す（見ている物が勝手に消えると、何を探していたか分からなくなる）
        _rebuildingList = true;
        try
        {
            Tops.Clear();
            foreach (var row in _allTops)
            {
                var reason = string.Empty;
                row.MatchReason = string.Empty;
                if (filter is null || MatchesFilter(row, filter, master!, out reason))
                {
                    row.MatchReason = reason;
                    Tops.Add(row);
                }
            }

            MarkSubMatches();
        }
        finally
        {
            _rebuildingList = false;
        }

        var typedExisting = TypedExisting;
        foreach (var row in _allTops)
        {
            row.IsNameMatch = ReferenceEquals(row, typedExisting);
        }

        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(FilterResultText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    private bool MatchesFilter(TagTopRow row, ItemTextFilter filter, UserTagMaster master, out string reason)
    {
        reason = string.Empty;

        // 名前で当たったときは理由を出さない（名前は行に出ている）
        if (filter.MatchesNameOrMemo(row.Name, null))
        {
            return true;
        }

        // 入力中のメモも照らす（保存を待たない）。選んでいる大分類の下書きは、行の Memo にまだ入っていない
        if (filter.MatchesNameOrMemo(row.Name, ReferenceEquals(row, Selected) ? MemoDraft : row.Memo))
        {
            reason = "メモ";
            return true;
        }

        var top = master.Tops.FirstOrDefault(entry =>
            string.Equals(entry.Name, row.Name, StringComparison.CurrentCultureIgnoreCase));

        var hits = top?.Subs.Where(sub => filter.MatchesNameOrMemo(sub.Name, sub.Memo)).ToList() ?? [];
        if (hits.Count == 0)
        {
            return false;
        }

        reason = hits.Count == 1 ? $"小分類「{hits[0].Name}」" : $"小分類「{hits[0].Name}」ほか {hits.Count - 1} 件";
        return true;
    }

    /// <summary>右に開いている大分類の小分類のうち、左の欄の語に当たった物を印す（開いたときに、当たった小分類が分かるように）。</summary>
    private void MarkSubMatches()
    {
        var filter = ItemTextFilter.Create(_filterText, _main.Search.CreateSearchFacts());
        foreach (var sub in Subs)
        {
            sub.IsFilterMatch = filter is not null && filter.MatchesNameOrMemo(sub.Name, sub.MemoDraft);
        }
    }

    private bool _isAddingSub;
    private string _itemFilter = string.Empty;
    private string _newSubText = string.Empty;

    /// <summary>
    /// 小分類を足す欄の文字。大分類・属性を足す欄と同じふつうの欄にした（メモ25 C）：
    /// 今ある小分類を候補に出すと、選んでも「既にあります」で断られるだけだった（今ある名前を並べても重複を誘うだけ）
    /// </summary>
    public string NewSubText
    {
        get => _newSubText;
        set
        {
            if (SetField(ref _newSubText, value ?? string.Empty))
            {
                // 打ち直したら前の「既にあります」は古い
                AddSubNotice.Clear();
            }
        }
    }

    /// <summary>
    /// 小分類を足す欄のすぐ下の1行（大分類の側の <see cref="AddNoticeText"/> と同じ）。欄の側が1行分の場所を常に取ってあるので、出ても消えても下は動かない
    /// </summary>
    public AreaNotice AddSubNotice { get; } = new();

    /// <summary>試験が文と色を読む口（画面は <see cref="AddSubNotice"/> を結ぶ）</summary>
    internal string AddSubNoticeText => AddSubNotice.Text;

    internal bool AddSubNoticeIsWarning => AddSubNotice.IsWarning;

    private void SayAddSub(string text, bool warning) => AddSubNotice.Set(text, warning);

    /// <summary>小分類を足す欄を出しているか（普段は隠す。ユーザ指示 2026-09-18：入力欄が並ぶと読みづらい）。</summary>
    public bool IsAddingSub
    {
        get => _isAddingSub;
        set => SetField(ref _isAddingSub, value);
    }

    /// <summary>
    /// この大分類の中を商品名で探す（ユーザ指示 2026-09-18）。
    /// 当たった商品を持つ小分類だけを開いて出す——どの小分類に入れたか分からない物を辿るため
    /// </summary>
    public string ItemFilter
    {
        get => _itemFilter;
        set
        {
            if (SetField(ref _itemFilter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasItemFilter));
                RebuildSubs();
                OnPropertyChanged(nameof(ItemFilterResultText));
            }
        }
    }

    public bool HasItemFilter => _itemFilter.Trim().Length > 0;

    private int _itemFilterHits;
    private int _subFilterHits;

    public string ItemFilterResultText => HasItemFilter
        ? $"「{_itemFilter.Trim()}」に当たる小分類 {_subFilterHits} 件・商品 {_itemFilterHits} 件"
        : string.Empty;

    /// <summary>絞り込んでいるか。絞っている間は、右の小分類と中の商品も同じ語で絞る。</summary>
    public bool HasFilter => _filterText.Trim().Length > 0;

    public string FilterResultText => HasFilter
        ? $"「{_filterText.Trim()}」に当たる大分類 {Tops.Count} 件"
        : string.Empty;

    public bool HasSubs => Subs.Count > 0;

    /// <summary>
    /// 小分類をまとめて開く・畳む（ユーザ指示 2026-09-19）。1つずつ三角を押して回るのは、小分類の多い大分類で手間。
    /// 開けるのは中身のある小分類だけ（中身の無い物は三角も押せない）。探している間は、出ている物だけが相手
    /// </summary>
    public RelayCommand ToggleAllCommand { get; }

    private IEnumerable<TagSubRow> OpenableSubs => Subs.Where(row => row.IsUsed && !row.IsHidden);

    /// <summary>開ける物が全部開いていれば「すべて畳む」、1つでも畳んでいれば「すべて開く」。</summary>
    public bool AllSubsExpanded => OpenableSubs.Any() && OpenableSubs.All(row => row.IsExpanded);

    public string ToggleAllText => AllSubsExpanded ? "すべて折りたたむ" : "すべて開く";

    private void ToggleAll()
    {
        var expand = !AllSubsExpanded;
        foreach (var row in OpenableSubs.ToList())
        {
            row.IsExpanded = expand;
        }

        OnPropertyChanged(nameof(ToggleAllText));
    }

    private void RebuildSubs()
    {
        Subs.Clear();
        SubNames.Clear();
        if (Selected is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        var master = _services.Store.UserTags.Load();
        var top = master.Tops.FirstOrDefault(entry =>
            string.Equals(entry.Name, Selected.Name, StringComparison.CurrentCultureIgnoreCase));

        if (top is null)
        {
            OnPropertyChanged(nameof(HasSubs));
            return;
        }

        _subCounts.TryGetValue(Selected.Name, out var usage);

        foreach (var sub in top.Subs)
        {
            var row = new TagSubRow
            {
                Name = sub.Name,
                Top = top.Name,
                Memo = sub.Memo,
                ItemCount = usage?.SubCounts.GetValueOrDefault(sub.Name) ?? 0,
                MemoDraft = sub.Memo ?? string.Empty,
            };

            row.MemoEdited += _ =>
            {
                _subMemoPending = true;
                _saveSubMemo.Request();
            };
            row.ExpandRequested += entry => FillSubItemsAsync(entry).Forget();

            // 開く・畳む・隠す・中身が届く のたびに、右の平らな一覧を組み直す（まとめて1回）
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(TagSubRow.IsExpanded) or nameof(TagSubRow.IsHidden))
                {
                    RequestLines();
                }
            };
            row.Items.CollectionChanged += (_, _) => RequestLines();

            // 開いていた小分類は、商品ページから戻ってきたときも開いたままにする（ユーザ指示 2026-09-18）
            var key = SubKey(row);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(TagSubRow.IsExpanded))
                {
                    return;
                }

                if (row.IsExpanded)
                {
                    ExpandedSubs.Add(key);
                }
                else
                {
                    ExpandedSubs.Remove(key);
                }

                // 1つずつ開け閉めしても、「すべて開く／すべて畳む」の言い方を合わせる
                OnPropertyChanged(nameof(ToggleAllText));
            };

            if (row.IsUsed && ExpandedSubs.Contains(key))
            {
                row.IsExpanded = true;
            }

            // 名前の変更は窓で聞く（ユーザ指示 2026-09-18：行に入力欄を常設せず、ボタンから）
            row.RenameCommand = new RelayCommand(() => AskRenameSubAsync(row).Forget());
            row.DeleteCommand = new RelayCommand(() => DeleteSubAsync(row).Forget());
            row.ShowItemsCommand = new RelayCommand(
                () => _main.ShowItemsWithTag(row.Top, row.Name),
                () => row.IsUsed);
            row.MoveCommand = new RelayCommand(() => MoveSubToTopAsync(row).Forget(), () => _allTops.Count > 1);
            row.MoveTargets = _allTops
                .Where(entry => !string.Equals(entry.Name, top.Name, StringComparison.CurrentCultureIgnoreCase))
                .Select(entry => entry.Name)
                .ToList();
            Subs.Add(row);
            SubNames.Add(row.Name);
        }

        // 自分以外を寄せ先の候補にする。自分に改名しても何も起きないので出さない
        foreach (var row in Subs)
        {
            row.OtherNames = SubNames.Where(name => name != row.Name).ToList();
        }

        ApplyItemFilter();
        MarkSubMatches();
        OnPropertyChanged(nameof(HasSubs));
        OnPropertyChanged(nameof(ToggleAllText));
    }

    /// <summary>
    /// 商品名で絞る（ユーザ指示 2026-09-18）。当たった商品を持つ小分類だけを開いて出し、
    /// 中身も当たった商品だけにする。空にしたら元の（覚えている）開き方に戻す
    /// </summary>
    private void ApplyItemFilter()
    {
        // 書き方は検索画面と同じ（ユーザ指示 2026-09-19）
        var filter = ItemTextFilter.Create(_itemFilter, _main.Search.CreateSearchFacts());
        _itemFilterHits = 0;

        if (filter is null)
        {
            foreach (var row in Subs)
            {
                row.IsHidden = false;
                row.Items.Clear();
                row.IsExpanded = row.IsUsed && ExpandedSubs.Contains(SubKey(row));

                if (row.IsExpanded)
                {
                    FillSubItemsAsync(row).Forget();
                }
            }

            return;
        }

        var all = _main.Search.SnapshotItems();
        _subFilterHits = 0;

        foreach (var row in Subs)
        {
            // 小分類の名前で当たったら、中の商品は全部出す（ユーザ指示 2026-09-19：
            // 名前で探した人は、その小分類の中身を見たい。商品名でさらに削ると探した物が隠れる）
            // メモも探す（メモ10-③ 2026-10-02）。入力中の文もそのまま照らす（保存を待たない）
            var nameHit = filter.MatchesNameOrMemo(row.Name, row.MemoDraft);

            var matched = all
                .Where(item => item.Local.UserTags.Any(tag =>
                    string.Equals(tag.Top, row.Top, StringComparison.CurrentCultureIgnoreCase)
                    && tag.Subs.Any(sub => string.Equals(sub, row.Name, StringComparison.CurrentCultureIgnoreCase))))
                .Where(item => nameHit || filter.Matches(item))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            row.Items.Clear();
            foreach (var item in matched)
            {
                row.Items.Add(CreateItemRow(item, ThumbnailPathFor(item)));
            }

            if (nameHit)
            {
                _subFilterHits++;
            }
            else
            {
                _itemFilterHits += matched.Count;
            }

            // 名前で当たった小分類は、中身が無くても出す（使っていない小分類を名前で探すこともある）
            row.IsHidden = !nameHit && matched.Count == 0;
            row.IsExpanded = matched.Count > 0;
        }
    }

    /// <summary>マスタに載っているサブレベル名。寄せ先の候補に使う。</summary>
    private IReadOnlyList<string> SubNamesOf(string top)
        => _services.Store.UserTags.Load().Tops
            .FirstOrDefault(entry => string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase))
            ?.Subs.Select(sub => sub.Name).ToList()
            ?? [];

    private void RebuildOtherNames()
    {
        OtherTopNames.Clear();
        foreach (var name in _allTops
            .Where(row => Selected is null || row.Name != Selected.Name)
            .Select(row => row.Name))
        {
            OtherTopNames.Add(name);
        }
    }

    private OrphanTagRow CreateOrphanRow(OrphanUserTag orphan)
    {
        var row = new OrphanTagRow
        {
            Top = orphan.Top,
            Sub = orphan.Sub,
            ItemCount = orphan.ItemCount,

            // サブの寄せ先は同じトップの中だけ。別のトップのサブへは寄せられない
            MergeCandidates = orphan.Sub is null
                ? _allTops.Select(top => top.Name).ToList()
                : SubNamesOf(orphan.Top),
        };

        row.AddToMasterCommand = new RelayCommand(() => AddOrphanToMasterAsync(row).Forget());
        row.MergeCommand = new RelayCommand(parameter => MergeOrphanAsync(row, parameter as string).Forget());
        row.RemoveCommand = new RelayCommand(() => RemoveOrphanAsync(row).Forget());

        return row;
    }

    private async Task SubmitFilterAsync()
    {
        var existing = TypedExisting;
        if (existing is not null)
        {
            Selected = existing;
            return;
        }

        await AddTopAsync(FilterText);
    }

    private async Task AddTopAsync(string? name)
    {
        // 改行やタブは空白に寄せて1行にする（I13）
        var trimmed = NameText.Normalize(name);

        // 空のまま押したときに黙って終わらない（I1）。押した人は「やった」と思っている
        if (trimmed.Length == 0)
        {
            SayAdd("大分類の名前を入れてから押してください。", warning: true);
            return;
        }

        // 長すぎるものは**切らずに断る**（切ると打った名前と食い違う・I13）
        if (NameText.IsTooLong(trimmed))
        {
            SayAdd(NameText.TooLongMessage("大分類の名前"), warning: true);
            return;
        }

        // 既にある名前は足されない。**足していないのに「追加しました」と言わない**（I2）
        if (_allTops.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            SayAdd($"「{trimmed}」は既にあります。", warning: true);
            Selected = _allTops.FirstOrDefault(row =>
                string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(trimmed));
        // 足せたら欄を空ける（続けて足せるように）。空にすると絞り込みも外れ、足した大分類が一覧に並ぶ
        FilterText = string.Empty;
        await ReloadAsync();
        _main.RefreshMasters();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        // 欄を空けると打ち直しの扱いで知らせが消えるので、空けた後に出す
        SayAdd($"「{trimmed}」を追加しました。", warning: false);
    }

    private async Task AddSubAsync(string? name)
    {
        if (Selected is null)
        {
            return;
        }

        var trimmed = NameText.Normalize(name);
        if (trimmed.Length == 0)
        {
            SayAddSub("小分類の名前を入れてから押してください。", warning: true);
            return;
        }

        if (NameText.IsTooLong(trimmed))
        {
            SayAddSub(NameText.TooLongMessage("小分類の名前"), warning: true);
            return;
        }

        if (Subs.Any(row => string.Equals(row.Name, trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            SayAddSub($"「{trimmed}」は既にあります。", warning: true);
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.AddUserTag(Selected.Name, trimmed));
        var parent = Selected.Name;

        // 足せたら欄を空ける（続けて足せるように）
        NewSubText = string.Empty;
        await ReloadAsync();
        _main.RefreshMasters();

        // 欄を空けると知らせが消えるので、空けた後に出す
        SayAddSub($"「{parent}」に「{trimmed}」を追加しました。", warning: false);
    }

    /// <summary>
    /// 改名。既にある名前を指すと統合になる。どちらも戻せないので、
    /// 何件のitemが書き換わるかを出してから確認を取る。
    /// </summary>
    private async Task RenameTopAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.Ordinal))
        {
            return;
        }

        var merging = _allTops.Any(row =>
            string.Equals(row.Name, target, StringComparison.CurrentCultureIgnoreCase));

        // 統合だけもう一度聞く（D5。理由は小分類の方に書いた）
        if (merging && !Confirm(
                $"「{Selected.Name}」を「{target}」に統合します。\n\n"
                + $"{Selected.ItemCount} 件の商品を書き換えます。小分類は「{target}」側へまとめます。\n"
                + MemoNotice(Selected.Memo, target)
                + "この操作は元に戻せません。",
                "大分類を統合する"))
        {
            return;
        }

        // 名前を変えただけなら名前の近く。統合は片方の行が消えるので、一覧の見出しの近く
        var notice = merging ? ListNotice : NameNotice;
        var result = await RewriteTagsAsync(
            new UiCommand.RenameUserTag(Selected.Name, null, target), "名前を変更できませんでした。", notice);
        string? done = null;
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            done = rewritten.Result.WasMerged
                ? $"「{target}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。"
                : $"「{target}」に変更し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。";
        }

        var keep = target;
        await ReloadAsync();
        Selected = _allTops.FirstOrDefault(row =>
            string.Equals(row.Name, keep, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;

        // 名前が変わると選び直しで名前の近くの知らせが消えるので、選び直した後に出す
        if (done is not null)
        {
            notice.Show(done);
        }

        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 削除。マスタから消すだけだとitem側に参照が残るので、付けていたitemからも外す。
    /// userTagが空になるitemは編集の対象に戻るので、その件数も先に出す。
    /// </summary>
    private async Task DeleteTopAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var message = Selected.ItemCount == 0
            ? $"「{Selected.Name}」を削除します。\n\nどの商品にも付いていません。"
            : $"「{Selected.Name}」を削除します。\n\n"
                + $"{Selected.ItemCount} 件の商品から、この大分類と小分類が外れます。\n"
                + "\nこの操作は元に戻せません。同じ名前で作り直しても、商品への割り当ては戻りません。";

        if (!Confirm(message, "大分類を削除する"))
        {
            return;
        }

        var result = await RewriteTagsAsync(new UiCommand.DeleteUserTag(Selected.Name), "削除できませんでした。", ListNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            ListNotice.Show(rewritten.Result.ItemsLeftUntagged > 0
                ? $"削除し、{rewritten.Result.ItemsUpdated} 件の商品から外しました。"
                    + $"{rewritten.Result.ItemsLeftUntagged} 件はユーザータグが空になり、未編集に戻りました。"
                : $"削除し、{rewritten.Result.ItemsUpdated} 件の商品から外しました。");
        }

        Selected = null;
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>名前を変える窓を出してから実行する。既にある名前を選ぶと統合になる。</summary>
    private async Task AskRenameSubAsync(TagSubRow row)
    {
        var model = new RenameTagDialogViewModel("小分類", row.Name, row.ItemCount, row.OtherNames);
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameSubAsync(row, model.Target);
    }

    /// <summary>大分類の名前を変える窓。</summary>
    private async Task AskRenameTopAsync()
    {
        if (Selected is null)
        {
            return;
        }

        var others = _allTops.Where(row => row.Name != Selected.Name).Select(row => row.Name).ToList();
        var model = new RenameTagDialogViewModel("大分類", Selected.Name, Selected.ItemCount, others);
        if (new Views.RenameTagDialog(model).ShowDialog() != true)
        {
            return;
        }

        await RenameTopAsync(model.Target);
    }

    private async Task RenameSubAsync(TagSubRow row, string? newName)
    {
        var target = newName?.Trim();
        if (string.IsNullOrEmpty(target)
            || string.Equals(target, row.Name, StringComparison.Ordinal))
        {
            return;
        }

        // **統合だけもう一度聞く**（ユーザ判断 2026-09-20・D5）。統合は元に戻せないが、
        // 名前の変更は同じ手順で戻せる。戻せるものまで2回聞くと、聞かれること自体が読まれなくなる
        var merging = Subs.Any(entry => string.Equals(entry.Name, target, StringComparison.CurrentCultureIgnoreCase));
        if (merging && !Confirm(
                $"「{row.Top}」の「{row.Name}」を「{target}」に統合します。\n\n"
                + $"{row.ItemCount} 件の商品を書き換えます。\n"
                + MemoNotice(row.Memo, target)
                + "\nこの操作は元に戻せません。",
                "小分類を統合する"))
        {
            return;
        }

        var result = await RewriteTagsAsync(new UiCommand.RenameUserTag(row.Top, row.Name, target), "名前を変更できませんでした。", SubNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            SubNotice.Show(merging
                ? $"「{target}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。"
                : $"「{target}」に変更し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private async Task DeleteSubAsync(TagSubRow row)
    {
        var message = row.ItemCount == 0
            ? $"「{row.Top}」から「{row.Name}」を削除します。\n\nどの商品にも付いていません。"
            : $"「{row.Top}」から「{row.Name}」を削除します。\n\n"
                + $"{row.ItemCount} 件の商品からこの小分類が外れます。「{row.Top}」自体は付いたままです。\n"
                + "\nこの操作は元に戻せません。同じ名前で作り直しても、商品への割り当ては戻りません。";

        if (!Confirm(message, "小分類を削除する"))
        {
            return;
        }

        var result = await RewriteTagsAsync(new UiCommand.DeleteUserTag(row.Top, row.Name), "削除できませんでした。", SubNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            SubNotice.Show($"「{row.Name}」を削除し、{rewritten.Result.ItemsUpdated} 件の商品から外しました。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 改名・削除の命令を送り、できなかったら呼び手の渡した出し先（<paramref name="failureNotice"/>）に出す。
    /// 前は画面の1行に一律で出していたので、押した所から離れた所に失敗が出ていた。
    ///
    /// 命令は書けなかった例外（ファイルを掴まれた・ドライブが外れた）をそのまま投げ、入口は Forget() でログに残すだけなので、
    /// 前は押しても何も起きなかったように見えた。「できなかった」の結果も受けずに捨てていた。
    /// 例外でも読み直しは続ける——途中まで書き換えた商品があり得るので、今の数を見せる
    /// </summary>
    private async Task<CommandResult?> RewriteTagsAsync(UiCommand command, string failedText, AreaNotice failureNotice)
    {
        try
        {
            var result = await _services.Commands.ExecuteAsync(command);
            if (result is CommandResult.Failed failed)
            {
                failureNotice.Warn(failed.Message);
            }

            return result;
        }
        catch (Exception exception)
        {
            Core.Diagnostics.AppLog.Error("ユーザータグの書き換え", exception);
            failureNotice.Warn(failedText + Core.Services.FailureText.Cause(exception));
            return null;
        }
    }

    /// <summary>
    /// トップレベルを並べ替える。並びは検索の絞り込みにも編集の候補にもそのまま出るので、
    /// 「よく使う順」に置けること自体が機能になる。itemは名前で参照しているので触らない。
    ///
    /// 絞り込み中は見えている分しか動かせないため、隠れている行の位置は保つ。
    /// </summary>
    /// <summary>
    /// ドラッグで置き換える。**「候補の並べ替え」のときだけ**動かす（メモ10-⑤ 2026-10-02）。
    /// 名前順・件数順のままだと次の読み直しで元に戻り「動かなかった」ように見えるが、
    /// 並べ方を勝手に切り替えるのも望まれなかったので、つかみを出さず・落としても動かさない
    /// </summary>
    public async Task MoveTopAsync(TagTopRow moved, TagTopRow target, bool after)
    {
        if (!SortsManually)
        {
            return;
        }

        var order = _allTops.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(order));
        await ReloadAsync();

        // 並びは検索の絞り込みにもそのまま出るので、そちらも作り直す
        _main.RefreshMasters();
    }

    public async Task MoveSubAsync(TagSubRow moved, TagSubRow target, bool after)
    {
        if (!SortsManually)
        {
            return;
        }

        var order = Subs.Select(row => row.Name).ToList();
        if (!Reorder(order, moved.Name, target.Name, after))
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.ReorderUserTags(order, moved.Top));
        await ReloadAsync();
        _main.RefreshMasters();
    }

    /// <summary>抜いてから差し込む。落とす先の index は抜いた後で数え直す。</summary>
    private static bool Reorder(List<string> order, string moved, string target, bool after)
    {
        var from = order.IndexOf(moved);
        if (from < 0 || moved == target)
        {
            return false;
        }

        order.RemoveAt(from);

        var at = order.IndexOf(target);
        if (at < 0)
        {
            order.Insert(from, moved);
            return false;
        }

        var to = after ? at + 1 : at;
        if (to == from)
        {
            order.Insert(from, moved);
            return false;
        }

        order.Insert(to, moved);
        return true;
    }

    /// <summary>
    /// サブレベルを別のトップへ移す。削除して付け直すとitemの割当てが失われるので、
    /// 専用の操作にしてある。
    ///
    /// 滅多に使わない操作なので入力欄は常設せず、ここでダイアログを開いて
    /// 移動先と「サブが無くなった元のトップをどうするか」をまとめて決めてもらう。
    /// </summary>
    private async Task MoveSubToTopAsync(TagSubRow row)
    {
        var dialog = new Views.MoveSubDialog(
            new MoveSubDialogViewModel(_services.UserTags, row.Top, row.Name, row.MoveTargets));

        if (dialog.ShowDialog() != true
            || dialog.DataContext is not MoveSubDialogViewModel { Target: { } to })
        {
            return;
        }

        var drop = ((MoveSubDialogViewModel)dialog.DataContext).DropEmptySourceTop;

        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.MoveUserTagSub(row.Top, row.Name, to, drop));

        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            var parts = new List<string> { $"{rewritten.Result.ItemsUpdated} 件の商品を書き換え" };

            if (rewritten.Result.ItemsGainedTop > 0)
            {
                parts.Add($"うち {rewritten.Result.ItemsGainedTop} 件に「{to}」が新しく付きました");
            }

            if (rewritten.Result.ItemsSourceTopRemoved > 0)
            {
                parts.Add($"{rewritten.Result.ItemsSourceTopRemoved} 件から「{row.Top}」を外しました");
            }

            SubNotice.Show($"「{to}」の下へ移しました（{string.Join("、", parts)}）。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    private bool _memoPending;
    private bool _subMemoPending;

    /// <summary>
    /// 待っているメモを今書く。選び直す・読み直す前に呼ぶ——800ms の待ちの間に移ると、
    /// 待ちが明けたときには欄も小分類の行も移った先の物になっていて、打った文が消えていた
    /// </summary>
    private void FlushMemos() => FlushMemosAsync().Forget();

    /// <summary>
    /// 書く値は最初の await より前に控えるので、待たずに選び直しても控えた方が書かれる。
    /// 読み直すときは、古いファイルを読まないよう書き終わるまで待つ
    /// </summary>
    private async Task FlushMemosAsync()
    {
        var top = _memoPending ? SaveMemoAsync() : Task.CompletedTask;
        var subs = _subMemoPending ? SaveSubMemosAsync() : Task.CompletedTask;
        _saveMemo.Cancel();
        _saveSubMemo.Cancel();
        await Task.WhenAll(top, subs);
    }

    private async Task SaveMemoAsync()
    {
        _memoPending = false;
        if (Selected is not { } row)
        {
            return;
        }

        // 読み直すと打っている途中の欄が戻るので、ここでは読み直さない（ショップ・アバターと同じ）。
        // 代わりに行の値を書き換えて、選び直したときに古いメモが出ないようにする
        var memo = MemoDraft;
        row.Memo = memo;
        await _services.Commands.ExecuteAsync(new UiCommand.SetUserTagMemo(row.Name, null, memo));
    }

    /// <summary>小分類のメモを保存する。打つたびではなく、止まってから変わったものだけ書く。</summary>
    private async Task SaveSubMemosAsync()
    {
        _subMemoPending = false;

        // 変わった行と文を先に控える。書く間に選び直されて行が入れ替わっても、控えた方を書く
        var changed = Subs
            .Where(row => row.MemoDraft != (row.Memo ?? string.Empty))
            .Select(row => (Row: row, Memo: row.MemoDraft))
            .ToList();

        foreach (var (row, memo) in changed)
        {
            row.Memo = memo;
            await _services.Commands.ExecuteAsync(new UiCommand.SetUserTagMemo(row.Top, row.Name, memo));
        }

    }

    /// <summary>
    /// 小分類の中身（商品）を詰める。開いたときだけ読む——全部の小分類で先に読むと、
    /// 商品のJSONを何度も読み直すことになる
    /// </summary>
    private async Task FillSubItemsAsync(TagSubRow row)
    {
        if (row.Items.Count > 0)
        {
            return;
        }

        var items = _main.Search.SnapshotItems()
            .Where(item => item.Local.UserTags.Any(tag =>
                string.Equals(tag.Top, row.Top, StringComparison.CurrentCultureIgnoreCase)
                && tag.Subs.Any(sub => string.Equals(sub, row.Name, StringComparison.CurrentCultureIgnoreCase))))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToList();

        // 行ごとの1枚目の場所は裏で引く（画像のフォルダを見るので。小分類を開くたびに、中の商品の数だけ画面のスレッドで見ていた）
        var paths = await Task.Run(() => ModificationRowBuilder.ThumbnailPathsOf(_services, _main.Thumbnails, items));

        RunOnUiThread(() =>
        {
            foreach (var (itemId, path) in paths)
            {
                _thumbnailPaths[itemId] = path;
            }

            // 待つ間に開き直して中身が入っていれば、二重に足さない
            if (row.Items.Count > 0)
            {
                return;
            }

            foreach (var item in items)
            {
                row.Items.Add(CreateItemRow(item, paths.GetValueOrDefault(item.Id)));
            }
        });
    }

    /// <summary>
    /// 商品の1枚目の場所。一度引いたら画面の間は覚える（小分類の中を探す欄は1文字ごとに行を組み直し、
    /// そのたびに中の商品の数だけ画像のフォルダを見ていた）。この画面は開くたびに作り直すので、古い場所は残らない
    /// </summary>
    private string? ThumbnailPathFor(ItemRecord item)
    {
        if (!_thumbnailPaths.TryGetValue(item.Id, out var path))
        {
            path = new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, ItemRecord>()).ItemThumbnailPath(item);
            _thumbnailPaths[item.Id] = path;
        }

        return path;
    }

    private readonly Dictionary<string, string?> _thumbnailPaths = new(StringComparer.Ordinal);

    /// <summary>作った商品の行。画像が届いたときに引き直す先（弱く持つ。探す欄は1文字ごとに行を作り直すので、強く持つと溜まり続ける）。</summary>
    private readonly ItemRowRegistry<TagItemRow> _itemRows = new(row => row.ItemId);

    void IItemImagesListener.NoteItemImagesSaved(string itemId)
    {
        var rows = _itemRows.Find(itemId);
        if (rows.Count == 0)
        {
            return;
        }

        var path = _main.Search.FindItem(itemId) is { } item
            ? new ModificationRowBuilder(_services, _main.Thumbnails, new Dictionary<string, ItemRecord>()).ItemThumbnailPath(item)
            : null;
        _thumbnailPaths[itemId] = path;
        foreach (var row in rows)
        {
            row.RefreshImages(path);
        }
    }

    /// <summary>小分類の中に出す商品1件。絵の引き方は改変の一覧と同じものを使う（場所は呼び手が引いておく）。</summary>
    private TagItemRow CreateItemRow(ItemRecord item, string? thumbnailPath)
    {
        var entry = new TagItemRow
        {
            ItemId = item.Id,
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            ThumbnailPath = thumbnailPath,
            Thumbnails = _main.Thumbnails,
            CardFactory = () => _main.Search.CardFor(item.Id),
        };

        _itemRows.Add(entry);
        entry.OpenCommand = new RelayCommand(() => _main.ShowItem(item));
        return entry;
    }

    // ---- カードの操作（検索画面と同じ・IItemCardHost） ----
    // 小分類の中のカードは、枠の Tag にこの画面が入る。受け先が無いと、右クリックのメニューは出るのに押しても何も起きなかった。
    // 中身は検索画面の物をそのまま借りる（ショップ・フォルダビュー・改変と同じ形）

    public void OpenItem(ItemCardViewModel card) => _main.ShowItem(card.Item);

    public void OpenBooth(ItemCardViewModel? card) => _main.Search.OpenBooth(card);

    public Task ToggleFavoriteAsync(ItemCardViewModel card) => _main.Search.ToggleFavoriteAsync(card);

    public RelayCommand OpenBoothCommand => _main.Search.OpenBoothCommand;

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand RevealCommand => _main.Search.RevealCommand;

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;

    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;

    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;


    /// <summary>右クリックの「改変に追加…」。選びはこの画面に無いので、押した1件だけ（検索の画面と同じ命令）。</summary>

    public RelayCommand CardAddToModificationCommand => _main.Search.CardAddToModificationCommand;

    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    private RelayCommand? _hideItem;

    /// <summary>
    /// 非表示にする。書くのは検索画面と同じ命令で、検索の写しを読み直し終えてからこの画面を組み直す
    /// （小分類の中の商品は検索の写しから引くので、先に組み直すと古い商品のまま残る）
    /// </summary>
    public RelayCommand HideItemCommand => _hideItem ??= new RelayCommand(parameter => HideItemAsync(parameter as ItemCardViewModel).Forget());

    private async Task HideItemAsync(ItemCardViewModel? card)
    {
        await _main.Search.HideItemAsync(card);
        await ReloadAsync();
    }

    /// <summary>参照だけ残っている名前を、そのままマスタへ作る。名前が正しかった場合の直し方。</summary>
    private async Task AddOrphanToMasterAsync(OrphanTagRow row)
    {
        await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.AddUserTag(row.Top, row.Sub)
            : new UiCommand.AddUserTag(row.Top));

        ListNotice.Show($"「{row.DisplayName}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに表示されるようになります。");
        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>名前が変わっていた場合の直し方。既存の分類へ寄せる。</summary>
    private async Task MergeOrphanAsync(OrphanTagRow row, string? target)
    {
        var name = target?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!Confirm(
            $"「{row.DisplayName}」を「{name}」に統合します。\n\n{row.ItemCount} 件の商品を書き換えます。",
            "ユーザータグを統合する"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.RenameUserTag(row.Top, row.Sub, name)
            : new UiCommand.RenameUserTag(row.Top, null, name));

        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            ListNotice.Show($"「{name}」に統合し、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>もう使わない名前だった場合の直し方。itemから外す。</summary>
    private async Task RemoveOrphanAsync(OrphanTagRow row)
    {
        var notice = row.IsSub
            ? $"「{row.Top}」自体は付いたままです。"
            : string.Empty;

        if (!Confirm(
            $"「{row.DisplayName}」を {row.ItemCount} 件の商品から外します。\n\n{notice}この操作は元に戻せません。",
            "参照を外す"))
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(row.IsSub
            ? new UiCommand.DeleteUserTag(row.Top, row.Sub)
            : new UiCommand.DeleteUserTag(row.Top));
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            ListNotice.Show(rewritten.Result.ItemsLeftUntagged > 0
                ? $"「{row.DisplayName}」を {rewritten.Result.ItemsUpdated} 件の商品から外しました。"
                    + $"{rewritten.Result.ItemsLeftUntagged} 件はユーザータグが空になり、未編集に戻りました。"
                : $"「{row.DisplayName}」を {rewritten.Result.ItemsUpdated} 件の商品から外しました。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 統合でメモがどうなるかを一行で伝える。黙って寄せ先に書き足すと、
    /// 後から読んだときに出所が分からないメモが増えることになる。
    /// </summary>
    private static string MemoNotice(string? memo, string target)
        => string.IsNullOrWhiteSpace(memo)
            ? string.Empty
            : $"メモは「{target}」側に「「元の名前」から統合：…」として追記します。\n";

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
