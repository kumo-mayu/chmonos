using Chmonos.Core.Storage;
using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>サムネイルに出す画像の役割の選択肢。</summary>
public sealed class ThumbnailRoleOption
{
    public required ThumbnailRole Value { get; init; }

    public string Label => ImageRoles.Label(Value);

    /// <summary>読み上げと自動操作から見える名前。既定だと型名になる</summary>
    public override string ToString() => Label;
}

/// <summary>改変の画面から開くアプリの選択肢。見つからない方は選べない。</summary>
public sealed class ProjectManagerOption : ViewModelBase
{
    private bool _isAvailable;

    public required ProjectManagerChoice Value { get; init; }

    public required string Label { get; init; }

    public bool IsAvailable
    {
        get => _isAvailable;
        set => SetField(ref _isAvailable, value);
    }

    /// <summary>読み上げと自動操作から見える名前。既定だと型名になる</summary>
    public override string ToString() => Label;
}

/// <summary>取り込み元フォルダの1行。</summary>
/// <summary>ショートカット1件の割り当て（#43）。</summary>
public sealed class ShortcutRow : ViewModelBase
{
    public required Chmonos.App.Services.ShortcutAction Action { get; init; }

    public required string Label { get; init; }

    /// <summary>設定に書く形（"Ctrl+Enter"）。空は割り当てなし。</summary>
    public string Gesture { get; private set; } = string.Empty;

    /// <summary>欄に見せる形（"Ctrl + →"）。</summary>
    public string DisplayText => Chmonos.App.Services.Shortcuts.Display(Gesture);

    public RelayCommand? ResetCommand { get; set; }

    public void SetGesture(string gesture)
    {
        Gesture = gesture;
        OnPropertyChanged(nameof(Gesture));
        OnPropertyChanged(nameof(DisplayText));
    }
}

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

/// <summary>非表示にした商品／管理対象から除外したファイルの1行。</summary>
public sealed class RestorableRow
{
    public required string Key { get; init; }

    public required string Label { get; init; }

    public string SubText { get; init; } = string.Empty;

    public RelayCommand? RestoreCommand { get; set; }

    /// <summary>その商品のページを開く（非表示にした商品だけ。動線の点検 D9：戻す前に中身を確かめられるように）。</summary>
    public RelayCommand? OpenCommand { get; set; }

    public bool CanOpen => OpenCommand is not null;
}

