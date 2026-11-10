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
public sealed class TagTopRow : ManageMasterRow
{
    public required int SubCount { get; init; }

    // 画面の言葉は「大分類／小分類」に揃える（ユーザ指示 2026-09-19：「サブ」「トップ」と混ざっていた）
    public string SubCountText => SubCount == 0 ? "小分類なし" : $"小分類 {SubCount} 件";

    public string ItemCountText => ItemCount == 0 ? "未使用" : $"{ItemCount}";
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
/// タグの管理画面。userTagマスタ（トップ／サブの2階層）を編集する。属性の管理と共通の所は
/// <see cref="ManageViewModelBase{TRow, TOrphan}"/> が持ち、ここには小分類の段（行・中身・移す・入れる）を置く。
///
/// item側は名前で参照しているので、改名も削除も全itemの書き換えを伴う。
/// 戻せない操作なので、実行前に「何件が書き換わるか」を必ず数えて見せる。
/// マスタに無い名前をitemが参照したままの状態もここに出す。
/// 要確認はそれを知らせるだけで、直せる場所はこの画面しかないため。
/// </summary>
public sealed class TagManageViewModel : ManageViewModelBase<TagTopRow, OrphanTagRow>
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    private readonly Dictionary<TagSubRow, TagSubFootLine> _subFeet = new(ReferenceEqualityComparer.Instance);
    private bool _linesPending;

    /// <summary>
    /// 右側の平らな一覧（<see cref="ManageViewModelBase{TRow, TOrphan}.Lines"/>）の間には、畳んだ小分類のまとまり（<see cref="TagSubRunLine"/>）と、
    /// 開いた小分類（<see cref="TagSubRow"/>・商品の段・<see cref="TagSubFootLine"/>）を挟む。
    /// 小分類の中の商品を WrapPanel に全部並べていた頃は、すべて開くとカード約560枚を全部作り、メモリが約300MB増えた（2026-09-24）。
    ///
    /// 組み直しを1回にまとめる。「すべて開く」は小分類の数だけ、開いた小分類の中身は商品の数だけ知らせが続くので、
    /// そのたびに組むと同じ組み直しを何百回もすることになる
    /// </summary>
    protected override void RequestLines()
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
        var target = new List<object> { this };

        if (HasSelection)
        {
            var card = IsCardMode;
            var columns = ManageItemLayout.ColumnsFor(ItemsWidth, card ? Services.CardMetrics.SlotWidth : ManageItemLayout.ListSlotWidth);
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
                if (ItemsWidth > 0)
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

        target.Add(Foot);
        CollectionSync.Apply(Lines, target);
    }

    /// <summary>
    /// 選んでいた大分類と、開いていた小分類を覚える（ユーザ指示 2026-09-18：商品ページから戻ると
    /// 先頭に戻ってしまい、どこを見ていたか分からなくなる）。画面は開くたびに作り直すので、型の側で持つ。
    /// アプリを閉じるまでの記憶でよい（次の起動で開き直すほどの話ではない）
    /// </summary>
    private static string? _lastSelectedTop;

    private static readonly HashSet<string> ExpandedSubs = new(StringComparer.Ordinal);

    protected override string? LastSelectedName
    {
        get => _lastSelectedTop;
        set => _lastSelectedTop = value;
    }

    /// <summary>大分類と小分類の組を1つの鍵にする。名前には入らない改行で区切る。</summary>
    private static string SubKey(TagSubRow row) => row.Top + "\n" + row.Name;

    private bool _isBusy;

    public TagManageViewModel(AppServiceContainer services, MainViewModel main)
        : base(services, main, "tag", "tags.list", ParseSort(services.UiState.TagSort, TagSortMode.Name))
    {
        _services = services;
        _main = main;

        // 小分類の入れ替え（大分類を選び直す・読み直す）で、右の平らな一覧を組み直す。頭と下の枠は読み終わる前から出しておく
        Subs.CollectionChanged += (_, _) => RequestLines();
        RequestLines();

        AddSubCommand = new RelayCommand(() => AddSubAsync(NewSubText).Forget(), () => Selected is not null);
        NestTopCommand = new RelayCommand(() => NestTopAsync().Forget());
        ToggleAllCommand = new RelayCommand(ToggleAll);

        // 大分類のメモは土台の部品（MemoAutoSave）、小分類のメモは行ごとに打ちかけを持つのでこちらで待つ
        _saveSubMemo = new Debounced(TimeSpan.FromMilliseconds(800), SaveSubMemosAsync);

        ReloadAsync().Forget();
    }

