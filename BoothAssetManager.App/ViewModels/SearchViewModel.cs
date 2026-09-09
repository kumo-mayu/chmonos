using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>結果一覧の1行。仮想化の単位。</summary>
public sealed class CardRow
{
    public required IReadOnlyList<ItemCardViewModel> Cards { get; init; }
}

/// <summary>
/// 検索画面。アプリの生存期間中1つだけ持ち回るので、条件やスクロール位置がそのまま残る。
///
/// 絞り込み（離散値）と文字列検索を分けているのは、
/// 「なぜこの結果になったか」が分かるようにするため。
/// </summary>
public sealed class SearchViewModel : ViewModelBase
{
    /// <summary>カード1枚が占める幅（カード228 + 右マージン14）。列数の計算に使う。</summary>
    private const double CardSlotWidth = 242;

    /// <summary>結果一覧の左右の余白（ScrollViewerのPadding分）。</summary>
    private const double ResultsPadding = 36;

    private readonly AppServiceContainer _services;
    private readonly ThumbnailLoader _thumbnails;
    private readonly Dictionary<string, ItemCardViewModel> _cards = new(StringComparer.Ordinal);

    /// <summary>
    /// 商品ごとの検索対象文字列。正規化が高くつくので読み込み時に1度だけ作る。
    /// 入力1文字ごとに作り直すと、全商品ぶんの説明文を毎回畳むことになる。
    /// </summary>
    private Dictionary<string, Core.Services.SearchHaystack> _haystacks = new(StringComparer.Ordinal);
    private List<ItemRecord> _allItems = [];
    private List<ItemCardViewModel> _matches = [];
    private string _queryText = string.Empty;
    private Core.Services.SearchNode _queryNode = new Core.Services.SearchNode.All();
    private bool _searchBody;
    private bool _searchPaths;
    private string? _selectedCategory;
    private bool _ownedOnly;
    private bool _missingOnly;
    private bool _givenOnly;
    private bool _receivedOnly;
    private string? _avatarFilterId;
    private string? _avatarFilterName;
    private bool _includeViaBase = true;
    private Core.Services.AvatarCompatibilityIndex? _compatibility;
    private bool _isLoading;
    private bool _isFilterPanelCollapsed;
    private int _columns = 1;
    private SortOption _sort = DefaultSort;
    private List<string> _attributeNames = [];

    /// <summary>ライブラリにあるBOOTHタグの全種類。候補の元。</summary>
    private List<string> _boothTagNames = [];

    /// <summary>要確認に未読の更新通知が残っている商品。「更新の有無」の条件で使う。</summary>
    private HashSet<string> _unreadItemIds = [];

    private MainViewModel? _main;

    /// <summary>「取り込み中に n 件増えました」の1行を出すために見る。</summary>
    public MainViewModel? Main => _main;

    public SearchViewModel(AppServiceContainer services, ThumbnailLoader thumbnails)
    {
        _services = services;
        _thumbnails = thumbnails;
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        AddAttributeFilterCommand = new RelayCommand(parameter => AddAttributeFilter(parameter as string));
        AddBoothTagFilterCommand = new RelayCommand(parameter => AddBoothTagFilter(parameter as string));
        SelectAllCommand = new RelayCommand(SelectAllMatches);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        SendSelectionToEditCommand = new RelayCommand(SendSelectionToEdit, () => SelectedCount > 0);
        OpenBoothCommand = new RelayCommand(parameter => OpenBooth(parameter as ItemCardViewModel));
        OpenShopCommand = new RelayCommand(parameter => OpenShop(parameter as ItemCardViewModel));
        CopyLinkCommand = new RelayCommand(parameter => CopyLink(parameter as ItemCardViewModel));
        EditItemCommand = new RelayCommand(parameter => _ = EditItemAsync(parameter as ItemCardViewModel));
        RevealCommand = new RelayCommand(parameter => Reveal(parameter as ItemCardViewModel));
        HideItemCommand = new RelayCommand(parameter => _ = HideItemAsync(parameter as ItemCardViewModel));
        AddExtraFilterCommand = new RelayCommand(parameter => AddExtraFilter(parameter as string));
        ToggleFilterPanelCommand = new RelayCommand(ToggleFilterPanel);
        SetAvatarFilterCommand = new RelayCommand(parameter => SetAvatarFilter(parameter as string));
        ClearAvatarFilterCommand = new RelayCommand(ClearAvatarFilter);
        _isFilterPanelCollapsed = services.Settings.FilterPanelCollapsed;

        // 前回積んでいた条件の種類だけを戻す。値は戻さない
        foreach (var name in services.Settings.SearchExtraFilters)
        {
            if (Enum.TryParse<ExtraFilterKind>(name, out var kind))
            {
                AddExtraFilter(kind, save: false);
            }
        }

        _ = ReloadAsync();
    }

    /// <summary>
    /// 絞り込みパネルを畳んでいるか。
    ///
    /// 畳んでも条件は外さない。**畳むのは見えなくすることで、外すことではない**ので、
    /// 畳んだ姿に効いている条件の数を出して、結果が絞られていることが分かるようにする。
    /// </summary>
    public bool IsFilterPanelCollapsed
    {
        get => _isFilterPanelCollapsed;
        private set
        {
            if (SetField(ref _isFilterPanelCollapsed, value))
            {
                OnPropertyChanged(nameof(FilterPanelWidth));
            }
        }
    }

