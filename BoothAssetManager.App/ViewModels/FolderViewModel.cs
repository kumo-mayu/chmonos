using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Diagnostics;
using BoothAssetManager.Core.Services;
using static BoothAssetManager.Core.Services.PathText;

namespace BoothAssetManager.App.ViewModels;

public enum FolderViewRowKind
{
    Volume,
    Root,
    Folder,
    File,
    Unresolved,
    ItemFolder,
}

/// <summary>木に置く1件（管理しているファイル・未確定のファイル・フォルダごと登録した商品）。</summary>
public sealed class FolderViewEntry
{
    public required string Path { get; init; }

    public required FolderViewRowKind Kind { get; init; }

    public ItemRecord? Item { get; init; }

    public UnresolvedFile? Unresolved { get; init; }

    /// <summary>同じ中身（ハッシュ）が置いてあるほかの場所（ユーザ判断：それぞれの場所に出し「ほかに n か所」と添える）。</summary>
    public IReadOnlyList<string> Others { get; init; } = [];

    public bool IsMissing { get; set; }

    /// <summary>外付けのドライブ文字が変わって今の文字に読み替えた物なら、記録したときの文字（<see cref="Path"/> は今の場所）。</summary>
    public string? RecordedLetter { get; init; }

    /// <summary>zip の横にある、展開したフォルダ（ユーザ判断：zip の下に薄く出す）。</summary>
    public string? ExtractedFolder { get; set; }

    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\')) is { Length: > 0 } name ? name : Path;
}

/// <summary>木の1つのフォルダ（記録したパスから組む。ディスクは読み回らない）。</summary>
internal sealed class FolderViewNode
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public Dictionary<string, FolderViewNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<FolderViewEntry> Entries { get; } = [];

    /// <summary>この下（子孫を含む）にファイルを持つ商品。</summary>
    public Dictionary<string, ItemRecord> Items { get; } = new(StringComparer.Ordinal);

    /// <summary>この下（子孫を含む）の未確定。ハッシュで1件。</summary>
    public Dictionary<string, UnresolvedFile> Unresolved { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FolderViewNode Child(string name)
    {
        if (!Children.TryGetValue(name, out var child))
        {
            child = new FolderViewNode { Path = $@"{Path}\{name}", Name = name };
            Children[name] = child;
        }

        return child;
    }

    /// <summary>名前の順（数字は数として比べる）に並べた子。木を組むとき（裏のスレッド）に1度だけ並べる。</summary>
    public List<FolderViewNode> OrderedChildren { get; private set; } = [];

    public List<FolderViewEntry> OrderedEntries { get; private set; } = [];

    /// <summary>
    /// 並べておく。**開け閉めのたびに並べない**——数字を数として比べる並べ方は1文字ずつ比べるので重く、
    /// 1つのフォルダに1000本あると、開くたびに1万回ほど比べることになる
    /// </summary>
    public void Order()
    {
        OrderedChildren = Children.Values.OrderBy(child => child.Name, NaturalComparer.Instance).ToList();
        OrderedEntries = Entries.OrderBy(entry => entry.Name, NaturalComparer.Instance).ToList();
        foreach (var child in OrderedChildren)
        {
            child.Order();
        }
    }

    public void Aggregate()
    {
        foreach (var child in Children.Values)
        {
            child.Aggregate();
            foreach (var (id, item) in child.Items)
            {
                Items[id] = item;
            }

            foreach (var (hash, file) in child.Unresolved)
            {
                Unresolved[hash] = file;
            }
        }

        foreach (var entry in Entries)
        {
            if (entry.Item is { } item)
            {
                Items[item.Id] = item;
            }

            if (entry.Unresolved is { } file)
            {
                Unresolved[file.Hash] = file;
            }
        }
    }
}

internal sealed class FolderViewRootModel
{
    public required FolderViewRoot Root { get; init; }

    public required FolderViewNode Node { get; init; }

    public required string Label { get; init; }

    /// <summary>この根の下に、読み替えた物を記録したときの文字（読み替えた結果だと分かるように・ユーザ指示 2026-09-14）。</summary>
    public SortedSet<string> RecordedLetters { get; } = new(StringComparer.Ordinal);
}

internal sealed class FolderViewVolume
{
    public required string Volume { get; init; }

    public required string Label { get; init; }

    public required bool IsOnline { get; init; }

    /// <summary>このボリュームに読み替えた物を、記録したときの文字。</summary>
    public SortedSet<string> RecordedLetters { get; } = new(StringComparer.Ordinal);

    public List<FolderViewRootModel> Roots { get; } = [];

    /// <summary>ボリューム全体をまとめた物（右に詳細を出すときに使う）。</summary>
    public required FolderViewNode Summary { get; init; }
}

/// <summary>左の一覧の1行。**見えている行だけを平らに並べる**（1つのフォルダに1000本あっても、仮想化した一覧で重くしない）。</summary>
public sealed class FolderViewRow : ViewModelBase, IHasItemCard
{
    public required string Key { get; init; }

    public required FolderViewRowKind Kind { get; init; }

    public required int Depth { get; init; }

    public required string Name { get; init; }

    public string Path { get; init; } = string.Empty;

    public string SubText { get; init; } = string.Empty;

    public bool HasSubText => SubText.Length > 0;

    public bool CanExpand { get; init; }

    public bool IsExpanded { get; init; }

    public string Glyph => CanExpand ? (IsExpanded ? "▾" : "▸") : string.Empty;

    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    /// <summary>取り外したドライブ・見つからないファイル。灰色で残す（木から消すと、持っていることを忘れる）。</summary>
    public bool IsDim { get; init; }

    public string? MissingText { get; init; }

    public bool HasMissingText => MissingText is not null;

    public string CountText { get; init; } = string.Empty;

    public int UnresolvedCount { get; init; }

    public bool HasUnresolvedBadge => UnresolvedCount > 0 && Kind is FolderViewRowKind.Volume or FolderViewRowKind.Root or FolderViewRowKind.Folder;

    /// <summary>
    /// 未確定を出していないときは件数を出さず、あることだけ分かる札にする（ユーザ指示 2026-09-15：完全に隠すと、
    /// 片付けていない物があることを忘れる。件数まで出すと、出さない設定なのにうるさい）
    /// </summary>
    public string UnresolvedBadgeText => FolderViewModel.ShowsUnresolvedNow ? $"未確定 {UnresolvedCount}" : "未確定あり";

    public string? UnresolvedBadgeTip => FolderViewModel.ShowsUnresolvedNow
        ? null
        : "この下に未確定のファイルがあります。左の「未確定」をオンにすると表示されます。";