/// <summary>
/// 設定。
///
/// 変更は settings.json に即保存する。ただし通信間隔や画像の解像度のように
/// 起動時にサービスへ渡している値は、次の起動から効く。どれがそうなのかは
/// 画面に書いておく（黙って効かないのがいちばん困る）。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase, ILeavingScreen
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
        // ボタンの enabled は RelayCommand の一括通知で戻るが、理由の文は自分で書き換える。
        //
        // **離れるときに外す**（`OnLeaving`）。主画面はアプリと同じ寿命、この画面は開くたびに作り直しなので、
        // 外さないと捨てたはずの設定画面が生き残り、取り込みのたびに開いた回数ぶん同じ知らせが走る
        _main.PropertyChanged += OnMainChanged;

        // 書き出し・戻す・移動は主画面が持つ（画面を移って戻っても、進み具合と押せない状態を失わない。公開前の点検 2026-10-01）。
        // 走っていればその1行を、離れている間に終わっていればその知らせを、この画面の1行に出す
        _main.StoreJobEnded += OnStoreJobEnded;
        if (_main.StoreJob != StoreJobKind.None)
        {
            _status = _main.StoreJobText;
        }
        else
        {
            var notice = _main.TakeStoreJobNotice();
            _status = notice.Text;
            _exportedZip = notice.ExportedZip;
        }

        // 取り込み元の数は、読み込み・追加・外す・外したのを戻すのどれでも変わる。1か所で見出しの数を合わせる
        Folders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FoldersCountText));

        AddFolderCommand = new RelayCommand(AddFolder);
        OpenRootCommand = new RelayCommand(OpenRoot);
        ChangeRootCommand = new RelayCommand(() => ChangeRootAsync().Forget(), () => CanChangeRoot);
        RestartCommand = new RelayCommand(Restart);
        // 門は1つなので、移動とも重ねない（重ねた方は門の前で待つだけで、押した顔をして進まない）。
        // ほかの長い作業（対応アバターの検出・候補の検索）とも重ねない。帯は1本しか持てない（ユーザ判断 2026-10-01）
        ExportBackupCommand = new RelayCommand(
            () => ExportBackupAsync().Forget(),
            () => _main.StoreJob == StoreJobKind.None && !_main.IsLongJobRunning);
        RevealExportedZipCommand = new RelayCommand(() => _main.RevealExportedZip(_exportedZip), () => HasExportedZip);
        RestoreBackupCommand = new RelayCommand(() => RestoreBackupAsync().Forget(), () => _main.StoreJob == StoreJobKind.None && CanChangeRoot);
        ClearSearchHistoryCommand = new RelayCommand(ClearSearchHistory);

        var settings = services.Settings;
        _suppressSave = true;
        _showSubTagsInList = settings.ShowSubTagsInList;
        _showAdult = settings.ShowAdult;
        _showHiddenCountInSearch = settings.ShowHiddenCountInSearch;
        _placeNewConditionNearSameKind = settings.PlaceNewConditionNearSameKind;
        _showSortDividers = settings.ShowSortDividers;
        _showAcquiredSortDividers = settings.ShowAcquiredSortDividers;
        _thumbnailRole = settings.ThumbnailRole;
        _gallerySwitchOnHover = settings.GallerySwitchOnHover;
        _returnToSearchWhenEditDone = settings.ReturnToSearchWhenEditDone;
        _showEditQueueStrip = settings.ShowEditQueueStrip;
        _notifyOnUpdateByDefault = settings.NotifyOnUpdateByDefault;
        _resumeFetchInBackground = settings.ResumeFetchInBackground;
        _saveImages = settings.SaveImages;
        _refreshIntervalDays = settings.RefreshIntervalDays;
        _notificationRetentionCount = settings.NotificationRetentionCount;
        _searchHistoryCount = settings.SearchHistoryCount;
        _fetchIntervalMs = settings.FetchIntervalMs;
        _imageMaxEdgePixels = settings.ImageMaxEdgePixels;
        _imageQuality = settings.ImageQuality;
        _modificationImageMaxEdgePixels = settings.ModificationImageMaxEdgePixels;
        _saveModificationImagesAtOriginalSize = settings.SaveModificationImagesAtOriginalSize;
        _shopBannerRecheckDays = settings.ShopBannerRecheckDays;
        _avatarDetectRecheckDays = settings.AvatarDetectRecheckDays;
        _startImportOnDrop = settings.StartImportOnDrop;
        _startImportOnLaunch = settings.StartImportOnLaunch;
        _projectManager = settings.ProjectManager;

        foreach (var action in Enum.GetValues<Services.ShortcutAction>())
        {
            ShortcutRows.Add(CreateShortcutRow(
                action, Services.Shortcuts.GestureOf(settings.Shortcuts ?? new ShortcutSettings(), action)));
        }

        _suppressSave = false;

        LoadAsync().Forget();
        DetectProjectManagersAsync().Forget();
    }

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsImporting))
        {
            OnPropertyChanged(nameof(CanChangeRoot));
            OnPropertyChanged(nameof(RootLockedNote));
        }
        else if (args.PropertyName == nameof(MainViewModel.StoreJob))
        {
            OnPropertyChanged(nameof(IsBackingUp));
            OnPropertyChanged(nameof(IsMovingStore));
            OnPropertyChanged(nameof(CanChangeRoot));
            OnPropertyChanged(nameof(RootLockedNote));
        }
        else if (args.PropertyName == nameof(MainViewModel.StoreJobText) && _main.StoreJob != StoreJobKind.None)
        {
            Status = _main.StoreJobText;
        }
        else if (args.PropertyName == nameof(MainViewModel.LongJobBlockedNote))
        {
            OnPropertyChanged(nameof(CanChangeRoot));
            OnPropertyChanged(nameof(RootLockedNote));
        }
    }

    private void OnStoreJobEnded(StoreJobOutcome result)
    {
        Status = result.Text;
        SetExportedZip(result.ExportedZip);
    }

    /// <summary>離れたら主画面の知らせを外す（外さないと、開いた回数ぶん生き残って同じ知らせが走る）。</summary>
    public void OnLeaving()
    {
        _main.PropertyChanged -= OnMainChanged;
        _main.StoreJobEnded -= OnStoreJobEnded;
    }

    public RelayCommand AddFolderCommand { get; }

    public RelayCommand OpenRootCommand { get; }

    /// <summary>保存先を選び直す。Cドライブ以外に置きたいときの入口。</summary>
    public RelayCommand ChangeRootCommand { get; }

    public RelayCommand RestartCommand { get; }

    public ObservableCollection<ImportFolderRow> Folders { get; } = [];

    /// <summary>
    /// 取り込み元の履歴を開いているか（ユーザ判断 2026-09-28：取り込むたびに増えて膨大になるので畳めるようにする）。
    /// 設定画面は開くたびに作り直されるので、アプリを閉じるまでここに持つ（商品ページのバリエーションの欄と同じ）
    /// </summary>
    public bool IsFoldersExpanded
    {
        get => s_foldersExpanded;
        set
        {
            if (s_foldersExpanded != value)
            {
                s_foldersExpanded = value;
                OnPropertyChanged(nameof(IsFoldersExpanded));
            }
        }
    }

    private static bool s_foldersExpanded = true;

    /// <summary>見出しの右に出す数。畳んでいても何件あるかは分かるように。</summary>
    public string FoldersCountText => $"{Folders.Count} 件";

    private bool _startImportOnDrop;

    /// <summary>落としたらそのまま取り込みを始める（#38）。</summary>
    public bool StartImportOnDrop
    {
        get => _startImportOnDrop;
        set { if (SetField(ref _startImportOnDrop, value)) { Save(); } }
    }

    private bool _startImportOnLaunch;

    /// <summary>起動時に監視フォルダの新着と前回の取り込みの続きを取り込む（#38・続きはユーザ判断 2026-09-29）。次に起動したときから効く。</summary>
    public bool StartImportOnLaunch
    {
        get => _startImportOnLaunch;
        set { if (SetField(ref _startImportOnLaunch, value)) { Save(); } }
    }

    /// <summary>ショートカットの割り当て（#43）。1操作1行。</summary>
    public ObservableCollection<ShortcutRow> ShortcutRows { get; } = [];

    private ShortcutRow CreateShortcutRow(Services.ShortcutAction action, string gesture)
    {
        var row = new ShortcutRow { Action = action, Label = Services.Shortcuts.ActionLabel(action) };
        row.SetGesture(gesture);
        row.ResetCommand = new RelayCommand(() =>
            AssignShortcut(row, Services.Shortcuts.GestureOf(new ShortcutSettings(), action)));
        return row;
    }

    /// <summary>
    /// 割り当てる。**同じキーが別の操作に付いていたら、そちらを外して知らせる。**
    /// 黙って2つに同じキーを残すと、押したときにどちらが動くか分からない。
    /// </summary>
    public void AssignShortcut(ShortcutRow row, string gesture)
    {
        if (gesture.Length > 0)
        {
            foreach (var other in ShortcutRows.Where(entry => !ReferenceEquals(entry, row)
                         && string.Equals(entry.Gesture, gesture, StringComparison.OrdinalIgnoreCase)))
            {
                other.SetGesture(string.Empty);
                Status = $"同じキーだったので、「{other.Label}」の割り当てを外しました。";
            }
        }

        row.SetGesture(gesture);
        Save();
    }

    private ShortcutSettings BuildShortcuts()
        => ShortcutRows.Aggregate(new ShortcutSettings(), (settings, row) => Services.Shortcuts.With(settings, row.Action, row.Gesture));

    /// <summary>
    /// 監視対象フォルダ。取り込み元（履歴）とは意味が違うので別に並べる。
    /// あちらは「ここから取り込んだことがある」、こちらは「ここを見ておいて」。
    /// </summary>
    public ObservableCollection<ImportFolderRow> Watched { get; } = [];

    public bool HasWatched => Watched.Count > 0;

    public ObservableCollection<RestorableRow> Hidden { get; } = [];

    /// <summary>
    /// 除外したファイルの行。**欄を開いている間だけ作る**（閉じている間は空）。
    /// 除外は溜まる一方で上限が無く、1件ごとに行を作って仮想化しない一覧に並べていた頃は、
    /// 5,000 件で設定を開くたびに 7 秒余り止まり、メモリが 450MB を超えた（`docs/research/large-files-2026-09-30.md`）。
    /// 閉じている間は件数だけ出せば足りる。開いた一覧は見える分だけ行の部品を作る（View の側で仮想化）。
    /// </summary>
    public RangeObservableCollection<RestorableRow> Excluded { get; } = new();

    /// <summary>読んだ除外の記録（新しい順）。件数と、開いたときの行の元。</summary>
    private IReadOnlyList<Core.Services.ExcludedFile> _excludedFiles = [];

    /// <summary>
    /// 除外したファイルの欄を開いているか。既定は閉じる（開くまで行を作らない）。
    /// ほかの畳む欄と同じく、アプリを閉じるまで覚える（`docs/spec/ui-rules.md`。設定画面は開くたびに作り直される）。
    /// </summary>
    public bool IsExcludedExpanded
    {
        get => s_excludedExpanded;
        set
        {
            if (s_excludedExpanded == value)
            {
                return;
            }

            s_excludedExpanded = value;
            OnPropertyChanged(nameof(IsExcludedExpanded));
            SyncExcludedRows();
        }
    }

    private static bool s_excludedExpanded;

    /// <summary>
    /// 商品ページで「この商品から外す」を押したファイル。
    /// 外した記録が見えないと、なぜその商品へ紐付かないのかを探す場所が無くなる。
    /// </summary>
    public ObservableCollection<RestorableRow> Detached { get; } = [];

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

                // 1行が別の知らせに替わったら「エクスプローラで開く」も引っ込める（今の文と関係の無い口を残さない）
                SetExportedZip(null);
            }
        }
    }

    public bool HasStatus => Status.Length > 0;

    private string? _exportedZip;

    /// <summary>
    /// 1行の知らせの横に「エクスプローラで開く」を出すか（書き出しに成功した知らせを出している間だけ）。
    /// 設定の画面にいない間に終わったときの帯と同じ口を持たせる：どこで知らせを見ても、次にすることへ同じ1押しで行ける
    /// </summary>
    public bool HasExportedZip => _exportedZip is not null;

    /// <summary>書き出した zip を選んだ状態でエクスプローラを開く（帯の「エクスプローラで開く」と同じ）。</summary>
    public RelayCommand RevealExportedZipCommand { get; }

    private void SetExportedZip(string? zip)
    {
        if (_exportedZip == zip)
        {
            return;
        }

        _exportedZip = zip;
        OnPropertyChanged(nameof(HasExportedZip));
        RelayCommand.RaiseCanExecuteChanged();
    }

    // ---- 表示 ----

    private bool _showSubTagsInList;
    public bool ShowSubTagsInList
    {
        get => _showSubTagsInList;
        set { if (SetField(ref _showSubTagsInList, value)) { Save(_main.ReloadLibraryAsync); } }
    }

    // どちらも今出ている一覧と件数を変えるので、保存したら組み直す（小分類タグ・サムネイルの役割と同じ）。
    // 組み直していなかったので、次に別の条件で検索し直すまで変わらず、
    // 「効くときと効かないときがある」ように見えていた
    private bool _showAdult;
    public bool ShowAdult
    {
        get => _showAdult;
        set { if (SetField(ref _showAdult, value)) { Save(_main.ReloadLibraryAsync); } }
    }

    private bool _showHiddenCountInSearch;
    public bool ShowHiddenCountInSearch
    {
        get => _showHiddenCountInSearch;
        set { if (SetField(ref _showHiddenCountInSearch, value)) { Save(_main.ReloadLibraryAsync); } }
    }

    private bool _placeNewConditionNearSameKind;

    /// <summary>検索で追加する条件を、同じ種類の条件のすぐ下に置くか。検索は足すたびに今の値を読むので、知らせは要らない。</summary>
    public bool PlaceNewConditionNearSameKind
    {
        get => _placeNewConditionNearSameKind;
        set { if (SetField(ref _placeNewConditionNearSameKind, value)) { Save(); } }
    }

    private bool _showSortDividers;

    /// <summary>検索の並べ替えの区切りの札。今出ている一覧が変わるので、保存したら組み直す（R-18 の表示と同じ）。</summary>
    public bool ShowSortDividers
    {
        get => _showSortDividers;
        set { if (SetField(ref _showSortDividers, value)) { Save(_main.ReloadLibraryAsync); } }
    }

    private bool _showAcquiredSortDividers;

    /// <summary>
    /// 入手日で並べたときの札（ユーザ判断 2026-10-01）。親（<see cref="ShowSortDividers"/>）に従う子で、親が切れている間は押せないだけで値は残す
    /// （親を入れ直したとき、前に選んだ形へ戻るように）。
    /// </summary>
    public bool ShowAcquiredSortDividers
    {
        get => _showAcquiredSortDividers;
        set { if (SetField(ref _showAcquiredSortDividers, value)) { Save(_main.ReloadLibraryAsync); } }
    }

    private ThumbnailRole _thumbnailRole;

    /// <summary>
    /// サムネイルに出す画像の役割。
    ///
    /// 変えたら一覧を組み直す。カードは組むときに役割を受け取るので、
    /// 組み直さないと前の絵が残る。
    /// </summary>
    public ThumbnailRole ThumbnailRole
    {
        get => _thumbnailRole;
        set
        {
            if (SetField(ref _thumbnailRole, value))
            {
                OnPropertyChanged(nameof(SelectedThumbnailRole));
                Save(_main.ReloadLibraryAsync);
            }
        }
    }

    public static IReadOnlyList<ThumbnailRoleOption> ThumbnailRoles { get; } =
    [
        new ThumbnailRoleOption { Value = Core.Models.ThumbnailRole.Default },
        new ThumbnailRoleOption { Value = Core.Models.ThumbnailRole.Booth },
        new ThumbnailRoleOption { Value = Core.Models.ThumbnailRole.Modified },
        new ThumbnailRoleOption { Value = Core.Models.ThumbnailRole.Other },
    ];

    public ThumbnailRoleOption SelectedThumbnailRole
    {
        get => ThumbnailRoles.First(option => option.Value == ThumbnailRole);
        set { if (value is not null) { ThumbnailRole = value.Value; } }
    }

    private ProjectManagerChoice _projectManager;
    private Services.UnityTools? _projectManagerTools;

    /// <summary>
    /// VCC と ALCOM の両方があるとき、改変の画面から開く方（ユーザ指示 2026-09-29）。
    /// 選べるのは見つかった方だけ。見つからない方を選んでいても（後で消した）、改変の画面は見つかった方を開く
    /// </summary>
    public ProjectManagerChoice ProjectManager
    {
        get => _projectManager;
        set
        {
            if (SetField(ref _projectManager, value))
            {
                OnPropertyChanged(nameof(ProjectManagerNote));
                OnPropertyChanged(nameof(ProjectManagerShown));
                Save();
            }
        }
    }

    /// <summary>
    /// 欄に出す選択肢。**選んでいた方が見つからないときは「vcc:// に合わせる」を出す**（公開前の点検 2026-10-01・ユーザ判断）。
    /// </summary>
    /// <remarks>
    /// VCC を選んだまま VCC を消すと、欄は押せない「VCC」を選んだ形のまま、下の説明は「ALCOMを開きます」と食い違っていた。
    /// 改変の画面は見つかった方に倒す（<see cref="Chmonos.Core.Services.ProjectManagerApps.Buttons"/>）ので、欄もその形で見せる。
    /// **保存した設定は書き換えない**——入れ直して戻ってきたら、選んでいた方に戻る。
    /// 見せている値と同じ値が欄から返ってきたとき（見せ方を変えたことへの欄の応え）は、選び直しではないので保存しない
    /// </remarks>
    public ProjectManagerChoice ProjectManagerShown
    {
        get => ProjectManagers.FirstOrDefault(option => option.Value == _projectManager) is { IsAvailable: false }
            && _projectManagerTools is not null
                ? ProjectManagerChoice.VccLink
                : _projectManager;
        set
        {
            if (value != ProjectManagerShown)
            {
                ProjectManager = value;
            }
        }
    }

    /// <summary>選択肢。VCC・ALCOM は見つかるまで選べない（調べ終わるのは画面を開いた直後）。</summary>
    public IReadOnlyList<ProjectManagerOption> ProjectManagers { get; } =
    [
        new ProjectManagerOption { Value = ProjectManagerChoice.VccLink, Label = "vcc:// に合わせる", IsAvailable = true },
        new ProjectManagerOption { Value = ProjectManagerChoice.Vcc, Label = "VCC" },
        new ProjectManagerOption { Value = ProjectManagerChoice.Alcom, Label = "ALCOM" },
    ];

    /// <summary>今、改変の画面でどちらを開くか。調べ終わるまでは空。</summary>
    public string ProjectManagerNote => _projectManagerTools switch
    {
        null => string.Empty,
        { HasVcc: true, HasAlcom: true } tools => ProjectManager == ProjectManagerChoice.VccLink
            ? $"改変の画面から開くアプリです。vcc:// に合わせると、今は{(tools.LinkOpensAlcom ? "ALCOM" : "VCC")}を開きます。"
            : "改変の画面から開くアプリです。",
        { HasVcc: true } => "見つかったのはVCCだけなので、改変の画面からはVCCを開きます。",
        { HasAlcom: true } => "見つかったのはALCOMだけなので、改変の画面からはALCOMを開きます。",
        _ => "VCCかALCOMを入れると、改変の画面から開けます。",
    };

    /// <summary>レジストリとファイルを見るので裏で調べる（改変の画面と同じ <see cref="Services.UnityTools.Detect"/>）。</summary>
    private async Task DetectProjectManagersAsync()
    {
        var tools = await Task.Run(() => _services.DetectUnityTools());
        _projectManagerTools = tools;
        ProjectManagers[1].IsAvailable = tools.HasVcc;
        ProjectManagers[2].IsAvailable = tools.HasAlcom;
        OnPropertyChanged(nameof(ProjectManagerNote));
        OnPropertyChanged(nameof(ProjectManagerShown));
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

    private bool _showEditQueueStrip;

    /// <summary>編集画面の上に、続く商品を小さな絵で並べるか（ユーザ指示）。</summary>
    public bool ShowEditQueueStrip
    {
        get => _showEditQueueStrip;
        set { if (SetField(ref _showEditQueueStrip, value)) { Save(); } }
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
    /// **取るか取らないかは、保存した直後の取得から効く**（サービスは今の設定を毎回読む。SettingsSource）。
    /// ただし今ある商品の画像を裏で少しずつ取りに行くのは起動時に始まるので、
    /// 入れた場合のその分だけは次に開いたときからになる。黙っていると「入れたのに取りに行かない」に見える。
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

            Services.Notice.Show(
                value
                    ? "これから取る商品から、画像も取ります。\n\n"
                        + "今ある商品の画像は、次に開いたときから、使っていない間に少しずつ取得します。"
                    : "これから取る商品は、画像を取りません。\n\n"
                        + "取り込みは速くなり、一覧とギャラリーは文字だけになります。\n"
                        + "検索・絞り込み・統計はこれまで通り使えます。\n"
                        + "既にある画像は消しません。",
                value ? "画像を取るようにしました" : "画像を取らないようにしました",
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

    /// <summary>
    /// 範囲の外を打たれたら、**丸めたことを言う**（ユーザ判断 2026-09-20・I11）。
    /// 前は黙って丸めていて、500 と打っても 365 で保存され、出るのは「保存しました。」だけだった。
    /// </summary>
    private int Clamped(int value, int min, int max, string what, string unit)
    {
        var clamped = Math.Clamp(value, min, max);
        if (clamped != value)
        {
            // 保存が終わってから出す（先に入れると「保存しました。」で消える）
            _clampNote = $"{what}に入れられるのは {min}〜{max}{unit}です。{clamped}{unit}にしました。";
            Status = _clampNote;
        }

        return clamped;
    }

    private string _clampNote = string.Empty;

    private int _refreshIntervalDays;
    public int RefreshIntervalDays
    {
        get => _refreshIntervalDays;
        set
        {
            var clamped = Clamped(value, 1, 365, "商品情報を取り直す間隔", " 日");
            if (SetField(ref _refreshIntervalDays, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    private int _notificationRetentionCount;
    public int NotificationRetentionCount
    {
        get => _notificationRetentionCount;
        set
        {
            var clamped = Clamped(value, 20, 5000, "要確認に残す件数", " 件");
            if (SetField(ref _notificationRetentionCount, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    private int _searchHistoryCount;

    /// <summary>
    /// 検索の履歴を残す件数。
    ///
    /// 横に並ぶスロットなので、増やすほど端まで送る手間が増える。
    /// 上限は <see cref="Core.Services.SearchHistory.MaxLimit"/>——
    /// それ以上並べても選べない。
    /// </summary>
    public int SearchHistoryCount
    {
        get => _searchHistoryCount;
        set
        {
            var clamped = Clamped(value, 1, Core.Services.SearchHistory.MaxLimit, "検索の履歴を残す件数", " 件");
            if (SetField(ref _searchHistoryCount, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// 履歴を全部消す。
    ///
    /// **取り返しがつかないので聞く。**条件そのものは消えないと書いておく——
    /// 「検索がリセットされる」と読まれると押せない。
    /// </summary>
    public RelayCommand ClearSearchHistoryCommand { get; }

    private void ClearSearchHistory()
    {
        var answer = Services.Notice.Show(
            "検索の履歴をすべて消します。\n\n"
            + "名前を付けたものも一緒に消えます。元には戻せません。\n"
            + "いまの検索の条件は変わりません。",
            "検索の履歴を削除",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        ClearSearchHistoryAsync().Forget();
    }

    /// <summary>
    /// 検索の履歴を全部消す。**書き終えてから検索画面の履歴を読み直す。**
    /// 前は書くのを待たずに読み直していて、消したはずの履歴が検索画面に残って見えることがあった。
    /// </summary>
    private async Task ClearSearchHistoryAsync()
    {
        // 検索画面が履歴を書く列と同じ列を通す。直に書くと、商品を開いた直後の「足す」がまだ列にいるとき、
        // 消した後にそれが書かれて、消したはずの1件が戻る
        var commands = _services.Commands;
        await _services.BackgroundWrites.RunAsync(() => commands.ExecuteAsync(
            new Core.Commands.UiCommand.ChangeSearchHistory(_ => new Core.Services.SearchHistoryList())));
        _main.Search.RestoreHistory();
    }

    private int _shopBannerRecheckDays;
    public int ShopBannerRecheckDays
    {
        get => _shopBannerRecheckDays;
        set
        {
            var clamped = Clamped(value, 1, 365, "ショップのバナーを確かめ直す間隔", " 日");
            if (SetField(ref _shopBannerRecheckDays, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    private int _avatarDetectRecheckDays;
    public int AvatarDetectRecheckDays
    {
        get => _avatarDetectRecheckDays;
        set
        {
            var clamped = Clamped(value, 1, 365, "対応アバターを検出し直す間隔", " 日");
            if (SetField(ref _avatarDetectRecheckDays, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    // ---- 取得と画像（次の起動から効く） ----

    private int _fetchIntervalMs;
    public int FetchIntervalMs
    {
        get => _fetchIntervalMs;
        // 下限は約束の1.5秒（AppSettings.MinFetchIntervalMs）。短く打っても1500に戻す
        set
        {
            var clamped = Clamped(value, AppSettings.MinFetchIntervalMs, 10000, "BOOTHへ問い合わせる間隔", " ミリ秒");
            if (SetField(ref _fetchIntervalMs, clamped))
            {
                Save();
            }
            else if (clamped != value)
            {
                // 同じ値に戻っただけだと欄に打った数字が残るので、戻したことを欄に映す
                OnPropertyChanged();
            }
        }
    }

    private int _imageMaxEdgePixels;
    public int ImageMaxEdgePixels
    {
        get => _imageMaxEdgePixels;
        set
        {
            var clamped = Clamped(value, 128, 2048, "画像の長辺", " px");
            if (SetField(ref _imageMaxEdgePixels, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    private int _imageQuality;
    public int ImageQuality
    {
        get => _imageQuality;
        set
        {
            var clamped = Clamped(value, 40, 100, "画像の品質", string.Empty);
            if (SetField(ref _imageQuality, clamped)) { Save(); }
            else if (clamped != value) { OnPropertyChanged(); }
        }
    }

    private int _modificationImageMaxEdgePixels;

    /// <summary>
    /// 改変に貼る写真の長辺。**商品画像より大きめが既定**（768）。
    ///
    /// 商品画像は一覧に並ぶサムネイルだが、改変の写真は見て
    /// 「何を使ったか」を思い出すもの。実測で768pxは1枚14KB。
    /// </summary>
    public int ModificationImageMaxEdgePixels
    {
        get => _modificationImageMaxEdgePixels;
        set
        {
            var clamped = Clamped(value, 128, 4096, "改変の写真の長辺", " px");
            if (SetField(ref _modificationImageMaxEdgePixels, clamped))
            {
                Save();
            }
        }
    }

    private bool _saveModificationImagesAtOriginalSize;

    /// <summary>改変の写真を原寸で保存するか。既定は切（4Kのスクショで1枚200KB前後に伸びる）。</summary>
    public bool SaveModificationImagesAtOriginalSize
    {
        get => _saveModificationImagesAtOriginalSize;
        set
        {
            if (SetField(ref _saveModificationImagesAtOriginalSize, value))
            {
                OnPropertyChanged(nameof(ModificationSizeEnabled));
                Save();
            }
        }
    }

    /// <summary>原寸で保存するときは長辺の指定が効かない。触れないようにして黙って無視しない。</summary>
    public bool ModificationSizeEnabled => !SaveModificationImagesAtOriginalSize;

    // ---- 保存先 ----

    public string RootPath => _usage?.Root ?? _services.Paths.Root;

    public string ImageUsageText => _usage is null
        ? "…"
        : $"{Core.Models.DisplayText.Size(_usage.ImageBytes)} / {_usage.ImageCount:N0} ファイル";

    public string ItemUsageText => _usage is null
        ? "…"
        : $"{Core.Models.DisplayText.Size(_usage.ItemBytes)} / {_usage.ItemCount:N0} ファイル";

    public bool HasHidden => Hidden.Count > 0;

    public bool HasExcluded => _excludedFiles.Count > 0;

    public string HiddenText => $"{Hidden.Count} 件";

    // 行ではなく記録で数える（閉じている間は行が無い）
    public string ExcludedText => $"{_excludedFiles.Count} 件";

    /// <summary>
    /// 除外の行を、開閉と読んだ記録に合わせる。閉じたら捨て、開いていれば記録の並びに寄せる。
    /// 読み直し（除外を解除した後など）で残った行は使い回し、差分だけ抜き差しする——
    /// まとめて差し替えると、一覧の流した位置が先頭へ戻り、解除した近くを見失う。
    /// 初めて開くときは1回の知らせでまとめて入れる（1件ずつ足すと件数ぶん知らせが飛ぶ）。
    /// </summary>
    private void SyncExcludedRows()
    {
        if (!IsExcludedExpanded)
        {
            if (Excluded.Count > 0)
            {
                Excluded.ReplaceAll([]);
            }

            return;
        }

        if (Excluded.Count == 0)
        {
            Excluded.ReplaceAll(_excludedFiles.Select(MakeExcludedRow));
            return;
        }

        var existing = new Dictionary<string, RestorableRow>(StringComparer.Ordinal);
        foreach (var row in Excluded)
        {
            existing.TryAdd(row.Key, row);
        }

        var target = _excludedFiles
            .Select(file => existing.Remove(file.Hash, out var kept) ? kept : MakeExcludedRow(file))
            .ToList();
        CollectionSync.Apply(Excluded, target);
    }

    private RestorableRow MakeExcludedRow(Core.Services.ExcludedFile file)
    {
        var hash = file.Hash;
        return new RestorableRow
        {
            Key = file.Hash,
            Label = file.Path,
            SubText = file.Reason ?? string.Empty,
            RestoreCommand = new RelayCommand(() => RestoreAsync(hash).Forget()),
        };
    }

    public bool HasDetached => Detached.Count > 0;

    public string DetachedText => $"{Detached.Count} 件";

    private async Task OpenHiddenAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    private async Task LoadAsync()
    {
        // 読めなかったときも「読み込み中」を下ろす（成功した道でしか下ろしていなかった）
        try
        {
            await LoadCoreAsync();
        }
        finally
        {
            RunOnUiThread(() => IsLoading = false);
        }
    }

    private async Task LoadCoreAsync()
    {
        var usage = await _services.SettingsStore.LoadUsageAsync();
        var hidden = await _services.SettingsStore.LoadHiddenAsync();
        var excluded = await _services.SettingsStore.LoadExcludedAsync();
        var detached = await _services.SettingsStore.LoadDetachedAsync();

        // 在るかは画面のスレッドの外で見る（技術的負債 4-2）。取り込み元・監視は外付けやネットワークにもある
        var folderPaths = _services.Settings.ImportFolders.Concat(_services.Settings.WatchedFolders)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var exists = await Task.Run(() => folderPaths.ToDictionary(
            path => path, Core.Services.DiskCheck.FolderExists, StringComparer.OrdinalIgnoreCase));

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
                    Exists = exists.GetValueOrDefault(path),
                    RemoveCommand = new RelayCommand(() => RemoveFolder(captured)),
                });
            }

            Watched.Clear();
            foreach (var path in _services.Settings.WatchedFolders)
            {
                var captured = path;
                Watched.Add(new ImportFolderRow
                {
                    Path = path,
                    Exists = exists.GetValueOrDefault(path),
                    RemoveCommand = new RelayCommand(() => RemoveWatchedAsync(captured).Forget()),
                });
            }

            OnPropertyChanged(nameof(HasWatched));

            Hidden.Clear();
            foreach (var item in hidden)
            {
                var captured = item.ItemId;
                Hidden.Add(new RestorableRow
                {
                    Key = item.ItemId,
                    Label = item.Name,
                    RestoreCommand = new RelayCommand(() => UnhideAsync(captured).Forget()),
                    OpenCommand = new RelayCommand(() => OpenHiddenAsync(captured).Forget()),
                });
            }

            _excludedFiles = excluded;
            SyncExcludedRows();

            Detached.Clear();
            foreach (var record in detached)
            {
                var hash = record.Hash;
                var itemId = record.ItemId;
                Detached.Add(new RestorableRow
                {
                    Key = hash + ":" + itemId,
                    Label = record.Path,
                    SubText = $"「{record.ItemName}」から外しました",
                    RestoreCommand = new RelayCommand(() => ForgetDetachedAsync(hash, itemId).Forget()),
                });
            }

            IsLoading = false;

            foreach (var name in new[]
            {
                nameof(RootPath), nameof(ImageUsageText), nameof(ItemUsageText),
                nameof(HasHidden), nameof(HasExcluded), nameof(HiddenText), nameof(ExcludedText),
                nameof(HasDetached), nameof(DetachedText),
            })
            {
                OnPropertyChanged(name);
            }
        });
    }

    /// <summary>
    /// 監視をやめる。取り込んだ記録には触らない。
    /// **この画面の一覧の写しで書かない。**外すのはこの1件だけ（画面を開いた後に取り込み画面やフォルダビューが足した監視を消さない）。
    /// </summary>
    private async Task RemoveWatchedAsync(string path)
    {
        var row = Watched.FirstOrDefault(entry =>
            string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        Watched.Remove(row);
        OnPropertyChanged(nameof(HasWatched));
        _main.NoteFolderRemoved($"「{path}」の監視をやめました。", "監視を再開", async () =>
        {
            if (Watched.All(entry => !string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                Watched.Add(row);
                OnPropertyChanged(nameof(HasWatched));
            }

            await SaveAsync(settings => Core.Services.FolderListChange.SetWatched(settings, path, watch: true));
        });
        await SaveAsync(settings => Core.Services.FolderListChange.SetWatched(settings, path, watch: false));
    }

    /// <summary>
    /// 変更のたびに保存する。設定画面に「保存」ボタンを置かないのは、
    /// 押し忘れたまま閉じて設定が消える方が困るため。
    /// </summary>
    /// <param name="then">保存が済んでから行うこと。一覧の組み直しは新しい設定を読むので、保存より先に走ると前の値で組んでしまう。</param>
    /// <remarks>
    /// **取り込み元と監視はここで書かない**（足す・外すを押した所で1件ずつ書く）。
    /// 前は保存のたびに画面の取り込み元の一覧で丸ごと書いていたので、画面を開いた後に取り込みで足された取り込み元が、
    /// 別の項目（表示の切り替えなど）を変えただけで消えていた（技術的負債 1-1 の再発）。
    /// </remarks>
    private void Save(Func<Task>? then = null)
    {
        if (_suppressSave)
        {
            return;
        }

        // 画面が持つ項目だけを、ディスクの今の設定に当てる。画面を開いた後に別の所が書いた項目（画面の状態・監視など）を消さない。
        // 組み合わせは画面のスレッドでここで写しておく（当てるのは錠の中で、別のスレッドのことがある）
        var shortcuts = BuildShortcuts();
        ThenAsync(SaveAsync(current => current with
        {
            ShowSubTagsInList = ShowSubTagsInList,
            ShowAdult = ShowAdult,
            ShowHiddenCountInSearch = ShowHiddenCountInSearch,
            PlaceNewConditionNearSameKind = PlaceNewConditionNearSameKind,
            ShowSortDividers = ShowSortDividers,
            ShowAcquiredSortDividers = ShowAcquiredSortDividers,
            ThumbnailRole = ThumbnailRole,
            GallerySwitchOnHover = GallerySwitchOnHover,
            ReturnToSearchWhenEditDone = ReturnToSearchWhenEditDone,
            ShowEditQueueStrip = ShowEditQueueStrip,
            NotifyOnUpdateByDefault = NotifyOnUpdateByDefault,
            ResumeFetchInBackground = ResumeFetchInBackground,
            SaveImages = SaveImages,
            RefreshIntervalDays = RefreshIntervalDays,
            NotificationRetentionCount = NotificationRetentionCount,
            SearchHistoryCount = SearchHistoryCount,
            ShopBannerRecheckDays = ShopBannerRecheckDays,
            AvatarDetectRecheckDays = AvatarDetectRecheckDays,
            FetchIntervalMs = FetchIntervalMs,
            ImageMaxEdgePixels = ImageMaxEdgePixels,
            ImageQuality = ImageQuality,
            ModificationImageMaxEdgePixels = ModificationImageMaxEdgePixels,
            SaveModificationImagesAtOriginalSize = SaveModificationImagesAtOriginalSize,
            Shortcuts = shortcuts,
            StartImportOnDrop = StartImportOnDrop,
            StartImportOnLaunch = StartImportOnLaunch,
            ProjectManager = ProjectManager,
        }), then).Forget();
    }

    private static async Task ThenAsync(Task saving, Func<Task>? then)
    {
        await saving;
        if (then is not null)
        {
            await then();
        }
    }

    private async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        try
        {
            await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeSettings(change));

            // 範囲の外を打たれたときは、丸めたことを出す（I11）。「保存しました。」で上書きしない
            Status = _clampNote.Length > 0 ? _clampNote : "保存しました。";
            _clampNote = string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 原因はこちらでは分からないので、断定も指示もしない。
            // 見当だけ添えて、判断はユーザに残す
            Core.Diagnostics.AppLog.Error("設定画面：設定の保存", exception);
            Status = $"保存できませんでした。{Core.Services.FailureText.Cause(exception)}　もう一度変えると保存し直します。";
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

        // 畳んだまま足すと、足した行が見えず何も起きなかったように見える
        IsFoldersExpanded = true;
        Folders.Add(new ImportFolderRow
        {
            Path = path,
            Exists = true,
            RemoveCommand = new RelayCommand(() => RemoveFolder(path)),
        });

        // 足すのはこの1件だけ（Save の注を参照）
        SaveAsync(settings => Core.Services.FolderListChange.AddImportFolders(settings, [path])).Forget();
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
        SaveAsync(settings => Core.Services.FolderListChange.RemoveImportFolder(settings, path)).Forget();

        // 戻すと設定の並びでは末尾に足されるので、画面の行も末尾に戻す（次に開いたときと同じ並び）。
        // 外した後に同じフォルダを足し直していたら、二重にしない
        _main.NoteFolderRemoved($"「{path}」を取り込み元から外しました。", "取り込み元に戻す", async () =>
        {
            if (Folders.All(entry => !string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                Folders.Add(row);
            }

            await SaveAsync(settings => Core.Services.FolderListChange.AddImportFolders(settings, [path]));
        });
    }

    private RelayCommand? _resetPaneWidthsCommand;

    /// <summary>ドラッグで変えた画面の幅を全部戻す（ユーザ判断 2026-09-14）。1か所だけなら境目のダブルクリックで戻せる。</summary>
    public RelayCommand ResetPaneWidthsCommand => _resetPaneWidthsCommand ??= new RelayCommand(() =>
    {
        _services.PaneWidths.ResetAll();
        Status = "画面の幅をすべて元に戻しました。";
    });

    private async Task UnhideAsync(string itemId)
    {
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.UnhideItem(itemId));
        Status = "非表示を解除しました。検索に戻ります。";
        await LoadAsync();
        _main.Search.ReloadAsync().Forget();
    }

    /// <summary>
    /// 外した記録を捨てる。外したのが間違いだったときの戻し方。
    /// 次の取り込みで、手掛かりがその商品を指すならまた紐付く。
    /// </summary>
    private async Task ForgetDetachedAsync(string hash, string itemId)
    {
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ForgetDetached(hash, itemId));
        Status = "外した記録を消しました。次の取り込みで、読み取った情報が指すならまたその商品に紐付きます。";
        await LoadAsync();
    }

    private async Task RestoreAsync(string hash)
    {
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RestoreExcluded(hash));
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
            && !_main.IsImporting
            // 書き出し・戻しと重ねない（E8）。どちらも保存先の場所を書くので、後に押した方が勝って案内と食い違っていた
            && !IsBackingUp
            && !IsMovingStore
            // ほかの長い作業とも重ねない（帯は1本。移動は帯を使う。ユーザ判断 2026-10-01）。「戻す」もこれを見る
            && !_main.IsLongJobRunning;

    /// <summary>
    /// 押せない理由。**押せる顔をして効かないより、押せなくして理由を出す。**
    /// 取り込み中を塞ぐのは、運んでいる間の書き込みが元の場所へ行ってしまうため。
    /// </summary>
    public string RootLockedNote
    {
        get
        {
            // ほかの長い作業（検出・候補の検索）で、場所を変える・書き出し・戻すがまとめて押せない間。
            // 3つのボタンごとに吹き出しを付けるより、この1行で言う方が読まれる（ui-empty-and-errors「まとまりごと押せなくなるとき」）。
            // 走り終われば消える一時の理由なので、ずっと続く理由（環境変数）より先に出す（環境変数のときも書き出しは普段押せる）
            if (_main.IsLongJobRunning && _main.StoreJob == StoreJobKind.None)
            {
                return _main.LongJobBlockedNote;
            }

            if (Core.Storage.StoreLocation.Resolve().Source == Core.Storage.StoreRootSource.Environment)
            {
                return $"環境変数{Core.Storage.AppPaths.RootVariable}で保存先が指定されているため、ここからは変えられません。";
            }

            if (_main.IsImporting)
            {
                return "取り込みが走っている間は場所を変えられません。終わるか、中断してから変えてください。";
            }

            if (IsMovingStore)
            {
                return "保存先を移動しています。終わるまでお待ちください。";
            }

            return IsBackingUp
                ? "バックアップの書き出し・戻しが終わってから変えてください。"
                : string.Empty;
        }
    }

    /// <summary>
    /// 運ぶのは裏で（ユーザ判断 2026-09-20・E8）。**画面のスレッドで回していたので、数GBならその間ずっと無反応だった。**
    /// `UiCommand` を通すのは、運んでいる間の書き込みを止めるため（`StoreWriteGate`）。
    /// 進み具合は「n/N」で出す。止めている間も読む操作（画面を見る・検索する）はできる。
    /// </summary>
    private async Task<Core.Storage.StoreMoveResult> MoveStoreAsync(string source, string destination, bool replace)
    {
        // **どの画面からでも止められるようにする**（ユーザ判断 2026-09-21・C3）。
        // 運んでいる間は書き込みの門を持つので、止める手立てが無いと全部の保存が無期限に待たされる。
        // 途中で止めても元には手を付けていないので、保存先を古いままにすれば何も失われない
        using var stop = new CancellationTokenSource();
        var job = _main.BeginLongJob("保存先を移動しています", "移動が終わるまで、保存は待たされます。見ることはできます。", stop);
        if (job is null)
        {
            // 押せなくしてあるが、場所を選ぶ窓を出している間に別の作業が始まり得る。何も運んでいない
            return new Core.Storage.StoreMoveResult
            {
                Succeeded = false,
                Copied = 0,
                Bytes = 0,
                Error = _main.LongJobBlockedNote,
            };
        }

        _main.BeginStoreJob(StoreJobKind.Move, replace ? "置き換えています…" : "引っ越しています…");

        try
        {
            var progress = new Progress<Core.Storage.StoreMoveProgress>(report =>
            {
                var text = (replace ? "置き換えています… " : "引っ越しています… ")
                    + $"{report.Copied:N0}/{report.Total:N0}";
                _main.ReportStoreJob(text);
                _main.ReportLongJob(text);
            });

            var result = await _services.Commands.ExecuteAsync(
                new Core.Commands.UiCommand.MoveStore(source, destination, replace, progress),
                cancellationToken: stop.Token);

            return result is Core.Commands.CommandResult.StoreMoved moved
                ? moved.Result
                : new Core.Storage.StoreMoveResult
                {
                    Succeeded = false,
                    Copied = 0,
                    Bytes = 0,
                    Error = (result as Core.Commands.CommandResult.Failed)?.Message ?? "移動できませんでした。",
                };
        }
        finally
        {
            job.Dispose();

            // 結果は呼び手が窓で返す（失敗の窓・開き直しの窓）ので、1行の知らせは出さない
            _main.EndStoreJob(string.Empty);
        }
    }

    /// <summary>運んでいる最中か。二重に押させない・バックアップと重ねさせない。画面を開き直しても続く（主画面が持つ）。</summary>
    public bool IsMovingStore => _main.StoreJob == StoreJobKind.Move;

    private async Task ChangeRootAsync()
    {
        if (!CanChangeRoot)
        {
            return;
        }

        // 初回画面と同じ規則：選んだ場所の中に「Chmonos」を作って使う（ライブラリがある場所ならそのまま）
        var chosen = PickFolder();
        var picked = chosen is null ? null : StoreLocation.RootFor(chosen);
        if (picked is null || string.Equals(picked, _services.Paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var source = _services.Paths.Root;

        // 選んだ先に既にライブラリがあるなら、どちらを残すかを選んでもらう。
        // 向こうが古い作りかけということもあるので、勝手にどちらかへ寄せない
        if (StoreLocation.LooksLikeStore(picked))
        {
            // 数えるだけでも全ファイルを舐めるので、画面のスレッドで回さない（E8）
            var here = await Task.Run(() => Core.Storage.StoreMover.Summarize(source));
            var there = await Task.Run(() => Core.Storage.StoreMover.Summarize(picked));

            // 「はい／いいえ」は本文と対応を覚えないと押せない。ボタンに何が起きるかを名乗らせる（ユーザ判断）
            var answer = Views.ChoiceDialog.Ask(
                "どちらのライブラリを残しますか",
                "選んだ場所には既にライブラリがあります。どちらを残しますか？",
                $"【今の保存先】{source}\n{Describe(here)}\n\n"
                + $"【選んだ場所】{picked}\n{Describe(there)}\n\n"
                + "「今のデータで置き換える」\n選んだ場所にあるものは消さず、「_置き換え前-（日時）」へ移動してから入れ替えます。\n\n"
                + "「選んだ場所のデータを使う」\n今のデータは元の場所に残ります。混ぜることはしません。",
                "今のデータで置き換える",
                "選んだ場所のデータを使う");

            if (answer == Views.ChoiceDialogResult.Cancel)
            {
                return;
            }

            if (answer == Views.ChoiceDialogResult.Second)
            {
                StoreLocation.Save(picked);
                PendingRoot = picked;
                RootNotice = $"次の起動から「{picked}」を使います。今のデータは「{source}」に残っています。";
                RaiseRootChanged();
                return;
            }

            if (!await PrepareToRelocateAsync())
            {
                return;
            }

            _services.ReleaseInstanceLock();
            var replaced = await MoveStoreAsync(source, picked, replace: true);

            if (!replaced.Succeeded)
            {
                _services.ReacquireInstanceLock();
                Services.Notice.Show(
                    $"置き換えられませんでした。\n\n{replaced.Error}\n\n"
                    + "保存先は元のままです。データは失われていません。"
                    + (replaced.ParkedAt is null
                        ? string.Empty
                        : $"\n\n選んだ場所のデータは「{replaced.ParkedAt}」に移動したままです。"),
                    "置き換えに失敗しました",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            StoreLocation.Save(picked);
            RestartIntoNewRoot(
                $"{replaced.Copied:N0} ファイルを「{picked}」へ移して置き換えました。\n\n"
                + $"元々あったものは「{replaced.ParkedAt}」に移動しました。不要なら、中身を確かめてから削除してください。\n\n"
                + "新しい場所で開き直します。",
                "置き換えました");
            return;
        }

        if (!StoreLocation.IsEmpty(picked))
        {
            Services.Notice.Show(
                $"選んだ場所には別のファイルが入っています。\n\n{picked}\n\n"
                + "空のフォルダか、このアプリのデータが入っている場所を選んでください。",
                "この場所は使えません",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var (files, bytes) = await Task.Run(() => Core.Storage.StoreMover.Measure(source));

        var move = Views.ChoiceDialog.Ask(
            "データを引っ越しますか",
            "今のデータを新しい場所へ引っ越しますか？",
            $"変更前：{source}\n変更後：{picked}\n"
            + $"今のデータ：{files:N0} ファイル / {Core.Models.DisplayText.Size(bytes)}\n\n"
            + "「引っ越す」\nコピーしてから元を消します。途中で失敗した場合は元のままにします。\n\n"
            + "「場所だけ変える」\n次の起動は、新しい場所の空の状態から始まります。今のデータは元の場所に残ります。",
            "引っ越す",
            "場所だけ変える");

        if (move == Views.ChoiceDialogResult.Cancel)
        {
            return;
        }

        if (move == Views.ChoiceDialogResult.First)
        {
            if (!await PrepareToRelocateAsync())
            {
                return;
            }

            // 実行中のロックを持ったままだと、元のフォルダを畳みきれない
            _services.ReleaseInstanceLock();

            var result = await MoveStoreAsync(source, picked, replace: false);

            if (!result.Succeeded)
            {
                _services.ReacquireInstanceLock();
                Services.Notice.Show(
                    $"引越しできませんでした。\n\n{result.Error}\n\n"
                    + "保存先は元のままです。データは失われていません。",
                    "引越しに失敗しました",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            StoreLocation.Save(picked);
            RestartIntoNewRoot(
                $"{result.Copied:N0} ファイルを「{picked}」へ移しました。\n\n"
                + (result.SourceRemoved ? string.Empty : $"元の場所「{source}」に消せなかったファイルが残っています。\n\n")
                + "新しい場所で開き直します。",
                "引っ越しました");
            return;
        }
        else
        {
            RootNotice = $"次の起動から「{picked}」を使います。今のデータは「{source}」に残っています。";
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
            : $"　{summary.Files:N0} ファイル / {Core.Models.DisplayText.Size(summary.Bytes)}"
                + (summary.LastWrite is { } at ? $"　最終更新 {at:yyyy-MM-dd HH:mm}" : string.Empty);

    // ---- バックアップと復元（#61） ----

    public RelayCommand ExportBackupCommand { get; }

    public RelayCommand RestoreBackupCommand { get; }

    /// <summary>書き出し・戻しの最中か。二重に押させない。画面を開き直しても続く（主画面が持つ）。</summary>
    public bool IsBackingUp => _main.StoreJob is StoreJobKind.Export or StoreJobKind.Restore;

    /// <summary>
    /// 保存先を1つの zip に書き出す。画像を含めるかは押したときに聞く——
    /// 画像は保存先の大半を占めるが、BOOTHから取り直せるので、無くても困らないことが多い。
    /// </summary>
    private async Task ExportBackupAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "バックアップの書き出し先",
            FileName = $"Chmonos-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            Filter = "zipファイル|*.zip",
            DefaultExt = ".zip",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var withImages = Views.ChoiceDialog.Ask(
            "バックアップを書き出す",
            $"画像（{ImageUsageText}）も含めますか？",
            "「画像も入れる」\n戻したときに取り直さずに済みますが、zipが大きくなります。\n\n"
            + "「画像は入れない」\n戻した後、使っていない間にBOOTHから少しずつ取り直します。",
            "画像も入れる",
            "画像は入れない");

        if (withImages == Views.ChoiceDialogResult.Cancel)
        {
            return;
        }

        await ExportBackupToAsync(dialog.FileName, withImages == Views.ChoiceDialogResult.First);
    }

    /// <summary>
    /// 選んだ場所へ書き出す（窓で聞き終えた後の本体。試験はここから呼ぶ）。
    ///
    /// **書き出しの間は保存が止まる**（束として食い違わないように書き込みの門を持つ。E8）。
    /// 前は帯も中止の口も無く、書き出し中に星を押すと、見た目はすぐ付くのに保存は終わるまで約65秒待たされた（公開前の点検 2026-10-01）。
    /// 保存先の移動と同じ帯を出し、どの画面からでも止められるようにする。止めても保存先には手を付けておらず、書きかけの zip も残さない
    /// </summary>
    internal async Task ExportBackupToAsync(string zipPath, bool withImages)
    {
        if (_main.StoreJob != StoreJobKind.None)
        {
            return;
        }

        // 保存先の窓と画像を含めるかの窓を出している間に、別の長い作業が始まり得る（帯は1本。始めない）
        using var stop = new CancellationTokenSource();
        var job = _main.BeginLongJob("バックアップを書き出しています", "書き出しが終わるまで、保存は待たされます。見ることはできます。", stop);
        if (job is null)
        {
            Status = _main.LongJobBlockedNote;
            return;
        }

        _main.BeginStoreJob(StoreJobKind.Export, "バックアップを書き出しています…");

        var outcome = StoreJobOutcome.None;
        try
        {
            // 何件中何件目かを出す（E8）。前は「書き出しています…」だけで、進んでいるのか止まっているのか読めなかった
            var progress = new Progress<Core.Storage.BackupProgress>(report =>
            {
                var text = $"バックアップを書き出しています… {report.Done:N0}/{report.Total:N0}";
                _main.ReportStoreJob(text);
                _main.ReportLongJob(text);
            });

            var result = await _services.Commands.ExecuteAsync(
                new Core.Commands.UiCommand.ExportBackup(_services.Paths.Root, zipPath, withImages, progress),
                cancellationToken: stop.Token);

            // 成功したときだけ zip の場所を持たせる（知らせに「エクスプローラで開く」が出る）。失敗・中止では zip が無い
            outcome = result switch
            {
                Core.Commands.CommandResult.BackupExported { Result: var exported } => new StoreJobOutcome(
                    $"バックアップに {exported.Files:N0} ファイル（{Core.Models.DisplayText.Size(exported.Bytes)}）を書き出しました。"
                    + (exported.SkippedLocked > 0 ? $" 開けなかった {exported.SkippedLocked} ファイルは入れていません。" : string.Empty),
                    zipPath),
                Core.Commands.CommandResult.Failed failed => new StoreJobOutcome(failed.Message),
                _ => StoreJobOutcome.None,
            };
        }
        catch (OperationCanceledException)
        {
            outcome = new StoreJobOutcome("バックアップの書き出しを中止しました。zipは作っていません。");
        }
        finally
        {
            job.Dispose();
            _main.EndStoreJob(outcome);
        }
    }

    /// <summary>
    /// バックアップを**別の空の場所**へ展開し、次の起動からそこを使う（ユーザ判断）。
    /// 今の保存先に重ねない——混ざると、どちらが正しいか分からなくなる。
    /// 今のデータは元の場所に残るので、戻したのが間違いでも「場所を変える」で戻れる。
    /// </summary>
    private async Task RestoreBackupAsync()
    {
        // 戻し終えると開き直すので、選ばせる前に書きかけを片付けてもらう（ユーザ判断 2026-09-23）
        if (!await PrepareToRelocateAsync())
        {
            return;
        }

        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "戻すバックアップを選ぶ",
            Filter = "zipファイル|*.zip",
        };

        if (open.ShowDialog() != true)
        {
            return;
        }

        if (!Core.Storage.BackupArchive.LooksLikeBackup(open.FileName))
        {
            Services.Notice.Show(
                "選んだzipには、このアプリの商品も設定も入っていません。\n\n"
                + "設定画面の「バックアップを書き出す」で作ったzipを選んでください。",
                "バックアップから戻す",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var chosen = PickFolder();
        var destination = chosen is null ? null : StoreLocation.RootFor(chosen);
        if (destination is null)
        {
            return;
        }

        if (!StoreLocation.IsEmpty(destination))
        {
            Services.Notice.Show(
                $"戻す先には既にファイルがあります。\n\n{destination}\n\n"
                + "別の空のフォルダを選んでください。",
                "この場所には戻せません",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        await RestoreBackupToAsync(open.FileName, destination);
    }

    /// <summary>
    /// 選んだ zip を選んだ空の場所へ戻す（窓で聞き終えた後の本体）。試験は失敗する道だけをここから呼ぶ
    /// （戻せると保存先の場所を書き換えて開き直すので、試験からは成功させない）。
    ///
    /// **戻す間も保存は止まる**（書き込みの門を持ち、戻し終えたら開き直すまで放さない）。前は帯が無く、
    /// 設定の画面の1行にしか進み具合が出なかった（ユーザ判断 2026-10-01）。書き出しと同じ帯を出すが「中止」は付けない：
    /// 途中で止めると戻す先が半端に展開されたまま残り、それを片付ける決まりがまだ無い
    /// </summary>
    internal async Task RestoreBackupToAsync(string zipPath, string destination)
    {
        var job = _main.BeginLongJob("バックアップから戻しています", "戻し終えるまで、保存は待たされます。終わったら開き直します。", stop: null);
        if (job is null)
        {
            // 押せなくしてあるが、zip と場所を選ぶ窓を出している間に別の作業が始まり得る
            Status = _main.LongJobBlockedNote;
            return;
        }

        // 状態は主画面に持たせる（画面を移って戻っても、進み具合と押せない状態を失わない。公開前の点検 2026-10-01）
        _main.BeginStoreJob(StoreJobKind.Restore, "バックアップから戻しています…");
        var outcome = string.Empty;
        try
        {
            var progress = new Progress<Core.Storage.BackupProgress>(report =>
            {
                var text = $"バックアップから戻しています… {report.Done:N0}/{report.Total:N0}";
                _main.ReportStoreJob(text);
                _main.ReportLongJob(text);
            });

            var result = await _services.Commands.ExecuteAsync(
                new Core.Commands.UiCommand.RestoreBackup(zipPath, destination, progress));

            if (result is Core.Commands.CommandResult.Failed failed)
            {
                outcome = failed.Message;
                return;
            }

            var files = (result as Core.Commands.CommandResult.BackupRestored)?.Files ?? 0;
            StoreLocation.Save(destination);
            job.Dispose();
            _main.EndStoreJob(string.Empty);
            RestartIntoNewRoot(
                $"バックアップの {files:N0} ファイルを「{destination}」に戻しました。\n\n"
                + $"今までのデータは「{_services.Paths.Root}」に残っています。\n\n"
                + "戻した場所で開き直します。",
                "バックアップから戻しました");
        }
        finally
        {
            job.Dispose();

            // 戻せたときは上で閉じてある（開き直しの窓が返事）。失敗・例外のときだけここで閉じる
            if (_main.StoreJob == StoreJobKind.Restore)
            {
                _main.EndStoreJob(outcome);
            }
        }
    }

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

    /// <summary>
    /// 保存先を運ぶ前の片付け。運び終えると開き直すので（ユーザ判断 2026-09-23）、
    /// **保存していない編集の入力は開き直しで消える**——黙って捨てず、先に保存してもらう。
    /// 0.8秒待ちの自動保存（メモ）は、門が開いた後に古い保存先へ書かれないよう、運ぶ前に書き出す。
    /// </summary>
    private async Task<bool> PrepareToRelocateAsync()
    {
        if (_main.Drafts.HasAny)
        {
            Services.Notice.Show(
                $"編集途中の商品（{_main.Drafts.Count} 件）に、保存していない入力があります。\n\n"
                + "移動が終わるとアプリを開き直すので、その入力は消えてしまいます。"
                + "編集画面で保存してから、もう一度選んでください。",
                "先に編集を保存してください",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return false;
        }

        await _main.FlushPendingWritesAsync();
        return true;
    }

    /// <summary>
    /// 運び終えたら、その場で新しい保存先で開き直す（ユーザ判断 2026-09-23）。
    ///
    /// 前は開き直しを任せていたので、それまでの保存は**古い保存先**へ行き、次の起動で新しい場所を読むと消えていた
    /// （引越しなら、消したはずの元のフォルダを作り直してもいた）。
    /// 書き込みの門は、運ぶ命令が済んだときに**閉じたまま**返してくる（<c>StoreHold.KeepClosedUntilRestart</c>）。
    /// 知らせを読んでいる間に裏の作業が古い場所へ書かないようにするためで、待たされた書き込みはプロセスと一緒に終わる。
    /// 前はここで閉じ直していたが、命令が一度開けた隙間に待っていた書き込みが古い保存先へ流れていた。
    /// **ここで門を取り直さない**（閉じたままの門は開かないので、永久に待つ）。
    /// </summary>
    private void RestartIntoNewRoot(string message, string title)
    {
        _main.BeginRelocationRestart();

        Services.Notice.Show(
            message,
            title,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);

        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            // 開き直す手立てが無い。古い場所で続けさせると書いた物が消えるので、閉じて手で開いてもらう
            Services.Notice.Show(
                "自動で開き直せませんでした。アプリを閉じます。もう一度開いてください。",
                title,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        }

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

}
