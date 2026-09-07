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
    private List<ItemRecord> _allItems = [];
    private List<ItemCardViewModel> _matches = [];
    private string _queryText = string.Empty;
    private string? _selectedCategory;
    private bool _ownedOnly;
    private bool _isLoading;
    private int _columns = 1;

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

    /// <summary>
    /// 結果を行単位で持つ。行を仮想化の単位にすることで、画面に出ている行のカードだけが実体化する。
    /// WPFには仮想化する WrapPanel が無いので、列数をこちら側で決めて行に切っている。
    /// </summary>
    public ObservableCollection<CardRow> Rows { get; } = [];

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

            RunOnUiThread(() =>
            {
                // カードは絞り込みのたびには作り直さず、itemごとに1つを使い回す。
                // 作り直すと、件数に比例した生成コストがキー入力のたびに掛かる。
                _cards.Clear();
                foreach (var item in _allItems)
                {
                    _cards[item.Id] = ToCard(item);
                }

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
        _matches = _allItems
            .Where(Matches)
            .Select(item => _cards[item.Id])
            .ToList();

        RebuildRows();

        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

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
