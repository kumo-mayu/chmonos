using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面。BoothIDが決まらなかったファイルに、IDを与えるか管理対象から除外する。
///
/// ID確定だけをまとめて先に片付ける形にしている（設計メモの Resolve → Edit）。
/// 1件ごとにID確定とメタデータ入力を交互にやらないのは、
/// 調べる作業と主観で決める作業とで頭の使い方が違うため。
/// 確定したものはこの画面で溜めておき、最後にまとめて編集へ送る。
/// </summary>
public sealed partial class ResolveViewModel : ViewModelBase, ISelectionScreen, ILeavingScreen
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    /// <summary>
    /// 離れたら一覧の読み直しを取り消す（既知 P8）。この画面は開くたびに作り直すので、離れた後の一覧は誰も見ない。
    /// **候補の検索は止めない**——人が押した長い作業で、どの画面からでも止められるようにしてある（C2）。
    /// 未確定の突き合わせ（書き込み）も止めない
    /// </summary>
    private readonly CancellationTokenSource _leaving = new();

    public void OnLeaving() => _leaving.Cancel();

    /// <summary>「取り込み中に n 件増えました」の1行を出すために見る。</summary>
    public MainViewModel Main => _main;
    /// <summary>確定して、まだ編集へ送っていない商品のID。画面を離れても残るよう主画面が持つ。</summary>
    private List<string> _settledItemIds => _main.ResolveSettledItemIds;

    private UnresolvedRow? _selected;
    private string _itemIdInput = string.Empty;
    private string _localNameInput = string.Empty;
    private ItemPreview? _preview;
    private string _statusText = string.Empty;
    private bool _isBusy;

    /// <param name="scope">
    /// 扱う未確定を絞る。フォルダビューの右側に組み込むとき、選んだファイル（またはフォルダの下）だけにする（ユーザ判断 2026-09-13）。
    /// </param>
    public ResolveViewModel(AppServiceContainer services, MainViewModel main, Func<UnresolvedFile, bool>? scope = null)
    {
        _services = services;
        _main = main;
        _scope = scope;

        FilesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Files);
        FilesView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(
            nameof(UnresolvedRow.GroupKey), null, StringComparison.OrdinalIgnoreCase));
        FilesView.Filter = row => row is UnresolvedRow file && MatchesFilter(file);
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);

        // 走っている最中に止めるのは「書き込む操作」だけにする。
        // 候補を出す・候補を確認する・ブラウザで開くは読み取りだけなので、
        // 確定を待っている間も次のファイルを調べられる
        // （確定処理は開始時に対象を控えるので、途中でプレビューが変わっても安全）
        ProposeCommand = new RelayCommand(() => ProposeAsync().Forget(), () => HasSelection);
        // 取得中は押せないようにする。他のボタンには入っていて、ここだけ抜けていた
        PreviewCommand = new RelayCommand(() => PreviewAsync(ItemIdInput).Forget(), () => CanPreview && !IsBusy);
        UseCandidateCommand = new RelayCommand(parameter => UseCandidateAsync(parameter).Forget(), parameter => parameter is CandidateRow);
        AssignCommand = new RelayCommand(() => AssignAsync().Forget(), () => HasPreview && HasSelection && !IsBusy && !IsBlockedByListedZip);
        ExcludeCommand = new RelayCommand(() => ExcludeAsync().Forget(), () => HasSelection && !IsBusy);
        UseLocalNameCommand = new RelayCommand(
            parameter => { if (parameter is string name) { LocalNameInput = name; } },
            parameter => parameter is string);
        RegisterLocalCommand = new RelayCommand(
            () => RegisterLocalAsync().Forget(),
            () => HasSelection && !IsBusy && !IsBlockedByListedZip && !string.IsNullOrWhiteSpace(LocalNameInput));
        SendSettledToEditCommand = new RelayCommand(SendSettledToEdit, () => _settledItemIds.Count > 0);
        OpenLastSettledCommand = new RelayCommand(() => OpenLastSettledAsync().Forget(), () => _settledItemIds.Count > 0);
        OpenBoothCommand = new RelayCommand(OpenBoothSearch, () => HasSelection);

        SelectFolderCommand = new RelayCommand(SelectFolder, parameter => parameter is string);
        SelectGroupCommand = new RelayCommand(SelectGroup, parameter => parameter is string);
        // 取り込みで未確定が増えたときに読み直す。画面ごと作り直すのが一番確実
        ReloadCommand = new RelayCommand(_main.ShowResolve);
        SelectAllCommand = new RelayCommand(SelectAll);
        RegisterFolderOfCommand = new RelayCommand(parameter => RegisterFolderOfAsync(parameter).Forget(), parameter => parameter is string && !IsBusy);
        UseOriginZipCommand = new RelayCommand(UseOriginZip, () => CanUseOriginZip);
        TreatAsOriginZipCommand = new RelayCommand(TreatAsOriginZip, parameter => parameter is not null);
        UndoExcludeCommand = new RelayCommand(() => UndoExcludeAsync().Forget(), () => HasUndoExclude && !IsBusy);
        RevealCommand = new RelayCommand(RevealSelected, () => HasSelection);
        OpenImportCommand = new RelayCommand(_main.ShowImport);
        RegisterFolderCommand = new RelayCommand(() => RegisterFolderAsync().Forget(), () => CanRegisterFolder);
        ClearChecksCommand = new RelayCommand(ClearChecks);
        ExcludeCheckedCommand = new RelayCommand(() => ExcludeCheckedAsync().Forget(), () => HasChecked && !IsBusy);
        AssignCheckedCommand = new RelayCommand(() => AssignCheckedAsync().Forget(), () => HasChecked && HasPreview && !IsBusy);

        ReloadAsync().Forget();
    }

    public RelayCommand SelectFolderCommand { get; }

    /// <summary>元zipの束をまとめて1つの対象にする。</summary>
    public RelayCommand SelectGroupCommand { get; }

    /// <summary>「取り込み中に n 件増えました」を押したときの読み直し。</summary>
    public RelayCommand ReloadCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand RegisterFolderOfCommand { get; }

    public RelayCommand UseOriginZipCommand { get; }

    public RelayCommand TreatAsOriginZipCommand { get; }

    public RelayCommand UndoExcludeCommand { get; }

    public RelayCommand RevealCommand { get; }

    public RelayCommand OpenImportCommand { get; }

    // ---- 左の一覧を探す（ユーザ指示 2026-09-17：件数が多く、どこを見ればよいか分からなかった） ----

    private static readonly System.Globalization.CompareInfo Compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;

    /// <summary>探すときは大文字小文字・かなの種類・全角半角を区別しない（ほかの画面と同じ）。</summary>
    private const System.Globalization.CompareOptions Loose = System.Globalization.CompareOptions.IgnoreCase
        | System.Globalization.CompareOptions.IgnoreKanaType | System.Globalization.CompareOptions.IgnoreWidth;

    private string _filterText = string.Empty;

    /// <summary>一覧を探す語。空白で区切った語がすべて、ファイル名・フォルダ・展開元のzipの名前のどれかに入っている行だけを出す。</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetField(ref _filterText, value))
            {
                FilesView.Refresh();
                OnPropertyChanged(nameof(HasFilterText));
                OnPropertyChanged(nameof(ActiveGroupText));

                // 選んでいた行が隠れると一覧の選択が外れ、右側（まとめて操作する枠を含む）が消えて何もできなくなった。
                // 見えている先頭の行を選ぶ（画面で確かめて見つけた 2026-09-17）
                if (Selected is null || !MatchesFilter(Selected))
                {
                    Selected = FilesView.Cast<UnresolvedRow>().FirstOrDefault();
                }
            }
        }
    }

    public bool HasFilterText => FilterText.Length > 0;

    public RelayCommand ClearFilterCommand { get; }

    private bool MatchesFilter(UnresolvedRow row)
    {
        var words = FilterText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            Compare.IndexOf(row.FileName, word, Loose) >= 0
            || Compare.IndexOf(row.DirectoryText, word, Loose) >= 0
            || (row.Origin is { } origin && Compare.IndexOf(origin.ArchiveName, word, Loose) >= 0));
    }

    public RelayCommand RegisterFolderCommand { get; }

    public RelayCommand ClearChecksCommand { get; }

    public RelayCommand ExcludeCheckedCommand { get; }

    public RelayCommand AssignCheckedCommand { get; }

    private void RemoveRows(IReadOnlyList<UnresolvedRow> rows)
    {
        foreach (var row in rows)
        {
            row.SelectionChanged -= OnCheckedChanged;
            Files.Remove(row);
        }

        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(SettledCount));
        OnPropertyChanged(nameof(HasSettled));
        OnPropertyChanged(nameof(SettledText));
        OnCheckedChanged();

        // 片付けた直後にナビの件数も減らす。次に画面を開き直すまで古い数字が残ると、
        // 作業が進んでいないように見える
        _main.RefreshBadges();

        Selected = Files.FirstOrDefault();
    }

    public RangeObservableCollection<UnresolvedRow> Files { get; } = [];

    /// <summary>フォルダごとに束ねた表示用のビュー。一覧の見出しがそのまま操作の単位になる。</summary>
    public System.ComponentModel.ICollectionView FilesView { get; }

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public RelayCommand ProposeCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand UseCandidateCommand { get; }

    public RelayCommand AssignCommand { get; }

    public RelayCommand ExcludeCommand { get; }

    public RelayCommand RegisterLocalCommand { get; }

    /// <summary>
    /// 名前の候補。**自動では入れず、押したら入る。**（Q6）
    ///
    /// 登録簿の名前は他商品の記述から拾った推定を含むので、自動で入れると
    /// それが観測なのか推定なのかが後から分からなくなる。
    /// 押して入れば「自分が決めた」記録になり、
    /// 「推定には出典と要確認を添える」という方針とも揃う。
    /// </summary>
    public ObservableCollection<string> LocalNameSuggestions { get; } = [];

    public bool HasLocalNameSuggestions => LocalNameSuggestions.Count > 0;

    public RelayCommand UseLocalNameCommand { get; }

    public RelayCommand SendSettledToEditCommand { get; }

    /// <summary>
    /// 最後に確定した商品を開く（動線の点検 D3）。以前は編集へ送るしかなく、正しく結び付いたかを
    /// 商品ページでその場で確かめられなかった
    /// </summary>
    public RelayCommand OpenLastSettledCommand { get; }

    public RelayCommand OpenBoothCommand { get; }

    public UnresolvedRow? Selected
    {
        get => _selected;
        set
        {
            if (SetField(ref _selected, value))
            {
                OnSelectionChanged();
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool HasStatus => StatusText.Length > 0;

    public string ItemIdInput
    {
        get => _itemIdInput;
        set
        {
            if (SetField(ref _itemIdInput, value))
            {
                OnPropertyChanged(nameof(CanPreview));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// BOOTHに無い商品として登録するときの名前。
    /// 選び直すたびにファイル名から下書きを入れる（そのままでも通るように）。
    /// </summary>
    public string LocalNameInput
    {
        get => _localNameInput;
        set
        {
            if (SetField(ref _localNameInput, value))
            {
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>登録したときに付く仮ID。押す前に見せる（何が起きるかを隠さない）。</summary>
    public string LocalIdPreview
        => Selected is null ? string.Empty : LocalItemId.For(Selected.File.Hash);

    public bool CanPreview => ItemIdInput.Trim().Length > 0;

    public ItemPreview? Preview
    {
        get => _preview;
        private set
        {
            if (SetField(ref _preview, value))
            {
                OnPropertyChanged(nameof(HasPreview));
                OnPropertyChanged(nameof(PreviewTitle));
                OnPropertyChanged(nameof(PreviewDetail));
                OnPropertyChanged(nameof(PreviewOwnedNote));
                OnPropertyChanged(nameof(IsPreviewOwned));
                OnPropertyChanged(nameof(AssignOutcomeText));
                OnPropertyChanged(nameof(HasAssignOutcome));
                OnPropertyChanged(nameof(CanRegisterFolder));
                OnPropertyChanged(nameof(RegisterFolderText));
                OnPropertyChanged(nameof(RegisterTargetSummary));
                OnPropertyChanged(nameof(RegisterTargetFolder));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasPreview => Preview is not null;

    public string PreviewTitle => Preview?.Name ?? string.Empty;

    public string PreviewDetail => Preview is null
        ? string.Empty
        : string.Join("　", new[]
        {
            Preview.ShopName,
            Preview.CategoryText,
            Preview.Price is { } price ? $"¥{price:N0}" : null,
            Preview.PublishedAt is { } published ? $"公開 {published:yyyy-MM-dd}" : null,
        }.Where(part => !string.IsNullOrEmpty(part)));

    public bool IsPreviewOwned => Preview?.IsAlreadyOwned == true;

    /// <summary>
    /// 商品の状態。文ではなくチップにして、商品の情報の一部として見せる。
    /// 説明を読んでいる途中に文で出すと、状態の断り書き（＝だから何かができない）に読める。
    /// </summary>
    public string PreviewOwnedNote => "ライブラリにあり";

    /// <summary>
    /// 押したら何が起きるか。ボタンのすぐ上に、既にあるかどうかに関わらず必ず出す。
    /// 「追加されるのかされないのか」を文から読み取らせないための行。
    /// </summary>
    public string AssignOutcomeText => Preview is null
        ? string.Empty
        : IsPreviewOwned
            ? $"確定すると：{OutcomeSubject}が、既にある商品に加わります"
            : $"確定すると：この商品を新しく登録して、{OutcomeSubject}を紐付けます";

    /// <summary>束を選んでいるときは件数まで言う。1件のつもりで押して全件が動くことが無いように。</summary>
    private string OutcomeSubject => ActiveGroup is null
        ? "このファイル"
        : GroupSubject;

    /// <summary>束を言う言い方。zipの中身かフォルダのファイルかで分ける。</summary>
    private string GroupSubject => ActiveRows.FirstOrDefault()?.HasOrigin == true
        ? $"元zip「{ActiveGroup}」の中身 {ActiveRows.Count} 件"
        : $"フォルダ「{Path.GetFileName(ActiveGroup)}」のファイル {ActiveRows.Count} 件";

    public bool HasAssignOutcome => Preview is not null;

    public int RemainingCount => Files.Count;

    private readonly Func<UnresolvedFile, bool>? _scope;

    /// <summary>
    /// フォルダビューの右側に組み込んだとき（ユーザ判断 2026-09-13：未確定の画面の右側をそのまま組み込む）。
    /// 上の帯と左の一覧を隠し、絞った分（<c>scope</c>）だけを扱う。
    /// </summary>
    public bool IsEmbedded { get; init; }

    public bool ShowsChrome => !IsEmbedded;

    private PaneColumn? _listPane;

    /// <summary>左の一覧の列。単独の画面ではドラッグで幅を変えられる（ユーザ判断 2026-09-14）。組み込んだときは一覧を出さない（幅0）。</summary>
    public PaneColumn ListPane => _listPane ??= new PaneColumn(_services.PaneWidths, "resolve.list")
    {
        Fixed = IsEmbedded ? new System.Windows.GridLength(0) : null,
    };

    public string RemainingText => $"未確定 {Files.Count} 件";

    public int SettledCount => _settledItemIds.Count;

    public bool HasSettled => _settledItemIds.Count > 0;

    public string SettledText => $"確定して、まだ編集へ送っていない商品 {_settledItemIds.Count} 件";

    // --- 選択中ファイルの手掛かり ---

    public IReadOnlyList<string> SelectedPaths => Selected?.File.Paths ?? [];

    public IReadOnlyList<string> SelectedContents => Selected?.File.Contents ?? [];

    public bool HasContents => SelectedContents.Count > 0;

    public string ContentsSummary => $"アーカイブの中身 {SelectedContents.Count} 件";

    public string? ZoneText => Selected?.File.ZoneHostUrl;

    public bool HasZone => !string.IsNullOrEmpty(ZoneText);

    /// <summary>記録にあった元zipのパス。今もそこにあるとは限らない（消した・別のPCで展開した）。</summary>
    public string? SelectedOriginText => Selected?.Origin is { } origin
        && !string.Equals(origin.ArchivePath, Selected.File.Paths.FirstOrDefault(), StringComparison.OrdinalIgnoreCase)
            ? origin.ArchivePath
            : null;

    public bool HasSelectedOrigin => SelectedOriginText is not null;

    /// <summary>
    /// 選んだファイルが展開した中身で、元のzipが今もディスクにあるか。あれば「元zipで登録」を出す（ユーザ指示 2026-09-17）。
    /// zipで登録するのが一番きれい——配布された単位と一致し、展開したフォルダは自動で対象から外れる。
    /// zipがあるかは一覧を読むときにzipごとに1回だけ見てある（<see cref="UnresolvedRow.HasOriginZip"/>）。
    /// </summary>
    public bool CanUseOriginZip => Selected?.HasOriginZip == true;

    /// <summary>
    /// 元のzipの行を選ぶ。そこから普通に商品IDを決めて登録できる。
    /// zipが未確定の一覧に無いとき（既に商品に結び付いている・取り込んでいない）は、選ぶ先が無いので理由と次の一手を言う。
    /// </summary>
    private void UseOriginZip()
    {
        if (Selected?.Origin is not { } origin)
        {
            return;
        }

        var zipRow = Files.FirstOrDefault(row =>
            string.Equals(row.File.Paths.FirstOrDefault(), origin.ArchivePath, StringComparison.OrdinalIgnoreCase));
        if (zipRow is not null)
        {
            Selected = zipRow;
            return;
        }

        StatusText = $"元のzip「{origin.ArchiveName}」は未確定にありません。取り込み画面にzipをドロップしてください。";
        OnPropertyChanged(nameof(HasStatus));
        DecisionFocusRequested?.Invoke();
    }

    private bool _isLoaded;
    private bool _loadFailed;

    /// <summary>
    /// 一覧を一度でも読み終えたか。読み終えるまでは一覧が0件なので、「未確定のファイルはありません」と
    /// 区別するために要る（E1：読み込み中・探して0件・1つも無いを言い分ける）。
    /// </summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetField(ref _isLoaded, value);
    }

    /// <summary>一覧を読めなかった。空の表示は出さず、上の1行で理由を言う。</summary>
    public bool LoadFailed
    {
        get => _loadFailed;
        private set => SetField(ref _loadFailed, value);
    }

    /// <summary>
    /// 開くたびに、既にitem側が持っているファイルを未確定から均してから読み直す。
    /// 確定の途中で落ちると両方に残るため。
    /// </summary>
    public async Task ReloadAsync()
    {
        var healed = 0;
        string? failure = null;
        var token = _leaving.Token;

        try
        {
            // 均すのは書き込みなので、離れても取り消さない（途中で止めると両方に残ったままになる）
            healed = await _services.Commands.ExecuteAsync(new UiCommand.ReconcileUnresolved())
                is CommandResult.Counted counted ? counted.Count : 0;

            // 元のzipが登録済みの中身を出さないために、商品が持っているファイルの場所を読む
            await LoadOwnedPathsAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            // 均せなくても一覧は出す。黙って空にする方がずっと悪い
            Core.Diagnostics.AppLog.Error("未確定の画面：突き合わせ", exception);
            failure = "未確定の一覧の照合に失敗しました。確定済みのものが一覧に残っていることがあります。"
                + Core.Services.FailureText.Cause(exception) + "　画面を開き直すともう一度試します。";
        }

        // 行は裏で組む（ディスクを見る所を画面のスレッドに乗せない）。差し替えは下で画面のスレッドに戻してから
        ReloadedRows? reloaded = null;
        Exception? loadError = null;
        try
        {
            reloaded = await BuildReloadAsync();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            loadError = exception;
        }

        // 読み込みはUIスレッド以外で終わることがあるので、必ず戻してから触る
        RunOnUiThread(() =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (reloaded is not null)
            {
                ApplyReload(reloaded);
            }
            else if (loadError is not null)
            {
                // 読めなかったのを「未確定のファイルはありません」と見せない（E4：失敗を黙って捨てない）。
                // 前はここで落ちると読み込み中の0件のまま残り、無いように見えた
                Core.Diagnostics.AppLog.Error("未確定の画面：一覧の読み込み", loadError);
                failure = "未確定の一覧を読めませんでした。" + Core.Services.FailureText.Cause(loadError)
                    + "　少し待ってから画面を開き直してください。";
                LoadFailed = true;
            }

            IsLoaded = true;

            if (failure is not null)
            {
                StatusText = failure;
            }
            else if (healed > 0)
            {
                StatusText = $"既に確定済みだった {healed} 件を一覧から外しました。";
            }

            OnPropertyChanged(nameof(HasStatus));
        });
    }

    private readonly Dictionary<string, ArchiveContentJudgement> _judgements =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, bool> _originExists = new(StringComparer.OrdinalIgnoreCase);

    private static bool OriginExists(string archivePath, Dictionary<string, bool> cache)
    {
        if (!cache.TryGetValue(archivePath, out var exists))
        {
            exists = File.Exists(archivePath);
            cache[archivePath] = exists;
        }

        return exists;
    }

    /// <summary>裏で組んだ一覧。画面のスレッドでは <see cref="ApplyReload"/> で1回で差し替えるだけにする。</summary>
    private sealed record ReloadedRows(
        List<UnresolvedRow> Rows,
        int HiddenByRegisteredZip,
        Dictionary<string, ArchiveContentJudgement> Judgements,
        Dictionary<string, bool> OriginExists);

    /// <summary>
    /// 一覧を読み直す。**ディスクを見る所は全部裏で行い、一覧は1回で差し替える**（2026-09-24）。
    /// 前は画面のスレッドで、未確定の記録を同期で読み、フォルダごとに中を列挙し（展開物の見分け）、zip ごとに在るかを見て、
    /// 一覧を空にしてから1件ずつ足していた（束でまとめた一覧は1件ごとに振り分け直す）。取り込み元が外付けだと1回に数秒止まり得た
    /// </summary>
    private async Task ReloadRowsAsync() => ApplyReload(await BuildReloadAsync());

    /// <summary>行を裏で組む。画面の状態（商品が持っているファイル・取り込み元・範囲）は、ここで写してから渡す。</summary>
    private Task<ReloadedRows> BuildReloadAsync()
    {
        var scope = _scope;
        var owned = new HashSet<string>(_ownedPaths, StringComparer.OrdinalIgnoreCase);
        var importFolders = _services.Settings.ImportFolders.ToList();
        return Task.Run(() => BuildReload(scope, owned, importFolders));
    }

    private void ApplyReload(ReloadedRows reloaded)
    {
        // 見分けと zip の有無の控えは、この後の操作（元zipで登録した後に中身を外すなど）でも使う
        _judgements.Clear();
        foreach (var (key, value) in reloaded.Judgements)
        {
            _judgements[key] = value;
        }

        _originExists.Clear();
        foreach (var (key, value) in reloaded.OriginExists)
        {
            _originExists[key] = value;
        }

        foreach (var row in reloaded.Rows)
        {
            row.SelectionChanged += OnCheckedChanged;
        }

        Files.ReplaceAll(reloaded.Rows);

        HiddenByRegisteredZipCount = reloaded.HiddenByRegisteredZip;
        OnPropertyChanged(nameof(HasHiddenByRegisteredZip));
        OnPropertyChanged(nameof(HiddenByRegisteredZipText));
        OnPropertyChanged(nameof(ShowsInlineHidden));

        Selected = Files.FirstOrDefault();
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
    }

    private ReloadedRows BuildReload(Func<UnresolvedFile, bool>? scope, IReadOnlySet<string> owned, IReadOnlyList<string> importFolders)
    {
        var unresolved = _services.Store.Unresolved.Load().Where(file => scope?.Invoke(file) ?? true).ToList();
        var judgements = new Dictionary<string, ArchiveContentJudgement>(StringComparer.OrdinalIgnoreCase);
        var originExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<UnresolvedRow>();

        // 元zipの分かるものを先に、zip名の順で並べる（一覧の束はこの並びで出る）。
        // 分からないものは従来どおりフォルダごとにまとめる。1つのアーカイブを展開した中身が
        // 固まって見えるので、まとめて外す判断がしやすい
        var withOrigin = unresolved
            .Select(file => (File: file, Origin: UnresolvedOrigin.For(file)))
            .OrderBy(entry => entry.Origin is null)
            .ThenBy(entry => entry.Origin?.ArchiveName
                ?? (entry.File.Paths.Count > 0 ? Path.GetDirectoryName(entry.File.Paths[0]) : string.Empty),
                StringComparer.OrdinalIgnoreCase)
            // zip自身を、そのzipを展開した中身の束より先に置く（束はこの並びの順に出る）
            .ThenBy(entry => entry.Origin is { } origin && entry.File.Paths.Count > 0
                && string.Equals(origin.ArchivePath, entry.File.Paths[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(entry => entry.File.SizeBytes)
            .ToList();

        // 展開物の根が別の物まで巻き込んでいないかを見るための場所（zipの中身・zip自身・商品が持っているファイル）
        var rootContext = new UnpackRootContext(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            withOrigin
                .Where(entry => entry.Origin is not null && entry.File.Paths.Count > 0)
                .Select(entry => entry.File.Paths[0])
                .Concat(owned)
                .ToList(),
            importFolders);

        var hiddenByRegisteredZip = 0;
        foreach (var (file, origin) in withOrigin)
        {
            var path = file.Paths.Count > 0 ? file.Paths[0] : string.Empty;

            // 元のzipが登録済みで今もあるなら、中身は出さない（同じ配布物の写し。zipを消せば戻ってくる）
            if (IsCoveredByRegisteredZip(file, origin, owned, originExists))
            {
                hiddenByRegisteredZip++;
                continue;
            }

            // 展開物の中身かどうかを見ておく。フォルダ単位で同じ結果になるので、
            // 1件ごとにディスクを叩き直さないようキャッシュする
            var judgement = path.Length > 0 ? JudgeCached(path, judgements) : ArchiveContentJudgement.NotContent;

            // 元のzipが今もあるか（zipごとに1回だけ見る）。あれば「zipが無い展開物」ではない——
            // フォルダの目印（.unitypackage・.url）だけで決めると、zipが残っていても「zipが無い」と出た（画面で確かめて見つけた 2026-09-17）
            var originRemains = origin is not null && OriginExists(origin.ArchivePath, originExists);

            var row = new UnresolvedRow
            {
                File = file,
                FileName = path.Length > 0 ? Path.GetFileName(path) : file.Hash[..12],
                DirectoryText = path.Length > 0 ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                IsArchiveContent = judgement.IsContent && !originRemains,
                HasOriginZip = originRemains && !string.Equals(origin!.ArchivePath, path, StringComparison.OrdinalIgnoreCase),
                ContentReason = judgement.Reason,
                ProductFolder = judgement.ProductFolder,
                UnpackRoot = judgement.IsContent && !originRemains && path.Length > 0
                    ? UnpackRootFor(path, judgement.ProductFolder, rootContext)
                    : null,
                Origin = origin,
            };

            rows.Add(row);
        }

        return new ReloadedRows(rows, hiddenByRegisteredZip, judgements, originExists);
    }

    private void OnSelectionChanged()
    {
        // 選んだ行が検索で隠れていれば検索を消す（「元zipで登録」などで隠れた行へ移ったとき、左で何を選んでいるか分からなかった）
        if (Selected is not null && !MatchesFilter(Selected))
        {
            _filterText = string.Empty;
            OnPropertyChanged(nameof(FilterText));
            OnPropertyChanged(nameof(HasFilterText));
            FilesView.Refresh();
        }

        HasSearched = false;

        // 行を選び直したら、束ではなくその1件を扱う。束は見出しのボタンからだけ立つ
        ActiveGroup = null;

        ItemIdInput = string.Empty;
        Preview = null;
        StatusText = string.Empty;

        // 名前は下書きを入れておく。そのままでも通る形にしておかないと、
        // 「登録できる」と言いながら毎回入力を強いることになる。
        // 元zipが分かれば、中の1ファイルの名前（cloth.psd など）より商品名に近い
        LocalNameInput = Selected is null
            ? string.Empty
            : BoothAssetManager.Core.Resolution.FileNameQuery.ToNameDraft(Selected.Origin?.ArchiveName ?? Selected.FileName);

        Candidates.Clear();
        LocalNameSuggestions.Clear();
        foreach (var id in Selected?.File.CandidateItemIds ?? [])
        {
            Candidates.Add(WithBooth(new CandidateRow
            {
                ItemId = id,
                Title = $"商品ID {id}",
                Source = "取り込み時に読み取った情報",
            }));
        }

        AddRegistryCandidates();

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RegisterTargetFolder));
        OnPropertyChanged(nameof(RegisterTargetName));
        OnPropertyChanged(nameof(SearchTargetPath));
        OnPropertyChanged(nameof(SearchTargetText));
        OnPropertyChanged(nameof(RegisterFolderText));
        OnPropertyChanged(nameof(RegisterTargetSummary));
        OnPropertyChanged(nameof(CanRegisterFolder));
        OnPropertyChanged(nameof(SelectedPaths));
        OnPropertyChanged(nameof(SelectedContents));
        OnPropertyChanged(nameof(HasContents));
        OnPropertyChanged(nameof(ContentsSummary));
        OnPropertyChanged(nameof(ZoneText));
        OnPropertyChanged(nameof(HasZone));
        OnPropertyChanged(nameof(SelectedOriginText));
        OnPropertyChanged(nameof(HasSelectedOrigin));
        OnPropertyChanged(nameof(CanUseOriginZip));

        // 選び直したら「このファイルだけを扱う」は切り、zipの単位を決め直す
        _singleFileOnly = false;
        OnPropertyChanged(nameof(SingleFileOnly));
        ApplyZipUnit();
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(LocalIdPreview));
        OnPropertyChanged(nameof(HasLocalNameSuggestions));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>確定する前に中身を見る。設計メモの「候補を入れた時点で1件取得して確認」。</summary>
    private async Task PreviewAsync(string itemId)
    {
        // 数字でもBOOTHの商品URLでも受ける。ブラウザから来るのは普通URLの方
        var trimmed = Core.Services.BoothItemId.Parse(itemId);
        if (trimmed is null)
        {
            StatusText = itemId.Trim().Length == 0
                ? string.Empty
                : "商品IDが読み取れませんでした。数字か、BOOTHの商品ページのURLを入れてください。";
            OnPropertyChanged(nameof(HasStatus));
            return;
        }

        // URLを貼られた場合は、読み取ったIDに置き換えて何を見ているか分かるようにする
        if (trimmed != itemId.Trim())
        {
            ItemIdInput = trimmed;
        }

        // 取得の間も左の一覧は選び直せる。選び直した後に届いた結果を入れると、
        // 別のファイルに対して「確定」が押せてしまう。始めた時点の選択と違えば捨てる
        var startedWith = Selected;

        IsBusy = true;
        StatusText = "商品情報を取得しています…";
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.PreviewItem(trimmed));
            if (!ReferenceEquals(Selected, startedWith))
            {
                return;
            }

            if (result is CommandResult.PreviewLoaded loaded)
            {
                Preview = loaded.Preview;
                StatusText = string.Empty;
            }
            else if (result is CommandResult.Failed failed)
            {
                Preview = null;
                StatusText = failed.Message;
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private async Task AssignAsync()
    {
        if (Selected is null || Preview is null)
        {
            return;
        }

        // 元zipの束を選んでいればその全件。1件ずつの確定を順に掛ける
        // （2件目以降は既にある商品へ加わるだけで、BOOTHへは行かない）
        var targets = ActiveRows;
        var itemId = Preview.Id;

        IsBusy = true;
        try
        {
            var settled = new List<UnresolvedRow>();
            string? failure = null;
            foreach (var row in targets)
            {
                var result = await _services.Commands.ExecuteAsync(new UiCommand.AssignItemId(row.File.Hash, itemId));
                if (result is CommandResult.Failed failed)
                {
                    failure ??= failed.Message;
                    continue;
                }

                settled.Add(row);
            }

            if (settled.Count == 0)
            {
                StatusText = failure ?? string.Empty;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            // 確定したものはここで溜めて、最後にまとめて編集へ送る
            if (!_settledItemIds.Contains(itemId))
            {
                _settledItemIds.Add(itemId);
            }

            if (targets.Count == 1)
            {
                AfterSettled();
                return;
            }

            RemoveRows(settled);
            StatusText = settled.Count == targets.Count
                ? $"{settled.Count} 件を確定しました。"
                : $"{settled.Count} / {targets.Count} 件を確定しました。残りは失敗しました。{failure}";
            OnPropertyChanged(nameof(HasStatus));
            HideCoveredContents(settled);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 未確定の第三の出口。**BOOTHに無い商品として登録する。**
    ///
    /// 確定（BOOTHから取れる）でも除外（BOOTH商品ではない）でもないものが実在する——
    /// 非公開・削除済みになった商品を買っていた場合。
    /// 未確定に置き続けると二度と復活しないものが永久に溜まり、
    /// 除外に入れると統計からも検索からも消える。
    ///
    /// **更新が走らないことは、押す前に言う。**半年後に
    /// 「なぜこの商品だけ情報が増えないのか」にならないよう、商品ページにも常設で出す。
    /// </summary>
    private async Task RegisterLocalAsync()
    {
        if (Selected is null || string.IsNullOrWhiteSpace(LocalNameInput))
        {
            return;
        }

        // zipの中身の束を立てていれば、その全件を同じ仮の商品にする（1zip＝1商品）
        var targets = ActiveRows;
        var name = LocalNameInput.Trim();
        var what = targets.Count == 1 ? Selected.FileName : GroupSubject;
        var answer = Services.Notice.Show(
            $"{what} を「{name}」として登録します。\n\n"
            + $"仮のID（{LocalIdPreview}）を付けます。BOOTHから情報を取得しないので、名前も画像も増えません。\n\n"
            + "あとで商品IDが分かったら、編集画面の「IDを変える」で移せます。",
            "BOOTHに無い商品として登録する",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.RegisterLocalItem(targets[0].File.Hash, name));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            // 確定と同じ扱いで溜める。まとめて編集へ送れば、支払額もそのまま入れられる
            if (result is CommandResult.ItemSaved saved && !_settledItemIds.Contains(saved.ItemId))
            {
                _settledItemIds.Add(saved.ItemId);
            }

            if (targets.Count == 1 || result is not CommandResult.ItemSaved created)
            {
                AfterSettled();
                return;
            }

            // 残りの中身は、できた仮の商品に加える（BOOTHへは行かない）
            var settled = new List<UnresolvedRow> { targets[0] };
            foreach (var row in targets.Skip(1))
            {
                if (await _services.Commands.ExecuteAsync(new UiCommand.AssignItemId(row.File.Hash, created.ItemId)) is not CommandResult.Failed)
                {
                    settled.Add(row);
                }
            }

            RemoveRows(settled);
            StatusText = settled.Count == targets.Count
                ? $"{settled.Count} 件を登録しました。"
                : $"{settled.Count} / {targets.Count} 件を登録しました。残りは失敗しました。";
            OnPropertyChanged(nameof(HasStatus));
            HideCoveredContents(settled);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExcludeAsync()
    {
        var targets = ActiveRows;
        if (targets.Count == 0)
        {
            return;
        }

        var what = targets.Count == 1 ? targets[0].FileName : GroupSubject;
        var answer = Services.Notice.Show(
            $"{what} を管理対象から除外します。\n\n"
            + "ファイル自体は消しません。設定の「隠したもの」から戻せます。",
            "管理対象から除外する",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.Cancel);

        if (answer != System.Windows.MessageBoxResult.OK)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var reason = targets.Count == 1 ? "未確定画面から除外" : "未確定画面から元zipごと除外";
            foreach (var row in targets)
            {
                await _services.Commands.ExecuteAsync(new UiCommand.ExcludeFile(row.File.Hash, row.File.Paths, reason));
            }

            RememberExcluded(targets);
            if (targets.Count == 1)
            {
                AfterSettled(registered: false);
            }
            else
            {
                RemoveRows(targets);
                StatusText = $"{targets.Count} 件を管理対象から除外しました。";
                OnPropertyChanged(nameof(HasStatus));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>1件片付いたら一覧から外し、次の1件へ自動で移る。</summary>
    /// <param name="registered">商品に登録したか（管理対象から除外したときは false）。登録したら、そのzipの中身も一覧から外す。</param>
    private void AfterSettled(bool registered = true)
    {
        // 次に選ぶのは、検索で見えている行の中の次（隠れている行を選ぶと、左で何を選んでいるか分からない）
        var visible = FilesView.Cast<UnresolvedRow>().ToList();
        var index = Selected is null ? -1 : visible.IndexOf(Selected);
        var settledRow = Selected;
        if (Selected is not null)
        {
            Files.Remove(Selected);
            visible.Remove(Selected);
        }

        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(SettledCount));
        OnPropertyChanged(nameof(HasSettled));
        OnPropertyChanged(nameof(SettledText));

        _main.RefreshBadges();

        Selected = visible.Count == 0
            ? Files.FirstOrDefault()
            : visible[Math.Min(index < 0 ? 0 : index, visible.Count - 1)];

        RelayCommand.RaiseCanExecuteChanged();

        if (registered)
        {
            HideCoveredContents(settledRow is null ? [] : [settledRow]);
        }
    }

    /// <summary>確定したものを対象に編集の画面へ移る。ID確定と入力を分ける設計の受け渡し口。</summary>
    private void SendSettledToEdit()
    {
        if (_settledItemIds.Count == 0)
        {
            return;
        }

        var ids = _settledItemIds.ToList();
        _settledItemIds.Clear();
        _main.ShowEditAsync(ids).Forget();
    }

    private async Task OpenLastSettledAsync()
    {
        if (_settledItemIds.Count > 0 && await _services.Store.Items.LoadAsync(_settledItemIds[^1]) is { } item)
        {
            _main.ShowItem(item);
        }
    }

    /// <summary>自分で探したいときのために、ファイル名でBOOTH検索を開く。</summary>
    private void OpenBoothSearch()
    {
        if (SearchTargetPath is not { } target)
        {
            return;
        }

        // 自動検索と同じ対象で引く。片方だけファイル名、片方だけフォルダ名では読めない
        var query = FileNameQuery.ToSearchQuery(target);
        var url = Core.Booth.BoothClient.SearchUrl(query.Length > 0 ? query : Path.GetFileName(target));

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 開けなくても作業は続けられる
        }
    }

}
