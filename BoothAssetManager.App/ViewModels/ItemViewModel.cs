using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Images;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

public sealed class GalleryImage
{
    public required string Path { get; init; }

    public required BitmapSource? Image { get; init; }

    /// <summary>BOOTH側の一覧から消えた画像。手元には残しておく。</summary>
    public bool IsOrphaned { get; init; }
}

public sealed class VariationRow
{
    public required string Name { get; init; }

    public required string PriceText { get; init; }

    public bool IsPurchased { get; init; }

    /// <summary>BOOTH側に現存しない購入記録か。</summary>
    public bool IsGone { get; init; }
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
public sealed class ItemViewModel : ViewModelBase
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
        OpenInExplorerCommand = new RelayCommand(OpenInExplorer, parameter => parameter is string);
        SelectImageCommand = new RelayCommand(SelectImage, parameter => parameter is GalleryImage);

        BuildGallery();
        BuildVariations();
        BuildLocalFiles();
    }

    public ItemRecord Item { get; }

    public RelayCommand BackCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand OpenInExplorerCommand { get; }

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

    public string FileSummary => Item.IsDownloaded
        ? $"{Item.Local.LocalFiles.Count} 件 / {FormatSize(Item.LogicalSizeBytes)}"
        : "ファイルなし";

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
            if (SetField(ref _selectedIndex, value))
            {
                OnPropertyChanged(nameof(SelectedImage));
                OnPropertyChanged(nameof(GalleryCounter));
            }
        }
    }

    public BitmapSource? SelectedImage => Images.Count == 0 ? null : Images[SelectedIndex].Image;

    public string GalleryCounter => Images.Count == 0 ? string.Empty : $"{SelectedIndex + 1} / {Images.Count}";

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