    private readonly Debounced _saveSubMemo;

    /// <summary>待っているメモを今書く（画面を離れる前・閉じる前）。</summary>
    public override Task FlushPendingWritesAsync() => Task.WhenAll(MemoSaver.FlushAsync(), _saveSubMemo.RunNowAsync());

    protected override string KindName => "大分類";

    /// <summary>`userTags.json` の並べ方を覚える（ユーザ指示 2026-09-18。既定は名前順）。</summary>
    protected override void SaveSort(string saved)
        => _main.SaveUiStateAsync(state => state with { TagSort = saved }).Forget();

    protected override async Task ReorderBySortAsync()
    {
        var tops = SortNames(AllRows.Select(row => (row.Name, row.ItemCount)));
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
    }

    protected override UiCommand AddCommandFor(string name) => new UiCommand.AddUserTag(name);

    protected override UiCommand MemoCommandFor(TagTopRow row, string memo) => new UiCommand.SetUserTagMemo(row.Name, null, memo);

    protected override UiCommand ReorderCommandFor(IReadOnlyList<string> order) => new UiCommand.ReorderUserTags(order);

    protected override string RewriteLogLabel => "ユーザータグの書き換え";

    protected override void ShowItemsOf(string name) => _main.ShowItemsWithTag(name);

    /// <summary>左の一覧の大分類。</summary>
    public ObservableCollection<TagTopRow> Tops => VisibleRows;

    public ObservableCollection<TagSubRow> Subs { get; } = [];

    /// <summary>サブレベルの改名の寄せ先候補。同じトップの中だけを候補にする。</summary>
    public ObservableCollection<string> SubNames { get; } = [];

    /// <summary>欄の右の「追加」（土台の <see cref="ManageViewModelBase{TRow, TOrphan}.AddCommand"/>。画面はこの名前で結ぶ）。</summary>
    public RelayCommand AddTopCommand => AddCommand;

    public RelayCommand AddSubCommand { get; }

    /// <summary>大分類の名前を変える窓（土台の <see cref="ManageViewModelBase{TRow, TOrphan}.AskRenameCommand"/>）。</summary>
    public RelayCommand RenameTopCommand => AskRenameCommand;

    /// <summary>大分類を消す（土台の <see cref="ManageViewModelBase{TRow, TOrphan}.DeleteCommand"/>）。</summary>
    public RelayCommand DeleteTopCommand => DeleteCommand;

    /// <summary>大分類を別の大分類の小分類にする。押せるかは <see cref="CanNestTop"/> を見た目（IsEnabled）で出す（wpf.md：CanExecute は止まることがある）</summary>
    public RelayCommand NestTopCommand { get; }

    protected override void ClearNoticesOfPrevious() => SubNotice.Clear();

