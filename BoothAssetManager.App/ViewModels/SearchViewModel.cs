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
    private bool _searchAlternates;
    private string? _selectedCategory;
    private bool _ownedOnly;
    private bool _missingOnly;
    private bool _givenOnly;
    private bool _receivedOnly;
    private string? _avatarFilterId;
    private string? _avatarFilterName;

    /// <summary>ショップで絞っているときの鍵（サブドメイン、手元だけのショップは local: 付き）と見せる名前。</summary>
    private string? _shopFilterKey;
    private string? _shopFilterName;
    private bool _includeViaBase = true;
    private Core.Services.AvatarCompatibilityIndex? _compatibility;
    private bool _isLoading;
    private bool _isFilterPanelCollapsed;
    private int _columns = 1;
    private SortOption _sort = DefaultSort;

    /// <summary>「最近」の足跡。絞り込み1回ぶんの間だけ持つ写し</summary>
    private RecentTimes? _recentTimes;

    /// <summary>改変から引いた「どのアバターにどの商品を使ったか」。null は「まだ読んでいない」</summary>
    private ModificationUsage? _modificationUsage;
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
        AddSelectionToFavoritesCommand = new RelayCommand(() => _ = AddSelectionToFavoritesAsync(), () => SelectedCount > 0);
        AddSelectionToModificationCommand = new RelayCommand(() => _ = AddSelectionToModificationAsync(), () => SelectedCount > 0);
        SendSelectionToUnityCommand = new RelayCommand(() => _ = SendSelectionToUnityAsync(), () => SelectedCount > 0 && !IsSendingToUnity);
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
        ClearShopFilterCommand = new RelayCommand(ClearShopFilter);
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

    public RelayCommand ClearShopFilterCommand { get; }

    /// <summary>
    /// ショップで絞っているか。ショップ画面の「検索でこのショップの商品を絞る」から入る（#55）。
    /// ショップ画面に絞り込みを作り直すより、検索の絞り込みをそのまま使えた方が同じ操作で済む（ユーザ判断）。
    /// </summary>
    public bool HasShopFilter => _shopFilterKey is not null;

    public string ShopFilterName => _shopFilterName ?? string.Empty;

    private void ClearShopFilter()
    {
        _shopFilterKey = null;
        _shopFilterName = null;
        RaiseShopFilterChanged();
        ApplyFilters();
    }

    private void RaiseShopFilterChanged()
    {
        OnPropertyChanged(nameof(HasShopFilter));
        OnPropertyChanged(nameof(ShopFilterName));
    }

    /// <summary>今アバターで絞っているか。絞っているときだけ、外す手段と素体経由の切り替えを出す。</summary>
    public bool HasAvatarFilter => _avatarFilterId is not null;

    public string AvatarFilterName => _avatarFilterName ?? string.Empty;

    private bool _avatarFilterHasBase;

    /// <summary>
    /// 今絞っているアバターが共通素体に属しているか。
    ///
    /// **属していないときに素体経由の切り替えを出さない。**
    /// 素体が無ければ経由する先も無いので、どちらに倒しても結果が変わらない。
    /// 効かない選択肢を並べると、結果が変わらないのを見て「壊れている」と読まれる。
    /// </summary>
    public bool AvatarFilterHasBase => _avatarFilterHasBase;

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
        _avatarFilterHasBase = HasBase(id);
        _compatibility = null;
        RaiseAvatarFilterChanged();
        ApplyFilters();
    }

    private void ClearAvatarFilter()
    {
        _avatarFilterId = null;
        _avatarFilterName = null;
        _avatarFilterHasBase = false;
        RaiseAvatarFilterChanged();
        ApplyFilters();
    }

    /// <summary>このアバターが共通素体グループに属しているか。登録簿を引くだけで通信は要らない。</summary>
    private bool HasBase(string avatarItemId)
        => _services.Store.Avatars.Load().Entries
            .Any(entry => entry.ItemId == avatarItemId && !string.IsNullOrWhiteSpace(entry.BaseName));

    private void RaiseAvatarFilterChanged()
    {
        OnPropertyChanged(nameof(HasAvatarFilter));
        OnPropertyChanged(nameof(AvatarFilterName));
        OnPropertyChanged(nameof(AvatarFilterHasBase));
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
        RefreshExtraSuggestions();
    }

    /// <summary>
    /// 候補から積む条件に、候補を入れる。
    ///
    /// **持ち主をこちらにする。**候補はアバターの登録簿から作るもので、
    /// 条件そのものではない。条件の側に持たせると、登録簿が変わっても古いまま残る。
    /// </summary>
    private void RefreshExtraSuggestions()
    {
        var registry = _services.Store.Avatars.Load();

        var baseNames = registry.Entries
            .Select(entry => entry.BaseName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();

        foreach (var filter in ExtraFilters.Where(filter => filter.IsSuggest))
        {
            var source = filter.Kind switch
            {
                ExtraFilterKind.UsedOn => AvatarSuggestions.ToList(),
                ExtraFilterKind.BaseAvatar => baseNames,
                _ => [],
            };

            filter.Suggestions.Clear();
            foreach (var value in source)
            {
                filter.Suggestions.Add(value);
            }

            filter.NoteSuggestionsChanged();
        }
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

        // **積んだ直後に候補を入れる。**入れないと「候補がありません」と出て、
        // 積んだのに何も選べない条件になる
        if (filter.IsSuggest)
        {
            RefreshExtraSuggestions();
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

    /// <summary>
    /// 商品を開く。**ここで検索の履歴を1件積む**（ユーザ指示）。
    ///
    /// 絞り込みは打つたびに変わるので、変わるたびに残すとゴミになる。
    /// 「探して見つけた」が一区切りで、実りのあった検索だけが残る。
    /// </summary>
    /// <summary>
    /// 裏の取得がこの商品の画像を置いた（UIスレッドで呼ばれる）。カードを描き直させる。
    /// 一覧ごと組み直さないのは、絞り込みやスクロール位置を崩さないため。
    /// </summary>
    public void NoteItemImagesSaved(string itemId)
    {
        if (_cards.TryGetValue(itemId, out var card))
        {
            card.RefreshImages();
        }
    }

    public void OpenItem(ItemCardViewModel card)
    {
        _ = RecordHistoryAsync();
        _main?.ShowItem(card.Item);
    }

    // ---- 検索の履歴 ----

    /// <summary>
    /// 履歴のスロット。新しいものが先。
    ///
    /// 検索欄の上に横に並べる。押すとその条件に戻る。
    /// </summary>
    public ObservableCollection<SearchHistorySlot> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    /// <summary>いまの画面の状態を1件の記録にする。</summary>
    private Core.Models.SearchHistoryEntry CurrentSearch()
    {
        var tags = new List<string>();
        foreach (var tag in TagFilters)
        {
            // 入れ子は「親/子」で持つ。親だけ選んでいる場合は親の名前だけ
            if (tag.HasSelectedSubs)
            {
                tags.AddRange(tag.SelectedSubs.Select(sub => $"{tag.Name}/{sub}"));
            }
            else if (tag.IsSelected)
            {
                tags.Add(tag.Name);
            }
        }

        return new Core.Models.SearchHistoryEntry
        {
            Text = _queryText,
            Category = _selectedCategory == AllCategories ? null : _selectedCategory,
            OwnedOnly = _ownedOnly,
            MissingOnly = _missingOnly,
            GivenOnly = _givenOnly,
            ReceivedOnly = _receivedOnly,
            SearchBody = _searchBody,
            SearchPaths = _searchPaths,
            SearchAlternates = _searchAlternates,
            AvatarName = _avatarFilterName,
            AvatarId = long.TryParse(_avatarFilterId, out var avatarId) ? avatarId : null,
            AvatarHasBase = _avatarFilterHasBase,
            UserTags = tags,
            BoothTags = BoothTagFilters.Select(tag => tag.Name).ToList(),

            // 全開の軸は条件になっていないので持たない
            Attributes = AttributeFilters
                .Where(filter => filter.Min > 0 || filter.Max < 100)
                .Select(filter => new Core.Models.AttributeRange(filter.Name, filter.Min, filter.Max))
                .ToList(),
            Sort = _sort.Label == DefaultSort.Label ? null : _sort.Label,
            UsedAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// 履歴を積んで保存する。
    ///
    /// **同じ条件は積まない**（ユーザ指示）。指紋が同じなら上に持ち上げるだけ。
    /// 判断は <see cref="Core.Services.SearchHistory.Add"/> に集めてある。
    /// </summary>
    private async Task RecordHistoryAsync()
    {
        var entry = CurrentSearch();
        if (entry.IsEmpty)
        {
            return;
        }

        var stored = _services.Store.SearchHistory.Load();
        var updated = Core.Services.SearchHistory.Add(
            stored.Entries,
            entry,
            _services.Store.Settings.Load().SearchHistoryCount);

        await _services.Store.SearchHistory.SaveAsync(new Core.Services.SearchHistoryList { Entries = updated });
        LoadHistory(updated);
    }

    /// <summary>スロットを組み直す。</summary>
    private void LoadHistory(IReadOnlyList<Core.Models.SearchHistoryEntry> entries)
    {
        History.Clear();
        foreach (var entry in entries)
        {
            History.Add(new SearchHistorySlot(entry, ApplyHistory, RemoveHistoryAsync));
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    /// <summary>保存済みの履歴を読んでスロットに出す。画面を開くときに1回。</summary>
    public void RestoreHistory()
        => LoadHistory(_services.Store.SearchHistory.Load().Entries);

    private async Task RemoveHistoryAsync(Core.Models.SearchHistoryEntry entry)
    {
        var stored = _services.Store.SearchHistory.Load();
        var updated = Core.Services.SearchHistory.Remove(stored.Entries, entry.Fingerprint);
        await _services.Store.SearchHistory.SaveAsync(new Core.Services.SearchHistoryList { Entries = updated });
        LoadHistory(updated);
    }

    /// <summary>
    /// 履歴の条件に戻す。
    ///
    /// **まず全部クリアしてから積む。**今の条件の上に重ねると、
    /// 履歴に無い条件が残って「押したのに違う結果」になる。
    /// </summary>
    private void ApplyHistory(Core.Models.SearchHistoryEntry entry)
    {
        ClearFilters();

        _queryText = entry.Text;
        _queryNode = Core.Services.SearchQuery.Parse(entry.Text);
        _selectedCategory = entry.Category ?? AllCategories;
        _ownedOnly = entry.OwnedOnly;
        _missingOnly = entry.MissingOnly;
        _givenOnly = entry.GivenOnly;
        _receivedOnly = entry.ReceivedOnly;
        _searchBody = entry.SearchBody;
        _searchPaths = entry.SearchPaths;
        _searchAlternates = entry.SearchAlternates;

        _avatarFilterName = entry.AvatarName;
        _avatarFilterId = entry.AvatarId?.ToString();
        _avatarFilterHasBase = entry.AvatarHasBase;
        RaiseAvatarFilterChanged();

        foreach (var tag in TagFilters)
        {
            // 親だけの指定と「親/子」の両方を受ける
            if (entry.UserTags.Contains(tag.Name))
            {
                tag.SetSilently(true);
            }

            foreach (var sub in tag.Subs)
            {
                sub.SetSilently(entry.UserTags.Contains($"{tag.Name}/{sub.Name}"));
            }
        }

        foreach (var tag in entry.BoothTags)
        {
            AddBoothTagFilter(tag);
        }

        foreach (var range in entry.Attributes)
        {
            AddAttributeFilter(range.Name);
            if (AttributeFilters.FirstOrDefault(filter =>
                    string.Equals(filter.Name, range.Name, StringComparison.CurrentCultureIgnoreCase)) is { } filter)
            {
                filter.Min = range.Min;
                filter.Max = range.Max;
            }
        }

        if (entry.Sort is not null
            && SortOptions.FirstOrDefault(option => option.Label == entry.Sort) is { } sort)
        {
            _sort = sort;
        }

        foreach (var name in new[]
        {
            nameof(QueryText), nameof(SelectedCategory), nameof(OwnedOnly),
            nameof(GivenOnly), nameof(ReceivedOnly), nameof(SearchBody),
            nameof(SearchPaths), nameof(SearchAlternates), nameof(Sort),
        })
        {
            OnPropertyChanged(name);
        }

        ApplyFilters();
    }

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
            // BOOTHに無い商品には送り先が無い（押しても何も起きないのが正しい）
            if (Core.Booth.BoothClient.PageUrlFor(card.Item) is { } url)
            {
                Shell.OpenUrl(url);
            }
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

        if (Core.Booth.BoothClient.PageUrlFor(card.Item) is not { } url)
        {
            return;
        }

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
    /// <summary>
    /// お気に入りの星を切り替える（#70・ユーザ指示「searchのitem要素で空いている下の方に星のトグル」）。
    ///
    /// 星だけを名指しして書く。カードが抱えているのは前回の読み込み時の写しで、
    /// 丸ごと書き戻すとその間に取り込みや検出が入れた項目まで古い値に戻る。
    /// 一覧ごと読み直さないのは、星1つのためにスクロール位置や並びを崩さないため。
    /// </summary>
    public async Task ToggleFavoriteAsync(ItemCardViewModel card)
    {
        var next = !card.IsFavorite;
        card.IsFavorite = next;

        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SaveItemLocal(
            card.Item.Id, card.Item.Local with { IsFavorite = next }, LocalOwners.Favorite));

        if (result is Core.Commands.CommandResult.Failed)
        {
            // 書けなかったら戻す。付いたように見えて次に開くと消えている、を起こさない
            card.IsFavorite = !next;
            return;
        }

        // 絞り込みは読み込み時の一覧を見るので、そちらの写しも差し替える
        var index = _allItems.FindIndex(item => item.Id == card.Item.Id);
        if (index >= 0)
        {
            _allItems[index] = _allItems[index] with { Local = _allItems[index].Local with { IsFavorite = next } };
        }

        if (ExtraFilters.Any(filter => filter.Kind == ExtraFilterKind.Favorite))
        {
            ApplyFilters();
        }
    }

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

    /// <summary>選んだ物をまとめてお気に入りに入れる（#44）。</summary>
    public RelayCommand AddSelectionToFavoritesCommand { get; }

    /// <summary>選んだ物をまとめて改変に足す（#44）。</summary>
    public RelayCommand AddSelectionToModificationCommand { get; }

    /// <summary>選んだ物の unitypackage を、開いている Unity へ順に送る（#69）。</summary>
    public RelayCommand SendSelectionToUnityCommand { get; }

    private bool _isSendingToUnity;

    /// <summary>送っている最中か。二重に始めさせない。</summary>
    public bool IsSendingToUnity
    {
        get => _isSendingToUnity;
        private set
        {
            if (SetField(ref _isSendingToUnity, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _unityQueueText = string.Empty;

    /// <summary>今どこまで送ったか。取り込み画面は Unity 側に出るので、こちらには進み具合だけを出す。</summary>
    public string UnityQueueText
    {
        get => _unityQueueText;
        private set
        {
            if (SetField(ref _unityQueueText, value))
            {
                OnPropertyChanged(nameof(HasUnityQueueText));
            }
        }
    }

    public bool HasUnityQueueText => UnityQueueText.Length > 0;

    /// <summary>
    /// 選んだ商品の unitypackage を、選んだ順（表示中の並び）に1件ずつ Unity へ積む（#69・ユーザ追加要望）。
    /// 1件ずつ取り込み画面が出るので、利用者が Import か Cancel を押すと次が出る。
    /// </summary>
    private async Task SendSelectionToUnityAsync()
    {
        const string title = "Unityへ順に送る";
        var cards = SelectedCards();
        var queue = new List<(ItemCardViewModel Card, Core.Services.UnityPackageEntry Package)>();
        var nothing = new List<string>();

        foreach (var card in cards)
        {
            var packages = Services.UnityImportQueue.PackagesOf(card.Item);
            if (packages.Count == 0)
            {
                nothing.Add(card.Name);
                continue;
            }

            queue.AddRange(packages.Select(package => (card, package)));
        }

        if (queue.Count == 0)
        {
            System.Windows.MessageBox.Show(
                "選んだ商品には、Unityへ送れるもの（zip の中の .unitypackage）が入っていませんでした。",
                title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (Services.UnityTargetPicker.Pick(title) is not { } editor)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"{queue.Count} 件を、Unityの「{editor.ProjectName ?? "名前の分からないプロジェクト"}」へ順に送ります。\n\n"
            + "1件ずつ取り込み画面が出ます。Unity側で「Import」（入れない物は「Cancel」）を押すと、次の1件が出ます。\n"
            + "1つの zip に依存するものが入っていれば、zip に入っている順に送ります。"
            + (nothing.Count > 0 ? $"\n\n送れるものが無い {nothing.Count} 件は飛ばします。" : string.Empty),
            title,
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.OK);

        if (confirm != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsSendingToUnity = true;
        try
        {
            var progress = new Progress<Services.UnityQueueProgress>(report => UnityQueueText = report.Text);
            var outcomes = await Services.UnityImportQueue.RunAsync(
                editor.ProcessId, queue.Select(entry => entry.Package).ToList(), progress, CancellationToken.None);

            // 「使った」の足跡。Unityへ送ったことが一番強い証拠（Unityへ送る と同じ扱い）
            var opened = outcomes.Where(outcome => outcome.Opened).Select(outcome => outcome.Package).ToHashSet();
            foreach (var itemId in queue.Where(entry => opened.Contains(entry.Package)).Select(entry => entry.Card.Item.Id).Distinct())
            {
                _ = _services.Recent.TouchAsync(itemId, Core.Services.RecentKind.Used);
            }

            var failed = outcomes.Where(outcome => !outcome.Opened).ToList();
            UnityQueueText = string.Empty;
            System.Windows.MessageBox.Show(
                failed.Count == 0
                    ? $"{opened.Count} 件の取り込み画面を順に出しました。"
                    : $"{opened.Count} 件の取り込み画面を出しました。{failed.Count} 件は送れませんでした：\n\n"
                        + string.Join("\n", failed.Select(outcome => $"・{outcome.Package.Name}：{outcome.Problem}").Distinct().Take(6)),
                title,
                System.Windows.MessageBoxButton.OK,
                failed.Count == 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsSendingToUnity = false;
        }
    }

    /// <summary>選んだカード。表示中の並びを先に、絞り込みを変えて見えなくなった物を後に。</summary>
    private List<ItemCardViewModel> SelectedCards()
    {
        var cards = _matches.Where(card => card.IsSelected).ToList();
        cards.AddRange(_cards.Values.Where(card => card.IsSelected && !cards.Contains(card)));
        return cards;
    }

    /// <summary>
    /// 選んだ物に星を付ける。付いている物はそのまま（外す操作ではない）。
    /// 選択は解かない——続けて「改変に足す」などをしたいことがある。
    /// </summary>
    private async Task AddSelectionToFavoritesAsync()
    {
        foreach (var card in SelectedCards().Where(card => !card.IsFavorite))
        {
            await ToggleFavoriteAsync(card);
        }
    }

    /// <summary>
    /// 選んだ物を1つの改変に足す。どの改変かは商品ページの「改変に足す」と同じ画面で1回だけ選ぶ。
    ///
    /// **既にその改変に入っている商品は重ねて足さない。**まとめて足すときは、
    /// どれが入っていたかを1件ずつ覚えていないので、同じ物が2行並ぶと記録を確かめにくい
    /// （別の版を2回入れたいときは、商品ページから1件ずつ足せる）。
    /// </summary>
    private async Task AddSelectionToModificationAsync()
    {
        const string title = "改変に足す";
        var cards = SelectedCards();
        if (cards.Count == 0)
        {
            return;
        }

        var model = ModificationPicking.BuildDialog(
            _services,
            title,
            $"選んだ {cards.Count} 件を改変に足します。",
            // 送らないので、どのファイルを使ったかは分からない。**推定で埋めない**
            "どのファイルを使ったかは残りません（商品ページからUnityへ送ると残ります）。",
            (await _services.Modifications.LoadAllAsync()).Modifications,
            existingLabel: "今ある改変に足す",
            commitLabel: "足す",
            emptyText: "改変がまだありません。新しく作って、そこに足せます。");

        if (new Views.PickModificationDialog(model).ShowDialog() != true)
        {
            return;
        }

        if (await ModificationPicking.ResolvePickedAsync(_services, model, title, project: null) is not { } record)
        {
            return;
        }

        var present = record.Members.Select(member => member.ItemId).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var card in cards.Where(card => !present.Contains(card.Item.Id)))
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddModificationMember(
                record.Id,
                new ModificationMember { ItemId = card.Item.Id, AddedAt = DateTimeOffset.Now }));
            added++;
        }

        var skipped = cards.Count - added;
        System.Windows.MessageBox.Show(
            skipped == 0
                ? $"「{record.Name}」に {added} 件を足しました。"
                : $"「{record.Name}」に {added} 件を足しました。{skipped} 件は既に入っていたので、重ねて足していません。",
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

        // 改変が変わったので、「着せているアバター」の絞り込みが読み直すようにする
        NoteModificationsChanged();
    }

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

                // 打ち直したら、前の語で広げた式は捨てる。
                // 残すと次の検索が前の語の別表記で当たってしまう
                ClearWidening();
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// 別の表記でも探すか。
    ///
    /// 切っていても**0件のときは自動で広げる**（何も出ないより出た方がよく、
    /// 広げたことは結果の上に出るので誤解も生まない）。
    /// 入にすると、当たっているときも一緒に広げる——
    /// 「tori」で当たった商品があっても『鳥』の商品を見たい場面があるため。
    /// </summary>
    public bool SearchAlternates
    {
        get => _searchAlternates;
        set
        {
            if (SetField(ref _searchAlternates, value))
            {
                ClearWidening();
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
        .Select(item => item.ShopSubdomain)
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
                .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
                .ToList();

            // 検索対象の文字列はここで作る。正規化は全商品の説明文を畳むので、
            // UIスレッドに乗せると読み込みのたびに画面が固まる
            _haystacks = _allItems.ToDictionary(
                item => item.Id,
                item => Core.Services.SearchText.Build(item, _services.KanjiReadings),
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
                // ユーザが入れた分類も一覧に出す。入れられるのに絞り込みに出ないなら、
                // 入れる意味が半分無くなる
                foreach (var category in _allItems
                    .Select(item => item.CategoryName)
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

                // 全件の読み込みと検索対象の文字列作りが出したゴミを、ここでOSへ返させる（#71）
                BoothAssetManager.App.Services.MemoryTrim.Request();
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
        _avatarFilterHasBase = HasBase(avatarItemId);
        _compatibility = null;
        RaiseAvatarFilterChanged();
        OnPropertyChanged(nameof(IncludeViaBase));
        ApplyFilters();
    }

    /// <summary>
    /// このショップの商品だけで絞り込む。ショップ画面からの導線（#55）。
    /// 他の条件は外してから絞る——前の条件が残っていると「このショップの商品」に見えない。
    /// </summary>
    public void ShowOnlyShop(string shopKey, string shopName)
    {
        ClearFilters();
        _shopFilterKey = shopKey;
        _shopFilterName = shopName;
        RaiseShopFilterChanged();
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
        _avatarFilterHasBase = false;
        RaiseAvatarFilterChanged();
        _shopFilterKey = null;
        _shopFilterName = null;
        RaiseShopFilterChanged();

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
        // 足跡は1回だけ読んで、この絞り込みの間は使い回す。
        // 1商品ごとに読み直すと、件数に比例してファイルを開くことになる
        _recentTimes = NeedsRecent() ? LoadRecentTimes() : null;

        // 改変はファイルを読むので待てない。**読めたらもう一度絞り込む**——
        // 一度きりの読みにしておくと、条件を積んだ直後だけ0件に見える
        if (NeedsModifications() && _modificationUsage is null)
        {
            _ = LoadModificationUsageAsync();
        }

        _matches = SortItems(_allItems.Where(item => Matches(item)))
            .Select(item => _cards[item.Id])
            .ToList();

        // 0件のときは自動で広げる。悪くなりようが無い（0件のままか、増えるか）。
        // 「別の表記も探す」を入れているときは、当たっていても広げる——
        // 0件のときしか使えないのはこちらの都合で、ユーザの都合ではない
        if (_matches.Count == 0 || _searchAlternates)
        {
            TryWiden();
        }

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

    /// <summary>広げて探したときに使った別表記。0件でなければ空。</summary>
    private Core.Services.SearchNode? _widenedNode;

    private readonly Dictionary<string, List<Core.Search.BridgeCandidate>> _widenedTerms = [];

    /// <summary>
    /// 打った語の別表記でもう一度探す。
    ///
    /// 索引を組むのに数秒かかることがあるので、**別のスレッドで**動かして
    /// 出来たら結果を差し替える。打っている手は止めない。
    /// </summary>
    private void TryWiden()
    {
        var node = _queryNode;
        if (node is Core.Services.SearchNode.All || !_services.Bridge.IsAvailable)
        {
            ClearWidening();
            return;
        }

        var token = ++_widenToken;

        _ = Task.Run(() =>
        {
            var used = new Dictionary<string, List<Core.Search.BridgeCandidate>>(StringComparer.Ordinal);
            var widened = _services.Bridge.Widen(node, used);
            if (used.Count == 0)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                // 待っている間に打ち直されていたら捨てる。
                // 当たっている検索を広げるのは、トグルを入れているときだけ
                if (token != _widenToken || (_matches.Count > 0 && !_searchAlternates))
                {
                    return;
                }

                _widenedNode = widened;
                _widenedTerms.Clear();
                foreach (var (term, candidates) in used)
                {
                    _widenedTerms[term] = candidates;
                }

                _matches = SortItems(_allItems.Where(item => Matches(item)))
                    .Select(item => _cards[item.Id])
                    .ToList();

                if (_matches.Count == 0)
                {
                    // 広げても出なかった。広げた印は出さない（何も変わっていないので）
                    ClearWidening();
                }

                // 広げた結果でも件数の内訳は合っていてほしい
                RefreshFacetCounts();
                RebuildRows();
                OnPropertyChanged(nameof(ResultSummary));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(WidenedText));
                OnPropertyChanged(nameof(HasWidened));
                OnPropertyChanged(nameof(EmptyHint));
            });
        });
    }

    private int _widenToken;

    private void ClearWidening()
    {
        if (_widenedNode is null && _widenedTerms.Count == 0)
        {
            return;
        }

        _widenedNode = null;
        _widenedTerms.Clear();
        OnPropertyChanged(nameof(WidenedText));
        OnPropertyChanged(nameof(HasWidened));
    }

    public bool HasWidened => _widenedTerms.Count > 0;

    /// <summary>
    /// 何で当たったかを1行で出す。
    /// **辞書は引いた結果を説明できる**のが埋め込みとの分かれ目なので、説明を捨てない。
    /// </summary>
    public string WidenedText
    {
        get
        {
            if (_widenedTerms.Count == 0)
            {
                return string.Empty;
            }

            var parts = _widenedTerms.Select(entry =>
                $"「{entry.Key}」を {string.Join("・", entry.Value.Take(4).Select(c => c.Text))}");

            return string.Join(" / ", parts) + " としても探しました。";
        }
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

        if (_shopFilterName is not null)
        {
            parts.Add($"ショップ：{_shopFilterName}");
        }

        if (_avatarFilterName is not null)
        {
            // 素体を持たないアバターに「素体経由を含む」と書かない。
            // 経由する先が無いので、書いてあると効いていないのに効いたように読める
            parts.Add(_includeViaBase && _avatarFilterHasBase
                ? $"{_avatarFilterName}（素体経由を含む）"
                : _avatarFilterName);
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

        // ショップでの絞り込み。束ねる鍵はショップ一覧と同じ（名前は変わり得るので鍵で見る）
        if (_shopFilterKey is not null
            && !string.Equals(item.ShopSubdomain, _shopFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
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
            && !string.Equals(item.CategoryName, _selectedCategory, StringComparison.CurrentCulture))
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
            && ExtraFilters.Any(filter => !filter.Matches(item, _unreadItemIds, _recentTimes, _modificationUsage)))
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
                    string.Equals(item.CategoryName, option.Name, StringComparison.CurrentCulture));
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
            || Core.Services.SearchQuery.Matches(
                _widenedNode ?? _queryNode,
                haystack,
                _searchBody,
                _searchPaths,

                // 読みは広げるときだけ見る。組み立てた「あり得る読み」には外れも混じるので、
                // 普段の検索から当たると「なぜこれが出たのか」が説明できなくなる
                includeReadings: _widenedNode is not null);

    /// <summary>
    /// 表示順を適用する。属性で並べたときは、未評価を昇順・降順どちらでも常に末尾に置く。
    /// 未評価は「値が小さい」のではなく「値が無い」ので、0として混ぜると誤読させる。
    /// </summary>
    /// <summary>
    /// この絞り込みで足跡が要るか。
    ///
    /// 積んでいなければ読まない。関係の無い検索でファイルを開く理由が無い。
    /// </summary>
    private bool NeedsRecent()
        => ExtraFilters.Any(filter => filter.Shape == ExtraFilterShape.Days && filter.IsActive);

    private RecentTimes LoadRecentTimes() => new(
        _services.Recent.Times(Core.Services.RecentKind.Added),
        _services.Recent.Times(Core.Services.RecentKind.Used),
        _services.Recent.Times(Core.Services.RecentKind.Viewed));

    /// <summary>改変を読む必要があるか。積んでいなければ読まない。</summary>
    private bool NeedsModifications()
        => ExtraFilters.Any(filter => filter.Kind == ExtraFilterKind.UsedOn && filter.IsActive);

    private async Task LoadModificationUsageAsync()
    {
        ModificationUsage usage;
        try
        {
            usage = ModificationUsage.From((await _services.Modifications.LoadAllAsync()).Modifications);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 読めなくても「空」で置く。null のままだと読み直しを繰り返す
            usage = ModificationUsage.Empty;
        }

        RunOnUiThread(() =>
        {
            _modificationUsage = usage;
            ApplyFilters();
        });
    }

    /// <summary>
    /// 改変が変わったので、次の絞り込みで読み直す。
    ///
    /// 改変は別の画面で増えたり減ったりする。持ち続けると
    /// 「作ったのに絞り込みに出ない」が起きる。
    /// </summary>
    public void NoteModificationsChanged()
    {
        _modificationUsage = null;
        RefreshExtraSuggestions();
    }

    /// <summary>その並び順が「最近」の足跡を見るものなら、どの種類か。</summary>
    private static Core.Services.RecentKind? RecentKindOf(SortKind kind) => kind switch
    {
        SortKind.RecentlyAdded => Core.Services.RecentKind.Added,
        SortKind.RecentlyUsed => Core.Services.RecentKind.Used,
        SortKind.RecentlyViewed => Core.Services.RecentKind.Viewed,
        _ => null,
    };

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
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture);

            var ordered = sort.Descending
                ? rated.OrderByDescending(item => item.Local.Attributes[attributeName])
                : rated.OrderBy(item => item.Local.Attributes[attributeName]);

            return ordered.Concat(unrated);
        }

        // ---- 「最近」の3種 ----
        //
        // **足跡が無い商品は後ろにまとめる。**時刻を MinValue で代えると
        // 「まだ無い」が「一番古い」に化けて、昇順にしたときに先頭へ来てしまう。
        if (RecentKindOf(sort.Kind) is { } recentKind)
        {
            var times = _services.Recent.Times(recentKind);

            var stamped = items.Where(item => times.ContainsKey(item.Id)).ToList();
            var untouched = items
                .Where(item => !times.ContainsKey(item.Id))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture);

            var byTime = sort.Descending
                ? stamped.OrderByDescending(item => times[item.Id])
                : stamped.OrderBy(item => times[item.Id]);

            return byTime.Concat(untouched);
        }

        return sort.Kind switch
        {
            SortKind.Name => sort.Descending
                ? items.OrderByDescending(item => item.DisplayName, StringComparer.CurrentCulture)
                : items.OrderBy(item => item.DisplayName, StringComparer.CurrentCulture),
            SortKind.Size => sort.Descending
                ? items.OrderByDescending(item => item.LogicalSizeBytes)
                : items.OrderBy(item => item.LogicalSizeBytes),
            SortKind.WishList => sort.Descending
                ? items.OrderByDescending(item => item.Booth.WishListsCount)
                : items.OrderBy(item => item.Booth.WishListsCount),
            _ => sort.Descending
                ? items.OrderByDescending(item => item.Local.AcquiredAt ?? DateOnly.MinValue)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture)
                : items.OrderBy(item => item.Local.AcquiredAt ?? DateOnly.MaxValue)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCulture),
        };
    }


    private ItemCardViewModel ToCard(ItemRecord item)
    {
        var missing = item.Local.LocalFiles.Any(file => file.Paths.Count == 0);

        return new ItemCardViewModel(
            item,
            _thumbnails,
            _services.Paths.ItemImagesDir(item.Id),
            _services.Settings.ThumbnailRole)
        {
            Name = item.DisplayName,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            SizeText = item.IsDownloaded ? Core.Models.DisplayText.Size(item.LogicalSizeBytes) : "未取得",
            IsOwned = item.IsDownloaded,
            NeedsEdit = item.Local.UserTags.Count == 0,
            HasMissingFile = missing,
            UserTagText = string.Join(" / ", item.Local.UserTags.Select(tag => tag.Top)),
        };
    }

}