    public bool IsFolderLike => Kind is FolderViewRowKind.Volume or FolderViewRowKind.Root or FolderViewRowKind.Folder;

    public bool IsUnresolved => Kind == FolderViewRowKind.Unresolved;

    public bool IsItemFile => Kind is FolderViewRowKind.File or FolderViewRowKind.ItemFolder;

    public FolderViewEntry? Entry { get; init; }

    internal FolderViewNode? Node { get; init; }

    public string OthersText => Entry is { Others.Count: > 0 } entry ? $"ほかに {entry.Others.Count} か所" : string.Empty;

    public bool HasOthers => OthersText.Length > 0;

    public bool HasExtracted => Entry?.ExtractedFolder is not null;

    /// <summary>
    /// 絵の場所を探す手順。**行が画面に出たときに初めて探す**（行を作るたびに探すと、画像のフォルダの一覧と並べ替えが
    /// 行の数だけ走る。一覧は見えている行しか作らないので、画面に出た分だけで済む）。
    /// </summary>
    internal Func<string?>? ThumbnailPathFactory { get; init; }

    private string? _thumbnailPath;
    private bool _thumbnailResolved;

    private string? ThumbnailPath
    {
        get
        {
            if (!_thumbnailResolved)
            {
                _thumbnailPath = ThumbnailPathFactory?.Invoke();
                _thumbnailResolved = true;
            }

            return _thumbnailPath;
        }
    }

    public ThumbnailLoader? Thumbnails { get; init; }

    public BitmapSource? Thumbnail => ThumbnailPath is { } path
        ? Thumbnails?.PeekForTile(path, () => OnPropertyChanged(nameof(Thumbnail)))
        : null;

    /// <summary>
    /// 商品の絵に乗せたときに出す大きめの絵（ユーザ指摘 2026-09-14：フォルダビューの商品の絵に乗せても何も出なかった。
    /// 改変の画面の使ったものと同じ 180px）。一覧の絵は小さく縮めて読んでいて引き伸ばすとぼやけるので、カードの大きさで読み直す。
    /// **吹き出しが開いたときに初めて読む**（行を作るたびに全部読むとメモリを食う）
    /// </summary>
    public BitmapSource? HoverImage => ThumbnailPath is { } path
        ? Thumbnails?.PeekForCard(path, () => OnPropertyChanged(nameof(HoverImage)))
        : null;

    public bool HasHoverImage => ThumbnailPath is not null;

    /// <summary>吹き出しに添える商品名。</summary>
    public string ItemName => Entry?.Item?.DisplayName ?? Name;

    /// <summary>カードを作るのに要る物（画像の置き場と設定）。木の行から右クリックを出すために受け取る。</summary>
    public AppServiceContainer? Services { get; init; }

    private ItemCardViewModel? _card;

    /// <summary>
    /// 右クリック（カードと同じメニュー）で使う商品のカード（ユーザ指示 2026-09-20・M3）。
    /// **押されたときに初めて作る**——木は1000行並ぶことがあるので、行を作るたびにカードまで作ると重い。
    /// 商品に結び付いていない行（フォルダ・未確定）は null で、メニューの項目は押せない
    /// </summary>
    ItemCardViewModel? IHasItemCard.Card
    {
        get
        {
            if (_card is not null)
            {
                return _card;
            }

            if (Entry?.Item is not { } item || Thumbnails is null || Services is null)
            {
                return null;
            }

            // 右クリックのメニューは商品だけを見る（絵や札は出さない）ので、名前だけ埋めれば足りる
            return _card = new ItemCardViewModel(
                item, Thumbnails, Services.Paths.ItemImagesDir(item.Id), Services.Settings.ThumbnailRole)
            {
                Name = item.DisplayName,
            };
        }
    }

    public string Initial => AvatarText.InitialOf(Entry?.Item?.DisplayName ?? Name);
}

/// <summary>
/// フォルダビュー（ユーザ仕様 2026-09-13 <c>docs/history/folder-view.md</c>）。
///
/// 左はフォルダの木（ファイルまで・未確定も印付きで）、右は選んだ物の詳細。管理しているファイルは商品ページを、
/// 未確定のファイルは未確定の画面の右側を、そのまま組み込む。根の決め方は <see cref="FolderViewRoots"/>。
/// **木は保存した記録のパスから組み、ディスクを読み回らない**（取り外したドライブも最後に分かっていた形で出す）。
/// </summary>
public sealed class FolderViewModel : ViewModelBase, ISelectionScreen, IPendingWrites, ILeavingScreen
{
    // 開いた・畳んだはアプリを閉じるまで覚える（改変の画面と同じ・ユーザ判断）
    private static readonly HashSet<string> s_expanded = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> s_collapsed = new(StringComparer.OrdinalIgnoreCase);

    // 木に商品を出すかの切り替え（ユーザ指示 2026-09-14）。開いた・畳んだと同じく、アプリを閉じるまで覚える
    private static bool s_showItems = true;
    private static bool s_showManaged = true;
    private static bool s_showUnresolved = true;

    /// <summary>未確定を出しているか（木の行・右の子フォルダのカードが件数の出し方を決めるのに読む）。</summary>
    internal static bool ShowsUnresolvedNow => s_showUnresolved;

    private readonly AppServiceContainer _services;
    private PaneColumn? _listPane;

    /// <summary>左の木の列。ドラッグで幅を変えられる（ユーザ判断 2026-09-14）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "folder.list");
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;

    private List<FolderViewVolume> _volumes = [];
    private FolderViewRow? _selected;
    private object? _detail;
    private string _filter = string.Empty;
    private string _status = string.Empty;
    private bool _isLoading = true;
    private string? _pendingSelect;
    private int _lastResolveCount = -1;

