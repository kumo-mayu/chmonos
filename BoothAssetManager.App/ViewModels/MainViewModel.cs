using System.IO;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 画面の切り替えと、ナビに出す件数を持つ。
/// 画面はタブではなく同一ウィンドウ内の差し替えにする（決定事項）。
/// </summary>
public sealed class MainViewModel : ViewModelBase
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
        ShowSettingsCommand = new RelayCommand(ShowSettings);
        ShowTagManageCommand = new RelayCommand(ShowTagManage);
        ShowAttributeManageCommand = new RelayCommand(ShowAttributeManage);
        ToggleNavCommand = new RelayCommand(ToggleNav);
        ApplyPendingCommand = new RelayCommand(() => _ = ReloadLibraryAsync());

        _isNavCollapsed = services.Settings.NavCollapsed;

        RefreshCounts();
        ShowStartScreen();
        StartBacklogResume();
        StartWatchScan();
    }

    private CancellationTokenSource? _backlog;

    /// <summary>
    /// 使っていない間に進める2つを背景で走らせる。
    ///
    /// ⑤ 前の取り込みで取り切れなかった画像を取り直す。
    ///    対象は手元のJSONだけで決まる（<c>Booth.Images</c> の件数とディスクの差）ので、
    ///    フォルダの走査は起きない。**起動時に黙ってドライブを舐めに行くのとは質が違う。**
    ///
    /// ⑦ 期限の来た商品を取り直す。梯子のいちばん下で、急ぐ理由が無い唯一の段。
    ///
    /// **⑤を先にするのは、見た目の穴の方が先に目に入るから。**
    /// どちらも取り込みが始まれば優先順位で自然に譲るので、待たせる必要はない。
    ///
    /// 失敗しても黙って終える。ユーザが頼んだ作業ではないので、
    /// 邪魔をしてまで知らせる価値がない（どちらも次の起動でまた試す）。
    /// </summary>
    private void StartBacklogResume()
    {
        if (!_services.Settings.ResumeFetchInBackground)
        {
            return;
        }

        _backlog = new CancellationTokenSource();
        var token = _backlog.Token;

        // 進み具合は常設の1行に「何を n/N」で出す（ユーザ指示）。以前は通信の様子
        // （間隔を空けています）しか出ず、何をしているのか読めなかった。
        // UIスレッドで作っておく（Progress は作ったスレッドへ知らせを戻す）
        IProgress<(int Done, int Total)> ReportAs(string label)
            => new Progress<(int Done, int Total)>(
                report => BoothActivity.ReportWork(WorkSource.Background, label, report.Done, report.Total));

        var images = ReportAs("画像を取得中");
        var avatars = ReportAs("アバターの画像を取得中");
        var due = ReportAs("商品の更新を確認中");

        _ = Task.Run(async () =>
        {
            try
            {
                await _services.Backlog.ResumeAsync(images, token);
                BoothActivity.EndWork(WorkSource.Background);

                // 持っていないアバターの1枚目（U18）。商品の画像の穴の方が先に目に入るので⑤の後
                await _services.AvatarImages.SyncAsync(avatars, token);
                BoothActivity.EndWork(WorkSource.Background);

                await _services.Due.RunAsync(due, token);

                // ⑦で商品ページが変わっていれば要確認が増える。件数を出し直す
                RunOnUiThread(RefreshCounts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
            }
            finally
            {
                BoothActivity.EndWork(WorkSource.Background);
            }
        }, token);
    }

    /// <summary>
    /// 取り込みの後、新しく見つかったアバターの1枚目を続けて取る（ユーザ判断 2026-09-12）。
    /// 以前は次の起動の裏の取得まで待っていたので、取り込んだ直後の一覧や候補は頭文字のままだった。
    /// 検出のときにJSONを取っているので1枚目のURLは分かっており、ここで増えるのは画像の取得だけ。
    /// 起動時の裏の取得と重なっても、<see cref="Core.Services.AvatarImageSync"/> が1本ずつ回すので二重には取らない。
    /// </summary>
    public void StartAvatarImageSync()
    {
        var token = (_backlog ??= new CancellationTokenSource()).Token;
        IProgress<(int Done, int Total)> progress = new Progress<(int Done, int Total)>(
            report => BoothActivity.ReportWork(WorkSource.Background, "アバターの画像を取得中", report.Done, report.Total));

        _ = Task.Run(async () =>
        {
            try
            {
                await _services.AvatarImages.SyncAsync(progress, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 取れなくても次の起動でまた試す。頼まれた作業ではないので邪魔をしない
            }
            finally
            {
                BoothActivity.EndWork(WorkSource.Background);
            }
        }, token);
    }

    /// <summary>閉じるときに背景の取得を止める。</summary>
    public void StopBackgroundWork()
    {
        _backlog?.Cancel();
        _watch?.Cancel();
    }

    private CancellationTokenSource? _watch;

    /// <summary>
    /// 監視対象フォルダに新しいファイルが無いかを、起動時に見る。
    ///
    /// **走査してよいのは、ユーザが「ここを見ておいて」と指示したフォルダだけ。**
    /// 監視に入っていないフォルダは今まで通り、押されるまで見に行かない。
    ///
    /// 見つけても**既定では取り込みを始めない**。走査は手元のディスクを読むだけだが、
    /// 取り込みはBOOTHへの通信で1件あたり十数秒かかる。起動した瞬間に黙って始めると、
    /// ユーザがこれからやろうとしていた操作と行列を取り合う。件数を出して押させる。
    ///
    /// 設定「起動時に監視フォルダの新着を取り込む」を入れた人だけ、そのまま始める（#38）。
    /// そのときも画面は切り替えない——起動した直後に画面が飛ぶと、しようとしていた操作の邪魔になる。
    /// 進み具合は常設の1行に出る。
    /// </summary>
    private void StartWatchScan()
    {
        if (_services.Settings.WatchedFolders.Count == 0)
        {
            return;
        }

        _watch = new CancellationTokenSource();
        var token = _watch.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _services.Watch.FindNewAsync(_services.Settings.WatchedFolders, token);
                if (!result.HasNew)
                {
                    return;
                }

                if (_services.Settings.StartImportOnLaunch)
                {
                    RunOnUiThread(() => Import.AddDroppedPaths(result.NewFiles, startImmediately: true));
                    return;
                }

                RunOnUiThread(() =>
                {
                    WatchedNewFiles = result.NewFiles;
                    OnPropertyChanged(nameof(WatchedNewCount));
                    OnPropertyChanged(nameof(HasWatchedNew));
                    OnPropertyChanged(nameof(WatchedNewText));
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 見に行けなくても起動は妨げない。次の起動でまた見る
            }
        }, token);
    }

    /// <summary>監視対象で見つかった、まだ見ていないファイル。</summary>
    public IReadOnlyList<string> WatchedNewFiles { get; private set; } = [];

    public int WatchedNewCount => WatchedNewFiles.Count;

    public bool HasWatchedNew => WatchedNewCount > 0;

    public string WatchedNewText => $"監視対象に新しいファイルが {WatchedNewCount} 件あります。";

    /// <summary>見つかったぶんを取り込み対象に積む。押されて初めて通信が始まる。</summary>
    public void TakeWatchedNew()
    {
        if (!HasWatchedNew)
        {
            return;
        }

        var files = WatchedNewFiles;
        WatchedNewFiles = [];
        OnPropertyChanged(nameof(WatchedNewCount));
        OnPropertyChanged(nameof(HasWatchedNew));
        OnPropertyChanged(nameof(WatchedNewText));

        Import.AddDroppedPaths(files);
        ShowImport();
    }

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
        _ = SaveUiStateAsync(settings => settings with { NavCollapsed = IsNavCollapsed });
    }

    /// <summary>
    /// 画面が覚えている状態（ナビの畳み方・ウィンドウの位置）を書き戻す。
    /// 設定画面が別に読み書きしているので、こちらの変更も同じ経路を通して
    /// 開いているAppSettingsを取り替えておく。
    /// </summary>
    public async Task SaveUiStateAsync(Func<AppSettings, AppSettings> update)
    {
        var next = update(_services.Settings);
        _services.ReplaceSettings(next);
        await _services.SettingsStore.SaveAsync(next);
    }

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

    /// <summary>
    /// ショップ一覧。検索と違って持ち回さないのは、集計が取り込みや編集で変わるため。
    /// 開き直した時点で数え直す。
    /// </summary>
    public void ShowShops() => CurrentViewModel = new ShopsViewModel(_services, this, Thumbnails);

    public void ShowShop(Core.Services.ShopSummary shop)
        => CurrentViewModel = new ShopViewModel(shop, _services, this, Thumbnails);

    /// <summary>サブドメインからショップ画面を開く。商品ページの作者名からの経路。</summary>
    public async Task ShowShopAsync(string subdomain)
    {
        var shops = await _services.Shops.LoadAsync();
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

    public void NoteWindowActivated() => (CurrentViewModel as ItemViewModel)?.NoteUnityChanged();

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
                OnPropertyChanged(nameof(IsStatsActive));
                OnPropertyChanged(nameof(IsAvatarsActive));
                OnPropertyChanged(nameof(IsSettingsActive));
                OnPropertyChanged(nameof(IsTagManageActive));
                OnPropertyChanged(nameof(IsAttributeManageActive));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(BackButtonText));
            }
        }
    }

    // ---- 画面の履歴（U23） ----
    //
    // 「戻る」は常に直前の画面へ（ブラウザと同じ・ユーザ判断）。以前は開くときに戻り先を1つ渡す作りで、
    // 渡さない入口（要確認・説明のリンク・落とす／貼る・ファイルを外した後など）は検索へ落ち、
    // 商品→ショップ→商品のように往復すると2段目から先を失っていた。
    // ナビで移っても履歴は切らない（ユーザ判断：「一瞬の確認の可能性もあります」）

    /// <summary>
    /// 覚えておく画面の数。一日中開いたままでも伸び続けないように。
    /// 100（ユーザ指示 2026-09-12）。編集画面で商品を移るたびに1つ積むようにしたので、50では編集の数十件で押し出される。
    /// 1つは開き直す手順だけ（画面は持たない）なので、100でも軽い
    /// </summary>
    private const int MaxHistory = 100;

    /// <summary>
    /// 戻るの文言に載せる名前の長さ。長い商品名が上部バーを占領して隣の情報を押し出さないように
    /// （以前の商品ページの決め事と同じ30字。手元の15件で名前は中央28字・最長48字）
    /// </summary>
    private const int MaxBackLabel = 30;

    private sealed record HistoryEntry(string Label, Action Restore);

    private enum Navigation
    {
        Push,
        Replace,
        Back,
    }

    private readonly List<HistoryEntry> _history = [];
    private Navigation _nextNavigation = Navigation.Push;

    public bool CanGoBack => _history.Count > 0;

    /// <summary>戻るボタンの文言。行き先の名前を出す（どこへ戻るのか分からないと押せない）。</summary>
    public string BackButtonText => _history.Count > 0 ? $"← {_history[^1].Label}に戻る" : "← 検索に戻る";

    /// <summary>直前の画面へ戻る。履歴が無ければ検索へ。</summary>
    public void GoBack()
    {
        if (_history.Count == 0)
        {
            ShowSearch();
            return;
        }

        var entry = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        _nextNavigation = Navigation.Back;
        entry.Restore();
    }

    private void Remember(object leaving)
    {
        if (EntryFor(leaving) is not { } entry)
        {
            return;
        }

        _history.Add(entry);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>
    /// 画面を戻すための控え。**画面そのものは持たず、開き直す手順を持つ。**
    /// 画面を抱えると、戻るまでその画面の画像や一覧を握ったままになる（#71でメモリを押し上げた型）。
    /// 検索と取り込みの画面は1つを持ち回しているので、絞り込みやスクロール位置もそのまま戻る
    /// </summary>
    private HistoryEntry? EntryFor(object screen) => screen switch
    {
        SearchViewModel => new HistoryEntry("検索", ShowSearch),
        ItemViewModel item => new HistoryEntry(Shorten(item.Name), () => _ = RestoreItemAsync(item.Item.Id)),
        ShopViewModel shop => new HistoryEntry(Shorten(shop.Shop.Name), () => ShowShop(shop.Shop)),
        ModificationViewModel modification => new HistoryEntry(
            Shorten(modification.Record.Name), () => ShowModification(modification.Record)),
        AvatarsViewModel avatars => new HistoryEntry("アバターの管理", RestoreAvatars(avatars.Selected?.ItemId)),
        ShopsViewModel => new HistoryEntry("ショップ一覧", ShowShops),
        StatsViewModel => new HistoryEntry("統計", ShowStats),
        ImportViewModel => new HistoryEntry("取り込み", ShowImport),
        ResolveViewModel => new HistoryEntry("未確定", ShowResolve),
        InboxViewModel => new HistoryEntry("要確認", ShowInbox),
        TagManageViewModel => new HistoryEntry("タグの管理", ShowTagManage),
        AttributeManageViewModel => new HistoryEntry("属性の管理", ShowAttributeManage),
        SettingsViewModel => new HistoryEntry("設定", ShowSettings),
        EditViewModel edit => EditEntry(edit),
        _ => null,
    };

    /// <summary>
    /// 編集画面の中で商品を移るとき、今の商品を履歴に積む（ユーザ指示 2026-09-12：
    /// Alt＋← で、編集画面で前に開いていた商品へ戻れるように）。
    /// </summary>
    public void RememberEditStep(EditViewModel edit)
    {
        Remember(edit);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackButtonText));
    }

    /// <summary>
    /// 編集画面の控え。**どの商品を開いていたか（位置）まで預ける。**
    /// 指定して入った編集は順番そのもの（<see cref="EditRun"/>）も預け、未編集の順番は edit-session.json から開き直す（ユーザ判断）
    /// </summary>
    private HistoryEntry EditEntry(EditViewModel edit)
    {
        var run = edit.Run;
        var index = edit.Index;
        var label = edit.HasItem ? $"編集（{Shorten(edit.Name)}）" : "編集";
        return new HistoryEntry(label, () => _ = RestoreEditAsync(run, index));
    }

    private async Task RestoreEditAsync(EditRun? run, int index)
    {
        // 同じ順番の編集を開いている間は、画面はそのままで位置だけ戻す。
        // 作り直すと、店名の候補を作るために全件を読み直す（#71。2000件で重い）
        if (CurrentViewModel is EditViewModel current && ReferenceEquals(current.Run, run))
        {
            // 画面の差し替えが起きないので、戻るの印をここで下ろす（残すと次の画面移動が履歴に積まれない）
            _nextNavigation = Navigation.Push;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(BackButtonText));
            await current.ShowStepAsync(index);
            return;
        }

        var edit = new EditViewModel(_services, this, Thumbnails);
        CurrentViewModel = edit;

        if (run is not null)
        {
            await edit.ResumeRunAsync(run, index);
        }
        else
        {
            await edit.ResumeAsync(index);
        }
    }

    /// <summary>アバター画面は、選んでいたアバターを選んだ状態で戻す。</summary>
    private Action RestoreAvatars(string? selectedId)
        => selectedId is null ? ShowAvatars : () => ShowAvatar(selectedId);

    /// <summary>
    /// 商品ページは開き直した時点の中身で出す（覚えた時の中身は、その後の編集で古くなっている）。
    /// 消えた商品（IDを変えた・登録を外した）は飛ばして、もう1つ前へ戻る
    /// </summary>
    private async Task RestoreItemAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            ShowItem(item);
            return;
        }

        _nextNavigation = Navigation.Push;
        GoBack();
    }

    private static string Shorten(string label)
        => label.Length <= MaxBackLabel ? label : label[..MaxBackLabel] + "…";

    public bool IsSearchActive => CurrentViewModel is SearchViewModel;

    public bool IsImportActive => CurrentViewModel is ImportViewModel;

    public bool IsEditActive => CurrentViewModel is EditViewModel;

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
    /// ウィンドウに落とされた／貼り付けられたものを振り分ける。
    ///
    /// 受け口をウィンドウ1つにしているのは、**落ちてくるものが2種類しか無い**から。
    /// 画面ごとに受けると、同じものを落としたのに画面によって結果が変わる。
    ///
    /// **勝手に処理を始めない。**ファイルは取り込みの対象に積むだけで、実行は押してから。
    /// 持っていない商品のURLは、外部への通信を伴うので必ず尋ねる。
    /// </summary>
    public async Task HandleDropAsync(IReadOnlyList<string>? paths, string? text, bool hasBitmap = false)
    {
        // 判断は Core 側の規則に任せる。画面を立ち上げずに確かめられるようにするため。
        //
        // 商品ページを開いているときだけ規則が変わる。**足す先が決まっているから**——
        // 決まっていない場所で「この商品の画像に足しますか」と聞いても答えられない
        // 編集画面も同じ（ユーザ判断：画像の追加などは商品ページと同等。落とす・貼るも含む）
        var decision = CurrentItemPage is not null
            ? Core.Services.DropRouting.DecideOnItemPage(paths, text, hasBitmap, _services.Store.Items.Exists)
            : Core.Services.DropRouting.Decide(paths, text, _services.Store.Items.Exists);

        switch (decision.Action)
        {
            case Core.Services.DropAction.AddImageToItem:
                await AddDroppedImagesAsync(paths, hasBitmap, decision.ImageUrl);
                return;

            case Core.Services.DropAction.AskImageOrItem:
                await AskImageOrItemAsync(decision.ItemId!, paths, hasBitmap, decision.ImageUrl);
                return;

            case Core.Services.DropAction.Import:
                ShowImport();

                // 落としたらそのまま始める（#38。設定で切れる）
                Import.AddDroppedPaths(paths!, startImmediately: _services.Settings.StartImportOnDrop);
                return;

            case Core.Services.DropAction.OpenItem:
                if (await _services.Store.Items.LoadAsync(decision.ItemId!) is { } owned)
                {
                    ShowItem(owned);
                }

                return;

            case Core.Services.DropAction.OfferToRegister:
                await OfferToRegisterAsync(decision.ItemId!);
                return;

            case Core.Services.DropAction.OpenShop:
                await ShowShopAsync(decision.Shop!);
                return;

            default:
                return;
        }
    }


    /// <summary>
    /// 落とした／貼った画像を、いま開いている商品に足す。
    ///
    /// ファイルとクリップボードの絵で、足したあとの流れは同じにしてある。
    /// </summary>
    private async Task AddDroppedImagesAsync(
        IReadOnlyList<string>? paths,
        bool hasBitmap,
        string? imageUrl = null)
    {
        if (CurrentItemPage is not { } item)
        {
            return;
        }

        if (paths is { Count: > 0 })
        {
            await item.AddImageFilesAsync(paths.Where(Core.Services.DropRouting.LooksLikeImage).ToList());
            return;
        }

        if (hasBitmap && ReadClipboardImage() is { } bytes)
        {
            await item.AddImageBytesAsync(bytes);
            await item.ReloadGalleryAsync();
            return;
        }

        // ブラウザからの絵はURLだけで落ちてくる。取りに行く。
        // **BOOTHの画像置き場だけ**（DropRouting が確かめている）で、
        // 人が押した操作なので他の取得より先に出る
        if (imageUrl is not null)
        {
            using var priority = Core.Booth.BoothClient.Prioritize(Core.Booth.BoothPriority.PinnedImage);

            var fetched = await _services.Client.GetBinaryAsync(imageUrl);
            if (!fetched.IsSuccess || fetched.Value is null)
            {
                System.Windows.MessageBox.Show(
                    "BOOTHから画像を取れませんでした。",
                    "画像を足す",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            await item.AddImageBytesAsync(fetched.Value);
            await item.ReloadGalleryAsync();
        }
    }

    /// <summary>
    /// BOOTH由来の画像を受け取ったとき。
    ///
    /// **その商品を開きたいのか、この商品の画像に足したいのかは決まらない。**
    /// BOOTHの商品ページから絵をドラッグすると、その絵のURLに商品IDが入っているので、
    /// 落としたものだけからは意図が読めない。ここだけ人に聞く。
    /// </summary>
    private async Task AskImageOrItemAsync(
        string itemId,
        IReadOnlyList<string>? paths,
        bool hasBitmap,
        string? imageUrl = null)
    {
        if (CurrentItemPage is not { } item)
        {
            return;
        }

        var name = await _services.Store.Items.LoadAsync(itemId) is { } known
            ? $"「{known.DisplayName}」"
            : $" {itemId} ";

        // 「はい／いいえ」は本文と対応を覚えないと押せない。ボタンに何が起きるかを名乗らせる（#18・ユーザ指摘）
        var answer = Views.ChoiceDialog.Ask(
            "BOOTHの画像を受け取りました",
            "この画像をどうしますか？",
            $"商品を開く …… 商品{name}のページへ移ります\n"
            + $"画像として足す …… いま開いている「{item.Name}」の画像に加えます",
            "商品を開く",
            "画像として足す");

        switch (answer)
        {
            case Views.ChoiceDialogResult.First:
                await OpenOrOfferAsync(itemId);
                return;

            case Views.ChoiceDialogResult.Second:
                await AddDroppedImagesAsync(paths, hasBitmap, imageUrl);
                return;

            default:
                return;
        }
    }

    /// <summary>手元にあれば開き、無ければ登録するか尋ねる。落としたURLと同じ扱い。</summary>
    private async Task OpenOrOfferAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } owned)
        {
            ShowItem(owned);
            return;
        }

        await OfferToRegisterAsync(itemId);
    }

    /// <summary>
    /// クリップボードの絵をPNGの生データにする。
    ///
    /// スクリーンショットは**ファイルではなく絵そのもの**で置かれるので、
    /// パス経由では受け取れない。ここで一度PNGに固めてから、
    /// 足す側でBOOTHと同じ圧縮を通す。
    /// </summary>
    private static byte[]? ReadClipboardImage()
    {
        try
        {
            if (System.Windows.Clipboard.GetImage() is not { } source)
            {
                return null;
            }

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or NotSupportedException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            return null;
        }
    }

    /// <summary>
    /// 手元に無い商品のURLを受けたとき。
    ///
    /// この経路が、**贈答品や気になっている未購入品を登録する道**にもなる。
    /// ファイルが手元に来ないものは取り込みからは入らないので、ここが唯一の入口。
    /// </summary>
    private async Task OfferToRegisterAsync(string itemId)
    {
        var answer = System.Windows.MessageBox.Show(
            $"商品 {itemId} はライブラリにありません。\n\n"
                + "BOOTHから情報を取得して、ファイルを持たない商品として登録しますか？\n"
                + "（贈った商品や、気になっている商品をここから登録できます）",
            "BOOTHのURLを受け取りました",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RegisterItem(itemId));

        if (result is Core.Commands.CommandResult.Failed failure)
        {
            System.Windows.MessageBox.Show(
                failure.Message,
                "登録できませんでした",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            return;
        }

        await ReloadLibraryAsync();

        if (await _services.Store.Items.LoadAsync(itemId) is { } added)
        {
            ShowItem(added);
        }
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

    // ---- 取り込みの途中の商品（U8・U10） ----
    //
    // ①で商品ができた時点で検索・ショップ・件数に出す（JSONだけで最低限の表示はできる）。
    // 編集は③（対応アバターの検出）が済むまで出さない（ユーザ判断）。
    // 判断は取り込みと同じ作業の集まり（ImportWorkSet）を見て行い、画面側に別の状態を持たない

    private Core.Scanning.ImportWorkSet? _importWork;
    private int _lastAwaitingCount;
    private int _reflectedAdded;
    private DateTime _lastReflectAt = DateTime.MinValue;

    /// <summary>
    /// 増えた商品を一覧へ入れる間隔の下限。
    /// 読み直しは全商品を読み、検索用の文字列を作り直す。①は1.5秒に1件進むので、
    /// 増えるたびに読み直すと取り込みの間じゅう読み直し続けることになる。
    /// 10秒に1回なら、増えた商品は遅くとも10秒で一覧に出る
    /// </summary>
    private static readonly TimeSpan ReflectInterval = TimeSpan.FromSeconds(10);

    /// <summary>取り込みが始まったときに、その作業の集まりを受け取る。</summary>
    public void AttachImportWork(Core.Scanning.ImportWorkSet work)
    {
        _importWork = work;
        _lastAwaitingCount = 0;
        _reflectedAdded = 0;
    }

    /// <summary>この商品が、取り込みの③を待っているか。待っている間は編集に出さない。</summary>
    public bool IsAwaitingDetection(string itemId)
        => IsImporting && _importWork?.IsAwaitingDetection(itemId) == true;

    /// <summary>
    /// 取り込みの進み具合が届くたびに呼ぶ。③待ちの数が変わったら編集の可否を知らせ直し、
    /// 増えた商品を一覧へ入れる（読んでいる途中なら「押すと反映」の1行にする）。
    /// </summary>
    public void NoteImportProgress()
    {
        if (_importWork is not { } work)
        {
            return;
        }

        var awaiting = work.AwaitingDetectionCount;
        var gateChanged = awaiting != _lastAwaitingCount;
        _lastAwaitingCount = awaiting;

        if (gateChanged)
        {
            OnEditGateChanged();
        }

        var unseen = work.AddedCount - _reflectedAdded;
        if (unseen <= 0 && !gateChanged)
        {
            return;
        }

        // 一覧を下へ読み進めている最中は足元を動かさない。件数だけ出して、反映は押してもらう（U10）
        if (CurrentViewModel is SearchViewModel && Search.IsScrolledDown)
        {
            if (unseen > 0)
            {
                PendingItemCount = unseen;
            }

            return;
        }

        // ③が済んで編集に出せるようになったときは待たせない。札（取り込み中）を早く外す方が要る
        if (!gateChanged && DateTime.UtcNow - _lastReflectAt < ReflectInterval)
        {
            return;
        }

        _ = ReloadLibraryAsync();
    }

    /// <summary>
    /// 編集に出せる商品が変わった。押せるボタンと件数を知らせ直す。
    /// 開いている商品ページには直接伝える——イベントで配ると、閉じた商品ページが購読したまま残る
    /// </summary>
    private void OnEditGateChanged()
    {
        RelayCommand.RaiseCanExecuteChanged();
        CurrentItemPage?.RefreshEditLock();

        RefreshCounts();
    }

    /// <summary>
    /// 1行を消す。反映したときと、その一覧から離れたときに呼ぶ。
    ///
    /// 離れたら消すのは、「押すと反映」が**その一覧を今読んでいる人のためのもの**だから。
    /// 離れた時点で守るものが無くなるので、戻ってきたら黙って最新にする。
    /// </summary>
    public void ClearPendingItems() => RunOnUiThread(() => PendingItemCount = 0);

    private void RefreshCounts()
    {
        UnresolvedCount = _services.Store.Unresolved.Load().Count;
        NeedsEditCount = Search.NeedsEditCount;
        UnreadCount = _services.Notifications.Load().Count(record => !record.IsRead);
    }
}
