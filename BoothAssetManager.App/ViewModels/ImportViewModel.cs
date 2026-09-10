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

    /// <summary>実行中の作業集合。走らせている最中に足せるので、ここを握っておく。</summary>
    private ImportWorkSet? _work;

    private bool _isRunning;
    private string? _stackNotice;
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

        foreach (var folder in services.Settings.WatchedFolders)
        {
            Watched.Add(folder);
        }

        // 前回が途中で終わっていれば知らせる。中断は黙って起きるので、
        // 閉じた時に何件残っていたかをユーザは覚えていない
        var previous = services.Store.ImportState.Load();
        if (previous.HasProgress)
        {
            _interruptedText = previous.Text + "。もう一度押すと続きから進みます。";
        }

        // 実行中でも足せる。「1ファイルだけ後から見つかった」は普通に起きるので、
        // 終わるのを待たせない。押した先は同じ取り込みで、2本目は起こさない
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder, parameter => parameter is string);
        StartCommand = new RelayCommand(() => _ = StartOrStackAsync(), () => Folders.Count > 0);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        SelectAllUnpackedCommand = new RelayCommand(SelectAllUnpacked, () => HasUnpackedFolders);
        RemoveUnpackedCommand = new RelayCommand(() => _ = RemoveUnpackedAsync(), () => !IsRunning && HasUnpackedSelection);
        RemoveWatchedCommand = new RelayCommand(parameter => _ = RemoveWatchedAsync(parameter as string), parameter => parameter is string);
        TakeWatchedNewCommand = new RelayCommand(() => _main.TakeWatchedNew());
    }

    public ObservableCollection<string> Folders { get; } = [];

    public bool HasFolders => Folders.Count > 0;

    /// <summary>
    /// 対象が空のときに出す文。**次にやることを書く。**
    ///
    /// ボタンは既に押せない状態になっているが、それだけだと
    /// 「何を入れればここが埋まるのか」が画面から分からない。
    /// </summary>
    public string FoldersEmptyText =>
        "まだ何も入っていません。上の枠にフォルダかファイルを落とすか、「フォルダを選択」で選んでください。";

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
                OnPropertyChanged(nameof(StartText));

                // 設定画面が保存先の引越しを塞ぐために見る
                _main.IsImporting = value;

                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsRunning;

    /// <summary>
    /// 実行中は「積む」になる。押した先が別の取り込みではなく**今の取り込み**である
    /// ことが、文言だけで分かるようにする。
    /// </summary>
    public string StartText => IsRunning ? "今の取り込みに積む" : "取り込みを開始";

    private string? _interruptedText;

    /// <summary>
    /// 前回が途中で終わっていたことの記録。
    ///
    /// 出すのは、**中断が黙って起きる**から。閉じた時に何件残っていたかを
    /// ユーザは覚えていないので、次に開いたときに思い出せる材料を置く。
    /// </summary>
    public string? InterruptedText
    {
        get => _interruptedText;
        private set
        {
            if (SetField(ref _interruptedText, value))
            {
                OnPropertyChanged(nameof(HasInterrupted));
            }
        }
    }

    public bool HasInterrupted => !string.IsNullOrEmpty(InterruptedText);

    /// <summary>積んだ結果。押しても何も起きなかったときこそ要る。</summary>
    public string? StackNotice
    {
        get => _stackNotice;
        private set
        {
            if (SetField(ref _stackNotice, value))
            {
                OnPropertyChanged(nameof(HasStackNotice));
            }
        }
    }

    public bool HasStackNotice => !string.IsNullOrEmpty(StackNotice);

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
                OnPropertyChanged(nameof(NotFoundText));
                OnPropertyChanged(nameof(HasNotFound));
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
        : $"{SelectedUnpackedCount} フォルダ / {Core.Models.DisplayText.Size(UnpackedFolders.Where(row => row.IsSelected).Sum(row => row.Folder.TotalBytes))} を削除します。";

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
        var addedFolders = new List<string>();

        foreach (var path in paths)
        {
            if ((Directory.Exists(path) || File.Exists(path))
                && !Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Folders.Add(path);

                if (Directory.Exists(path) && !Watched.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    addedFolders.Add(path);
                }
            }
        }

        OnPropertyChanged(nameof(HasFolders));
        RelayCommand.RaiseCanExecuteChanged();

        if (addedFolders.Count > 0)
        {
            _ = OfferToWatchAsync(addedFolders);
        }
    }

    /// <summary>
    /// 足したフォルダを監視対象に入れるか聞く。
    ///
    /// 聞くのは<b>フォルダのときだけ</b>。ファイル1件を落としたのは
    /// 「これを取り込んで」であって「ここを見ておいて」ではない。
    ///
    /// 複数まとめて聞くのは、5つ落として5回聞かれると読まずに押されるため。
    /// </summary>
    private async Task OfferToWatchAsync(IReadOnlyList<string> folders)
    {
        var names = string.Join("\n", folders.Select(folder => "・" + folder));

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダを監視対象に入れますか。\n\n{names}\n\n"
            + "入れておくと、次に開いたときに新しいファイルが増えていないかを見ます。\n"
            + "見つかっても勝手には取り込まず、件数を出すだけです。",
            "監視対象に入れますか",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var folder in folders)
        {
            if (!Watched.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                Watched.Add(folder);
            }
        }

        OnPropertyChanged(nameof(HasWatched));
        await SaveWatchedAsync();
    }

    /// <summary>起動時に見つかった新しいファイルを出すために見る。</summary>
    public MainViewModel Main => _main;

    /// <summary>監視対象。取り込み対象（今回積んだもの）とは別で、起動をまたいで残る。</summary>
    public ObservableCollection<string> Watched { get; } = [];

    public bool HasWatched => Watched.Count > 0;

    public RelayCommand RemoveWatchedCommand { get; }

    /// <summary>見つかったぶんを取り込み対象へ積む。ここを押して初めて通信が始まる。</summary>
    public RelayCommand TakeWatchedNewCommand { get; }

    private async Task RemoveWatchedAsync(string? folder)
    {
        if (folder is null || !Watched.Remove(folder))
        {
            return;
        }

        OnPropertyChanged(nameof(HasWatched));
        await SaveWatchedAsync();
    }

    private async Task SaveWatchedAsync()
    {
        var next = _services.Settings with { WatchedFolders = Watched.ToList() };
        _services.ReplaceSettings(next);
        await _services.Store.Settings.SaveAsync(next);
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
        if (parameter is not string folder)
        {
            return;
        }

        Folders.Remove(folder);
        OnPropertyChanged(nameof(HasFolders));
        RelayCommand.RaiseCanExecuteChanged();

        // 実行中なら、まだ順番が来ていないものは取り下げられる。
        // 「積んだ直後に取り消したい」はこれで済むので、取り消し操作を別に作らない
        if (_work is { } running)
        {
            StackNotice = running.Remove(folder)
                ? "今の取り込みから取り下げました。"
                : "既に走査したので、今の取り込みからは外せません。中断すると止まります。";
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
                RemovalResults.Add($"{removedCount} フォルダを削除しました（{Core.Models.DisplayText.Size(freed)} 空きました）。");

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

    /// <summary>
    /// 走っていなければ始める。走っていれば**今の取り込みに積む**。
    ///
    /// 2本目を起こさないのは、取得の順序（画像より先にJSON）が2本では保てず、
    /// どちらの進捗を出すのかも決められなくなるため。
    /// </summary>
    private async Task StartOrStackAsync()
    {
        if (_work is not { } running)
        {
            await RunAsync();
            return;
        }

        var added = running.Add(ResolveUnpackedTargets());
        await SaveFoldersAsync();

        StackNotice = added == 0
            ? "積むものはありませんでした。選んだフォルダはすべて今の取り込みに入っています。"
            : $"{added} 件を今の取り込みに積みました。順番が来たら走査します。";
    }

    private async Task RunAsync()
    {
        var targets = ResolveUnpackedTargets();

        await SaveFoldersAsync();

        StackNotice = null;

        // 走らせ直したので、前回の中断はもう伝えることが無い
        InterruptedText = null;

        IsRunning = true;
        Summary = null;
        ErrorText = null;
        UnpackedFolders.Clear();
        Current = 0;
        Total = 0;
        PhaseText = "準備中…";

        _cancellation = new CancellationTokenSource();
        _work = new ImportWorkSet(targets);

        var progress = new Progress<ImportProgress>(report => RunOnUiThread(() =>
        {
            // 何を待っているのかと、待たなくてよいことの両方が1行で分かるようにする。
            // ④以降は「取得できたものから使える」が要点で、そこを書かないと
            // 全部終わるまで待つものだと読まれてしまう
            PhaseText = report.Phase switch
            {
                ImportPhase.Scanning => "1. ファイルを走査",
                ImportPhase.Resolving => "2. 商品IDを解決",
                ImportPhase.FetchingJson => "3. 商品の情報を取得",
                ImportPhase.FetchingHtml => "4. 商品ページを取得",
                ImportPhase.Detecting => "5. 対応アバターを検出",
                ImportPhase.FetchingThumbnails => "6. サムネイルを取得（取得できたものから編集できます）",
                ImportPhase.FetchingGallery => "7. ギャラリーを取得",
                _ => "8. ショップのアイコンを取得",
            };
            Current = report.Current;
            Total = report.Total;
            DetailText = report.Detail ?? string.Empty;

            // 1枚目が取れた時点で、その商品は一覧のカードとして成立する。
            // JSONの時点で反映すると絵の無い白いカードが並ぶので、ここまで待つ。
            //
            // 件数とバッジは即座に更新するが、一覧そのものは動かさない。
            // 読んでいる最中に足元が動くと、どこを見ていたか分からなくなる
            if (report.Phase == ImportPhase.FetchingThumbnails)
            {
                _main.NotePendingItems(report.Current);
                _main.RefreshBadges();
            }

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
                new UiCommand.ScanFolders(_work),
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
                        SizeText = Core.Models.DisplayText.Size(folder.TotalBytes),
                    };
                    row.PropertyChanged += OnUnpackedRowChanged;
                    UnpackedFolders.Add(row);
                }

                OnPropertyChanged(nameof(HasUnpackedFolders));
                RaiseSelectionChanged();

                AvatarSummaryText = DescribeDetection(imported.Summary);

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
            _work = null;
            IsRunning = false;
        }
    }

    /// <summary>
    /// 検出（③）の結果を1行にする。検出そのものは取り込みの1段として
    /// パイプラインの中で走るので、ここでは受け取ったまとめを読むだけ。
    /// </summary>
    private static string DescribeDetection(ImportSummary summary)
    {
        if (summary.AvatarDetectError is { } error)
        {
            return $"対応アバターの検出は途中で止まりました：{error}";
        }

        return summary.AvatarItemsUpdated == 0
            ? "対応アバターは見つかりませんでした。"
            : $"対応アバターを {summary.AvatarItemsUpdated} 件の商品に書きました（アバター {summary.AvatarsFound} 体）。";
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

    /// <summary>
    /// BOOTHに無かったファイルの1行。
    ///
    /// タイルを増やさないのは、**普段0の数字が常設で並ぶ**のを避けるため
    /// （ナビのバッジで「0件のときは出さない」と決めたのと同じ理由）。
    /// 1行なら、次にやること（未確定を見る）も一緒に言える。
    /// </summary>
    public string NotFoundText => Summary is { NotFound: > 0 } summary
        ? $"BOOTHで見つからなかったものが {summary.NotFound} 件あります。未確定に置いてあるので、そちらで確かめてください。"
        : string.Empty;

    public bool HasNotFound => NotFoundText.Length > 0;

}
