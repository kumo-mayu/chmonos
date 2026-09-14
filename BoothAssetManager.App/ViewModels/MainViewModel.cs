using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 画面の切り替えと、ナビに出す件数を持つ。
/// 画面はタブではなく同一ウィンドウ内の差し替えにする（決定事項）。
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private object? _currentViewModel;
    private int _unresolvedCount;
    private int _needsEditCount;
    private int _unreadCount;
    private bool _isNavCollapsed;

    public MainViewModel(AppServiceContainer services)
    {
        _services = services;
        Thumbnails = new ThumbnailLoader(services.Settings.ThumbnailCacheBudgetMb);

        // 検索画面は使い捨てにせず1つだけ持ち回る。
        // 商品ページから戻った時に、絞り込み条件やスクロール位置を保つため。
        // 通信の様子はアプリに1つ。常設の行も取り込み画面も、ここを見る
        BoothActivity = new BoothActivityViewModel(services.Client, System.Windows.Threading.Dispatcher.CurrentDispatcher);

        Search = new SearchViewModel(services, Thumbnails);
        Search.AttachMain(this);

        // 裏の取得が画像を置いたら、開いている画面へ知らせる。知らせないと起動し直すまで空のままだった。
        // 検索画面は持ち回るので常に、商品ページはそれが今の画面のときだけ。
        // ここ（アプリと同じ寿命）で1回だけ繋ぐので、画面ごとに外し忘れて残ることが無い
        services.Images.ItemImagesSaved += itemId => RunOnUiThread(() =>
        {
            Search.NoteItemImagesSaved(itemId);
            CurrentItemPage?.NoteImagesSaved(itemId);
        });

        // 前回の履歴をスロットに出す。検索画面は使い回すので1回読めばよい
        Search.RestoreHistory();
        Import = new ImportViewModel(services, this);

        // 初回の読み込みは非同期に走るので、件数が確定したタイミングで表示を更新する。
        Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SearchViewModel.TotalCount)
                or nameof(SearchViewModel.ShopCount)
                or nameof(SearchViewModel.NeedsEditCount))
            {
                OnPropertyChanged(nameof(LibrarySummary));
                NeedsEditCount = Search.NeedsEditCount;
            }
        };

        // 書きかけが増えたり消えたりすると「未:」の数も変わる（未から編へ移るので）
        Drafts.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NeedsEditBadgeCount));

        ShowSearchCommand = new RelayCommand(ShowSearch);
        ShowImportCommand = new RelayCommand(ShowImport);
        ShowEditCommand = new RelayCommand(() => _ = ShowEditAsync());
        ShowResolveCommand = new RelayCommand(ShowResolve);
        ShowInboxCommand = new RelayCommand(ShowInbox);
        ShowShopsCommand = new RelayCommand(ShowShops);
        ShowStatsCommand = new RelayCommand(ShowStats);
        ShowAvatarsCommand = new RelayCommand(ShowAvatars);
        ShowModificationsCommand = new RelayCommand(() => ShowModifications());
        ShowFoldersCommand = new RelayCommand(() => ShowFolders());
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ShowTagManageCommand = new RelayCommand(ShowTagManage);
        ShowAttributeManageCommand = new RelayCommand(ShowAttributeManage);
        ToggleNavCommand = new RelayCommand(ToggleNav);
        ApplyPendingCommand = new RelayCommand(() => _ = ReloadLibraryAsync());

        _isNavCollapsed = services.UiState.NavCollapsed;

        RefreshCounts();
        ShowStartScreen();
        StartBacklogResume();
        StartWatchScan();
    }

    private CancellationTokenSource? _backlog;

    /// <summary>
    /// 起動したときにどの画面を出すか。
    ///
    /// 商品が1件もないうちは検索画面に意味がない。空の一覧の横で
    /// 「まだ登録されていません」が3つ並ぶだけで、次に何をすればよいかも出てこない。
    /// 何もない人には入口を1つに絞る。
    ///
    /// 取り込みは走ったが1件も確定しなかった場合は、作業は未確定側に溜まっているので
    /// そちらへ送る（また取り込み画面に出しても、同じことをもう一度やらせるだけ）。
    ///
    /// 件数はファイルの数え上げだけで分かるので、検索の読み込みを待たない。
    /// </summary>
    private void ShowStartScreen()
    {
        if (_services.Store.Items.EnumerateItemIds().Count > 0)
        {
            ShowSearch();
            return;
        }

        if (UnresolvedCount > 0)
        {
            ShowResolve();
            return;
        }

        ShowImport();
    }

    /// <summary>
    /// ナビを畳んでアイコンだけにするか。
    /// 完全に消さないのは、どこにいるかと残作業の件数が見えなくなるため。
    /// </summary>
    public bool IsNavCollapsed
    {
        get => _isNavCollapsed;
        private set
        {
            if (SetField(ref _isNavCollapsed, value))
            {
                OnPropertyChanged(nameof(NavWidth));
            }
        }
    }

    /// <summary>畳んだときの幅は、アイコン16pxに左右の余白を足した値。</summary>
    public double NavWidth => IsNavCollapsed ? 56 : 208;

    public RelayCommand ToggleNavCommand { get; }

    private void ToggleNav()
    {
        IsNavCollapsed = !IsNavCollapsed;
        var collapsed = IsNavCollapsed;
        _ = SaveUiStateAsync(state => state with { NavCollapsed = collapsed });
    }

    /// <summary>
    /// 画面が覚えている状態（ナビの畳み方・ウィンドウの位置）を書き戻す。
    /// 設定とは別のファイル（ui-state.json・技術的負債 3-2）。書き込みは UiCommand.ChangeUiState で、変え方だけを渡す。
    /// </summary>
    public Task SaveUiStateAsync(Func<Core.Models.UiState, Core.Models.UiState> update)
        => _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeUiState(update));

    public ThumbnailLoader Thumbnails { get; }

    /// <summary>今BOOTHに対して何をしているか。全画面で同じものを見る。</summary>
    public BoothActivityViewModel BoothActivity { get; }

    public SearchViewModel Search { get; }

    public ImportViewModel Import { get; }

    public RelayCommand ShowSearchCommand { get; }

    public RelayCommand ShowImportCommand { get; }

    public RelayCommand ShowEditCommand { get; }

    public RelayCommand ShowResolveCommand { get; }

    public bool IsResolveActive => CurrentViewModel is ResolveViewModel;

    public RelayCommand ShowInboxCommand { get; }

    public bool IsInboxActive => CurrentViewModel is InboxViewModel;

    public RelayCommand ShowShopsCommand { get; }

    public bool IsShopsActive => CurrentViewModel is ShopsViewModel or ShopViewModel;

    public RelayCommand ShowFoldersCommand { get; }

    public bool IsFoldersActive => CurrentViewModel is FolderViewModel;

    /// <summary>
    /// フォルダビュー（ユーザ仕様 2026-09-13）。記録は他の画面でも変わるので、開くたびに作る。
    /// 行の鍵を渡すと、その行を選んで開く（戻るで戻ったとき）。
    /// </summary>
    public void ShowFolders(string? selectKey = null)
        => CurrentViewModel = new FolderViewModel(_services, this, Thumbnails, selectKey);

    /// <summary>このフォルダの下にファイルを持つ商品だけで検索する（フォルダビューからの導線）。</summary>
    public void ShowItemsInFolder(string path)
    {
        Search.ShowOnlyFolder(path);
        ShowSearch();
    }

    /// <summary>
    /// ショップ一覧。検索と違って持ち回さないのは、集計が取り込みや編集で変わるため。
    /// 開き直した時点で数え直すが、**全商品のJSONは読み直さない**——検索画面が起動時に読んだ写しから数える
    /// （ユーザ指示 2026-09-12：開くのが遅い）。写しが新しくなる時機は検索画面と同じ。
    /// </summary>
    public void ShowShops() => CurrentViewModel = new ShopsViewModel(_services, this, Thumbnails);

    public void ShowShop(Core.Services.ShopSummary shop)
        => CurrentViewModel = new ShopViewModel(shop, _services, this, Thumbnails);

    /// <summary>サブドメインからショップ画面を開く。商品ページの作者名からの経路。</summary>
    public async Task ShowShopAsync(string subdomain)
    {
        // ショップ一覧と同じく、全商品のJSONは読み直さずに写しから数える
        var items = Search.SnapshotItems();
        var shops = await Task.Run(() => _services.Shops.Summarize(items));
        var shop = shops.FirstOrDefault(entry =>
            string.Equals(entry.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

        if (shop is not null)
        {
            ShowShop(shop);
        }
    }

    public RelayCommand ShowStatsCommand { get; }

    public bool IsStatsActive => CurrentViewModel is StatsViewModel;

    /// <summary>
    /// 統計。集計は取り込み・編集のたびに変わるので、開き直した時点で数え直す
    /// （ショップ一覧と同じ扱い）。
    /// </summary>
    public void ShowStats() => CurrentViewModel = new StatsViewModel(_services, this);

    public RelayCommand ShowAvatarsCommand { get; }

    public bool IsAvatarsActive => CurrentViewModel is AvatarsViewModel;

    /// <summary>アバターの管理。検出や編集で中身が変わるので、開き直した時点で読み直す。</summary>
    public void ShowAvatars() => CurrentViewModel = new AvatarsViewModel(_services, this);

    /// <summary>
    /// このアバターを選んだ状態でアバター画面を開く（U13）。商品ページの対応アバターの札から、
    /// 手元に持っていないアバターのときに使う（持っていればその商品ページへ行く）。外の BOOTH へは飛ばさない。
    /// </summary>
    public void ShowAvatar(string itemId) => CurrentViewModel = new AvatarsViewModel(_services, this, itemId);

    public RelayCommand ShowModificationsCommand { get; }

    public bool IsModificationsActive => CurrentViewModel is ModificationHubViewModel;

    /// <summary>
    /// 改変の画面（Unityプロジェクト・アバター・改変の3つの見方）。記録は他の画面でも変わるので、開くたびに読み直す。
    /// 見方と右側に出していたものを渡すと、その状態で開く（戻るで戻ったとき）。
    /// </summary>
    public void ShowModifications(ModificationHubLevel? level = null, ModificationHubSelection? selection = null)
        => CurrentViewModel = new ModificationHubViewModel(_services, this, Thumbnails, level, selection);

    public RelayCommand ShowSettingsCommand { get; }

    public bool IsSettingsActive => CurrentViewModel is SettingsViewModel;

    /// <summary>設定。保存先の使用量を数え直すので、開くたびに作る。</summary>
    public void ShowSettings() => CurrentViewModel = new SettingsViewModel(_services, this);

    public RelayCommand ShowTagManageCommand { get; }

    public bool IsTagManageActive => CurrentViewModel is TagManageViewModel;

    /// <summary>タグの管理を開く。マスタは画面の外からも書き換わるので、毎回読み直す。</summary>
    public void ShowTagManage() => CurrentViewModel = new TagManageViewModel(_services, this);

    public RelayCommand ShowAttributeManageCommand { get; }

    public bool IsAttributeManageActive => CurrentViewModel is AttributeManageViewModel;

    public void ShowAttributeManage() => CurrentViewModel = new AttributeManageViewModel(_services, this);

    /// <summary>要確認の未読件数。「新しく起きたこと」なので、0のときはバッジ自体を出さない。</summary>
    public int UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (SetField(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(HasUnread));
            }
        }
    }

    public bool HasUnread => UnreadCount > 0;

    public void ShowInbox() => CurrentViewModel = new InboxViewModel(_services, this);

    /// <summary>件数だけを数え直す。画面側から既読にしたときなどに呼ぶ。</summary>
    public void RefreshBadges() => RefreshCounts();

    /// <summary>
    /// 未確定画面を開く。毎回作り直すのは、取り込みや除外で中身が変わるため。
    /// 開き直した時点の unresolved.json をそのまま読む。
    /// </summary>
    public void ShowResolve() => CurrentViewModel = new ResolveViewModel(_services, this);

    /// <summary>
    /// ウィンドウが手前に戻ったときに呼ばれる。
    ///
    /// アプリの外で変わったものを読み直す口。いまはUnityが開いているかだけ。
    /// 常時見張るのは無駄なので、人が戻ってきた瞬間に合わせる。
    /// </summary>
    /// <summary>
    /// いま開いている商品の操作の持ち主。商品ページならその画面、編集画面なら今の商品の分
    /// （ユーザ判断：画像の追加などの編集は両方で同等にする。落とす・貼る・画像が届いた知らせもこれに向ける）。
    /// </summary>
    public ItemViewModel? CurrentItemPage
        => CurrentViewModel as ItemViewModel ?? (CurrentViewModel as EditViewModel)?.ItemPage;

    public void NoteWindowActivated()
    {
        (CurrentViewModel as ItemViewModel)?.NoteUnityChanged();

        // 改変の画面は「読み直す」のボタンを持たない代わりに、戻ってくるたびに読み直す
        // （Unity を開いて戻る・Hub や VCC でプロジェクトを作って戻る・外で記録を直して戻る）
        (CurrentViewModel as ModificationHubViewModel)?.NoteWindowActivated();

        // フォルダビューも同じ。取り込みや未確定の片付けを別の画面でした後に、木を読み直す
        (CurrentViewModel as FolderViewModel)?.NoteWindowActivated();
    }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            // 離れる画面を履歴に積む（U23）。戻るで来たときと、同じ商品を開き直すときは積まない
            var navigation = _nextNavigation;
            _nextNavigation = Navigation.Push;
            if (navigation == Navigation.Push && _currentViewModel is not null && !ReferenceEquals(_currentViewModel, value))
            {
                Remember(_currentViewModel);
            }

            // 編集画面を離れるときは、今の商品の入力を書きかけとして控える（別の画面へ移っても消さない・ユーザ判断）
            if (_currentViewModel is EditViewModel leavingEdit && !ReferenceEquals(leavingEdit, value))
            {
                leavingEdit.CaptureDraft();
            }

            // ショップ一覧を離れたら、裏で走らせているアイコン取得を止める
            if (_currentViewModel is ShopsViewModel leaving && !ReferenceEquals(leaving, value))
            {
                leaving.StopFetching();
            }

            // 一覧を離れたら「押すと反映」は役目を終える。
            // 守っていたのは「読んでいる最中に足元を動かさない」ことだけなので、
            // 離れた時点で黙って最新にしてよい
            if (HasPendingItems && !ReferenceEquals(_currentViewModel, value))
            {
                _ = ReloadLibraryAsync();
            }

            if (SetField(ref _currentViewModel, value))
            {
                // 画面を移るのは手が止まる所。前の画面が作ったものをここで返させる（#71）。
                // 編集画面で30件送った後、何もしなければ440MBを握ったままだった
                Services.MemoryTrim.Request();

                OnPropertyChanged(nameof(IsSearchActive));
                OnPropertyChanged(nameof(IsImportActive));
                OnPropertyChanged(nameof(IsEditActive));
                OnPropertyChanged(nameof(IsResolveActive));
                OnPropertyChanged(nameof(IsInboxActive));
                OnPropertyChanged(nameof(IsShopsActive));
                OnPropertyChanged(nameof(IsFoldersActive));
                OnPropertyChanged(nameof(IsStatsActive));
                OnPropertyChanged(nameof(IsAvatarsActive));
                OnPropertyChanged(nameof(IsModificationsActive));
                OnPropertyChanged(nameof(IsSettingsActive));
                OnPropertyChanged(nameof(IsTagManageActive));
                OnPropertyChanged(nameof(IsAttributeManageActive));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(BackButtonText));
            }
        }
    }

    /// <summary>
    /// 編集画面を開く。前回の続きが残っていればそこから、無ければuserTag未設定のitemを積む。
    /// 検索から複数選んで入る経路は <paramref name="itemIds"/> で指定する。
    /// </summary>
    public async Task ShowEditAsync(IReadOnlyList<string>? itemIds = null)
    {
        // 取り込みの③がまだの商品は編集に出さない（U8・U10）。検索の複数選択から
        // 取り込み中の商品が混ざって来ることがあるので、入口で外す
        if (itemIds is not null)
        {
            var allowed = itemIds.Where(id => !IsAwaitingDetection(id)).ToList();
            if (allowed.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "選んだ商品は取り込みの途中です。対応アバターの検出が終わると編集できます。\n検索や商品ページで見ることは今でもできます。",
                    "まだ編集できません",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            itemIds = allowed;
        }

        var edit = new EditViewModel(_services, this, Thumbnails);
        CurrentViewModel = edit;

        if (itemIds is null)
        {
            await edit.ResumeAsync();
        }
        else
        {
            await edit.StartAsync(itemIds);
        }
    }

    /// <summary>
    /// 編集の書きかけ（アプリに1つ）。未編集の順番と指定して入った順番で共有する。
    /// </summary>
    public EditDraftStore Drafts { get; } = new();

    /// <summary>
    /// 閉じる前に、書きかけが残っていれば尋ねる（ユーザ判断）。閉じるのをやめるなら true。
    /// 「移動する」なら、書きかけの商品だけを並べて編集画面を開く。
    /// </summary>
    public bool ShouldCancelCloseForDrafts()
    {
        (CurrentViewModel as EditViewModel)?.CaptureDraft();
        if (!Drafts.HasAny)
        {
            return false;
        }

        var answer = Views.ChoiceDialog.Ask(
            "編集途中の商品があります",
            "編集途中の商品があります。編集画面に移動しますか？",
            $"このまま終了すると、編集途中の商品（{Drafts.Count} 件）の保存していない入力は消えてしまいます。\n\n"
            + "移動する …… 編集途中の商品だけを並べて編集画面を開きます\n"
            + "終了する …… 入力を捨てて終了します",
            "移動する",
            "終了する");

        switch (answer)
        {
            case Views.ChoiceDialogResult.First:
                _ = ShowEditAsync(Drafts.ItemIds);
                return true;

            case Views.ChoiceDialogResult.Second:
                return false;

            default:
                // キャンセルは「閉じるのをやめる」
                return true;
        }
    }

    /// <summary>
    /// 商品ページを開く。検索画面のインスタンスは保持したままなので、
    /// 戻ったときに絞り込み条件もスクロール位置もそのまま残る。
    /// </summary>
    /// <summary>改変の詳細を開く。商品ページと同じ格の画面</summary>
    public void ShowModification(Core.Models.ModificationRecord record)
        => CurrentViewModel = new ModificationViewModel(record, _services, this, Thumbnails);

    public void ShowItem(Core.Models.ItemRecord item)
    {
        // 「閲覧」の足跡。待たずに走らせる——足跡のために画面が止まる理由が無い
        _ = _services.Recent.TouchAsync(item.Id, Core.Services.RecentKind.Viewed);
        CurrentViewModel = new ItemViewModel(item, _services, this, Thumbnails);
    }

    /// <summary>
    /// 今の商品ページを開き直す（取り直した・ファイルを外した・IDを変えた後）。
    /// 履歴には積まない——積むと、戻るを押すたびに同じ商品の古い姿が出てくる
    /// </summary>
    public void ReplaceItem(Core.Models.ItemRecord item)
    {
        _nextNavigation = Navigation.Replace;
        ShowItem(item);
    }

    /// <summary>未確定ファイルの総件数。「残っている作業量」を示す。</summary>
    public int UnresolvedCount
    {
        get => _unresolvedCount;
        private set => SetField(ref _unresolvedCount, value);
    }

    /// <summary>userTagが未設定のitem数。こちらも総数で示す。</summary>
    public int NeedsEditCount
    {
        get => _needsEditCount;
        private set
        {
            if (SetField(ref _needsEditCount, value))
            {
                OnPropertyChanged(nameof(NeedsEditBadgeCount));
            }
        }
    }

    /// <summary>
    /// ナビの「未:」に出す数。未編集のうち、書きかけの無いもの。
    /// 「編:」（書きかけのある商品）と**重ならない数え方**にする（ユーザ指示）——未編集の商品に書きかけができると、
    /// 未から編へ1件移る。数え方は検索の未編集と同じ（ユーザータグが無く、取り込みの③を待っていない）
    /// </summary>
    public int NeedsEditBadgeCount => NeedsEditCount - Drafts.ItemIds.Count(id =>
        Search.FindItem(id) is { } item && item.Local.UserTags.Count == 0 && !IsAwaitingDetection(id));

    public string LibrarySummary => $"{Search.TotalCount} items / {Search.ShopCount} shops";

    public void ShowSearch()
    {
        // 改変は別の画面で増えたり減ったりする。戻ってきた時点で読み直させる
        Search.NoteModificationsChanged();
        CurrentViewModel = Search;
    }

    /// <summary>今のショートカットの割り当て（#43）。設定画面で保存するとすぐ変わる。</summary>
    public ShortcutSettings Shortcuts => _services.Settings.Shortcuts ?? new ShortcutSettings();

    /// <summary>
    /// ショートカットの操作を今の画面で行う。その画面に無い操作・今は押せない操作なら false
    /// （キーは他へ流れる。Ctrl+Enter が保存できないときは、欄の改行として働く）。
    /// </summary>
    public bool RunShortcut(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.SaveAndNext when CurrentViewModel is EditViewModel edit:
                return Run(edit.SaveAndNextCommand);
            case ShortcutAction.Skip when CurrentViewModel is EditViewModel edit:
                return Run(edit.SkipCommand);
            case ShortcutAction.FocusSearch:
                ShowSearch();
                return true;
            case ShortcutAction.Back:
                // どの画面でも直前の画面へ（U23）。編集画面も同じ（ユーザ判断 2026-09-12）——
                // 以前は編集画面だけ「前の1件へ」にしていたが、入力欄にいると効かず、他の画面と食い違っていた。
                // 前の1件へは「← 前へ」ボタンで行く。離れるときに書きかけは控えるので、戻っても入力は消えない
                if (!CanGoBack)
                {
                    return false;
                }

                GoBack();
                return true;
            default:
                return false;
        }

        static bool Run(RelayCommand command)
        {
            if (!command.CanExecute(null))
            {
                return false;
            }

            command.Execute(null);
            return true;
        }
    }

    /// <summary>このショップの商品を検索画面で見せる。ショップ画面から検索の絞り込みを使うための入口（#55）。</summary>
    public void ShowItemsOfShop(string shopKey, string shopName)
    {
        Search.ShowOnlyShop(shopKey, shopName);
        ShowSearch();
    }

    /// <summary>このuserTagが付いているitemを検索画面で見せる。件数から中身へ辿るための入口。</summary>
    public void ShowItemsWithTag(string top, string? sub = null)
    {
        Search.ShowOnly(top, sub);
        ShowSearch();
    }

    /// <summary>この属性を評価しているitemを検索画面で見せる。</summary>
    public void ShowItemsWithAttribute(string name)
    {
        Search.ShowOnlyAttribute(name);
        ShowSearch();
    }

    public void ShowImport()
    {
        // 設定画面で「起動時に取り込む」を変えて戻ってきたときに、監視対象の説明を合わせる（U7）
        Import.NoteShown();
        CurrentViewModel = Import;
    }

    /// <summary>
    /// マスタ（分類・属性）だけが変わったときに呼ぶ。itemには触っていないので、
    /// 全件の読み直しはせず、絞り込みの選択肢だけを作り直す。
    /// </summary>
    public void RefreshMasters() => Search.RefreshFacets();

    /// <summary>取り込み後など、ライブラリが変わったときに呼ぶ。</summary>
    public async Task ReloadLibraryAsync()
    {
        // 読み直しを始めた時点で、そこまでに増えた分は一覧へ入る
        _reflectedAdded = _importWork?.AddedCount ?? 0;
        _lastReflectAt = DateTime.UtcNow;

        await Search.ReloadAsync();
        RefreshCounts();
        OnPropertyChanged(nameof(LibrarySummary));
        ClearPendingItems();
    }

    private bool _isImporting;

    /// <summary>
    /// 取り込みが走っているか。
    ///
    /// 保存先の引越しを塞ぐために要る。走っている最中に運ぶと、
    /// 運び終わった後の書き込みが**元の場所へ**行ってしまう。
    /// </summary>
    public bool IsImporting
    {
        get => _isImporting;
        set
        {
            if (SetField(ref _isImporting, value))
            {
                RelayCommand.RaiseCanExecuteChanged();

                // 取り込みが終わったら（中断を含む）③待ちの印は全部外れる。
                // ②③の途中で止めた商品は「取り込み中」の札のまま一覧に残っているので、読み直して外す
                if (!value)
                {
                    var hadAwaiting = _lastAwaitingCount > 0;
                    _importWork = null;
                    _lastAwaitingCount = 0;
                    OnEditGateChanged();
                    if (hadAwaiting)
                    {
                        _ = ReloadLibraryAsync();
                    }
                }
            }
        }
    }

    private int _pendingItemCount;

    /// <summary>
    /// 取り込み中に増えて、まだ一覧へ反映していない件数。
    ///
    /// **件数とバッジは即座に更新するが、一覧そのものは勝手に並び替えない。**
    /// 読んでいる最中に足元が動くと、どこを見ていたか分からなくなる。
    /// 反映する時機はユーザに選ばせる。
    /// </summary>
    public int PendingItemCount
    {
        get => _pendingItemCount;
        private set
        {
            if (SetField(ref _pendingItemCount, value))
            {
                OnPropertyChanged(nameof(HasPendingItems));
                OnPropertyChanged(nameof(PendingItemText));
            }
        }
    }

    public bool HasPendingItems => PendingItemCount > 0;

    public string PendingItemText => $"取り込み中に {PendingItemCount} 件増えました（押すと反映）";

    /// <summary>反映するボタン。一覧を読み直して1行を消す。</summary>
    public RelayCommand ApplyPendingCommand { get; }

    /// <summary>取り込みが商品を1件ぶん見えるようにした。</summary>
    public void NotePendingItems(int count) => RunOnUiThread(() => PendingItemCount = count);

    private void RefreshCounts()
    {
        UnresolvedCount = _services.Store.Unresolved.Load().Count;
        NeedsEditCount = Search.NeedsEditCount;
        UnreadCount = _services.Notifications.Load().Count(record => !record.IsRead);
    }
}
