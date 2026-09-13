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
        OpenResolveCommand = new RelayCommand(() => _main.ShowResolve());
        ShowAddedCommand = new RelayCommand(() => _ = ShowAddedAsync());
    }

    public ObservableCollection<string> Folders { get; } = [];

    /// <summary>通信と作業の様子。使っていない間の取得を、この画面にも出すため。</summary>
    public BoothActivityViewModel Activity => _main.BoothActivity;

    /// <summary>落としたらそのまま取り込みを始める設定か（#38）。</summary>
    public bool StartsOnDrop => _services.Settings.StartImportOnDrop;

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
                OnPropertyChanged(nameof(HasUnresolvedResult));
                OnPropertyChanged(nameof(HasAddedResult));

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

    private string _stepText = string.Empty;

    /// <summary>段の中の小さな段。件数の前に出して、何を数えているかを読めるようにする。</summary>
    public string StepText
    {
        get => _stepText;
        private set => SetField(ref _stepText, value);
    }

    /// <summary>
    /// 起動したときに監視フォルダの新着を自動で取り込むか（#38 の設定）。監視対象の説明を出し分ける（U7）。
    /// 以前は設定に関係なく「見つけても勝手には取り込みません」と出していて、入にした人には嘘になっていた。
    /// </summary>
    public bool ImportsOnLaunch => _services.Settings.StartImportOnLaunch;

    /// <summary>監視対象の説明の「設定」から、設定画面へ移る。</summary>
    public RelayCommand ShowSettingsCommand => _main.ShowSettingsCommand;

    /// <summary>
    /// 取り込みの結果から次の画面へ（動線の点検 D1）。以前は「未確定で確かめてください」と文で言うだけで、
    /// そこへ飛ぶ道が無く、ナビから探し直していた
    /// </summary>
    public RelayCommand OpenResolveCommand { get; }

    /// <summary>取り込んだ物を検索で見る。最近手元に入った順に並べて開く。</summary>
    public RelayCommand ShowAddedCommand { get; }

    /// <summary>商品が決まらなかったファイルが残ったか。取り込み中は出さない（まだ増える）。</summary>
    public bool HasUnresolvedResult => !IsRunning && Summary is { } summary
        && (summary.UnresolvedFiles > 0 || summary.NotFound > 0);

    /// <summary>新しく商品が増えたか。取り込み中は出さない。</summary>
    public bool HasAddedResult => !IsRunning && Summary is { ItemsAdded: > 0 };

    private async Task ShowAddedAsync()
    {
        // 取り込みで増えた分は、検索の一覧を読み直すまで入らない（「押すと反映」の1行）。読み直してから並べる
        await _main.ReloadLibraryAsync();
        _main.Search.ShowRecentlyAddedFirst();
        _main.ShowSearch();
    }

    /// <summary>画面を開いたときに呼ぶ。設定画面で変えた値を説明に映す。</summary>
    public void NoteShown() => OnPropertyChanged(nameof(ImportsOnLaunch));

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

    // ---- 残り時間の見込み（U1） ----
    //
    // 取り込みの時間のほとんどはBOOTHへの問い合わせの間隔（1.5秒以上）を待つ時間なので、
    // 「残りの問い合わせの数 × 1件あたりの実測」で出す。数は取り込み（ImportWorkSet）が持っている。
    // 少なくとも3つに分けて出す（ユーザ判断）：今の段の残り／編集できるようになるまで（③の終わり）／画像を取り終わるまで

    /// <summary>1件あたりの時間をならす件数。減速や一時の遅れ1回で見込みが跳ねないように。</summary>
    private const int MeasuredWindow = 20;

    /// <summary>③の中で問い合わせる小さな段。AvatarService が出す名前と揃える。</summary>
    private const string DetectionCheckingStep = "見つかった商品を確かめています";

    private readonly Queue<double> _requestSeconds = new();
    private DateTime _lastRequestReportAt;
    private ImportPhase? _lastRequestPhase;
    private string _etaPhaseText = string.Empty;
    private string _etaEditableText = string.Empty;
    private string _etaImagesText = string.Empty;

    public string EtaPhaseText
    {
        get => _etaPhaseText;
        private set => SetField(ref _etaPhaseText, value);
    }

    public string EtaEditableText
    {
        get => _etaEditableText;
        private set => SetField(ref _etaEditableText, value);
    }

    public string EtaImagesText
    {
        get => _etaImagesText;
        private set => SetField(ref _etaImagesText, value);
    }

    public bool HasEstimate => EtaPhaseText.Length > 0 || EtaEditableText.Length > 0;

    /// <summary>
    /// 1件あたりの時間。①②④⑥の実測（間隔＋応答）をならしたもの。
    /// 測れるまでは間隔だけで数えるので、最初の数件は少なめに出る
    /// </summary>
    private double SecondsPerRequest => _requestSeconds.Count > 0
        ? _requestSeconds.Average()
        : _services.Settings.FetchIntervalMs / 1000.0;

    private void UpdateEstimate(ImportProgress report)
    {
        // 1件に1回だけ問い合わせる段で測る。⑤は1件で何枚も取り、③の確認は手元で済む商品が混ざるので使わない
        var measurable = report.Phase is ImportPhase.FetchingJson or ImportPhase.FetchingHtml
            or ImportPhase.FetchingThumbnails or ImportPhase.FetchingShopIcons;
        var now = DateTime.UtcNow;

        if (measurable)
        {
            if (_lastRequestPhase == report.Phase)
            {
                // 1分を超える間はスリープや長い減速の後なので、1件あたりには混ぜない
                var seconds = (now - _lastRequestReportAt).TotalSeconds;
                if (seconds is > 0 and < 60)
                {
                    _requestSeconds.Enqueue(seconds);
                    while (_requestSeconds.Count > MeasuredWindow)
                    {
                        _requestSeconds.Dequeue();
                    }
                }
            }

            _lastRequestPhase = report.Phase;
            _lastRequestReportAt = now;
        }
        else
        {
            _lastRequestPhase = null;
        }

        // 走査と解決は手元の作業で、この後の問い合わせの数もまだ分からない
        if (_work is not { } work || report.Phase is ImportPhase.Scanning or ImportPhase.Resolving)
        {
            ClearEstimate();
            return;
        }

        var (json, pages, images) = work.RequestsLeft;

        // ③の確認は、手元に持っている商品なら問い合わせずに済む。全部を問い合わせとして数えるので多めに出る
        var checking = report.Phase == ImportPhase.Detecting && report.Step == DetectionCheckingStep
            ? Math.Max(0, report.Total - report.Current)
            : 0;
        var perRequest = SecondsPerRequest;

        var phaseLeft = report.Phase switch
        {
            ImportPhase.FetchingJson => json,
            ImportPhase.FetchingHtml => pages,
            ImportPhase.Detecting => checking,
            _ => images,
        };
        EtaPhaseText = $"この段の残り {Duration(phaseLeft * perRequest)}";

        // ③の確認で何件問い合わせるかは、③に入るまで分からない。分からないことは分からないと書く
        var beforeDetection = report.Phase is ImportPhase.FetchingJson or ImportPhase.FetchingHtml;
        var editableLeft = json + pages + checking;
        EtaEditableText = work.AwaitingDetectionCount > 0 || json > 0
            ? $"編集できるまで {Duration(editableLeft * perRequest)}" + (beforeDetection ? "＋対応アバターの確認" : string.Empty)
            : "新しい商品はもう編集できます";

        EtaImagesText = _services.Settings.SaveImages
            ? $"画像を取り終わるまで {Duration((editableLeft + images) * perRequest)}"
            : "画像は保存しない設定です";

        OnPropertyChanged(nameof(HasEstimate));
    }

    private void ClearEstimate()
    {
        EtaPhaseText = string.Empty;
        EtaEditableText = string.Empty;
        EtaImagesText = string.Empty;
        OnPropertyChanged(nameof(HasEstimate));
    }

    /// <summary>「約 12 分」「約 1 時間 5 分」。秒まで出すと毎回変わって読めない。</summary>
    private static string Duration(double seconds)
    {
        if (seconds < 60)
        {
            return "1分以内";
        }

        var minutes = (int)Math.Ceiling(seconds / 60);
        return minutes < 60 ? $"約 {minutes} 分" : $"約 {minutes / 60} 時間 {minutes % 60} 分";
    }

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
                OnPropertyChanged(nameof(HasUnresolvedResult));
                OnPropertyChanged(nameof(HasAddedResult));
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
    /// <param name="startImmediately">
    /// 足したらそのまま取り込みを始めるか（#38）。落としたとき・起動時の自動開始で true。
    /// 「フォルダを足す」で選んだときは false——続けて他も足してから始めたいことがある。
    /// </param>
    public void AddDroppedPaths(IEnumerable<string> paths, bool startImmediately = false)
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

        // 走っていれば今の取り込みに積む。2本目は起こさない（StartOrStackAsync の決まり）
        if (startImmediately && Folders.Count > 0)
        {
            _ = StartOrStackAsync();
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
        var watched = Watched.ToList();
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSettings(
            settings => settings with { WatchedFolders = watched }));
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
        // 前はディスクから読んで書き、メモリの設定を直していなかった。そのため別の画面の保存（メモリの古い写し）で、
        // ここで足した取り込み元が消えていた（技術的負債 1-1）
        var folders = Folders.ToList();
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSettings(
            settings => settings with { ImportFolders = folders }));
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
        _main.AttachImportWork(_work);
        _requestSeconds.Clear();
        _lastRequestPhase = null;
        ClearEstimate();

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
                // リンクだけでなくタグ・種類の名前も見ているので「タグ」を入れる（ユーザ判断）
                ImportPhase.Detecting => "5. 商品ページとタグから対応アバターを検出",
                ImportPhase.FetchingThumbnails => "6. サムネイルを取得（取得できたものから編集できます）",
                ImportPhase.FetchingGallery => "7. ギャラリーを取得",
                _ => "8. ショップのアイコンを取得",
            };
            Current = report.Current;
            Total = report.Total;
            StepText = report.Step ?? string.Empty;
            UpdateEstimate(report);

            // 常設の1行にも同じ段と件数を出す（ユーザ指示）。取り込み画面の段の名前は長いので短い呼び名で
            _main.BoothActivity.ReportWork(WorkSource.Import, LineLabelOf(report.Phase), report.Current, report.Total);

            // 検出で確かめている商品は、問い合わせるまで名前が分からない。空にすると止まって見える
            DetailText = report.Detail
                ?? (report.Phase == ImportPhase.Detecting && report.Step is not null ? "名前を確かめています" : string.Empty);

            // ①で商品ができた時点で一覧へ出す（U8）。以前は1枚目が取れるまで待っていたが、
            // 画像を自動で取るようになり、名前・ショップ・タグで探せる方が先に要る。絵の無いカードは
            // 「画像を取得中」と出す。読んでいる途中の一覧だけは動かさない（U10）。
            // ③待ちの商品を編集に出すかどうかも、ここで知らせ直す
            _main.NoteImportProgress();

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

                // 新しく見つかったアバターの1枚目を続けて取る（次の起動まで待たせない）
                _main.StartAvatarImageSync();
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
            ClearEstimate();
            IsRunning = false;
            _main.BoothActivity.EndWork(WorkSource.Import);
        }
    }

    /// <summary>常設の1行に出す段の呼び名。取り込み画面の段の名前を1行に収まるよう短くしたもの。</summary>
    private static string LineLabelOf(ImportPhase phase) => phase switch
    {
        ImportPhase.Scanning => "取り込み：ファイルを走査中",
        ImportPhase.Resolving => "取り込み：商品IDを解決中",
        ImportPhase.FetchingJson => "取り込み：商品の情報を取得中",
        ImportPhase.FetchingHtml => "取り込み：商品ページを取得中",
        ImportPhase.Detecting => "取り込み：対応アバターを検出中",
        ImportPhase.FetchingThumbnails or ImportPhase.FetchingGallery => "取り込み：画像を取得中",
        _ => "取り込み：ショップのアイコンを取得中",
    };

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

        // 走らなかったのに「見つかりませんでした」と言っていた（自動で走っていないと受け取られた）
        if (!summary.AvatarDetectRan)
        {
            return "今回は対応アバターの検出をしていません（BOOTHから新しく取った商品が無いため）。";
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
        ? $"BOOTHで見つからなかったものが {summary.NotFound} 件あります。未確定に置いてあるので、下の「未確定を開く」から確かめてください。"
        : string.Empty;

    public bool HasNotFound => NotFoundText.Length > 0;

}
