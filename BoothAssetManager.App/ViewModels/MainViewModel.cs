using BoothAssetManager.App.Services;

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

    public MainViewModel(AppServiceContainer services)
    {
        _services = services;
        Thumbnails = new ThumbnailLoader(services.Settings.ThumbnailCacheBudgetMb);

        // 検索画面は使い捨てにせず1つだけ持ち回る。
        // 商品ページから戻った時に、絞り込み条件やスクロール位置を保つため。
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

        ShowSearch();
        RefreshCounts();
    }

    public ThumbnailLoader Thumbnails { get; }

    public SearchViewModel Search { get; }

    public ImportViewModel Import { get; }

    public RelayCommand ShowSearchCommand { get; }

    public RelayCommand ShowImportCommand { get; }

    public object? CurrentViewModel
    {
        get => _currentViewModel;
        private set
        {
            if (SetField(ref _currentViewModel, value))
            {
                OnPropertyChanged(nameof(IsSearchActive));
                OnPropertyChanged(nameof(IsImportActive));
            }
        }
    }

    public bool IsSearchActive => CurrentViewModel is SearchViewModel;

    public bool IsImportActive => CurrentViewModel is ImportViewModel;

    /// <summary>
    /// 商品ページを開く。検索画面のインスタンスは保持したままなので、
    /// 戻ったときに絞り込み条件もスクロール位置もそのまま残る。
    /// </summary>
    public void ShowItem(Core.Models.ItemRecord item)
        => CurrentViewModel = new ItemViewModel(item, _services, this, Thumbnails);

    /// <summary>未確定ファイルの総件数。「残っている作業量」を示す。</summary>
    public int UnresolvedCount
    {
        get => _unresolvedCount;
        private set => SetField(ref _unresolvedCount, value);
    }

    /// <summary>appTagが未設定のitem数。こちらも総数で示す。</summary>
    public int NeedsEditCount
    {
        get => _needsEditCount;
        private set => SetField(ref _needsEditCount, value);
    }

    public string LibrarySummary => $"{Search.TotalCount} items / {Search.ShopCount} shops";

    public void ShowSearch() => CurrentViewModel = Search;

    public void ShowImport() => CurrentViewModel = Import;

    /// <summary>取り込み後など、ライブラリが変わったときに呼ぶ。</summary>
    public async Task ReloadLibraryAsync()
    {
        await Search.ReloadAsync();
        RefreshCounts();
        OnPropertyChanged(nameof(LibrarySummary));
    }

    private void RefreshCounts()
    {
        UnresolvedCount = _services.Store.Unresolved.Load().Count;
        NeedsEditCount = Search.NeedsEditCount;
    }
}