    /// <summary>畳んだときの幅は、開くボタンと縦書きの見出しが通る分だけ。</summary>
    public double FilterPanelWidth => IsFilterPanelCollapsed ? 34 : 286;

    public RelayCommand ToggleFilterPanelCommand { get; }

    private void ToggleFilterPanel()
    {
        IsFilterPanelCollapsed = !IsFilterPanelCollapsed;
        var next = _services.Settings with { FilterPanelCollapsed = IsFilterPanelCollapsed };
        _services.ReplaceSettings(next);
        _ = _services.SettingsStore.SaveAsync(next);
    }

    /// <summary>
    /// 対応アバターの候補。「名前（商品ID）」で1行にしてある。
    ///
    /// **IDも同じ1行に入れているのは、VRChatでは商品IDで探す習慣があるから。**
    /// 候補の絞り込みは部分一致なので、名前でもIDでも同じ欄から引ける。
    /// 名前だけにすると、名前を思い出せずIDなら分かる場面で手が無くなる。
    /// </summary>
    public ObservableCollection<string> AvatarSuggestions { get; } = [];

    public RelayCommand SetAvatarFilterCommand { get; }

    public RelayCommand ClearAvatarFilterCommand { get; }

    /// <summary>今アバターで絞っているか。絞っているときだけ、外す手段と素体経由の切り替えを出す。</summary>
    public bool HasAvatarFilter => _avatarFilterId is not null;

    public string AvatarFilterName => _avatarFilterName ?? string.Empty;

    /// <summary>
    /// 素体経由の対応も含めるか。
    /// 既定で含めるのは「対応が確認できていないものを既定で隠さない」方針に合わせるため。
    /// </summary>
    public bool IncludeViaBase
    {
        get => _includeViaBase;
        set
        {
            if (SetField(ref _includeViaBase, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// 候補の1行から商品IDを取り出して絞る。
    /// 候補に無い語（打ち間違い）はそのままIDとして扱わない。
    /// </summary>
    private void SetAvatarFilter(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        var id = AvatarSuggestionText.IdOf(entry);
        if (id is null)
        {
            return;
        }

        _avatarFilterId = id;
        _avatarFilterName = AvatarSuggestionText.NameOf(entry);
        _compatibility = null;
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));
        ApplyFilters();
    }

    private void ClearAvatarFilter()
    {
        _avatarFilterId = null;
        _avatarFilterName = null;
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));
        ApplyFilters();
    }

    /// <summary>登録簿にあるアバターを候補に並べ直す。</summary>
    private void RefreshAvatarSuggestions()
    {
        AvatarSuggestions.Clear();

        var registry = _services.Store.Avatars.Load();
        foreach (var entry in registry.Entries.Where(entry => entry.AvatarOverride != false)
            .OrderBy(entry => entry.DisplayName ?? entry.BoothName ?? entry.ItemId, StringComparer.CurrentCulture))
        {
            AvatarSuggestions.Add(AvatarSuggestionText.Format(
                entry.DisplayName ?? entry.BoothName ?? entry.ItemId, entry.ItemId));
        }

        OnPropertyChanged(nameof(HasAvatarSuggestions));
    }

    public bool HasAvatarSuggestions => AvatarSuggestions.Count > 0;

    /// <summary>積んだ条件。常設に置かないものはここへ足していく。</summary>
    public ObservableCollection<ExtraFilter> ExtraFilters { get; } = [];

    /// <summary>まだ積んでいない条件の名前。「条件を追加」の候補に出す。</summary>
    public ObservableCollection<string> AvailableExtraFilters { get; } = [];

    public RelayCommand AddExtraFilterCommand { get; }

    public bool HasExtraFilters => ExtraFilters.Count > 0;

    private void AddExtraFilter(string? label)
    {
        var entry = ExtraFilterCatalog.All.FirstOrDefault(candidate => candidate.Label == label);
        if (entry is not null)
        {
            AddExtraFilter(entry.Kind);
        }
    }

    private void AddExtraFilter(ExtraFilterKind kind, bool save = true)
    {
        if (ExtraFilters.Any(filter => filter.Kind == kind))
        {
            return;
        }

        // 自分で足したときは「この軸で選ぶ」という意思表示なので入りにする。
        // 起動時の復元は意思表示ではないので切りにする。
        // 入りのまま戻すと、起動した瞬間に0件になり、原因が積んだ条件の中にあると気付けない。
        var filter = new ExtraFilter { Kind = kind, IsOn = save };
        filter.Changed += ApplyFilters;
        filter.RemoveCommand = new RelayCommand(() => RemoveExtraFilter(filter));

        if (kind == ExtraFilterKind.Folder)
        {
            filter.Descended += () => RebuildFolderRows(filter);
        }

        ExtraFilters.Add(filter);

        if (kind == ExtraFilterKind.Folder)
        {
            RebuildFolderRows(filter);
        }

        RefreshAvailableExtraFilters();

        if (save)
        {
            SaveExtraFilterKinds();
            ApplyFilters();
        }
    }

