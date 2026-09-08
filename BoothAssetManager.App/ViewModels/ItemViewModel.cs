using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Controls;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;
using BoothZipInspector;

namespace BoothAssetManager.App.ViewModels;

public sealed class GalleryImage : ViewModelBase
{
    private bool _isSelected;

    public required string Path { get; init; }

    public required BitmapSource? Image { get; init; }

    /// <summary>BOOTH側の一覧から消えた画像。手元には残しておく。</summary>
    public bool IsOrphaned { get; init; }

    /// <summary>今メインに出ている画像か。一覧のどれを見ているか分かるようにする。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetField(ref _isSelected, value);
    }
}

public sealed class VariationRow
{
    public required string Name { get; init; }

    public required string PriceText { get; init; }

    public bool IsPurchased { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }
}

/// <summary>フォルダとして所有している1件。中身は個別に記録していない。</summary>
public sealed class LocalFolderRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string SummaryText { get; init; }

    /// <summary>登録した場所に今もあるか。無ければ指し直しが要る。</summary>
    public bool IsMissing { get; init; }
}

public sealed class LocalFileRow
{
    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }

    public string? VariationLabel { get; init; }

    public bool HasVariationLabel => VariationLabel is not null;

    /// <summary>同じ中身が複数箇所にある状態。容量は1回しか数えない。</summary>
    public bool HasMultiplePaths => Paths.Count > 1;

    public string DuplicateNote => $"{Paths.Count}箇所に同じ実体";

    public bool IsMissing => Paths.Count == 0;
}

