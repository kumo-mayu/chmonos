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

        _ = Task.Run(async () =>
        {
            try
            {
                await _services.Backlog.ResumeAsync(cancellationToken: token);
                await _services.Due.RunAsync(cancellationToken: token);

                // ⑦で商品ページが変わっていれば要確認が増える。件数を出し直す
                RunOnUiThread(RefreshCounts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
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
    /// 見つけても<b>取り込みは始めない</b>。走査は手元のディスクを読むだけだが、
    /// 取り込みはBOOTHへの通信で1件あたり十数秒かかる。起動した瞬間に黙って始めると、
    /// ユーザがこれからやろうとしていた操作と行列を取り合う。件数を出して押させる。
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

    public void ShowShop(Core.Services.ShopSummary shop, (string Label, Action Go)? back = null)
        => CurrentViewModel = new ShopViewModel(shop, _services, this, Thumbnails, back);

    /// <summary>サブドメインからショップ画面を開く。商品ページの作者名からの経路。</summary>
    public async Task ShowShopAsync(string subdomain, (string Label, Action Go)? back = null)
    {
        var shops = await _services.Shops.LoadAsync();
        var shop = shops.FirstOrDefault(entry =>
            string.Equals(entry.Subdomain, subdomain, StringComparison.OrdinalIgnoreCase));

        if (shop is not null)
        {
            ShowShop(shop, back);
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

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
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
            }
        }
    }

    public bool IsSearchActive => CurrentViewModel is SearchViewModel;

    public bool IsImportActive => CurrentViewModel is ImportViewModel;

    public bool IsEditActive => CurrentViewModel is EditViewModel;

    /// <summary>
    /// 編集画面を開く。前回の続きが残っていればそこから、無ければuserTag未設定のitemを積む。
    /// 検索から複数選んで入る経路は <paramref name="itemIds"/> で指定する。
    /// </summary>
    public async Task ShowEditAsync(IReadOnlyList<string>? itemIds = null)
    {
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
    /// 商品ページを開く。検索画面のインスタンスは保持したままなので、
    /// 戻ったときに絞り込み条件もスクロール位置もそのまま残る。
    /// </summary>
    public void ShowItem(Core.Models.ItemRecord item, (string Label, Action Go)? back = null)
        => CurrentViewModel = new ItemViewModel(item, _services, this, Thumbnails, back);

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
        private set => SetField(ref _needsEditCount, value);
    }

    public string LibrarySummary => $"{Search.TotalCount} items / {Search.ShopCount} shops";

    public void ShowSearch() => CurrentViewModel = Search;

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

    public void ShowImport() => CurrentViewModel = Import;

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
        var decision = CurrentViewModel is ItemViewModel
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

                Import.AddDroppedPaths(paths!);
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
        if (CurrentViewModel is not ItemViewModel item)
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
        if (CurrentViewModel is not ItemViewModel item)
        {
            return;
        }

        var name = await _services.Store.Items.LoadAsync(itemId) is { } known
            ? $"「{known.DisplayName}」"
            : $" {itemId} ";

        var answer = System.Windows.MessageBox.Show(
            $"BOOTHの画像を受け取りました。\n\n"
            + $"「はい」…… 商品{name}を開きます\n"
            + $"「いいえ」… この商品「{item.Name}」の画像に足します",
            "BOOTHの画像を受け取りました",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        switch (answer)
        {
            case System.Windows.MessageBoxResult.Yes:
                await OpenOrOfferAsync(itemId);
                return;

            case System.Windows.MessageBoxResult.No:
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
