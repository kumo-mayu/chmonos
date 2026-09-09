using BoothAssetManager.Core.Storage;
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

    /// <summary>
    /// 取り込み元として登録したフォルダが、今その場所に無い。
    /// 外付けを外している場合もあるので、消せとは言わない。
    /// </summary>
    public string StatusText => Exists ? string.Empty : "今つながっていません";

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

        // 取り込みが始まる／終わると「場所を変える」の可否と理由が変わる。
        // ボタンの enabled は RelayCommand の一括通知で戻るが、理由の文は自分で書き換える
        _main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsImporting))
            {
                OnPropertyChanged(nameof(CanChangeRoot));
                OnPropertyChanged(nameof(RootLockedNote));
            }
        };

        AddFolderCommand = new RelayCommand(AddFolder);
        OpenRootCommand = new RelayCommand(OpenRoot);
        ChangeRootCommand = new RelayCommand(ChangeRoot, () => CanChangeRoot);
        RestartCommand = new RelayCommand(Restart);

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
        _resumeFetchInBackground = settings.ResumeFetchInBackground;
        _saveImages = settings.SaveImages;
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

    /// <summary>保存先を選び直す。Cドライブ以外に置きたいときの入口。</summary>
    public RelayCommand ChangeRootCommand { get; }

    public RelayCommand RestartCommand { get; }

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

    private bool _resumeFetchInBackground;

    /// <summary>
    /// 使っていない間もBOOTHから取り続けるか。前の取り込みで残った画像を次の起動で取り直す。
    /// 通信の様子は常設の1行に出るので、勝手に何かしていると見えるものには止める手段が要る。
    /// </summary>
    public bool ResumeFetchInBackground
    {
        get => _resumeFetchInBackground;
        set { if (SetField(ref _resumeFetchInBackground, value)) { Save(); } }
    }

    private bool _saveImages;

    /// <summary>
    /// 画像を取って置くか。切ると梯子の④⑤⑥が落ち、一覧とギャラリーが文字だけになる。
    /// 検索・絞り込み・統計は商品JSONだけで成立するので、機能は何も失われない。
    ///
    /// 切り替えたら再起動を促す。取得の可否は起動時に組み立てたサービスへ渡っているので、
    /// その場で切り替えるより「次からこうなる」と言い切る方が説明が短い。
    /// </summary>
    public bool SaveImages
    {
        get => _saveImages;
        set
        {
            if (!SetField(ref _saveImages, value))
            {
                return;
            }

            Save();

            if (_suppressSave)
            {
                return;
            }

            System.Windows.MessageBox.Show(
                value
                    ? "次に開いたときから画像を取り始めます。\n\n"
                        + "今ある商品の画像も、使っていない間に少しずつ取りに行きます。"
                    : "次に開いたときから画像を取りません。\n\n"
                        + "取り込みは速くなり、一覧とギャラリーは文字だけになります。\n"
                        + "検索・絞り込み・統計はこれまで通り使えます。\n"
                        + "既にある画像は消しません。",
                "再起動すると変わります",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

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
            ResumeFetchInBackground = ResumeFetchInBackground,
            SaveImages = SaveImages,
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
            // 原因はこちらでは分からないので、断定も指示もしない。
            // 見当だけ添えて、判断はユーザに残す
            Status = $"保存できませんでした：{exception.Message}（保存先が読み取り専用になっていることがあります）";
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

    /// <summary>
    /// 保存先を選び直す。
    ///
    /// 実行中に差し替えるには全サービスと全画面を作り直す必要があるので、再起動で効かせる。
    /// 引越しをするかは必ず聞く。「見る場所を変える」と「物を移す」は別の操作で、
    /// 数GBの移動を黙って始めてよいものではない。
    /// </summary>
    /// <summary>
    /// 環境変数で保存先を差し替えている間は、設定から変えても意味がない
    /// （次の起動でも環境変数が勝つ）。押せる顔をして効かないより、押せなくして理由を出す。
    /// </summary>
    public bool CanChangeRoot
        => Core.Storage.StoreLocation.Resolve().Source != Core.Storage.StoreRootSource.Environment
            && !_main.IsImporting;

    /// <summary>
    /// 押せない理由。**押せる顔をして効かないより、押せなくして理由を出す。**
    /// 取り込み中を塞ぐのは、運んでいる間の書き込みが元の場所へ行ってしまうため。
    /// </summary>
    public string RootLockedNote
    {
        get
        {
            if (Core.Storage.StoreLocation.Resolve().Source == Core.Storage.StoreRootSource.Environment)
            {
                return $"環境変数 {Core.Storage.AppPaths.RootVariable} で保存先が指定されているため、ここからは変えられません。";
            }

            return _main.IsImporting
                ? "取り込みが走っている間は場所を変えられません。終わるか、中断してから変えてください。"
                : string.Empty;
        }
    }

    private void ChangeRoot()
    {
        if (!CanChangeRoot)
        {
            return;
        }

        var picked = PickFolder();
        if (picked is null || string.Equals(picked, _services.Paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var source = _services.Paths.Root;

        // 選んだ先に既にライブラリがあるなら、どちらを残すかを選んでもらう。
        // 向こうが古い作りかけということもあるので、勝手にどちらかへ寄せない
        if (StoreLocation.LooksLikeStore(picked))
        {
            var here = Core.Storage.StoreMover.Summarize(source);
            var there = Core.Storage.StoreMover.Summarize(picked);

            var answer = System.Windows.MessageBox.Show(
                "選んだ場所には既にライブラリがあります。どちらを残しますか。\n\n"
                + $"【今の保存先】{source}\n{Describe(here)}\n\n"
                + $"【選んだ場所】{picked}\n{Describe(there)}\n\n"
                + "［はい］今のデータで置き換える\n"
                + "　　選んだ場所にあるものは消さず、「_置き換え前-（日時）」へ退けてから入れ替えます。\n\n"
                + "［いいえ］選んだ場所のデータをそのまま使う\n"
                + "　　今のデータは元の場所に残ります。混ぜることはしません。",
                "どちらのライブラリを残しますか",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.Cancel);

            if (answer == System.Windows.MessageBoxResult.Cancel)
            {
                return;
            }

            if (answer == System.Windows.MessageBoxResult.No)
            {
                StoreLocation.Save(picked);
                PendingRoot = picked;
                RootNotice = $"次の起動から「{picked}」を使います。今のデータは「{source}」に残っています。";
                RaiseRootChanged();
                return;
            }

            _services.ReleaseInstanceLock();
            var replaced = Core.Storage.StoreMover.Replace(source, picked);

            if (!replaced.Succeeded)
            {
                System.Windows.MessageBox.Show(
                    $"置き換えられませんでした。\n\n{replaced.Error}\n\n"
                    + "保存先は元のままです。データは失われていません。"
                    + (replaced.ParkedAt is null
                        ? string.Empty
                        : $"\n\n選んだ場所のデータは「{replaced.ParkedAt}」に退けたままです。"),
                    "置き換えに失敗しました",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            StoreLocation.Save(picked);
            PendingRoot = picked;
            RootNotice = $"{replaced.Copied:N0} ファイルを「{picked}」へ移して置き換えました。"
                + $"元々あったものは「{replaced.ParkedAt}」に残してあります（中身を確かめてから消してください）。"
                + "再起動すると新しい場所を使います。";
            RaiseRootChanged();
            return;
        }

        if (!StoreLocation.IsEmpty(picked))
        {
            System.Windows.MessageBox.Show(
                $"選んだ場所には別のファイルが入っています。\n\n{picked}\n\n"
                + "取り違えると中身が混ざるので、空のフォルダか、このアプリのデータが入っている場所を選んでください。",
                "この場所は使えません",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var (files, bytes) = Core.Storage.StoreMover.Measure(source);

        var move = System.Windows.MessageBox.Show(
            $"保存先を変えます。\n\n変更前：{source}\n変更後：{picked}\n\n"
            + $"今のデータ（{files:N0} ファイル / {FormatSize(bytes)}）を新しい場所へ引っ越しますか？\n\n"
            + "［はい］コピーしてから元を消します。途中で失敗した場合は元のままにします。\n"
            + "［いいえ］場所だけ変えます。新しい場所は空なので、次の起動では何も無い状態から始まります。",
            "データを引っ越しますか",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Yes);

        if (move == System.Windows.MessageBoxResult.Cancel)
        {
            return;
        }

        if (move == System.Windows.MessageBoxResult.Yes)
        {
            // 実行中のロックを持ったままだと、元のフォルダを畳みきれない
            _services.ReleaseInstanceLock();

            var result = Core.Storage.StoreMover.Move(source, picked);

            if (!result.Succeeded)
            {
                System.Windows.MessageBox.Show(
                    $"引越しできませんでした。\n\n{result.Error}\n\n"
                    + "保存先は元のままです。データは失われていません。",
                    "引越しに失敗しました",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            RootNotice = result.SourceRemoved
                ? $"{result.Copied:N0} ファイルを「{picked}」へ移しました。再起動すると新しい場所を使います。"
                : $"{result.Copied:N0} ファイルを「{picked}」へ移しました。元の場所に消せなかったファイルが残っています。";
        }
        else
        {
            RootNotice = $"次の起動から「{picked}」を使います。データは移していないので、"
                + $"「{source}」の中身は元の場所に残ります。";
        }

        StoreLocation.Save(picked);
        PendingRoot = picked;
        RaiseRootChanged();
    }

    /// <summary>
    /// ライブラリ2つを見比べてもらうための1行。
    /// 件数だけでは「どちらが新しいか」は分からないので、最終更新も出す。
    /// </summary>
    private static string Describe(Core.Storage.StoreSummary summary)
        => summary.Files == 0
            ? "　（空）"
            : $"　{summary.Files:N0} ファイル / {FormatSize(summary.Bytes)}"
                + (summary.LastWrite is { } at ? $"　最終更新 {at:yyyy-MM-dd HH:mm}" : string.Empty);

    /// <summary>再起動して初めて効くので、そこまで案内する。</summary>
    public string? PendingRoot { get; private set; }

    public bool HasPendingRoot => PendingRoot is not null;

    public string RootNotice { get; private set; } = string.Empty;

    private void RaiseRootChanged()
    {
        OnPropertyChanged(nameof(PendingRoot));
        OnPropertyChanged(nameof(HasPendingRoot));
        OnPropertyChanged(nameof(RootNotice));
    }

    private static string? PickFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "データの保存先を選ぶ",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void Restart()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
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