/// <summary>
/// 商品ページ。BOOTHの商品ページを参考にしつつ、ローカルの情報から組み立てる。
/// 閲覧専用にしているのは決定事項（編集はEdit画面へ一本化し、保存経路を1つに保つ）。
/// </summary>
public sealed class ItemViewModel : ViewModelBase, IInAppLinkNavigator
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private readonly ThumbnailLoader _thumbnails;
    private int _selectedIndex;

    public ItemViewModel(ItemRecord item, AppServiceContainer services, MainViewModel main, ThumbnailLoader thumbnails)
    {
        Item = item;
        _services = services;
        _main = main;
        _thumbnails = thumbnails;

        BackCommand = new RelayCommand(() => main.ShowSearch());
        OpenBoothCommand = new RelayCommand(OpenBooth);
        // 一度appTagを付けたitemは既定の編集キューに載らないので、ここから開く経路が要る
        EditCommand = new RelayCommand(() => _ = main.ShowEditAsync([item.Id]));
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, parameter => parameter is string);
        UnregisterFolderCommand = new RelayCommand(
            parameter => _ = UnregisterFolderAsync(parameter as string),
            parameter => parameter is string);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);

        BuildGallery();
        BuildVariations();
        BuildLocalFiles();
        BuildLocalFolders();
    }

    public ItemRecord Item { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand OpenInExplorerCommand { get; }

    public RelayCommand UnregisterFolderCommand { get; }

    /// <summary>
    /// フォルダの紐付けを解除する。ファイルには触らない。
    /// zipを後から手に入れると、展開先は自動で対象外になるが登録は残り、容量が二重に乗る。
    /// </summary>
    private async Task UnregisterFolderAsync(string? folderPath)
    {
        if (folderPath is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダの紐付けを解除します。\n\n{folderPath}\n\n"
            + "ファイルは消しません。以降このフォルダの中もスキャン対象に戻ります。",
            "フォルダの登録を解除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        await _services.Commands.ExecuteAsync(new UiCommand.UnregisterFolder(Item.Id, folderPath));

        var reloaded = await _services.Store.Items.LoadAsync(Item.Id);
        if (reloaded is not null)
        {
            _main.ShowItem(reloaded);
        }
    }

    public RelayCommand SelectImageCommand { get; }

    public ObservableCollection<GalleryImage> Images { get; } = [];

    public ObservableCollection<VariationRow> Variations { get; } = [];

    public ObservableCollection<LocalFileRow> LocalFiles { get; } = [];

    public string Name => Item.Booth.Name ?? Item.Id;

    public string ShopName => Item.Booth.Shop?.Name ?? "(ショップ不明)";

    public string ShopSubdomain => Item.Booth.Shop?.Subdomain ?? string.Empty;

    public string CategoryText => Item.Booth.Category is null
        ? string.Empty
        : Item.Booth.Category.ParentName is null
            ? Item.Booth.Category.Name
            : $"{Item.Booth.Category.ParentName} / {Item.Booth.Category.Name}";

    public string IdText => $"ID {Item.Id}";

    public string PublishedText => Item.Booth.PublishedAt is null
        ? string.Empty
        : $"公開 {Item.Booth.PublishedAt:yyyy-MM-dd}";

    public string WishText => $"♡ {Item.Booth.WishListsCount:N0}";

    public IReadOnlyList<string> Tags => Item.Booth.Tags;

    public IReadOnlyList<AppTagAssignment> AppTags => Item.Local.AppTags;

    public bool HasAppTags => Item.Local.AppTags.Count > 0;

    public IReadOnlyList<H2Section> Sections => Item.Booth.H2Sections;

    public bool HasSections => Item.Booth.H2Sections.Count > 0;

    public string? Description => Item.Booth.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Item.Booth.Description);

    public string? Memo => Item.Local.Memo;

    public bool HasMemo => !string.IsNullOrWhiteSpace(Item.Local.Memo);

    public IReadOnlyList<AvatarLink> Avatars => Item.Local.Avatars;

    public bool HasAvatars => Item.Local.Avatars.Count > 0;

    public IReadOnlyList<AttributeBar> Attributes { get; private set; } = [];

    public bool HasAttributes => Attributes.Count > 0;

    /// <summary>フォルダとして所有しているもの。zipが残っていない展開済みの配布物。</summary>
    public IReadOnlyList<LocalFolderRow> LocalFolders { get; private set; } = [];

    public bool HasLocalFolders => LocalFolders.Count > 0;

    public string FileSummary => Item.IsDownloaded
        ? $"{Item.Local.LocalFiles.Count} 件 / {FormatSize(Item.LogicalSizeBytes)}"
        : "ファイルなし";

    /// <summary>
    /// 入手日。手入力が無ければファイルの日付で代え、代えたことを明記する。
    /// 補った値を手入力と同じ顔で出すと、記録として信用できなくなる。
    /// </summary>
    public string AcquiredText
    {
        get
        {
            var acquired = AcquiredDateResolver.Resolve(Item);
            if (acquired.Value is not { } date)
            {
                return "-";
            }

            return acquired.IsFallback ? $"{date:yyyy-MM-dd}（ファイルの日付）" : date.ToString("yyyy-MM-dd");
        }
    }

    public string LastFetchedText => Item.Local.LastFetchedAt is null
        ? "-"
        : Item.Local.LastFetchedAt.Value.ToString("yyyy-MM-dd");

    public string NextFetchText => Item.Local.NextFetchDueAt is null
        ? "-"
        : Item.Local.NextFetchDueAt.Value.ToString("yyyy-MM-dd");

    public bool NotifyOnUpdate => Item.Local.NotifyOnUpdate;

    public int OrphanedImageCount => Images.Count(image => image.IsOrphaned);

    public bool HasOrphanedImages => OrphanedImageCount > 0;

    public string OrphanedImageText => $"BOOTHから削除された画像 {OrphanedImageCount} 枚（手元には残っています）";

    public int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            if (value < 0 || value >= Images.Count || value == _selectedIndex)
            {
                return;
            }

            Images[_selectedIndex].IsSelected = false;
            SetField(ref _selectedIndex, value);
            Images[_selectedIndex].IsSelected = true;

            OnPropertyChanged(nameof(SelectedImage));
            OnPropertyChanged(nameof(GalleryCounter));
        }
    }

    public BitmapSource? SelectedImage => Images.Count == 0 ? null : Images[SelectedIndex].Image;

    public string GalleryCounter => Images.Count == 0 ? string.Empty : $"{SelectedIndex + 1} / {Images.Count}";

    /// <summary>サムネイル一覧にマウスを乗せるだけで切り替えるか。設定で変えられる。</summary>
    public bool SwitchOnHover => _services.Settings.GallerySwitchOnHover;

    /// <summary>乗ってから切り替わるまでの滞留時間（ミリ秒）。通過しただけでは切り替えないための間。</summary>
    public int HoverDelayMs => Math.Max(0, _services.Settings.GalleryHoverDelayMs);

    /// <summary>
    /// サムネイル一覧のホバーでメイン画像を切り替える。
    /// マウスが一覧から離れても戻さない。最後に見た画像がそのまま残る方が、
    /// 大きい画像をじっくり見るときに扱いやすいため。
    /// </summary>
    public void HoverImage(GalleryImage image)
    {
        if (SwitchOnHover)
        {
            SelectImage(image);
        }
    }

    private void BuildGallery()
    {
        var directory = _services.Paths.ItemImagesDir(Item.Id);
        var onDisk = _thumbnails.ListFiles(directory);

        // BOOTHの並び順を正として、その順に手元のファイルを並べる
        var expected = new List<string>();
        foreach (var image in Item.Booth.Images)
        {
            var path = Path.Combine(directory, ImagePipeline.FileNameFor(image.OriginalUrl));
            if (onDisk.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(path);
                Images.Add(new GalleryImage { Path = path, Image = _thumbnails.Load(path) });
            }
        }

        // BOOTH側から消えた画像は、並びの後ろに控えめに続ける（消さずに残す方針）
        foreach (var path in onDisk.Where(path => !expected.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            Images.Add(new GalleryImage { Path = path, Image = _thumbnails.Load(path), IsOrphaned = true });
        }

        if (Images.Count > 0)
        {
            Images[0].IsSelected = true;
        }

        OnPropertyChanged(nameof(SelectedImage));
        OnPropertyChanged(nameof(GalleryCounter));
        OnPropertyChanged(nameof(HasOrphanedImages));
        OnPropertyChanged(nameof(OrphanedImageText));

        Attributes = Item.Local.Attributes
            .OrderByDescending(pair => pair.Value)
            .Select(pair => new AttributeBar { Name = pair.Key, Value = pair.Value })
            .ToList();
    }

    private void BuildVariations()
    {
        var ordered = Item.Local.OrderedVariations.ToDictionary(record => record.VariationId);

        foreach (var variation in Item.Booth.Variations)
        {
            var purchased = ordered.TryGetValue(variation.Id, out var record);
            Variations.Add(new VariationRow
            {
                Name = variation.Name ?? "（バリエーションなし）",
                PriceText = purchased && record!.Price is not null
                    ? $"¥{record.Price:N0} で購入"
                    : $"¥{variation.Price:N0}",
                IsPurchased = purchased,
            });
        }

        // BOOTH側から消えた購入記録も、支出の記録として残っているので出す
        var currentIds = Item.Booth.Variations.Select(variation => variation.Id).ToHashSet();
        foreach (var record in Item.Local.OrderedVariations.Where(record => !currentIds.Contains(record.VariationId)))
        {
            Variations.Add(new VariationRow
            {
                Name = record.NameSnapshot ?? $"variation {record.VariationId}",
                PriceText = record.Price is null ? "価格未入力" : $"¥{record.Price:N0} で購入",
                IsPurchased = true,
                IsGone = true,
            });
        }
    }

    private void BuildLocalFiles()
    {
        foreach (var file in Item.Local.LocalFiles)
        {
            var variation = file.VariationId is null
                ? null
                : Item.Booth.Variations.FirstOrDefault(entry => entry.Id == file.VariationId)?.Name;

            LocalFiles.Add(new LocalFileRow
            {
                FileName = file.Paths.Count > 0 ? Path.GetFileName(file.Paths[0]) : "(見つかりません)",
                SizeText = FormatSize(file.SizeBytes),
                Paths = file.Paths,
                VariationLabel = variation,
            });
        }
    }

    private void BuildLocalFolders()
    {
        LocalFolders = Item.Local.LocalFolders
            .Select(folder => new LocalFolderRow
            {
                Path = folder.Path,
                Name = System.IO.Path.GetFileName(folder.Path),
                SummaryText = $"{folder.FileCount} ファイル / {FormatSize(folder.TotalBytes)}",
                IsMissing = !Directory.Exists(folder.Path),
            })
            .ToList();
    }

    private void SelectImage(object? parameter)
    {
        if (parameter is GalleryImage image)
        {
            var index = Images.IndexOf(image);
            if (index >= 0)
            {
                SelectedIndex = index;
            }
        }
    }

    /// <summary>
    /// 本文中のBOOTH商品リンクのうち、ライブラリに持っているものはアプリ内で開く。
    /// 対応アバターなど、説明文から辿った先が手元にある商品であることは多い。
    /// </summary>
    public bool CanNavigate(Uri uri)
    {
        var itemId = BoothUrlExtractor.TryExtractItemId(uri.AbsoluteUri);
        return itemId is not null && _services.Store.Items.Exists(itemId);
    }

    public void Navigate(Uri uri)
    {
        var itemId = BoothUrlExtractor.TryExtractItemId(uri.AbsoluteUri);
        if (itemId is not null)
        {
            _ = OpenLinkedItemAsync(itemId);
        }
    }

    private async Task OpenLinkedItemAsync(string itemId)
    {
        var record = await _services.Store.Items.LoadAsync(itemId);
        if (record is not null)
        {
            _main.ShowItem(record);
        }
    }

    private void OpenBooth()
    {
        var url = Item.Booth.Url ?? BoothClient.ItemPageUrl(Item.Id);
        TryStart(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    /// <summary>エクスプローラで開いて、そのファイルを選択した状態にする。</summary>
    private void OpenInExplorer(object? parameter)
    {
        if (parameter is not string path)
        {
            return;
        }

        if (File.Exists(path))
        {
            TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else
        {
            var directory = Path.GetDirectoryName(path);
            if (Directory.Exists(directory))
            {
                TryStart(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
            }
        }
    }

    private static void TryStart(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくてもアプリは動き続ける
        }
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

public sealed class AttributeBar
{
    public required string Name { get; init; }

    public required int Value { get; init; }

    public double BarWidth => Value * 2.4;
}