    private void RemoveExtraFilter(ExtraFilter filter)
    {
        filter.Changed -= ApplyFilters;
        ExtraFilters.Remove(filter);
        RefreshAvailableExtraFilters();
        SaveExtraFilterKinds();
        ApplyFilters();
    }

    private void RefreshAvailableExtraFilters()
    {
        AvailableExtraFilters.Clear();
        foreach (var entry in ExtraFilterCatalog.All.Where(entry => ExtraFilters.All(f => f.Kind != entry.Kind)))
        {
            AvailableExtraFilters.Add(entry.Label);
        }

        OnPropertyChanged(nameof(HasExtraFilters));
    }

    /// <summary>
    /// 今いる階層の行を作り直す。
    ///
    /// 木は保存せず毎回ここで組む。1,300件でもパスの文字列を辿るだけなので軽い。
    /// </summary>
    private void RebuildFolderRows(ExtraFilter filter)
    {
        filter.Rows.Clear();
        filter.Crumbs.Clear();

        // パンくず。現在地であって条件ではないので、押すと移動するだけ
        filter.Crumbs.Add(new FolderCrumb
        {
            Label = "すべて",
            Path = null,
            IsLast = filter.CurrentPath is null,
            GoCommand = new RelayCommand(() => filter.CurrentPath = null),
        });

        if (filter.CurrentPath is { } current)
        {
            var walked = string.Empty;
            var segments = current.Split(System.IO.Path.DirectorySeparatorChar);

            for (var i = 0; i < segments.Length; i++)
            {
                walked = i == 0 ? segments[0] : walked + System.IO.Path.DirectorySeparatorChar + segments[i];
                var target = walked;

                filter.Crumbs.Add(new FolderCrumb
                {
                    Label = segments[i],
                    Path = target,
                    IsLast = i == segments.Length - 1,
                    GoCommand = new RelayCommand(() => filter.CurrentPath = target),
                });
            }
        }

        foreach (var node in Core.Services.FolderTree.Children(_allItems, filter.CurrentPath))
        {
            var path = node.Path;

            var row = new FolderRow
            {
                Path = path,
                Name = node.Name,
                Count = node.ItemCount,
                CanDescend = node.CanDescend,

                // 記録にはあるが今その場所が無い。外付けを外したときなど。
                // 消さずに残す：「どこに置いたっけ」を一番知りたいのがこの状況
                IsOffline = !Directory.Exists(path),
                CanAddToImport = !_services.Settings.ImportFolders.Contains(path, StringComparer.OrdinalIgnoreCase),
                IsSelected = filter.Selected.Contains(path, StringComparer.OrdinalIgnoreCase),
            };

            row.DescendCommand = new RelayCommand(() => filter.CurrentPath = path);
            row.OpenCommand = new RelayCommand(() => Shell.Reveal(path));
            row.AddToImportCommand = new RelayCommand(() => _ = AddImportFolderAsync(path));
            row.Changed += () =>
            {
                if (row.IsSelected)
                {
                    filter.Add(path);
                }
                else
                {
                    filter.Remove(path);
                }
            };

            filter.Rows.Add(row);
        }
    }