    protected override void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(CanNestTop));
        OnPropertyChanged(nameof(NestTopToolTip));
        RebuildSubs();
        AddSubNotice.Clear();
    }

    public override string SelectedUsageText => Selected is null
        ? string.Empty
        : Selected.ItemCount == 0
            ? "まだどの商品にも付いていません。編集画面で付けると、ここに件数が表示されます。"
            : $"{Selected.ItemCount} 件の商品に付いています";

    public override string ShowItemsToolTip => SelectedIsUsed
        ? "この大分類が付いている商品を、検索で開きます。"
        : "この大分類はまだどの商品にも付いていません。編集画面で付けると開けます。";

    /// <summary>
    /// 大分類を別の大分類の小分類にできるか。小分類を持つ大分類ではできない（ユーザ要望 2026-09-29：
    /// 小分類の下にもう1段は作れず、持っている小分類の行き場が無い）。入れ先が無い（大分類が1つだけ）ときもできない。
    /// ボタンは消さずに押せない形で置き、理由を吹き出しで言う（タグの管理の決め事）
    /// </summary>
    public bool CanNestTop => Selected is { SubCount: 0 } && AllRows.Count > 1;

    public string NestTopToolTip => Selected is { SubCount: > 0 }
        ? "小分類がある大分類は、小分類にできません。"
        : AllRows.Count > 1
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

        var targets = AllRows
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

        var result = await RewriteAsync(new UiCommand.NestUserTagTop(source.Name, into), "小分類にできませんでした。", ListNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            ListNotice.Show($"「{source.Name}」を「{into}」の小分類にし、{rewritten.Result.ItemsUpdated} 件の商品を書き換えました。");
        }

        await ReloadAsync();
        Selected = AllRows.FirstOrDefault(row =>
            string.Equals(row.Name, into, StringComparison.CurrentCultureIgnoreCase)) ?? Selected;
        _main.RefreshMasters();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// 右は小分類の行を作り直さず、今の行に絞り込みを当て直すだけにする——作り直すと、保存を待っている（0.8秒）小分類のメモの入力が捨てられる（点検29）
    /// </summary>
    protected override void RefilterItemsForSearchFacts()
    {
        ApplyItemFilter();
        MarkSubMatches();
    }

    /// <summary>
    /// 探すときに小分類の名前を引く一覧（<c>user-tags.json</c>）。画面の一覧を組んだときに読んだ物を控える。
    /// 前は探す欄の1文字ごとに画面のスレッドでファイルを読み直していた。一覧を変える操作は済んだ後に読み直す（<see cref="ReloadAsync"/>）ので、ここも一緒に新しくなる
    /// </summary>
    private Core.Models.UserTagMaster? _filterMaster;

    private IReadOnlyDictionary<string, UserTagUsage> _subCounts =
        new Dictionary<string, UserTagUsage>(StringComparer.CurrentCultureIgnoreCase);

    /// <summary>**探して0件と、そもそも1つも無いのを言い分ける**（検索・フォルダ・改変と同じ書き分け）。</summary>
    public override string EmptyText => HasFilter
        ? "探している言葉に当てはまる大分類がありません。言葉を変えるか、絞り込みを消してください。"
        : "大分類はまだありません。上の欄に名前を入れて追加するか、商品の編集画面でユーザータグを付けると、ここに並びます。";

    public int TopCount => AllRows.Count;

    /// <summary>小分類の見出しの近くの知らせ（ほかの出し先は土台の <see cref="ManageViewModelBase{TRow, TOrphan}.NameNotice"/> など）。</summary>
    public AreaNotice SubNotice { get; } = new();

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

    public override async Task ReloadAsync()
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

                var rows = master.Tops.Select(top => new TagTopRow
                {
                    Name = top.Name,
                    Memo = top.Memo,
                    SubCount = top.Subs.Count,
                    ItemCount = counts.TryGetValue(top.Name, out var entry) ? entry.ItemCount : 0,
                }).ToList();

                _subCounts = counts;

                ShowLoaded(rows, () => orphans.Select(CreateOrphanRow));
                OnPropertyChanged(nameof(TopCount));
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 探すのは大分類・小分類の**名前とメモだけ**。付けた商品の名前では当てない（メモ21-② 2026-10-03：
    /// 商品で当たる道があると、分類の名前を探したつもりが「なぜこの大分類が出たか」が分からなかった）。
    /// 名前以外で当たった行には、何で当たったかを名前の下に出す（<see cref="ReorderableRow.MatchReason"/>）。
    /// 大分類・小分類の名前にも検索画面と同じ式を当てる
    /// </summary>
    protected override Func<TagTopRow, string?> CreateMatcher(ItemTextFilter filter)
    {
        var master = _filterMaster ?? _services.Store.UserTags.Load();
        return row => MatchesFilter(row, filter, master, out var reason) ? reason : null;
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

    protected override void OnListFiltered() => MarkSubMatches();

    /// <summary>右に開いている大分類の小分類のうち、左の欄の語に当たった物を印す（開いたときに、当たった小分類が分かるように）。</summary>
    private void MarkSubMatches()
    {
        var filter = ItemTextFilter.Create(FilterText, _main.Search.CreateSearchFacts());
        foreach (var sub in Subs)
        {
            sub.IsFilterMatch = filter is not null && filter.MatchesNameOrMemo(sub.Name, sub.MemoDraft);
        }
    }

    private bool _isAddingSub;
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
    /// 小分類を足す欄のすぐ下の1行（大分類の側の <see cref="ManageViewModelBase{TRow, TOrphan}.AddNoticeText"/> と同じ）。欄の側が1行分の場所を常に取ってあるので、出ても消えても下は動かない
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
    /// この大分類の中を商品名で探す（ユーザ指示 2026-09-18。<see cref="ManageViewModelBase{TRow, TOrphan}.ItemFilter"/>）。
    /// 当たった商品を持つ小分類だけを開いて出す——どの小分類に入れたか分からない物を辿るため
    /// </summary>
    protected override void OnItemFilterChanged()
    {
        RebuildSubs();
        OnPropertyChanged(nameof(ItemFilterResultText));
    }

    private int _itemFilterHits;
    private int _subFilterHits;

    public override string ItemFilterResultText => HasItemFilter
        ? $"「{ItemFilter.Trim()}」に当たる小分類 {_subFilterHits} 件・商品 {_itemFilterHits} 件"
        : string.Empty;

    public bool HasSubs => Subs.Count > 0;

    /// <summary>
    /// 小分類をまとめて開く・畳む（ユーザ指示 2026-09-19）。1つずつ三角を押して回るのは、小分類の多い大分類で手間。
    /// 開けるのは中身のある小分類だけ（中身の無い物は三角も押せない）。探している間は、出ている物だけが相手
    /// </summary>
    public RelayCommand ToggleAllCommand { get; }

    private IEnumerable<TagSubRow> OpenableSubs => Subs.Where(row => row.IsUsed && !row.IsHidden);

    /// <summary>開ける物が全部開いていれば「すべて畳む」、1つでも畳んでいれば「すべて広げる」（「開く」は「Unityを開く」と取り違える。ユーザ判断 2026-11-03）。</summary>
    public bool AllSubsExpanded => OpenableSubs.Any() && OpenableSubs.All(row => row.IsExpanded);

    public string ToggleAllText => AllSubsExpanded ? "すべて折りたたむ" : "すべて広げる";

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

                // 1つずつ開け閉めしても、「すべて広げる／すべて折りたたむ」の言い方を合わせる
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
            row.MoveCommand = new RelayCommand(() => MoveSubToTopAsync(row).Forget(), () => AllRows.Count > 1);
            row.MoveTargets = AllRows
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
        var filter = ItemTextFilter.Create(ItemFilter, _main.Search.CreateSearchFacts());
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

    private OrphanTagRow CreateOrphanRow(OrphanUserTag orphan)
    {
        var row = new OrphanTagRow
        {
            Top = orphan.Top,
            Sub = orphan.Sub,
            ItemCount = orphan.ItemCount,

            // サブの寄せ先は同じトップの中だけ。別のトップのサブへは寄せられない
            MergeCandidates = orphan.Sub is null
                ? AllRows.Select(top => top.Name).ToList()
                : SubNamesOf(orphan.Top),
        };

        row.AddToMasterCommand = new RelayCommand(() => AddOrphanToMasterAsync(row).Forget());
        row.MergeCommand = new RelayCommand(parameter => MergeOrphanAsync(row, parameter as string).Forget());
        row.RemoveCommand = new RelayCommand(() => RemoveOrphanAsync(row).Forget());

        return row;
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
    internal override async Task RenameAsync(string? newName)
    {
        var target = newName?.Trim();
        if (Selected is null || string.IsNullOrEmpty(target)
            || string.Equals(target, Selected.Name, StringComparison.Ordinal))
        {
            return;
        }

        var merging = AllRows.Any(row =>
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
        var result = await RewriteAsync(
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
        Selected = AllRows.FirstOrDefault(row =>
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
    protected override async Task DeleteSelectedAsync()
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

        var result = await RewriteAsync(new UiCommand.DeleteUserTag(Selected.Name), "削除できませんでした。", ListNotice);
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

        var result = await RewriteAsync(new UiCommand.RenameUserTag(row.Top, row.Name, target), "名前を変更できませんでした。", SubNotice);
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

        var result = await RewriteAsync(new UiCommand.DeleteUserTag(row.Top, row.Name), "削除できませんでした。", SubNotice);
        if (result is CommandResult.UserTagsRewritten rewritten)
        {
            SubNotice.Show($"「{row.Name}」を削除し、{rewritten.Result.ItemsUpdated} 件の商品から外しました。");
        }

        await ReloadAsync();
        await _main.ReloadLibraryAsync();
    }

    /// <summary>
    /// トップレベルを並べ替える（ドラッグ。中身は土台の <see cref="ManageViewModelBase{TRow, TOrphan}.MoveAsync"/>）。
    /// itemは名前で参照しているので触らない
    /// </summary>
    public Task MoveTopAsync(TagTopRow moved, TagTopRow target, bool after) => MoveAsync(moved, target, after);

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

        // できなかったときは押した小分類の段の下に出す（ほかの書き換えと同じ道。前は何も出なかった）
        var result = await RewriteAsync(
            new UiCommand.MoveUserTagSub(row.Top, row.Name, to, drop), "移せませんでした。", SubNotice);

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

    private bool _subMemoPending;

    /// <summary>
    /// 待っているメモを今書く。選び直す・読み直す前に呼ぶ——800ms の待ちの間に移ると、
    /// 待ちが明けたときには欄も小分類の行も移った先の物になっていて、打った文が消えていた
    /// </summary>
    protected override void FlushMemos() => FlushMemosAsync().Forget();

    /// <summary>
    /// 書く値は最初の await より前に控えるので、待たずに選び直しても控えた方が書かれる。
    /// 読み直すときは、古いファイルを読まないよう書き終わるまで待つ。
    /// 小分類のメモは行ごとに打ちかけを持ち、変わった行を全部書く作りなので、大分類の部品（<see cref="MemoAutoSave{TOwner}"/>）とは別に待つ
    /// </summary>
    private async Task FlushMemosAsync()
    {
        var top = MemoSaver.FlushAsync();
        var subs = _subMemoPending ? SaveSubMemosAsync() : Task.CompletedTask;
        _saveSubMemo.Cancel();
        await Task.WhenAll(top, subs);
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
            path = ThumbnailPathOf(item);
            _thumbnailPaths[item.Id] = path;
        }

        return path;
    }

    private readonly Dictionary<string, string?> _thumbnailPaths = new(StringComparer.Ordinal);

    /// <summary>画像が届いたら、覚えている場所も引き直した物にする（次に行を作るときに古い場所を使わない）。</summary>
    protected override void OnThumbnailPathChanged(string itemId, string? path) => _thumbnailPaths[itemId] = path;

    /// <summary>参照だけ残っている名前を、そのままマスタへ作る。名前が正しかった場合の直し方。</summary>
    private async Task AddOrphanToMasterAsync(OrphanTagRow row)
    {
        // 一覧に無い名前の3つの操作も、できなかったときは一覧の見出しの近くに出す（前は何も出ず、できたと言っていた）
        var result = await RewriteAsync(
            row.IsSub ? new UiCommand.AddUserTag(row.Top, row.Sub) : new UiCommand.AddUserTag(row.Top),
            "一覧に追加できませんでした。",
            ListNotice);

        if (result is not null and not CommandResult.Failed)
        {
            ListNotice.Show($"「{row.DisplayName}」を一覧に追加しました。{row.ItemCount} 件の商品が絞り込みに表示されるようになります。");
        }
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

        var result = await RewriteAsync(
            row.IsSub ? new UiCommand.RenameUserTag(row.Top, row.Sub, name) : new UiCommand.RenameUserTag(row.Top, null, name),
            "統合できませんでした。",
            ListNotice);

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

        var result = await RewriteAsync(
            row.IsSub ? new UiCommand.DeleteUserTag(row.Top, row.Sub) : new UiCommand.DeleteUserTag(row.Top),
            "外せませんでした。",
            ListNotice);
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
}
