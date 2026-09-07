using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 検索画面。アプリの生存期間中1つだけ持ち回るので、条件やスクロール位置がそのまま残る。
///
/// 絞り込み（離散値）と文字列検索を分けているのは、
/// 「なぜこの結果になったか」が分かるようにするため。
/// </summary>
public sealed class SearchViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly ThumbnailLoader _thumbnails;
    private List<ItemRecord> _allItems = [];
    private string _queryText = string.Empty;
    private string? _selectedCategory;
    private bool _ownedOnly;
    private bool _isLoading;

    private MainViewModel? _main;

    public SearchViewModel(AppServiceContainer services, ThumbnailLoader thumbnails)
    {
        _services = services;
        _thumbnails = thumbnails;
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        _ = ReloadAsync();
    }

    /// <summary>画面遷移のために親を後から渡す（生成順の都合でコンストラクタでは受け取れない）。</summary>
    public void AttachMain(MainViewModel main) => _main = main;

    public void OpenItem(ItemCardViewModel card) => _main?.ShowItem(card.Item);

    public ObservableCollection<ItemCardViewModel> Results { get; } = [];

    public ObservableCollection<string> Categories { get; } = [];

    public RelayCommand ClearFiltersCommand { get; }

    /// <summary>name / shop / メモ / 説明文 を横断して探す。</summary>
    public string QueryText
    {
        get => _queryText;
        set
        {
            if (SetField(ref _queryText, value))
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

    public int NeedsEditCount => _allItems.Count(item => item.Local.AppTags.Count == 0);

    public string ResultSummary => $"{Results.Count} 件";

    public bool IsEmpty => !IsLoading && Results.Count == 0;

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

            RunOnUiThread(() =>
            {
                Categories.Clear();
                Categories.Add(AllCategories);
                foreach (var category in _allItems
                    .Select(item => item.Booth.Category?.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct(StringComparer.CurrentCulture)
                    .OrderBy(name => name, StringComparer.CurrentCulture))
                {
                    Categories.Add(category!);
                }

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

    private void ClearFilters()
    {
        _queryText = string.Empty;
        _selectedCategory = AllCategories;
        _ownedOnly = false;
        OnPropertyChanged(nameof(QueryText));
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(OwnedOnly));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var matches = _allItems.Where(Matches).ToList();

        Results.Clear();
        foreach (var item in matches)
        {
            Results.Add(ToCard(item));
        }

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(ItemRecord item)
    {
        if (_ownedOnly && !item.IsDownloaded)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(_selectedCategory)
            && _selectedCategory != AllCategories
            && !string.Equals(item.Booth.Category?.Name, _selectedCategory, StringComparison.CurrentCulture))
        {
            return false;
        }

        return MatchesText(item, _queryText);
    }

    /// <summary>
    /// 自由記述の横断検索。name / ショップ名 / メモ / 説明文（セクション本文）を対象にする。
    /// 説明文はプレーンテキストを持っているので、ここで表示用HTMLを読む必要はない。
    /// </summary>
    public static bool MatchesText(ItemRecord item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var trimmed = query.Trim();

        if (Contains(item.Booth.Name, trimmed)
            || Contains(item.Booth.Shop?.Name, trimmed)
            || Contains(item.Booth.Shop?.Subdomain, trimmed)
            || Contains(item.Local.Memo, trimmed)
            || Contains(item.Booth.Description, trimmed))
        {
            return true;
        }

        if (item.Booth.Tags.Any(tag => Contains(tag, trimmed)))
        {
            return true;
        }

        if (item.Local.LocalFiles.Any(file => file.Paths.Any(path => Contains(Path.GetFileName(path), trimmed))))
        {
            return true;
        }

        return item.Booth.H2Sections.Any(section =>
            Contains(section.Heading, trimmed) || Contains(section.Text, trimmed));
    }

    private static bool Contains(string? value, string query)
        => value is not null && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private ItemCardViewModel ToCard(ItemRecord item)
    {
        var missing = item.Local.LocalFiles.Any(file => file.Paths.Count == 0);

        return new ItemCardViewModel(item, _thumbnails, _services.Paths.ItemImagesDir(item.Id))
        {
            Name = item.Booth.Name ?? item.Id,
            ShopName = item.Booth.Shop?.Name ?? string.Empty,
            SizeText = item.IsDownloaded ? FormatSize(item.LogicalSizeBytes) : "未取得",
            IsOwned = item.IsDownloaded,
            NeedsEdit = item.Local.AppTags.Count == 0,
            HasMissingFile = missing,
            AppTagText = string.Join(" / ", item.Local.AppTags.Select(tag => tag.Top)),
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
