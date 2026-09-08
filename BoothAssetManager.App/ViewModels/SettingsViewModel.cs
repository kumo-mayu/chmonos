using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>サムネイルの大きさの選択肢。</summary>
public sealed class ThumbnailSizeOption
{
    public required string Label { get; init; }

    public required ThumbnailSize Value { get; init; }
}

/// <summary>取り込み元フォルダの1行。</summary>
public sealed class ImportFolderRow
{
    public required string Path { get; init; }

    public required bool Exists { get; init; }

    public string StatusText => Exists ? string.Empty : "見つかりません";

    public RelayCommand? RemoveCommand { get; set; }
}

/// <summary>非表示にした商品／管理から外したファイルの1行。</summary>
public sealed class RestorableRow
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string SubText { get; init; } = string.Empty;

    public RelayCommand? RestoreCommand { get; set; }
}

/// <summary>
/// 設定。
///
/// 変更は settings.json に即保存する。ただし通信間隔や画像の解像度のように
/// 起動時にサービスへ渡している値は、次の起動から効く。どれがそうなのかは
/// 画面に書いておく（黙って効かないのがいちばん困る）。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private bool _isLoading = true;
    private bool _suppressSave;
    private string _status = string.Empty;
    private StorageUsage? _usage;

    public SettingsViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        AddFolderCommand = new RelayCommand(AddFolder);
        OpenRootCommand = new RelayCommand(OpenRoot);

        var settings = services.Settings;
        _suppressSave = true;
        _tagsAtTop = settings.TagsAtTop;
        _showSubTagsInList = settings.ShowSubTagsInList;
        _showAdult = settings.ShowAdult;
        _showHiddenCountInSearch = settings.ShowHiddenCountInSearch;
        _thumbnailSize = settings.ThumbnailSize;
        _gallerySwitchOnHover = settings.GallerySwitchOnHover;
        _returnToSearchWhenEditDone = settings.ReturnToSearchWhenEditDone;
        _notifyOnUpdateByDefault = settings.NotifyOnUpdateByDefault;
        _refreshIntervalDays = settings.RefreshIntervalDays;
        _notificationRetentionCount = settings.NotificationRetentionCount;
        _fetchIntervalMs = settings.FetchIntervalMs;
        _imageMaxEdgePixels = settings.ImageMaxEdgePixels;
        _imageQuality = settings.ImageQuality;
        _shopBannerRecheckDays = settings.ShopBannerRecheckDays;
        _avatarDetectRecheckDays = settings.AvatarDetectRecheckDays;
        _suppressSave = false;

        _ = LoadAsync();
    }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand OpenRootCommand { get; }

    public ObservableCollection<ImportFolderRow> Folders { get; } = [];

    public ObservableCollection<RestorableRow> Hidden { get; } = [];

    public ObservableCollection<RestorableRow> Excluded { get; } = [];

    public static IReadOnlyList<ThumbnailSizeOption> ThumbnailSizes { get; } =
    [
        new ThumbnailSizeOption { Label = "小", Value = ThumbnailSize.Small },
        new ThumbnailSizeOption { Label = "中", Value = ThumbnailSize.Medium },
        new ThumbnailSizeOption { Label = "大", Value = ThumbnailSize.Large },
    ];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    // ---- 表示 ----

    private bool _tagsAtTop;
    public bool TagsAtTop
    {
        get => _tagsAtTop;
        set { if (SetField(ref _tagsAtTop, value)) { Save(); } }
    }

    private bool _showSubTagsInList;
    public bool ShowSubTagsInList
    {
        get => _showSubTagsInList;
        set { if (SetField(ref _showSubTagsInList, value)) { Save(); } }
    }

    private bool _showAdult;
    public bool ShowAdult
    {
        get => _showAdult;
        set { if (SetField(ref _showAdult, value)) { Save(); } }
    }

    private bool _showHiddenCountInSearch;
    public bool ShowHiddenCountInSearch
    {
        get => _showHiddenCountInSearch;
        set { if (SetField(ref _showHiddenCountInSearch, value)) { Save(); } }
    }

    private ThumbnailSize _thumbnailSize;
    public ThumbnailSize ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            if (SetField(ref _thumbnailSize, value))
            {
                OnPropertyChanged(nameof(SelectedThumbnailSize));
                Save();
            }
        }
    }

    /// <summary>ComboBoxに出す選択肢。enum名をそのまま見せない。</summary>
    public ThumbnailSizeOption SelectedThumbnailSize
    {
        get => ThumbnailSizes.First(option => option.Value == ThumbnailSize);
        set { if (value is not null) { ThumbnailSize = value.Value; } }
    }

    private bool _gallerySwitchOnHover;
    public bool GallerySwitchOnHover
    {
        get => _gallerySwitchOnHover;
        set { if (SetField(ref _gallerySwitchOnHover, value)) { Save(); } }
    }

    private bool _returnToSearchWhenEditDone;
    public bool ReturnToSearchWhenEditDone
    {
        get => _returnToSearchWhenEditDone;
        set { if (SetField(ref _returnToSearchWhenEditDone, value)) { Save(); } }
    }

    // ---- 更新 ----

    private bool _notifyOnUpdateByDefault;
    public bool NotifyOnUpdateByDefault
    {
        get => _notifyOnUpdateByDefault;
        set { if (SetField(ref _notifyOnUpdateByDefault, value)) { Save(); } }
    }

    private int _refreshIntervalDays;
    public int RefreshIntervalDays
    {
        get => _refreshIntervalDays;
        set { if (SetField(ref _refreshIntervalDays, Math.Clamp(value, 1, 365))) { Save(); } }
    }

    private int _notificationRetentionCount;
    public int NotificationRetentionCount
    {
        get => _notificationRetentionCount;
        set { if (SetField(ref _notificationRetentionCount, Math.Clamp(value, 20, 5000))) { Save(); } }
    }

    private int _shopBannerRecheckDays;
    public int ShopBannerRecheckDays
    {
        get => _shopBannerRecheckDays;
        set { if (SetField(ref _shopBannerRecheckDays, Math.Clamp(value, 1, 365))) { Save(); } }
    }

    private int _avatarDetectRecheckDays;
    public int AvatarDetectRecheckDays
    {
        get => _avatarDetectRecheckDays;
        set { if (SetField(ref _avatarDetectRecheckDays, Math.Clamp(value, 1, 365))) { Save(); } }
    }

    // ---- 取得と画像（次の起動から効く） ----

    private int _fetchIntervalMs;
    public int FetchIntervalMs
    {
        get => _fetchIntervalMs;
        set { if (SetField(ref _fetchIntervalMs, Math.Clamp(value, 500, 10000))) { Save(); } }
    }

    private int _imageMaxEdgePixels;
    public int ImageMaxEdgePixels
    {
        get => _imageMaxEdgePixels;
        set { if (SetField(ref _imageMaxEdgePixels, Math.Clamp(value, 128, 2048))) { Save(); } }
    }

    private int _imageQuality;
    public int ImageQuality
    {
        get => _imageQuality;
        set { if (SetField(ref _imageQuality, Math.Clamp(value, 40, 100))) { Save(); } }
    }

    // ---- 保存先 ----

    public string RootPath => _usage?.Root ?? _services.Paths.Root;

    public string ImageUsageText => _usage is null
        ? "…"
        : $"{FormatSize(_usage.ImageBytes)} / {_usage.ImageCount:N0} ファイル";

    public string ItemUsageText => _usage is null
        ? "…"
        : $"{FormatSize(_usage.ItemBytes)} / {_usage.ItemCount:N0} ファイル";

    public bool HasHidden => Hidden.Count > 0;

    public bool HasExcluded => Excluded.Count > 0;

    public string HiddenText => $"{Hidden.Count} 件";

    public string ExcludedText => $"{Excluded.Count} 件";

    private async Task LoadAsync()
    {
        var usage = await _services.SettingsStore.LoadUsageAsync();
        var hidden = await _services.SettingsStore.LoadHiddenAsync();
        var excluded = _services.SettingsStore.LoadExcluded();

        RunOnUiThread(() =>
        {
            _usage = usage;

            Folders.Clear();
            foreach (var path in _services.Settings.ImportFolders)
            {
                var captured = path;
                Folders.Add(new ImportFolderRow
                {
                    Path = path,
                    Exists = Directory.Exists(path),
                    RemoveCommand = new RelayCommand(() => RemoveFolder(captured)),
                });
            }

            Hidden.Clear();
            foreach (var item in hidden)
            {
                var captured = item.ItemId;
                Hidden.Add(new RestorableRow
                {
                    Key = item.ItemId,
                    Label = item.Name,
                    RestoreCommand = new RelayCommand(() => _ = UnhideAsync(captured)),
                });
            }

            Excluded.Clear();
            foreach (var file in excluded)
            {
                var captured = file.Hash;
                Excluded.Add(new RestorableRow
                {
                    Key = file.Hash,
                    Label = file.Path,
                    SubText = file.Reason ?? string.Empty,
                    RestoreCommand = new RelayCommand(() => _ = RestoreAsync(captured)),
                });
            }

            IsLoading = false;

            foreach (var name in new[]
            {
                nameof(RootPath), nameof(ImageUsageText), nameof(ItemUsageText),
                nameof(HasHidden), nameof(HasExcluded), nameof(HiddenText), nameof(ExcludedText),
            })
            {
                OnPropertyChanged(name);
            }
        });
    }

    /// <summary>
    /// 変更のたびに保存する。設定画面に「保存」ボタンを置かないのは、
    /// 押し忘れたまま閉じて設定が消える方が困るため。
    /// </summary>
    private void Save()
    {
        if (_suppressSave)
        {
            return;
        }

        var updated = _services.Settings with
        {
            TagsAtTop = TagsAtTop,
            ShowSubTagsInList = ShowSubTagsInList,
            ShowAdult = ShowAdult,
            ShowHiddenCountInSearch = ShowHiddenCountInSearch,
            ThumbnailSize = ThumbnailSize,
            GallerySwitchOnHover = GallerySwitchOnHover,
            ReturnToSearchWhenEditDone = ReturnToSearchWhenEditDone,
            NotifyOnUpdateByDefault = NotifyOnUpdateByDefault,
            RefreshIntervalDays = RefreshIntervalDays,
            NotificationRetentionCount = NotificationRetentionCount,
            ShopBannerRecheckDays = ShopBannerRecheckDays,
            AvatarDetectRecheckDays = AvatarDetectRecheckDays,
            FetchIntervalMs = FetchIntervalMs,
            ImageMaxEdgePixels = ImageMaxEdgePixels,
            ImageQuality = ImageQuality,
            ImportFolders = Folders.Select(row => row.Path).ToList(),
        };

        _ = SaveAsync(updated);
    }

    private async Task SaveAsync(AppSettings settings)
    {
        try
        {
            await _services.SettingsStore.SaveAsync(settings);
            _services.ReplaceSettings(settings);
            Status = "保存しました。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = $"保存できませんでした：{exception.Message}";
        }
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "取り込み元にするフォルダを選んでください",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (Folders.Any(row => string.Equals(row.Path, dialog.FolderName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var path = dialog.FolderName;
        Folders.Add(new ImportFolderRow
        {
            Path = path,
            Exists = true,
            RemoveCommand = new RelayCommand(() => RemoveFolder(path)),
        });

        Save();
    }

    private void RemoveFolder(string path)
    {
        var row = Folders.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            return;
        }

        Folders.Remove(row);
        Save();
    }

    private async Task UnhideAsync(string itemId)
    {
        await _services.SettingsStore.UnhideAsync(itemId);
        Status = "非表示を解除しました。検索に戻ります。";
        await LoadAsync();
        _ = _main.Search.ReloadAsync();
    }

    private async Task RestoreAsync(string hash)
    {
        await _services.SettingsStore.RestoreExcludedAsync(hash);
        Status = "除外を解除しました。次の取り込みでまた未確定として出てきます。";
        await LoadAsync();
    }

    private void OpenRoot()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = RootPath,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Status = "フォルダを開けませんでした。";
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
