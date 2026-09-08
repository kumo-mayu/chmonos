using System.IO;
using System.Collections.ObjectModel;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

public sealed class UnpackedFolderRow : ViewModelBase
{
    private bool _isSelected;

    public required UnpackedFolder Folder { get; init; }

    public required string Name { get; init; }

    public required string ArchiveName { get; init; }

    public required string SizeText { get; init; }

    public int FileCount { get; init; }

    /// <summary>削除対象に選ばれているか。既定はオフ（消す方を明示的に選ばせる）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>
/// 取り込み画面。フォルダを選ぶ（またはドロップする）と3フェーズを走らせる。
/// 進捗はバックエンドからUIスレッド以外で届くので、必ず <see cref="ViewModelBase.RunOnUiThread"/> を通す。
/// </summary>
public sealed class ImportViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    private string _phaseText = string.Empty;
    private string _detailText = string.Empty;
    private int _current;
    private int _total;
    private ImportSummary? _summary;
    private string? _errorText;
    private bool _isThrottled;

    public ImportViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        foreach (var folder in services.Settings.ImportFolders)
        {
            Folders.Add(folder);
        }

        AddFolderCommand = new RelayCommand(AddFolder, () => !IsRunning);
        RemoveFolderCommand = new RelayCommand(RemoveFolder, parameter => !IsRunning && parameter is string);
        StartCommand = new RelayCommand(() => _ = RunAsync(), () => !IsRunning && Folders.Count > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        SelectAllUnpackedCommand = new RelayCommand(SelectAllUnpacked, () => HasUnpackedFolders);
        RemoveUnpackedCommand = new RelayCommand(() => _ = RemoveUnpackedAsync(), () => !IsRunning && HasUnpackedSelection);
    }

    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<UnpackedFolderRow> UnpackedFolders { get; } = [];

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand RemoveFolderCommand { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllUnpackedCommand { get; }

    public RelayCommand RemoveUnpackedCommand { get; }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsRunning;

    public string PhaseText
    {
        get => _phaseText;
        private set => SetField(ref _phaseText, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetField(ref _detailText, value);
    }

    public int Current
    {
        get => _current;
        private set
        {
            if (SetField(ref _current, value))
            {
                OnPropertyChanged(nameof(ProgressText));
            }
        }
    }

    public int Total
    {
        get => _total;
        private set
        {
            if (SetField(ref _total, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(HasTotal));
            }
        }
    }

    public bool HasTotal => Total > 0;

    public string ProgressText => Total > 0 ? $"{Current} / {Total}" : Current.ToString();

    public ImportSummary? Summary
    {
        get => _summary;
        private set
        {
            if (SetField(ref _summary, value))
            {
                OnPropertyChanged(nameof(HasSummary));
            }
        }
    }

    public bool HasSummary => Summary is not null;

    public string? ErrorText
    {
        get => _errorText;
        private set
        {
            if (SetField(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>
    /// BOOTHから429を受けて取得間隔を広げている状態か。
    /// 黙って遅くなると原因が分からないので、遅い理由を画面に出す。
    /// </summary>
    public bool IsThrottled
    {
        get => _isThrottled;
        private set
        {
            if (SetField(ref _isThrottled, value))
            {
                OnPropertyChanged(nameof(ThrottleText));
            }
        }
    }

    public string ThrottleText =>
        $"BOOTHから待つよう指示があったため、取得間隔を {_services.Client.CurrentIntervalMs / 1000.0:0.#} 秒に広げています。";

    public bool HasUnpackedFolders => UnpackedFolders.Count > 0;

    public int SelectedUnpackedCount => UnpackedFolders.Count(row => row.IsSelected);

    public bool HasUnpackedSelection => SelectedUnpackedCount > 0;

    public string UnpackedSelectionText => SelectedUnpackedCount == 0
        ? "削除するフォルダを選んでください。"
        : $"{SelectedUnpackedCount} フォルダ / {FormatSize(UnpackedFolders.Where(row => row.IsSelected).Sum(row => row.Folder.TotalBytes))} を削除します。";

    /// <summary>削除の結果。何を消して何を消さなかったかを残す。</summary>
    public ObservableCollection<string> RemovalResults { get; } = [];

    public bool HasRemovalResults => RemovalResults.Count > 0;

    /// <summary>
    /// ドロップされたパスを受け取る。
    ///
    /// ファイルはそのファイルだけを対象にする。親フォルダへ広げると、
    /// ダウンロードフォルダの1件を落としただけでフォルダ全体が対象になってしまう。
    /// </summary>
    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if ((Directory.Exists(path) || File.Exists(path))
                && !Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(path);
            }
        }

        RelayCommand.RaiseCanExecuteChanged();
    }

    private void AddFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "取り込むフォルダを選択",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddDroppedPaths(dialog.FolderNames);
    }

    private void RemoveFolder(object? parameter)
    {
        if (parameter is string folder)
        {
            Folders.Remove(folder);
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private void Cancel() => _cancellation?.Cancel();

    private void OnUnpackedRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(UnpackedFolderRow.IsSelected))
        {
            RaiseSelectionChanged();
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedUnpackedCount));
        OnPropertyChanged(nameof(HasUnpackedSelection));
        OnPropertyChanged(nameof(UnpackedSelectionText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void SelectAllUnpacked()
    {
        // 全部入っているなら全解除。押すたびに切り替える
        var selectAll = UnpackedFolders.Any(row => !row.IsSelected);
        foreach (var row in UnpackedFolders)
        {
            row.IsSelected = selectAll;
        }
    }

    /// <summary>
    /// 選択された展開先フォルダを削除する。取り返しがつかない操作なので、必ず確認を挟む。
    /// 実際に消すのはごみ箱送りで、展開元のzipが残っていることはCore側で再確認している。
    /// </summary>
    private async Task RemoveUnpackedAsync()
    {
        var targets = UnpackedFolders.Where(row => row.IsSelected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var names = string.Join("\n", targets.Take(10).Select(row => $"・{row.Name}（{row.SizeText}）"));
        if (targets.Count > 10)
        {
            names += $"\n…ほか {targets.Count - 10} フォルダ";
        }

        var answer = System.Windows.MessageBox.Show(
            $"次の {targets.Count} フォルダをごみ箱へ移動します。\n\n{names}\n\n"
            + "いずれも展開元のアーカイブが手元に残っているものです。削除しますか？",
            "展開先フォルダの削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsRunning = true;
        RemovalResults.Clear();

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RemoveUnpackedFolders(targets.Select(row => row.Folder).ToList()));

            if (result is CommandResult.UnpackedFoldersRemoved removed)
            {
                var freed = removed.Results.Where(entry => entry.Removed).Sum(entry => entry.FreedBytes);
                var removedCount = removed.Results.Count(entry => entry.Removed);
                RemovalResults.Add($"{removedCount} フォルダを削除しました（{FormatSize(freed)} 空きました）。");

                foreach (var entry in removed.Results.Where(entry => !entry.Removed))
                {
                    RemovalResults.Add($"削除しませんでした: {Path.GetFileName(entry.Path)} — {entry.Reason}");
                }

                // 消えたものだけ一覧から外す。残ったものは理由と一緒に見えたままにする
                foreach (var row in targets.Where(row =>
                    removed.Results.Any(entry => entry.Removed && entry.Path == row.Folder.Path)))
                {
                    row.PropertyChanged -= OnUnpackedRowChanged;
                    UnpackedFolders.Remove(row);
                }
            }
            else if (result is CommandResult.Failed failed)
            {
                RemovalResults.Add(failed.Message);
            }
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(HasUnpackedFolders));
            OnPropertyChanged(nameof(HasRemovalResults));
            RaiseSelectionChanged();
        }
    }

    /// <summary>
    /// 対象フォルダを設定に残す。ファイルが欠落したときの再スキャン範囲も兼ねるので、
    /// 起動のたびに選び直させない。
    /// </summary>
    private async Task SaveFoldersAsync()
    {
        var current = _services.Store.Settings.Load();
        await _services.Store.Settings.SaveAsync(current with { ImportFolders = Folders.ToList() });
    }

    /// <summary>
    /// 指定されたファイルのうち、アーカイブの展開先の中にあるものを元のzipへ差し替える。
    ///
    /// 展開先が残っていると、そのファイルが配布物そのものなのか展開したものなのかを
    /// ファイル単体からは区別できない。zipが手元にあるならそちらの方が配布単位と一致するが、
    /// 意図してその1ファイルを指したのかもしれないので、どちらを使うかは尋ねる。
    /// 1件ずつ聞くと数が多いときに煩わしいので、その取り込み全体の方針として1回だけ聞く。
    /// </summary>
    private List<string> ResolveUnpackedTargets()
    {
        var targets = Folders.ToList();
        var origins = UnpackedFileResolver.FindOrigins(targets);
        if (origins.Count == 0)
        {
            return targets;
        }

        var sample = string.Join("\n", origins.Take(5)
            .Select(origin => $"・{Path.GetFileName(origin.FilePath)} → {Path.GetFileName(origin.ArchivePath)}"));
        if (origins.Count > 5)
        {
            sample += $"\n…ほか {origins.Count - 5} 件";
        }

        var answer = System.Windows.MessageBox.Show(
            $"指定されたファイルのうち {origins.Count} 件が、zipを展開したフォルダの中にあります。\n\n{sample}\n\n"
            + "元のzipの方を取り込みますか？\n"
            + "「はい」でzipに差し替え、「いいえ」で指定されたファイルをそのまま取り込みます。",
            "展開先のファイル",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Yes);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return targets;
        }

        foreach (var origin in origins)
        {
            var index = targets.FindIndex(path => string.Equals(path, origin.FilePath, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            // 同じzipが複数のファイルから指された場合は1つにまとめる
            if (targets.Contains(origin.ArchivePath, StringComparer.OrdinalIgnoreCase))
            {
                targets.RemoveAt(index);
            }
            else
            {
                targets[index] = origin.ArchivePath;
            }
        }

        return targets;
    }

    private async Task RunAsync()
    {
        var targets = ResolveUnpackedTargets();

        await SaveFoldersAsync();

        IsRunning = true;
        Summary = null;
        ErrorText = null;
        UnpackedFolders.Clear();
        Current = 0;
        Total = 0;
        PhaseText = "準備中…";

        _cancellation = new CancellationTokenSource();

        var progress = new Progress<ImportProgress>(report => RunOnUiThread(() =>
        {
            PhaseText = report.Phase switch
            {
                ImportPhase.Scanning => "1. ファイルを走査",
                ImportPhase.Resolving => "2. BoothIDを解決",
                _ => "3. BOOTHから取得（1件ずつ間隔を空けています）",
            };
            Current = report.Current;
            Total = report.Total;
            DetailText = report.Detail ?? string.Empty;

            // 減速は取得の合間に起きるので、進捗が届くたびに見る
            IsThrottled = _services.Client.IsThrottled;
            if (IsThrottled)
            {
                OnPropertyChanged(nameof(ThrottleText));
            }
        }));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ScanFolders(targets),
                progress,
                _cancellation.Token);

            if (result is CommandResult.Imported imported)
            {
                Summary = imported.Summary;
                foreach (var folder in imported.Summary.UnpackedFolders.OrderByDescending(entry => entry.TotalBytes))
                {
                    var row = new UnpackedFolderRow
                    {
                        Folder = folder,
                        Name = Path.GetFileName(folder.Path),
                        ArchiveName = Path.GetFileName(folder.ArchivePath),
                        FileCount = folder.FileCount,
                        SizeText = FormatSize(folder.TotalBytes),
                    };
                    row.PropertyChanged += OnUnpackedRowChanged;
                    UnpackedFolders.Add(row);
                }

                OnPropertyChanged(nameof(HasUnpackedFolders));
                RaiseSelectionChanged();

                await DetectAvatarsAsync(_cancellation.Token);

                PhaseText = "完了";
                DetailText = string.Empty;
                await _main.ReloadLibraryAsync();
            }
            else if (result is CommandResult.Failed failed)
            {
                ErrorText = failed.Message;
                PhaseText = "失敗";
            }
        }
        catch (OperationCanceledException)
        {
            PhaseText = "中断しました";
            DetailText = "再実行すると続きから進みます。";
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>
    /// 取り込みの続きとして対応アバターを検出する。
    ///
    /// 取り込んだ直後は説明文もタグも手元にあるので、ほとんどが通信なしで済む。
    /// BOOTHへ問い合わせるのは、まだ種類の分からない商品IDだけ。
    /// 失敗しても取り込み自体は成功しているので、ここでは止めずに知らせるだけにする。
    /// </summary>
    private async Task DetectAvatarsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var progress = new Progress<AvatarDetectProgress>(report =>
            {
                PhaseText = "4. 対応アバターを検出";
                DetailText = $"{report.Phase}　{report.Done} / {report.Total}";
            });

            var result = await _services.Avatars.DetectAsync(progress, cancellationToken);

            AvatarSummaryText = result.ItemsUpdated == 0
                ? "対応アバターは見つかりませんでした。"
                : $"対応アバターを {result.ItemsUpdated} 件の商品に書きました（アバター {result.AvatarsFound} 体）。";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 取り込みは終わっている。検出はアバター画面からやり直せる
            AvatarSummaryText = $"対応アバターの検出は途中で止まりました：{exception.Message}";
        }
    }

    private string _avatarSummaryText = string.Empty;

    /// <summary>取り込みの後に走らせた検出の結果。</summary>
    public string AvatarSummaryText
    {
        get => _avatarSummaryText;
        private set
        {
            if (SetField(ref _avatarSummaryText, value))
            {
                OnPropertyChanged(nameof(HasAvatarSummary));
            }
        }
    }

    public bool HasAvatarSummary => AvatarSummaryText.Length > 0;

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