    public FolderViewModel(AppServiceContainer services, MainViewModel main, ThumbnailLoader thumbnails, string? selectKey = null)
    {
        _services = services;
        _main = main;
        _thumbnails = thumbnails;
        _pendingSelect = selectKey;

        ToggleCommand = new RelayCommand(parameter => Toggle(parameter as FolderViewRow));
        ShowOtherCommand = new RelayCommand(parameter => ShowOther(parameter as FolderViewRow));
        RevealExtractedCommand = new RelayCommand(parameter => Shell.Reveal((parameter as FolderViewRow)?.Entry?.ExtractedFolder));
        SearchHereCommand = new RelayCommand(parameter =>
        {
            if (parameter is FolderViewDetail detail)
            {
                // 検索のフォルダの条件も同じ読み替えを通すので、今の場所のまま渡す
                _main.ShowItemsInFolder(detail.Path);
            }
        });
        // 右のフォルダの詳細からはその場所を開く。木の行（商品のファイル）の右クリックからは、
        // カードと同じ「エクスプローラで開く」（手元のファイルが2つ以上あれば選ばせる。M3）
        RevealCommand = new RelayCommand(parameter =>
        {
            if (parameter is FolderViewDetail detail)
            {
                Shell.Reveal(detail.Path);
                return;
            }

            _main.Search.RevealCommand.Execute(parameter);
        });
        ImportHereCommand = new RelayCommand(parameter => ImportHere(parameter as FolderViewDetail));
        ToggleWatchCommand = new RelayCommand(parameter => ToggleWatchAsync(parameter as FolderViewDetail).Forget());
        ExcludeUnresolvedCommand = new RelayCommand(parameter => ExcludeUnresolvedAsync(parameter as FolderViewDetail).Forget());
        OpenUnresolvedCommand = new RelayCommand(parameter =>
        {
            if (parameter is FolderViewDetail detail)
            {
                var hashes = detail.Unresolved.Select(file => file.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
                ShowResolve(file => hashes.Contains(file.Hash));
            }
        });
        LoadAsync().Forget();
    }

    /// <summary>見えている行。組み直すときはまとめて差し替える（知らせを1回にする）。</summary>
    public RangeObservableCollection<FolderViewRow> Rows { get; } = [];

    /// <summary>絞り込みの語。組み直しの最初に1度だけ整える（行ごとに Trim しない）。</summary>
    private string _needle = string.Empty;

    public FolderViewRow? Selected
    {
        get => _selected;
        set
        {
            // 行を差し替える間、一覧は選んでいた行を見失って「選択なし」を送ってくる。それを受けると右が消える
            // （切り替え・絞り込みのたびに消えていた）。差し替えの後で同じ鍵の行を選び直す
            if (_replacingRows && value is null)
            {
                return;
            }

            if (SetField(ref _selected, value))
            {
                ShowDetail(value);
            }
        }
    }

    private bool _replacingRows;

    /// <summary>戻るで戻ったときに、同じ行を選び直すための鍵。</summary>
    public string? SelectedKey => _selected?.Key;

    /// <summary>
    /// 右に組み込んだ物（商品ページなど）の待っている保存も拾う。
    /// 組み込んだときは今の画面がこちらなので、主画面から商品ページを直に探しても当たらない。
    /// </summary>
    public Task FlushPendingWritesAsync()
        => (Detail as IPendingWrites)?.FlushPendingWritesAsync() ?? Task.CompletedTask;

    public object? Detail
    {
        get => _detail;
        private set
        {
            var leaving = _detail as ResolveViewModel;
            if (SetField(ref _detail, value))
            {
                // 右に組み込んだ未確定を別の物に差し替えたら、未確定の画面を離れたのと同じ後始末をさせる
                // （読み込みの取り消しと、登録した商品を検索へ反映する読み直し）。フォルダビューを離れるときの OnLeaving は
                // 今の右側にしか届かないので、先に差し替えた未確定で登録した商品（仮IDを含む）が検索に出なかった
                leaving?.OnLeaving();
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public bool HasDetail => Detail is not null;

    // ---- 絞り込み（ユーザ判断：木全体を名前で絞る＋商品を出すか・管理対象・未確定の切り替え） ----

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetField(ref _filter, value))
            {
                OnPropertyChanged(nameof(HasFilter));
                Rebuild();
            }
        }
    }

    public bool HasFilter => Filter.Length > 0;

    /// <summary>
    /// 木に商品（管理しているファイル・未確定のファイル）を出すか（ユーザ指示 2026-09-14）。切ると純粋なフォルダの木になる。
    /// 商品は右の一覧で見られるので、木はフォルダだけで辿りたいときがある
    /// </summary>
    public bool ShowItems
    {
        get => s_showItems;
        set => SetToggle(ref s_showItems, value);
    }

    /// <summary>管理しているファイルを出すか。<see cref="ShowItems"/> が入のときだけ効く。</summary>
    public bool ShowManaged
    {
        get => s_showManaged;
        set => SetToggle(ref s_showManaged, value);
    }

    /// <summary>未確定のファイルを出すか。<see cref="ShowItems"/> が入のときだけ効く。</summary>
    public bool ShowUnresolved
    {
        get => s_showUnresolved;
        set => SetToggle(ref s_showUnresolved, value);
    }

    private void SetToggle(ref bool field, bool value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(nameof(ShowItems));
        OnPropertyChanged(nameof(ShowManaged));
        OnPropertyChanged(nameof(ShowUnresolved));
        Rebuild();

        // 右に出しているフォルダも、未確定の件数と案内の出し方が変わる（ユーザ指示 2026-09-15）
        if (Detail is FolderViewDetail detail)
        {
            detail.RefreshUnresolvedShown();
        }
    }

    /// <summary>木にファイルの行を出すか。</summary>
    private bool ShowsEntries => ShowItems && (ShowManaged || ShowUnresolved);

    /// <summary>
    /// 絞っているか。文字があるとき、またはファイルを出していて片方（管理対象・未確定）を切ったとき（そのときは、当てはまる
    /// ファイルのあるフォルダだけを開いて出す。前の［管理している物／未確定］と同じ）。フォルダだけの木は絞っていない
    /// </summary>
    private bool IsFiltering => Filter.Trim().Length > 0 || (ShowsEntries && !(ShowManaged && ShowUnresolved));

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>空のときに次にやることを書く。</summary>
    public string EmptyText => _loadFailure ?? (_isLoading
        ? "読み込んでいます…"
        : IsFiltering
            ? "当てはまるものがありません。絞り込みを変えてみてください。"
            : "まだ手元のファイルがありません。「取り込み」でフォルダを選ぶと、ここに置き場所ごとに並びます。");

    // ---- 操作 ----

    public RelayCommand ToggleCommand { get; }

    public RelayCommand ShowOtherCommand { get; }

    public RelayCommand RevealExtractedCommand { get; }

    public RelayCommand SearchHereCommand { get; }

    public RelayCommand RevealCommand { get; }

    /// <summary>「このフォルダのアイテムを取り込む」（ユーザ指示 2026-09-14。「取り込み元に足す」は何が起きるか分かりにくかった）。</summary>
    public RelayCommand ImportHereCommand { get; }

    /// <summary>監視中かを出し、足す・外すの両方をできるようにする（ユーザ指示 2026-09-14。前は足すだけで外せなかった）。</summary>
    public RelayCommand ToggleWatchCommand { get; }

    public RelayCommand ExcludeUnresolvedCommand { get; }

    public RelayCommand OpenUnresolvedCommand { get; }

    /// <summary>窓が手前に戻ったとき。取り込み・未確定の片付けを別の画面でした後に、木を読み直す。右に出している物は作り直さない。</summary>
    public void NoteWindowActivated() => LoadAsync().Forget();

    // ---- 読み込み ----

    /// <summary>
    /// 離れたら木の読み直しを取り消す（既知 P8）。この画面は開くたびに作り直すので、離れた後の木は誰も見ない。
    /// 取り消すのは読むところだけ。ドライブ文字の組を控える書き込みは、始まっていれば最後まで書く
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    public void OnLeaving()
    {
        _leaving.Cancel();

        // 右に組み込んだ画面（未確定など）は主画面からは見えないので、自分が伝える
        (Detail as ILeavingScreen)?.OnLeaving();
    }

    private async Task LoadAsync()
    {
        try
        {
            await LoadCoreAsync(_leaving.Token);
        }
        catch (OperationCanceledException) when (_leaving.IsCancellationRequested)
        {
            // 画面を離れた。投げ直さないのは、除外の後に読み直していた呼び手を失敗に見せないため
        }
        catch (Exception exception)
        {
            // 読み込み中を下ろさないと「読み込んでいます…」のまま戻らなかった（統計・アバターの画面と同じ直し・N6）。
            // 木が空なら空の表示の代わりに、前に読めた木が残っていれば状態の行に出す
            AppLog.Error("フォルダビューの読み込み", exception);
            var failure = $"フォルダの一覧を読み込めませんでした。{Core.Services.FailureText.Cause(exception)}";
            _isLoading = false;
            _loadFailure = failure;
            if (!IsEmpty)
            {
                Status = failure;
            }

            OnPropertyChanged(nameof(EmptyText));
        }
    }

    /// <summary>読み込みに失敗したときの文。空の表示の代わりに出す。次に読めたら消す。</summary>
    private string? _loadFailure;

    private async Task LoadCoreAsync(CancellationToken token)
    {
        var items = _main.Search.SnapshotItems();
        if (items.Count == 0)
        {
            items = (await _services.Store.Items.LoadAllAsync(cancellationToken: token)).Items;
        }

        var unresolved = _services.Store.Unresolved.Load();
        var built = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();

            // 開くたびにドライブ文字と通し番号の組を確かめ直す（ユーザ判断 2026-09-14：取り込みとフォルダビューを開いた時）。
            // ファイルが在るかを見るので裏で
            var recorded = RecordedPaths(items, unresolved);
            IReadOnlyDictionary<string, string> found;
            try
            {
                // 木は今控えてある組で読み替えて組む（読むだけなので門を通らない）。
                // 控え直しの結果を待たないのは、控え直しは書き込みの門を通るため。保存先を運ぶ間や書き出しの間は
                // 門が閉じていて数分待たされ、その間フォルダビューが「読み込み中」のまま止まっていた（止めている間も読む操作はできる決め事）。
                // 控え直し（ObserveVolumes）が返す読み替えも、書く前の組から出した物なので、待って組んでいた前と同じ木になる
                found = _services.Volumes.RefreshRemap();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or System.Text.Json.JsonException)
            {
                AppLog.Error("フォルダビュー：ドライブ文字の組を読む", exception);
                found = new Dictionary<string, string>();
            }

            // 組を volumes.json に控えるので、書き込みの道（保存先を運ぶ間の門）を通す。待たない
            _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ObserveVolumes(recorded), cancellationToken: token).Forget();

            token.ThrowIfCancellationRequested();
            return Build(items, unresolved, found);
        }, token);

        token.ThrowIfCancellationRequested();
        _volumes = built;
        _isLoading = false;
        if (_loadFailure is not null && Status == _loadFailure)
        {
            Status = string.Empty;
        }

        _loadFailure = null;

        if (_pendingSelect is { } pending)
        {
            _pendingSelect = null;
            ExpandTo(pending.StartsWith("e:", StringComparison.Ordinal) ? pending[2..] : pending[(pending.IndexOf(':') + 1)..]);
            Rebuild();
            Selected = Rows.FirstOrDefault(row => row.Key == pending);
        }
        else
        {
            Rebuild();
        }
    }

    /// <summary>木に置く記録のパス全部（ドライブ文字の組を確かめる材料）。</summary>
    private static List<string> RecordedPaths(IReadOnlyList<ItemRecord> items, IReadOnlyList<UnresolvedFile> unresolved)
        => items.SelectMany(item => item.Local.OwnedFiles.SelectMany(file => file.Paths)
                .Concat(item.Local.LocalFolders.Select(folder => folder.Path)))
            .Concat(unresolved.SelectMany(file => file.Paths))
            .ToList();

    /// <summary>
    /// 記録から木を組む（裏のスレッドで）。在るかどうかと、zip の横の展開したフォルダもここで見る。
    /// ドライブ文字が変わった外付けの物は、今の文字の下に置く（<paramref name="remap"/>・記録は書き換えない）。
    /// </summary>
    private static List<FolderViewVolume> Build(
        IReadOnlyList<ItemRecord> items,
        IReadOnlyList<UnresolvedFile> unresolved,
        IReadOnlyDictionary<string, string> remap)
    {
        string Current(string path) => VolumeTable.Apply(path, remap);
        string? Moved(string path) => Same(Current(path), path) ? null : VolumeTable.LetterOf(path);

        var entries = new List<FolderViewEntry>();
        foreach (var item in items)
        {
            foreach (var file in item.Local.OwnedFiles)
            {
                foreach (var path in file.Paths)
                {
                    entries.Add(new FolderViewEntry
                    {
                        Path = Current(path),
                        RecordedLetter = Moved(path),
                        Kind = FolderViewRowKind.File,
                        Item = item,
                        Others = file.Paths.Where(other => !Same(other, path)).Select(Current).ToList(),
                    });
                }
            }

            foreach (var folder in item.Local.LocalFolders)
            {
                entries.Add(new FolderViewEntry
                {
                    Path = Current(folder.Path),
                    RecordedLetter = Moved(folder.Path),
                    Kind = FolderViewRowKind.ItemFolder,
                    Item = item,
                });
            }
        }

        foreach (var file in unresolved)
        {
            foreach (var path in file.Paths)
            {
                entries.Add(new FolderViewEntry
                {
                    Path = Current(path),
                    RecordedLetter = Moved(path),
                    Kind = FolderViewRowKind.Unresolved,
                    Unresolved = file,
                    Others = file.Paths.Where(other => !Same(other, path)).Select(Current).ToList(),
                });
            }
        }

        entries.RemoveAll(entry => ParentOf(entry.Path) is null);

        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        var roots = FolderViewRoots.Roots(
            entries.Select(entry => ParentOf(entry.Path)!),
            path => FolderViewRoots.IsHardBoundary(path, oneDrive));

        var volumes = new List<FolderViewVolume>();
        foreach (var group in roots.GroupBy(root => root.Volume, StringComparer.OrdinalIgnoreCase))
        {
            var online = VolumeOnline(group.Key);
            var volume = new FolderViewVolume
            {
                Volume = group.Key,
                Label = VolumeLabel(group.Key, online),
                IsOnline = online,
                Summary = new FolderViewNode { Path = group.Key, Name = group.Key },
            };

            foreach (var root in group.OrderBy(root => root.Path, NaturalComparer.Instance))
            {
                volume.Roots.Add(new FolderViewRootModel
                {
                    Root = root,
                    Node = new FolderViewNode { Path = root.Path, Name = root.Path },
                    Label = RootLabel(root),
                });
            }

            volumes.Add(volume);
        }

        foreach (var entry in entries)
        {
            var folder = ParentOf(entry.Path)!;
            var volume = volumes.FirstOrDefault(candidate =>
                string.Equals(candidate.Volume, FolderViewRoots.VolumeOf(folder), StringComparison.OrdinalIgnoreCase));
            if (volume is null || FindRoot(volume, folder) is not { } root)
            {
                continue;
            }

            var node = root.Node;
            var relative = folder.Length > root.Root.Path.Length ? folder[root.Root.Path.Length..].Trim('\\') : string.Empty;
            foreach (var segment in relative.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                node = node.Child(segment);
            }

            // 取り外したボリュームは確かめない（全部「取り外しているドライブ」）
            entry.IsMissing = !volume.IsOnline
                || !(entry.Kind == FolderViewRowKind.ItemFolder ? Directory.Exists(entry.Path) : File.Exists(entry.Path));

            if (!entry.IsMissing && entry.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(entry.Path[..^4]))
            {
                entry.ExtractedFolder = entry.Path[..^4];
            }

            node.Entries.Add(entry);

            if (entry.RecordedLetter is { } from)
            {
                root.RecordedLetters.Add(from);
                volume.RecordedLetters.Add(from);
            }
        }

        foreach (var volume in volumes)
        {
            foreach (var root in volume.Roots)
            {
                root.Node.Aggregate();
                root.Node.Order();
                foreach (var (id, item) in root.Node.Items)
                {
                    volume.Summary.Items[id] = item;
                }

                foreach (var (hash, file) in root.Node.Unresolved)
                {
                    volume.Summary.Unresolved[hash] = file;
                }
            }
        }

        return volumes.OrderBy(volume => volume.Volume, NaturalComparer.Instance).ToList();
    }

    /// <summary>その置き場所が入る根。「直下など」は境目の直下と、まとめた小さなフォルダだけを受け持つ。</summary>
    private static FolderViewRootModel? FindRoot(FolderViewVolume volume, string folder)
    {
        var normal = volume.Roots
            .Where(root => !root.Root.IsLooseBucket
                && (Same(folder, root.Root.Path) || folder.StartsWith(root.Root.Path + @"\", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(root => root.Root.Path.Length)
            .FirstOrDefault();

        return normal ?? volume.Roots.FirstOrDefault(root => root.Root.IsLooseBucket
            && (Same(folder, root.Root.Path) || root.Root.LooseFolders.Any(loose => Same(folder, loose))));
    }

    /// <summary>
    /// 根の名前。ボリュームから下の経路を「 › 」でつないで**消さずに畳んだまま**見せる（ユーザ：経路は完全に消すのではなくスキップできればよい）。
    /// </summary>
    private static string RootLabel(FolderViewRoot root)
    {
        if (root.IsLooseBucket)
        {
            var leaf = root.Path.Length > root.Volume.Length ? Path.GetFileName(root.Path) : root.Volume;
            return $"{leaf}（直下など）";
        }

        var relative = root.Path.Length > root.Volume.Length ? root.Path[root.Volume.Length..].Trim('\\') : root.Path;
        return relative.Replace(@"\", " › ");
    }

    private static bool VolumeOnline(string volume)
    {
        try
        {
            return Directory.Exists(volume + @"\");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string VolumeLabel(string volume, bool online)
    {
        if (!online)
        {
            return $"{volume}（取り外しています）";
        }

        if (volume.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return volume;
        }

        try
        {
            var drive = new DriveInfo(volume);
            return drive.IsReady && drive.VolumeLabel.Length > 0 ? $"{volume}（{drive.VolumeLabel}）" : volume;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return volume;
        }
    }

    // ---- 行を並べる ----

    private void Rebuild()
    {
        var keep = _selected?.Key;
        _needle = Filter.Trim();
        var rows = new List<FolderViewRow>();

        foreach (var volume in _volumes)
        {
            var key = $"Volume:{volume.Volume}";
            var expanded = IsExpanded(key, byDefault: true);

            // 畳んだボリュームの中は作らない。絞り込み中は、当てはまる物があるかを見るために作る
            var children = new List<FolderViewRow>();
            if (expanded || IsFiltering)
            {
                foreach (var root in volume.Roots)
                {
                    AddFolder(root.Node, root.Label, FolderViewRowKind.Root, 1, !volume.IsOnline, children, textHit: false,
                        subText: root.RecordedLetters.Count > 0 ? $"記録では {string.Join("・", root.RecordedLetters)}" : string.Empty);
                }

                if (IsFiltering && children.Count == 0)
                {
                    continue;
                }
            }

            rows.Add(new FolderViewRow
            {
                Key = key,
                Kind = FolderViewRowKind.Volume,
                Depth = 0,
                Name = volume.Label,
                Path = volume.Volume,
                CanExpand = volume.Roots.Count > 0,
                IsExpanded = expanded,
                IsDim = !volume.IsOnline,

                // 読み替えた結果だと分かるように（ユーザ指示 2026-09-14）
                SubText = volume.RecordedLetters.Count > 0
                    ? $"{string.Join("・", volume.RecordedLetters)} として記録したものを {volume.Volume} で表示しています"
                    : string.Empty,
                CountText = $"商品 {volume.Summary.Items.Count}",
                UnresolvedCount = volume.Summary.Unresolved.Count,
                Node = volume.Summary,
            });

            if (expanded)
            {
                rows.AddRange(children);
            }
        }

        // まとめて差し替える（1行ずつ足すと、1000行で1000回の知らせになる）
        _replacingRows = true;
        try
        {
            Rows.ReplaceAll(rows);
        }
        finally
        {
            _replacingRows = false;
        }

        // 選んでいた行を選び直す。**右は作り直さない**（開き直すたびに商品ページの読み込みが走り、見ていた所が戻る）
        if (keep is not null)
        {
            _selected = rows.FirstOrDefault(row => row.Key == keep);
            OnPropertyChanged(nameof(Selected));
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <returns>何か出したか（絞り込みで空になったフォルダは出さない）。</returns>
    private bool AddFolder(
        FolderViewNode node,
        string label,
        FolderViewRowKind kind,
        int depth,
        bool offline,
        List<FolderViewRow> output,
        bool textHit,
        string subText = "")
    {
        // 1本道の段は1行に畳む（今の FolderTree と同じ）。畳んだ段は名前に「 › 」で残すので、経路は消えない
        var name = label;
        while (node.Entries.Count == 0 && node.Children.Count == 1)
        {
            var only = node.OrderedChildren[0];
            name = $"{name} › {only.Name}";
            node = only;
        }

        var key = $"{kind}:{node.Path}";
        var expanded = IsExpanded(key, byDefault: kind == FolderViewRowKind.Root);
        var row = new FolderViewRow
        {
            Key = key,
            Kind = kind,
            Depth = depth,
            Name = name,
            Path = node.Path,
            SubText = subText,
            CanExpand = node.Children.Count + (ShowsEntries ? node.Entries.Count : 0) > 0,
            IsExpanded = expanded,
            IsDim = offline,
            CountText = $"商品 {node.Items.Count}",
            UnresolvedCount = node.Unresolved.Count,
            Node = node,
        };

        // 絞っていないときは、**畳んだフォルダの中の行を作らない**（前は毎回、木の全部の行を作ってから捨てていた）
        if (!IsFiltering)
        {
            output.Add(row);
            if (expanded)
            {
                AddChildren(node, depth, offline, output, hit: false);
            }

            return true;
        }

        // 絞り込み中は、中に当てはまる物があるかを先に見る。無ければこのフォルダも出さない
        var children = new List<FolderViewRow>();
        AddChildren(node, depth, offline, children, textHit || TextHits(name));

        // フォルダだけの木では、名前が当たったフォルダは中身が無くても出す（当てはまるファイルが出てこないので）
        if (children.Count == 0 && (ShowsEntries || !TextHits(name)))
        {
            return false;
        }

        output.Add(row);
        output.AddRange(children);
        return true;
    }

    private void AddChildren(FolderViewNode node, int depth, bool offline, List<FolderViewRow> output, bool hit)
    {
        foreach (var child in node.OrderedChildren)
        {
            AddFolder(child, child.Name, FolderViewRowKind.Folder, depth + 1, offline, output, hit);
        }

        foreach (var entry in node.OrderedEntries)
        {
            if (EntryVisible(entry) && (hit || TextHits(entry.Name) || TextHits(entry.Item?.DisplayName)))
            {
                output.Add(EntryRow(entry, depth + 1, offline));
            }
        }
    }

    private FolderViewRow EntryRow(FolderViewEntry entry, int depth, bool offline) => new()
    {
        Key = $"e:{entry.Path}",
        Kind = entry.Kind,
        Depth = depth,
        Name = entry.Name,
        Path = entry.Path,
        SubText = entry.Kind switch
        {
            FolderViewRowKind.Unresolved => "未確定（商品が決まっていません）",
            FolderViewRowKind.ItemFolder => $"{entry.Item!.DisplayName}（フォルダごと登録した商品）",
            _ => entry.Item!.DisplayName,
        },
        IsDim = offline || entry.IsMissing,
        MissingText = !entry.IsMissing ? null : offline ? "取り外しているドライブ" : "見つかりません",
        Entry = entry,
        ThumbnailPathFactory = entry.Item is { } item ? () => ItemThumbnailPath(item) : null,
        Thumbnails = _thumbnails,
        Services = _services,
    };

    private bool EntryVisible(FolderViewEntry entry)
        => ShowsEntries && (entry.Kind == FolderViewRowKind.Unresolved ? ShowUnresolved : ShowManaged);

    private bool TextHits(string? text)
        => _needle.Length == 0 || (text is not null && text.Contains(_needle, StringComparison.CurrentCultureIgnoreCase));

    private bool IsExpanded(string key, bool byDefault)
        => IsFiltering || s_expanded.Contains(key) || (byDefault && !s_collapsed.Contains(key));

    private void Toggle(FolderViewRow? row)
    {
        if (row is null || !row.CanExpand || IsFiltering)
        {
            return;
        }

        if (row.IsExpanded)
        {
            s_expanded.Remove(row.Key);
            s_collapsed.Add(row.Key);
        }
        else
        {
            s_collapsed.Remove(row.Key);
            s_expanded.Add(row.Key);
        }

        Rebuild();
    }

    /// <summary>その場所まで木を開く（「ほかに n か所」と、戻るで戻ったとき）。</summary>
    private void ExpandTo(string path)
    {
        var folder = ParentOf(path) ?? path;
        var volume = FolderViewRoots.VolumeOf(folder);
        s_collapsed.Remove($"Volume:{volume}");

        for (var current = folder; current is not null && current.Length >= volume.Length; current = ParentOf(current))
        {
            foreach (var kind in new[] { "Root", "Folder" })
            {
                s_collapsed.Remove($"{kind}:{current}");
                s_expanded.Add($"{kind}:{current}");
            }
        }
    }

    /// <summary>同じ中身が置いてある、もう1つの場所へ移る。</summary>
    private void ShowOther(FolderViewRow? row)
    {
        if (row?.Entry is not { Others.Count: > 0 } entry)
        {
            return;
        }

        var target = entry.Others[0];
        Filter = string.Empty;
        ExpandTo(target);
        Rebuild();
        Selected = Rows.FirstOrDefault(candidate => candidate.Key == $"e:{target}");
    }

    // ---- 右に出す ----

    private void ShowDetail(FolderViewRow? row)
    {
        switch (row)
        {
            case null:
                Detail = null;
                break;
            case { Kind: FolderViewRowKind.Volume }:
                Detail = VolumeDetail(row);
                break;
            case { IsFolderLike: true, Node: { } node }:
                // 見出しは名前だけ（パスはその下に出る・ユーザ指示 2026-09-14）
                Detail = DetailFor(node, LeafName(node.Path), row.IsDim);
                break;
            case { Entry.Item: { } item }:
                ShowItem(item);
                break;
            case { Entry.Unresolved: { } file }:
                ShowResolve(candidate => string.Equals(candidate.Hash, file.Hash, StringComparison.OrdinalIgnoreCase));
                break;
        }
    }

    /// <summary>
    /// フォルダを選んだときの右側（ユーザ指示 2026-09-14）。検索画面と同じカードで、直下の商品と子フォルダを並べる。
    /// 文字で探すときは、この下の全部から探す。
    /// </summary>
    private FolderViewDetail DetailFor(FolderViewNode node, string title, bool offline)
    {
        var children = node.OrderedChildren.Select(child => FolderCard(child, child.Name, FolderViewRowKind.Folder, offline)).ToList();
        var direct = node.Entries.Where(entry => entry.Item is not null).Select(entry => entry.Item!);
        return NewDetail(node.Path, title, offline, children, Descendants(node, node.Path, offline), direct, node.Items.Values, node.Unresolved.Values);
    }

    /// <summary>ボリュームを選んだとき。直下に置き場所は無く、根が子フォルダになる。</summary>
    private FolderViewDetail? VolumeDetail(FolderViewRow row)
    {
        var volume = _volumes.FirstOrDefault(candidate => string.Equals(candidate.Volume, row.Path, StringComparison.OrdinalIgnoreCase));
        if (volume is null)
        {
            return null;
        }

        var offline = !volume.IsOnline;
        var roots = volume.Roots.Select(root => FolderCard(root.Node, root.Label, FolderViewRowKind.Root, offline)).ToList();
        var all = volume.Roots.SelectMany(root => new[] { FolderCard(root.Node, root.Label, FolderViewRowKind.Root, offline) }
            .Concat(Descendants(root.Node, volume.Volume, offline))).ToList();
        return NewDetail(volume.Volume, row.Name, offline, roots, all, [], volume.Summary.Items.Values, volume.Summary.Unresolved.Values);
    }

    private FolderViewDetail NewDetail(
        string path,
        string title,
        bool offline,
        IReadOnlyList<FolderBrowserFolderCard> children,
        IReadOnlyList<FolderBrowserFolderCard> all,
        IEnumerable<ItemRecord> direct,
        IEnumerable<ItemRecord> subtree,
        IEnumerable<UnresolvedFile> unresolved)
    {
        var settings = _services.Settings;
        var items = subtree.ToList();
        var detail = new FolderViewDetail(this, _main, _services, children, all, direct, items)
        {
            Path = path,
            Title = title,
            ItemCount = items.Count,
            Unresolved = unresolved.ToList(),
            IsOffline = offline,
            IsWatched = settings.WatchedFolders.Contains(path, StringComparer.OrdinalIgnoreCase),
        };
        detail.Rebuild();
        return detail;
    }

    /// <summary>
    /// 子フォルダのカード。1本道の段は木と同じく畳み、鍵も木の行と同じにする（押したら木のその行へ移れるように）。
    /// 名前は畳んだ先のフォルダの名前だけにし、パスはカードの下に出す（ユーザ指示 2026-09-14：経路を「 › 」でつないだ名前は分かりにくい）
    /// </summary>
    private static FolderBrowserFolderCard FolderCard(FolderViewNode node, string label, FolderViewRowKind kind, bool offline, string subText = "")
    {
        while (node.Entries.Count == 0 && node.Children.Count == 1)
        {
            node = node.OrderedChildren[0];
        }

        return new FolderBrowserFolderCard
        {
            Key = $"{kind}:{node.Path}",
            Path = node.Path,
            Name = label.EndsWith("（直下など）", StringComparison.Ordinal) ? label : LeafName(node.Path),
            ItemCount = node.Items.Count,
            UnresolvedCount = node.Unresolved.Count,
            IsDim = offline,
            SubText = subText,
            Node = node,
        };
    }

    /// <summary>この下の全部のフォルダ（文字で探すとき用）。場所は、今のフォルダから見た経路で添える。</summary>
    private static List<FolderBrowserFolderCard> Descendants(FolderViewNode node, string basePath, bool offline)
    {
        var output = new List<FolderBrowserFolderCard>();
        var stack = new Stack<FolderViewNode>(node.OrderedChildren.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var parent = ParentOf(current.Path) ?? current.Path;
            var relative = parent.Length > basePath.Length ? parent[basePath.Length..].Trim('\\').Replace(@"\", " › ") : string.Empty;
            output.Add(new FolderBrowserFolderCard
            {
                Key = $"{FolderViewRowKind.Folder}:{current.Path}",
                Path = current.Path,
                Name = current.Name,
                ItemCount = current.Items.Count,
                UnresolvedCount = current.Unresolved.Count,
                IsDim = offline,
                SubText = relative.Length > 0 ? relative : "このフォルダの直下",
                Node = current,
            });

            foreach (var child in current.OrderedChildren.AsEnumerable().Reverse())
            {
                stack.Push(child);
            }
        }

        return output;
    }

    /// <summary>
    /// 右の子フォルダのカードを押した。木の中でそのフォルダまで開いて選ぶ（右も移る）。
    /// 木の文字の絞り込みは外す（外さないと、移った先の行が絞り込みで隠れていることがある）。
    /// 切り替えで隠れている・1本道の途中のフォルダで行が無いときは、木はそのままで右だけ移る
    /// </summary>
    internal void OpenFolder(FolderBrowserFolderCard? card)
    {
        if (card?.Node is not { } node)
        {
            return;
        }

        Filter = string.Empty;
        ExpandTo(card.Path + @"\_");
        Rebuild();
        if (Rows.FirstOrDefault(row => row.Key == card.Key) is { } row)
        {
            Selected = row;
        }
        else
        {
            _selected = null;
            OnPropertyChanged(nameof(Selected));
            Detail = DetailFor(node, card.Name, card.IsDim);
        }
    }

    /// <summary>商品ページをそのまま右に組み込む（ユーザ判断。改変の画面に改変の画面を組み込んだのと同じ形）。</summary>
    private void ShowItem(ItemRecord item)
    {
        var page = new ItemViewModel(item, _services, _main, _thumbnails) { IsEmbedded = true };

        // 開き直すのは右側だけ（主画面ごと差し替えない）。ファイルを外すと木の形も変わるので読み直す
        //
        // **右がまだこのページのときだけ差し替える。**取り直しは BOOTH の順番を待つので、待つ間に別の行を選べる。
        // 前は選び直した右側を古い商品のページで上書きしていた。フォルダの画面を離れていたら何もしない
        // （この画面は開くたびに作り直すので、木は次に開くときに読み直される）。
        // 右から外れていたら、済んだことは下の帯で知らせる（単独の商品ページを離れたときと同じ）
        page.IsShownByOwner = () => ReferenceEquals(_main.CurrentViewModel, this) && ReferenceEquals(Detail, page);
        page.Replaced = updated =>
        {
            if (!ReferenceEquals(_main.CurrentViewModel, this))
            {
                return;
            }

            if (!ReferenceEquals(Detail, page))
            {
                LoadAsync().Forget();
                return;
            }

            if (updated is null)
            {
                Detail = null;
            }
            else
            {
                ShowItem(updated);
            }

            LoadAsync().Forget();
        };
        Detail = page;
    }

    /// <summary>未確定の画面の右側をそのまま組み込む（ユーザ判断）。片付いたら木を読み直す。</summary>
    private void ShowResolve(Func<UnresolvedFile, bool> scope)
    {
        var resolve = new ResolveViewModel(_services, _main, scope) { IsEmbedded = true };
        _lastResolveCount = -1;
        resolve.PropertyChanged += (_, e) =>
        {
            // 右を別の物に差し替えた後の、古い未確定の知らせは聞かない。
            // 聞くと共有の _lastResolveCount を古い件数で書き換え、要らない木の読み直しを起こす
            if (e.PropertyName != nameof(ResolveViewModel.RemainingCount) || !ReferenceEquals(Detail, resolve))
            {
                return;
            }

            var count = resolve.RemainingCount;
            if (_lastResolveCount >= 0 && count < _lastResolveCount)
            {
                LoadAsync().Forget();
            }

            _lastResolveCount = count;
        };
        Detail = resolve;
    }

    // ---- フォルダの操作 ----

    /// <summary>
    /// このフォルダを取り込みの対象に積んで、そのまま始める（フォルダを落としたときと同じ道）。
    /// 画面は移さず、下の帯に進み具合と「取り込み画面を開く」を出す（点検 2026-09-23・動線の点検 A3：
    /// 前は取り込み画面へ移り、フォルダを見ていた所を失った）。
    /// 監視に入れるかは隣の切り替えで決めるので、ここでは聞かない
    /// </summary>
    private void ImportHere(FolderViewDetail? detail)
    {
        if (detail is null || detail.IsOffline)
        {
            return;
        }

        _main.Import.AddDroppedPaths([detail.Path], startImmediately: true, offerWatch: false);
        _main.NoteImportQueued(started: true);
    }

    /// <summary>監視対象に足す・外す。取り込み画面の一覧と同じ所を通す（別々に書くと、片方の写しがもう片方の変更を消す）。</summary>
    private async Task ToggleWatchAsync(FolderViewDetail? detail)
    {
        if (detail is null)
        {
            return;
        }

        var watch = !detail.IsWatched;
        await _main.Import.SetWatchedAsync(detail.Path, watch);
        detail.IsWatched = watch;
        Status = watch
            ? $"「{detail.Path}」を監視しています。次に起動したとき、この中に新しいファイルが増えていないかを見ます。"
            : $"「{detail.Path}」の監視をやめました。取り込んだものはそのまま残ります。";
    }

    /// <summary>その下の未確定を全部（子のフォルダも含む）管理対象から除外する（ユーザ判断 2026-09-13）。数を出して確かめる。</summary>
    private async Task ExcludeUnresolvedAsync(FolderViewDetail? detail)
    {
        if (detail is not { Unresolved.Count: > 0 })
        {
            return;
        }

        var answer = Notice.Show(
            $"「{detail.Title}」の下の未確定 {detail.Unresolved.Count} 件を管理対象から除外します。\n\n"
            + "ファイル自体は消しません。次回以降のスキャンで未確定に表示されなくなります。",
            "管理対象から除外する",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        foreach (var file in detail.Unresolved)
        {
            await _services.Commands.ExecuteAsync(new UiCommand.ExcludeFile(file.Hash, file.Paths, "フォルダビューからフォルダごと除外"));
        }

        Status = $"{detail.Unresolved.Count} 件を管理対象から除外しました。";
        _main.RefreshBadges();
        await LoadAsync();
        ShowDetail(_selected);
    }

    // ---- 小道具 ----

    /// <summary>商品の1枚目。検索のカードと同じ選び方（BOOTHの並び・★・役割の指定）。</summary>
    private string? ItemThumbnailPath(ItemRecord item)
    {
        var directory = _services.Paths.ItemImagesDir(item.Id);
        var ordered = Core.Images.ItemImageOrder.Arrange(
            directory, item.Booth.Images, _thumbnails.ListFiles(directory), item.Local.UserImages);
        return Core.Images.ItemImageOrder.Thumbnail(
            ordered, item.Local.ThumbnailImage, _services.Settings.ThumbnailRole, item.Local.ImageRoles);
    }

    /// <summary>フォルダの名前だけ（ドライブの直下などで名前が無ければパスのまま）。</summary>
    private static string LeafName(string path)
        => System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;

    private static string? ParentOf(string path)
    {
        var trimmed = path.Replace('/', '\\').TrimEnd('\\');
        var index = trimmed.LastIndexOf('\\');
        if (index <= 0)
        {
            return null;
        }

        var parent = trimmed[..index];
        return parent.StartsWith(@"\\", StringComparison.Ordinal) && parent.Count(character => character == '\\') < 3
            ? null
            : parent;
    }

    // ---- 木の行の右クリック（ユーザ指示 2026-09-20・M3）。中身は検索画面と同じ命令を借りる ----
    //
    // メニュー（ItemCardResources の CardMenu）は一覧の Tag からこれらを名前で引く。
    // 押した行はカードそのものではないので、受け取る側が行からカードを取り出す（SearchViewModel.AsCard）

    public RelayCommand OpenBoothCommand => _main.Search.OpenBoothCommand;

    public RelayCommand OpenShopCommand => _main.Search.OpenShopCommand;

    public RelayCommand CopyLinkCommand => _main.Search.CopyLinkCommand;

    public RelayCommand EditItemCommand => _main.Search.EditItemCommand;

    public RelayCommand CardUnpackCommand => _main.Search.CardUnpackCommand;

    public RelayCommand CardSendToUnityCommand => _main.Search.CardSendToUnityCommand;

    public RelayCommand CardSendToUnityWithRecordCommand => _main.Search.CardSendToUnityWithRecordCommand;

    public RelayCommand CardSelectInUnityCommand => _main.Search.CardSelectInUnityCommand;

    public RelayCommand HideItemCommand => _main.Search.HideItemCommand;

    // ---- Esc で選択を解除（ユーザ指示 2026-09-20・M6）。選ぶのは右のフォルダの中身（FolderViewDetail）----

    // 右に組み込んだ未確定の画面も見る。単独の未確定画面では Esc で解除できるのに、
    // フォルダの右に出したときだけ解除できなかった
    bool ISelectionScreen.HasSelection => Detail switch
    {
        FolderViewDetail folder => folder.HasSelection,
        ISelectionScreen embedded => embedded.HasSelection,
        _ => false,
    };

    void ISelectionScreen.ClearSelection()
    {
        switch (Detail)
        {
            case FolderViewDetail folder:
                folder.ClearSelection();
                break;
            case ISelectionScreen embedded:
                embedded.ClearSelection();
                break;
        }
    }
}