    /// <summary>
    /// 取り込み元に足す。足すだけで、その場では読み込まない。
    /// そのフォルダの商品は既に登録済み（だから木に出ている）なので、
    /// 今すぐ読んでも新しく見つかるものはほぼ無い。
    /// 目的は今後の再スキャンと欠落検出の範囲に入れること。
    /// </summary>
    private async Task AddImportFolderAsync(string path)
    {
        var settings = _services.Settings;
        if (settings.ImportFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var next = settings with { ImportFolders = [.. settings.ImportFolders, path] };
        _services.ReplaceSettings(next);
        await _services.SettingsStore.SaveAsync(next);

        ImportFolderNotice = $"「{path}」を取り込み元に足しました。次の取り込みから、このフォルダも見ます。";
        OnPropertyChanged(nameof(ImportFolderNotice));
        OnPropertyChanged(nameof(HasImportFolderNotice));

        foreach (var filter in ExtraFilters.Where(f => f.Kind == ExtraFilterKind.Folder))
        {
            RebuildFolderRows(filter);
        }
    }

    /// <summary>取り込み元に足した結果。押しても何も起きなかったように見えないよう出す。</summary>
    public string ImportFolderNotice { get; private set; } = string.Empty;

    public bool HasImportFolderNotice => ImportFolderNotice.Length > 0;

    /// <summary>積んでいる種類だけを設定へ書く。値は書かない。</summary>
    private void SaveExtraFilterKinds()
    {
        var kinds = ExtraFilters.Select(filter => filter.Kind.ToString()).ToList();
        var next = _services.Settings with { SearchExtraFilters = kinds };
        _services.ReplaceSettings(next);
        _ = _services.SettingsStore.SaveAsync(next);
    }

    /// <summary>画面遷移のために親を後から渡す（生成順の都合でコンストラクタでは受け取れない）。</summary>
    public void AttachMain(MainViewModel main) => _main = main;

    public void OpenItem(ItemCardViewModel card) => _main?.ShowItem(card.Item);

    /// <summary>
    /// カードの右クリックから使う操作。
    /// UI要素をカードに増やさずに済ませたいので、出口はここへ集める。
    /// </summary>
    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand OpenShopCommand { get; }

    /// <summary>商品ページのURLをコピーする。人に教えるときに要る。</summary>
    public RelayCommand CopyLinkCommand { get; }

    public RelayCommand EditItemCommand { get; }

    public RelayCommand RevealCommand { get; }

    public RelayCommand HideItemCommand { get; }

    /// <summary>商品ページをブラウザで開く。中クリックからも呼ぶ。</summary>
    public void OpenBooth(ItemCardViewModel? card)
    {
        if (card is not null)
        {
            Shell.OpenUrl(card.Item.Booth.Url ?? Core.Booth.BoothClient.ItemPageUrl(card.Item.Id));
        }
    }

    /// <summary>
    /// 商品ページのURLをクリップボードへ。
    ///
    /// 人に商品を教えるときに要る。**落とす／貼るの逆向き**で、
    /// このアプリ同士なら受け取った側がそのまま貼って登録できる。
    /// </summary>
    private static void CopyLink(ItemCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        var url = card.Item.Booth.Url ?? Core.Booth.BoothClient.ItemPageUrl(card.Item.Id);

        try
        {
            System.Windows.Clipboard.SetText(url);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
        }
    }

    /// <summary>ショップはアプリ内の画面へ送る（外のBOOTHではなく、手持ちが見える方）。</summary>
    private void OpenShop(ItemCardViewModel? card)
    {
        var subdomain = card?.Item.Booth.Shop?.Subdomain;
        if (!string.IsNullOrWhiteSpace(subdomain) && _main is not null)
        {
            _ = _main.ShowShopAsync(subdomain, ("検索に戻る", () => _main.ShowSearch()));
        }
    }

    private async Task EditItemAsync(ItemCardViewModel? card)
    {
        if (card is not null && _main is not null)
        {
            await _main.ShowEditAsync([card.Item.Id]);
        }
    }

    /// <summary>手元のファイルをエクスプローラで開く。最初の1件を的にする。</summary>
    private void Reveal(ItemCardViewModel? card)
        => Shell.Reveal(card?.Item.Local.LocalFiles.SelectMany(file => file.Paths).FirstOrDefault());

    /// <summary>
    /// 検索とショップの件数から外す。設定画面から戻せるので確認は挟まない。
    /// </summary>
    private async Task HideItemAsync(ItemCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        // カードが抱えているのは前回の読み込み時の写しなので、非表示だけを名指しして書く。
        // 丸ごと書き戻すと、その間に取り込みや検出が入れた項目まで古い値に戻る
        await _services.Edit.SaveLocalAsync(
            card.Item.Id,
            card.Item.Local with { IsHidden = true },
            LocalOwners.Visibility);

        await ReloadAsync();
    }

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

    public RelayCommand ClearFiltersCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand SendSelectionToEditCommand { get; }

    /// <summary>選択中の件数。0より大きいときだけ操作バーを出す。</summary>
    public int SelectedCount => _cards.Values.Count(card => card.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionText => $"{SelectedCount} 件を選択中";

    private void OnCardSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));

