using System.IO;
using System.Collections.ObjectModel;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Scanning;

namespace BoothAssetManager.App.ViewModels;

public sealed class UnpackedFolderRow
{
    public required string Name { get; init; }

    public required string ArchiveName { get; init; }

    public required string SizeText { get; init; }

    public int FileCount { get; init; }
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
    }

    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<UnpackedFolderRow> UnpackedFolders { get; } = [];

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand RemoveFolderCommand { get; }

    public RelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

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

    public bool HasUnpackedFolders => UnpackedFolders.Count > 0;

    /// <summary>ドロップされたパスを受け取る。ファイルが落とされたらその親フォルダを対象にする。</summary>
    public void AddDroppedPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(folder);
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

    private async Task RunAsync()
    {
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
        }));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ScanFolders(Folders.ToList()),
                progress,
                _cancellation.Token);

            if (result is CommandResult.Imported imported)
            {
                Summary = imported.Summary;
                foreach (var folder in imported.Summary.UnpackedFolders.OrderByDescending(entry => entry.TotalBytes))
                {
                    UnpackedFolders.Add(new UnpackedFolderRow
                    {
                        Name = Path.GetFileName(folder.Path),
                        ArchiveName = Path.GetFileName(folder.ArchivePath),
                        FileCount = folder.FileCount,
                        SizeText = FormatSize(folder.TotalBytes),
                    });
                }

                OnPropertyChanged(nameof(HasUnpackedFolders));
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