        // 1件でも選ぶと「選ぶ操作」が主になる。カード全体が選択の的になり、
        // 中を見るのは専用のボタンへ移る（カードごとに知らせる必要がある）
        var selecting = HasSelection;
        foreach (var card in _cards.Values)
        {
            card.IsSelectionMode = selecting;
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>今の絞り込み結果を全部選ぶ。画面に出ていないものは選ばない。</summary>
    private void SelectAllMatches()
    {
        foreach (var card in _matches)
        {
            card.IsSelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var card in _cards.Values.Where(card => card.IsSelected))
        {
            card.IsSelected = false;
        }
    }

    /// <summary>
    /// 選んだitemを編集画面のキューに積んで送る。
    /// 絞り込んでから選ぶ流れになるので、並び順はそのまま渡す。
    /// </summary>
    private void SendSelectionToEdit()
    {
        var ids = _matches
            .Where(card => card.IsSelected)
            .Select(card => card.Item.Id)
            .ToList();

        // 絞り込みを変えた後でも、選択したものは全て送る
        foreach (var card in _cards.Values.Where(card => card.IsSelected && !ids.Contains(card.Item.Id)))
        {
            ids.Add(card.Item.Id);
        }

        if (ids.Count == 0 || _main is null)
        {
            return;
        }

        ClearSelection();
        _ = _main.ShowEditAsync(ids);
    }

    /// <summary>
    /// 文字列で探す。スペースでAND、<c>-語</c>で除外、<c>"..."</c>でフレーズ、
    /// <c>OR</c> と <c>( )</c> が使える。
    ///
    /// 既定の対象は 商品名／ショップ名／サブドメイン／メモ／BOOTHタグ。
    /// 本文とパスは当たりすぎて「なぜこれが出たのか」が分からなくなるので、
    /// トグルで明示的に広げたときだけ見る。
    /// </summary>
    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetField(ref _queryText, value))
            {
                // 式の解釈は入力ごとに1回。商品ごとにやると件数ぶん無駄に走る
                _queryNode = Core.Services.SearchQuery.Parse(_queryText);
                ApplyFilters();
            }
        }
    }

    /// <summary>説明文とh2セクションも探すか。</summary>
    public bool SearchBody
    {
        get => _searchBody;
        set
        {
            if (SetField(ref _searchBody, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// ファイルのパスも探すか。
    /// 自分でリネームしたファイルは商品名と一致しないので、パスしか手掛かりが無い場合がある。
    /// </summary>
    public bool SearchPaths
    {
        get => _searchPaths;
        set
        {
            if (SetField(ref _searchPaths, value))
            {
                ApplyFilters();
            }
        }
    }

    public string? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetField(ref _selectedCategory, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>ファイルを持っているものだけに絞る。</summary>
    public bool OwnedOnly
    {
        get => _ownedOnly;
        set
        {
            if (SetField(ref _ownedOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public int TotalCount => _allItems.Count;

    public int ShopCount => _allItems
        .Select(item => item.Booth.Shop?.Subdomain)
        .Where(subdomain => subdomain is not null)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    public int NeedsEditCount => _allItems.Count(item => item.Local.UserTags.Count == 0);

    public string ResultSummary => $"{_matches.Count} 件";

    public bool IsEmpty => !IsLoading && _matches.Count == 0;

    /// <summary>
    /// 結果一覧の表示幅が変わったときに呼ぶ。列数が変わったときだけ行を組み直す。
    /// </summary>
    public void SetViewportWidth(double width)
    {
        var columns = Math.Max(1, (int)((width - ResultsPadding) / CardSlotWidth));
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        RebuildRows();
    }

    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            var loaded = await _services.Store.Items.LoadAllAsync();
            _allItems = loaded.Items
                .OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
                .ThenBy(item => item.Booth.Name, StringComparer.CurrentCulture)
                .ToList();

            // 検索対象の文字列はここで作る。正規化は全商品の説明文を畳むので、
            // UIスレッドに乗せると読み込みのたびに画面が固まる
            _haystacks = _allItems.ToDictionary(
                item => item.Id,
                Core.Services.SearchText.Build,
                StringComparer.Ordinal);

            // 「更新の有無」は要確認の未読と同じものを指す。既読にすれば条件から外れる
            _unreadItemIds = _services.Notifications.Load()
                .Where(record => !record.IsRead && record.ItemId is not null)
                .Select(record => record.ItemId!)
                .ToHashSet(StringComparer.Ordinal);

            RunOnUiThread(() =>
            {
                // カードは絞り込みのたびには作り直さず、itemごとに1つを使い回す。
                // 作り直すと、件数に比例した生成コストがキー入力のたびに掛かる。
                _cards.Clear();
                foreach (var item in _allItems)
                {
                    var card = ToCard(item);
                    card.SelectionChanged += OnCardSelectionChanged;
                    _cards[item.Id] = card;
                }

                OnCardSelectionChanged();

                Categories.Clear();
                Categories.Add(new CategoryOption { Name = AllCategories, IsAll = true });
                foreach (var category in _allItems
                    .Select(item => item.Booth.Category?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.CurrentCulture)
                    .OrderBy(name => name, StringComparer.CurrentCulture))
                {
                    Categories.Add(new CategoryOption { Name = category! });
                }

                BuildFacets();

                _selectedCategory ??= AllCategories;
                OnPropertyChanged(nameof(SelectedCategory));
                ApplyFilters();
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(ShopCount));
                OnPropertyChanged(nameof(NeedsEditCount));
            });
        }
        finally
        {
            IsLoading = false;
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

    /// <summary>
    /// このuserTagだけで絞り込んだ状態にする。タグの管理から「この分類が付いているitem」を
    /// 見に来る導線。件数だけ見せられても、消していいか統合していいかは判断できない。
    /// </summary>
    public void ShowOnly(string top, string? sub = null)
    {
        ClearFilters();

        var filter = TagFilters.FirstOrDefault(entry =>
            string.Equals(entry.Name, top, StringComparison.CurrentCultureIgnoreCase));

        if (filter is null)
        {
            return;
        }

        filter.IsSelected = true;

        if (sub is not null)
        {
            filter.Subs
                .FirstOrDefault(entry => string.Equals(entry.Name, sub, StringComparison.CurrentCultureIgnoreCase))
                ?.SetSilently(true);
        }

        ApplyFilters();
    }

    /// <summary>
    /// この属性で評価済みのitemだけを出す。軸を 0〜100 で足すと、
    /// 「評価が入っているもの」がそのまま残る（未評価は軸を足した時点で外れる）。
    /// </summary>
    public void ShowOnlyAttribute(string name)
    {
        ClearFilters();
        AddAttributeFilter(name);
    }

    /// <summary>
    /// このアバターに対応している商品だけを出す。アバター管理からの導線。
    /// 既定では素体経由も含める（「対応が確認できていないもの」を既定で隠さない方針に合わせる）。
    /// </summary>
    public void ShowOnlyAvatar(string avatarItemId, string displayName, bool includeViaBase = true)
    {
        ClearFilters();
        _avatarFilterId = avatarItemId;
        _avatarFilterName = displayName;
        _includeViaBase = includeViaBase;
        _compatibility = null;
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));
        OnPropertyChanged(nameof(IncludeViaBase));
        ApplyFilters();
    }

    /// <summary>このカテゴリだけで絞り込む。統計の容量内訳から中身を見に来る導線。</summary>
    public void ShowOnlyCategory(string category)
    {
        ClearFilters();
        _selectedCategory = category;
        OnPropertyChanged(nameof(SelectedCategory));
        ApplyFilters();
    }

    /// <summary>
    /// 記録はあるのに置き場所が分からなくなったitemだけを出す。統計の積み残しからの導線。
    /// 消したのか移動しただけなのかはユーザにしか分からないので、判断できる形で並べる。
    /// </summary>
    public void ShowOnlyMissing()
    {
        ClearFilters();
        _missingOnly = true;
        ApplyFilters();
    }

    private void ClearFilters()
    {
        _queryText = string.Empty;
        _selectedCategory = AllCategories;
        _ownedOnly = false;
        _missingOnly = false;
        _givenOnly = false;
        _receivedOnly = false;
        _avatarFilterId = null;
        _avatarFilterName = null;
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));

        foreach (var tag in TagFilters)
        {
            tag.Reset();
        }

        foreach (var attribute in AttributeFilters)
        {
            attribute.Min = 0;
            attribute.Max = 100;
        }

        // 積んだタグは「条件をクリア」で外す。属性と違って幅を戻す概念が無いため
        BoothTagFilters.Clear();
        RefreshBoothTagSuggestions();

        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(OwnedOnly));
        OnPropertyChanged(nameof(GivenOnly));
        OnPropertyChanged(nameof(ReceivedOnly));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _matches = SortItems(_allItems.Where(item => Matches(item)))
            .Select(item => _cards[item.Id])
            .ToList();

        RefreshFacetCounts();
        RebuildRows();

        foreach (var filter in ExtraFilters.Where(f => f.Kind == ExtraFilterKind.Folder && f.Rows.Count == 0))
        {
            RebuildFolderRows(filter);
        }

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>
    /// 今どの条件で絞っているかを1行で示す。
    /// 「なぜこの結果になったか」が結果の隣で読めるようにするため。
    /// </summary>
    public string FilterSummary => string.Join(" / ", FilterParts());

    /// <summary>
    /// 効いている条件の数。畳んだパネルに出す。
    /// 畳むと条件そのものが見えなくなるので、数だけでも残さないと
    /// 「なぜか商品が少ない」の原因を探す場所が無くなる。
    /// </summary>
    public int ActiveFilterCount => FilterParts().Count;

    /// <summary>効いている条件を1つずつ文にする。要約にも件数にも同じものを使う。</summary>
    private List<string> FilterParts()
    {
        var parts = new List<string>();

        var tags = TagFilters.Where(filter => filter.IsSelected).ToList();
        foreach (var tag in tags)
        {
            var subs = tag.SelectedSubs.ToList();
            parts.Add(subs.Count == 0 ? tag.Name : $"{tag.Name}（{string.Join("・", subs)}）");
        }

        foreach (var attribute in AttributeFilters)
        {
            parts.Add($"{attribute.Name} {attribute.Min}〜{attribute.Max}%");
        }

        foreach (var tag in BoothTagFilters)
        {
            parts.Add($"タグ：{tag.Name}");
        }

        foreach (var extra in ExtraFilters.Where(filter => filter.IsActive))
        {
            parts.Add(extra.SummaryText);
        }

        if (_ownedOnly)
        {
            parts.Add("所持のみ");
        }

        if (_missingOnly)
        {
            parts.Add("ファイルが見つからない");
        }

        if (_givenOnly && _receivedOnly)
        {
            parts.Add("贈った・貰った");
        }
        else if (_givenOnly)
        {
            parts.Add("贈った");
        }
        else if (_receivedOnly)
        {
            parts.Add("貰った");
        }

        if (_avatarFilterName is not null)
        {
            parts.Add(_includeViaBase ? $"{_avatarFilterName}（素体経由を含む）" : _avatarFilterName);
        }

        if (!string.IsNullOrEmpty(_selectedCategory) && _selectedCategory != AllCategories)
        {
            parts.Add(_selectedCategory);
        }

        return parts;
    }

    public bool HasActiveFilters => ActiveFilterCount > 0;

    /// <summary>
    /// 0件のときの案内。絞って0件なのか、そもそも空なのかで次にやることが違う。
    /// </summary>
    public string EmptyHint => _allItems.Count == 0
        ? "「取り込み」からフォルダを読み込んでください"
        : HasActiveFilters || _queryText.Trim().Length > 0
            ? "「条件をクリア」で全件に戻ります"
            : "「取り込み」からフォルダを読み込んでください";

    /// <summary>絞り込み結果を、現在の列数で行に切り直す。</summary>
    private void RebuildRows()
    {
        Rows.Clear();
        for (var start = 0; start < _matches.Count; start += _columns)
        {
            Rows.Add(new CardRow
            {
                Cards = _matches.GetRange(start, Math.Min(_columns, _matches.Count - start)),
            });
        }
    }

    /// <summary>
    /// 絞り込みの軸。ファセットの件数を数えるとき、自分の軸だけを外して数えるために使う。
    /// 外さないと、userTagで「衣装」を選んだ瞬間に同じ欄の他のuserTagが全部0件になる。
    /// </summary>
    private enum FilterAxis
    {
        Owned,
        Avatar,
        Category,
        UserTag,
        BoothTag,
        Attribute,
        Extra,
    }

    /// <param name="except">この軸だけ適用しない。ファセットの件数を数えるときに指定する。</param>
    private bool Matches(ItemRecord item, FilterAxis? except = null)
    {
        if (except != FilterAxis.Owned)
        {
            if (_ownedOnly && !item.IsDownloaded)
            {
                return false;
            }

            if (_missingOnly && !item.Local.LocalFiles.Any(file => file.Paths.Count == 0))
            {
                return false;
            }

            // 贈った・貰ったは所持とは別の軸。貰ったものは手元にあり、贈ったものは手元に無いので、
            // 同じ札に入れると読み違える。両方選んだ場合は「どちらかに当てはまるもの」
            if (_givenOnly || _receivedOnly)
            {
                var matched = (_givenOnly && Core.Services.Purchases.WasGiven(item))
                    || (_receivedOnly && Core.Services.Purchases.WasReceived(item));

                if (!matched)
                {
                    return false;
                }
            }
        }

        // 対応アバターでの絞り込み。素体経由は推定なので、含めるかを選べるようにする
        if (except != FilterAxis.Avatar && _avatarFilterId is not null)
        {
            var match = (_compatibility ??= Core.Services.AvatarCompatibilityIndex.Build(
                _services.Store.Avatars.Load())).MatchFor(item.Local, _avatarFilterId);

            var accepted = _includeViaBase
                ? match is Core.Services.AvatarMatch.Direct or Core.Services.AvatarMatch.ViaBase
                : match == Core.Services.AvatarMatch.Direct;

            if (!accepted)
            {
                return false;
            }
        }

        if (except != FilterAxis.Category
            && !string.IsNullOrEmpty(_selectedCategory)
            && _selectedCategory != AllCategories
            && !string.Equals(item.Booth.Category?.Name, _selectedCategory, StringComparison.CurrentCulture))
        {
            return false;
        }

        // 選ばれたトップのいずれかに当てはまればよい（別のトップ同士はORで扱う）
        if (except != FilterAxis.UserTag)
        {
            var selectedTags = TagFilters.Where(filter => filter.IsSelected).ToList();
            if (selectedTags.Count > 0 && !selectedTags.Any(filter => filter.Matches(item)))
            {
                return false;
            }
        }

        // 属性は軸ごとにANDで積む。片側でも動かした軸では未評価が落ちる
        if (except != FilterAxis.Attribute && AttributeFilters.Any(filter => !filter.Matches(item)))
        {
            return false;
        }

        // BOOTHタグも積んだものをANDで。積むこと自体が「このタグで絞る」という意思表示
        if (except != FilterAxis.BoothTag && BoothTagFilters.Any(filter => !filter.Matches(item)))
        {
            return false;
        }

        // 積んだ条件は軸ごとにANDで積む。積むこと自体が「この軸で選ぶ」という意思表示
        if (except != FilterAxis.Extra
            && ExtraFilters.Any(filter => !filter.Matches(item, _unreadItemIds)))
        {
            return false;
        }

        return MatchesQuery(item);
    }

    /// <summary>
    /// 選択肢の横に出す件数を数え直す。
    ///
    /// 数えるのは「今の他の条件を適用した後」の件数。全体の件数だと、押してから0件と分かる。
    /// ただし自分の軸は自分を除いて数える（<see cref="FilterAxis"/> の説明を参照）。
    /// 0件の選択肢は消さずに薄く出す。消えると「さっきあった項目が無い」と探すことになる。
    /// </summary>
    private void RefreshFacetCounts()
    {
        var forCategory = _allItems.Where(item => Matches(item, FilterAxis.Category)).ToList();
        foreach (var option in Categories)
        {
            option.Count = option.IsAll
                ? forCategory.Count
                : forCategory.Count(item =>
                    string.Equals(item.Booth.Category?.Name, option.Name, StringComparison.CurrentCulture));
        }

        var forTags = _allItems.Where(item => Matches(item, FilterAxis.UserTag)).ToList();
        foreach (var filter in TagFilters)
        {
            filter.Count = forTags.Count(item => item.Local.UserTags.Any(entry =>
                string.Equals(entry.Top, filter.Name, StringComparison.CurrentCultureIgnoreCase)));

            foreach (var sub in filter.Subs)
            {
                sub.Count = forTags.Count(item => item.Local.UserTags.Any(entry =>
                    string.Equals(entry.Top, filter.Name, StringComparison.CurrentCultureIgnoreCase)
                    && entry.Subs.Contains(sub.Name, StringComparer.CurrentCultureIgnoreCase)));
            }
        }

        // 積んだタグは自分の軸を除いて数える。含めて数えると、積んだ瞬間に
        // 「今の結果と同じ件数」しか出ず、他のタグを足す判断ができない
        var forBoothTags = _allItems.Where(item => Matches(item, FilterAxis.BoothTag)).ToList();
        foreach (var filter in BoothTagFilters)
        {
            filter.Count = forBoothTags.Count(filter.Matches);
        }

        var forOwned = _allItems.Where(item => Matches(item, FilterAxis.Owned)).ToList();
        OwnedCount = forOwned.Count(item => item.IsDownloaded);
        MissingCount = forOwned.Count(item => item.Local.LocalFiles.Any(file => file.Paths.Count == 0));
        GivenCount = forOwned.Count(Core.Services.Purchases.WasGiven);
        ReceivedCount = forOwned.Count(Core.Services.Purchases.WasReceived);

        foreach (var name in new[]
        {
            nameof(OwnedCount), nameof(MissingCount), nameof(GivenCount), nameof(ReceivedCount),
            nameof(HasGiftRecords),
        })
        {
            OnPropertyChanged(name);
        }
    }

    /// <summary>「ファイルを持っているものだけ」を押したときの件数。</summary>
    public int OwnedCount { get; private set; }

    /// <summary>「ファイルが見つからない」を押したときの件数。</summary>
    public int MissingCount { get; private set; }

    /// <summary>
    /// 贈った・貰った商品の数。回数ではなく商品数（1つの商品を3人に贈っても1件）。
    /// 統計側は「贈った回数」「贈答に使った額」と書き分ける。
    /// </summary>
    public int GivenCount { get; private set; }

    public int ReceivedCount { get; private set; }

    /// <summary>贈答の記録が1件も無ければ、この行ごと出さない。</summary>
    public bool HasGiftRecords => GivenCount > 0 || ReceivedCount > 0;

    /// <summary>贈った商品だけに絞る。</summary>
    public bool GivenOnly
    {
        get => _givenOnly;
        set
        {
            if (SetField(ref _givenOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>貰った商品だけに絞る。</summary>
    public bool ReceivedOnly
    {
        get => _receivedOnly;
        set
        {
            if (SetField(ref _receivedOnly, value))
            {
                ApplyFilters();
            }
        }
    }

    private bool MatchesQuery(ItemRecord item)
        => !_haystacks.TryGetValue(item.Id, out var haystack)
            || Core.Services.SearchQuery.Matches(_queryNode, haystack, _searchBody, _searchPaths);

    /// <summary>
    /// 表示順を適用する。属性で並べたときは、未評価を昇順・降順どちらでも常に末尾に置く。
    /// 未評価は「値が小さい」のではなく「値が無い」ので、0として混ぜると誤読させる。
    /// </summary>
    private IEnumerable<ItemRecord> SortItems(IEnumerable<ItemRecord> items)
    {
        var sort = _sort;

        if (sort.Kind == SortKind.Attribute && sort.AttributeName is { } attributeName)
        {
            var rated = items
                .Where(item => item.Local.Attributes.ContainsKey(attributeName))
                .ToList();
            var unrated = items
                .Where(item => !item.Local.Attributes.ContainsKey(attributeName))
                .OrderBy(item => item.Booth.Name, StringComparer.CurrentCulture);

            var ordered = sort.Descending
                ? rated.OrderByDescending(item => item.Local.Attributes[attributeName])
                : rated.OrderBy(item => item.Local.Attributes[attributeName]);

            return ordered.Concat(unrated);
        }

        return sort.Kind switch
        {
            SortKind.Name => sort.Descending
                ? items.OrderByDescending(item => item.Booth.Name, StringComparer.CurrentCulture)
                : items.OrderBy(item => item.Booth.Name, StringComparer.CurrentCulture),
            SortKind.Size => sort.Descending
                ? items.OrderByDescending(item => item.LogicalSizeBytes)
                : items.OrderBy(item => item.LogicalSizeBytes),
            SortKind.WishList => sort.Descending
                ? items.OrderByDescending(item => item.Booth.WishListsCount)
                : items.OrderBy(item => item.Booth.WishListsCount),
            _ => sort.Descending
                ? items.OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
                    .ThenBy(item => item.Booth.Name, StringComparer.CurrentCulture)
                : items.OrderBy(item => item.Local.AcquiredAt ?? DateOnly.MaxValue)
                    .ThenBy(item => item.Booth.Name, StringComparer.CurrentCulture),
        };
    }


    private ItemCardViewModel ToCard(ItemRecord item)
    {
        var missing = item.Local.LocalFiles.Any(file => file.Paths.Count == 0);

        return new ItemCardViewModel(item, _thumbnails, _services.Paths.ItemImagesDir(item.Id))
        {
            Name = item.Booth.Name ?? item.Id,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            SizeText = item.IsDownloaded ? FormatSize(item.LogicalSizeBytes) : "未取得",
            IsOwned = item.IsDownloaded,
            NeedsEdit = item.Local.UserTags.Count == 0,
            HasMissingFile = missing,
            UserTagText = string.Join(" / ", item.Local.UserTags.Select(tag => tag.Top)),
        };
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
