using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 未確定画面。BoothIDが決まらなかったファイルに、IDを与えるか管理から外す。
///
/// ID確定だけをまとめて先に片付ける形にしている（設計メモの Resolve → Edit）。
/// 1件ごとにID確定とメタデータ入力を交互にやらないのは、
/// 調べる作業と主観で決める作業とで頭の使い方が違うため。
/// 確定したものはこの画面で溜めておき、最後にまとめて編集へ送る。
/// </summary>
public sealed partial class ResolveViewModel : ViewModelBase
{
    private readonly AppServiceContainer _services;
    private readonly MainViewModel _main;

    /// <summary>「取り込み中に n 件増えました」の1行を出すために見る。</summary>
    public MainViewModel Main => _main;
    private readonly List<string> _settledItemIds = [];

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

        // 走っている最中に止めるのは「書き込む操作」だけにする。
        // 候補を出す・候補を確認する・ブラウザで開くは読み取りだけなので、
        // 確定を待っている間も次のファイルを調べられる
        // （確定処理は開始時に対象を控えるので、途中でプレビューが変わっても安全）
        ProposeCommand = new RelayCommand(() => ProposeAsync().Forget(), () => HasSelection);
        // 取得中は押せないようにする。他のボタンには入っていて、ここだけ抜けていた
        PreviewCommand = new RelayCommand(() => PreviewAsync(ItemIdInput).Forget(), () => CanPreview && !IsBusy);
        UseCandidateCommand = new RelayCommand(parameter => UseCandidateAsync(parameter).Forget(), parameter => parameter is CandidateRow);
        AssignCommand = new RelayCommand(() => AssignAsync().Forget(), () => HasPreview && HasSelection && !IsBusy);
        ExcludeCommand = new RelayCommand(() => ExcludeAsync().Forget(), () => HasSelection && !IsBusy);
        UseLocalNameCommand = new RelayCommand(
            parameter => { if (parameter is string name) { LocalNameInput = name; } },
            parameter => parameter is string);
        RegisterLocalCommand = new RelayCommand(
            () => RegisterLocalAsync().Forget(),
            () => HasSelection && !IsBusy && !string.IsNullOrWhiteSpace(LocalNameInput));
        SendSettledToEditCommand = new RelayCommand(SendSettledToEdit, () => _settledItemIds.Count > 0);
        OpenLastSettledCommand = new RelayCommand(() => OpenLastSettledAsync().Forget(), () => _settledItemIds.Count > 0);
        OpenBoothCommand = new RelayCommand(OpenBoothSearch, () => HasSelection);

        SelectFolderCommand = new RelayCommand(SelectFolder, parameter => parameter is string);
        SelectGroupCommand = new RelayCommand(SelectGroup, parameter => parameter is string);
        ClearGroupCommand = new RelayCommand(() => ActiveGroup = null, () => HasActiveGroup);
        // 取り込みで未確定が増えたときに読み直す。画面ごと作り直すのが一番確実
        ReloadCommand = new RelayCommand(_main.ShowResolve);
        SelectAllCommand = new RelayCommand(SelectAll);
        InvestigateFolderCommand = new RelayCommand(parameter => InvestigateFolder(parameter), parameter => parameter is string && !IsBusy);
        SearchFolderInBrowserCommand = new RelayCommand(parameter => SearchFolderInBrowser(parameter), parameter => parameter is string);
        RegisterFolderOfCommand = new RelayCommand(parameter => RegisterFolderOfAsync(parameter).Forget(), parameter => parameter is string && !IsBusy);
        ExcludeFolderCommand = new RelayCommand(parameter => ExcludeFolderAsync(parameter).Forget(), parameter => parameter is string && !IsBusy);
        UseOriginZipCommand = new RelayCommand(UseOriginZip, () => CanUseOriginZip);
        RegisterFolderCommand = new RelayCommand(() => RegisterFolderAsync().Forget(), () => CanRegisterFolder);
        ClearChecksCommand = new RelayCommand(ClearChecks);
        ExcludeCheckedCommand = new RelayCommand(() => ExcludeCheckedAsync().Forget(), () => HasChecked && !IsBusy);
        AssignCheckedCommand = new RelayCommand(() => AssignCheckedAsync().Forget(), () => HasChecked && HasPreview && !IsBusy);

        ReloadAsync().Forget();
    }

    public RelayCommand SelectFolderCommand { get; }

    /// <summary>元zipの束をまとめて1つの対象にする。</summary>
    public RelayCommand SelectGroupCommand { get; }

    /// <summary>束をやめて、選んでいる1件だけを扱う。</summary>
    public RelayCommand ClearGroupCommand { get; }

    /// <summary>「取り込み中に n 件増えました」を押したときの読み直し。</summary>
    public RelayCommand ReloadCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand InvestigateFolderCommand { get; }

    public RelayCommand SearchFolderInBrowserCommand { get; }

    public RelayCommand RegisterFolderOfCommand { get; }

    public RelayCommand ExcludeFolderCommand { get; }

    public RelayCommand UseOriginZipCommand { get; }

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

    public ObservableCollection<UnresolvedRow> Files { get; } = [];

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
            : $"確定すると：この商品を新しく登録して、{OutcomeSubject}を結び付けます";

    /// <summary>束を選んでいるときは件数まで言う。1件のつもりで押して全件が動くことが無いように。</summary>
    private string OutcomeSubject => ActiveGroup is null
        ? "このファイル"
        : $"元zip「{ActiveGroup}」の {ActiveRows.Count} 件";

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

    public string SettledText => $"この画面で {_settledItemIds.Count} 件を確定しました";

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

        StatusText = $"元のzip「{origin.ArchiveName}」は未確定の一覧にありません。既に商品に結び付いているか、まだ取り込んでいません。"
            + "取り込み画面にzipを落とすと、商品に結び付くか未確定に出ます。";
        OnPropertyChanged(nameof(HasStatus));
        DecisionFocusRequested?.Invoke();
    }

    /// <summary>
    /// 開くたびに、既にitem側が持っているファイルを未確定から均してから読み直す。
    /// 確定の途中で落ちると両方に残るため。
    /// </summary>
    public async Task ReloadAsync()
    {
        var healed = 0;
        string? failure = null;

        try
        {
            healed = await _services.Commands.ExecuteAsync(new UiCommand.ReconcileUnresolved())
                is CommandResult.Counted counted ? counted.Count : 0;
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            // 均せなくても一覧は出す。黙って空にする方がずっと悪い
            Core.Diagnostics.AppLog.Error("未確定の画面：突き合わせ", exception);
            failure = $"未確定の突き合わせに失敗しました: {exception.Message}";
        }

        // 読み込みはUIスレッド以外で終わることがあるので、必ず戻してから触る
        RunOnUiThread(() =>
        {
            Reload();

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

    private bool OriginExists(string archivePath)
    {
        if (!_originExists.TryGetValue(archivePath, out var exists))
        {
            exists = File.Exists(archivePath);
            _originExists[archivePath] = exists;
        }

        return exists;
    }

    public void Reload()
    {
        var unresolved = _services.Store.Unresolved.Load().Where(file => _scope?.Invoke(file) ?? true).ToList();
        _judgements.Clear();
        _originExists.Clear();

        Files.Clear();

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

        foreach (var (file, origin) in withOrigin)
        {
            var path = file.Paths.Count > 0 ? file.Paths[0] : string.Empty;

            // 展開物の中身かどうかを見ておく。フォルダ単位で同じ結果になるので、
            // 1件ごとにディスクを叩き直さないようキャッシュする
            var judgement = path.Length > 0 ? JudgeCached(path) : ArchiveContentJudgement.NotContent;

            // 元のzipが今もあるか（zipごとに1回だけ見る）。あれば「zipが無い展開物」ではない——
            // フォルダの目印（.unitypackage・.url）だけで決めると、zipが残っていても「zipが無い」と出た（画面で確かめて見つけた 2026-09-17）
            var originRemains = origin is not null && OriginExists(origin.ArchivePath);

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
                Origin = origin,
            };

            row.SelectionChanged += OnCheckedChanged;
            Files.Add(row);
        }

        Selected = Files.FirstOrDefault();
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
    }

    private void OnSelectionChanged()
    {
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
                Source = "取り込み時の手掛かり",
            }));
        }

        AddRegistryCandidates();

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(RegisterTargetFolder));
        OnPropertyChanged(nameof(RegisterTargetName));
        OnPropertyChanged(nameof(SearchTargetPath));
        OnPropertyChanged(nameof(SearchTargetText));
        OnPropertyChanged(nameof(RegisterFolderText));
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

        IsBusy = true;
        StatusText = "商品情報を取得しています…";
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.PreviewItem(trimmed));
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
                : $"{settled.Count} / {targets.Count} 件を確定しました（残りは失敗：{failure}）。";
            OnPropertyChanged(nameof(HasStatus));
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

        var name = LocalNameInput.Trim();
        var answer = System.Windows.MessageBox.Show(
            $"{Selected.FileName} を「{name}」として登録します。\n\n"
            + $"BOOTHには無い商品なので、仮のID（{LocalIdPreview}）を付けます。\n"
            + "この商品はBOOTHから情報を取り直しません（名前も画像も増えません）。\n\n"
            + "あとで本物の商品IDが分かったら、商品ページでファイルを外して付け直せます。",
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
                new UiCommand.RegisterLocalItem(Selected.File.Hash, name));

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

            AfterSettled();
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

        var what = targets.Count == 1 ? targets[0].FileName : $"元zip「{ActiveGroup}」の {targets.Count} 件";
        var answer = System.Windows.MessageBox.Show(
            $"{what} を管理対象から外します。\n\n"
            + "ファイル自体は消しません。次回以降のスキャンで未確定に出てこなくなります。",
            "管理対象から外す",
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

            if (targets.Count == 1)
            {
                AfterSettled();
            }
            else
            {
                RemoveRows(targets);
                StatusText = $"{targets.Count} 件を管理対象から外しました。";
                OnPropertyChanged(nameof(HasStatus));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>1件片付いたら一覧から外し、次の1件へ自動で移る。</summary>
    private void AfterSettled()
    {
        var index = Selected is null ? -1 : Files.IndexOf(Selected);
        if (index >= 0)
        {
            Files.RemoveAt(index);
        }

        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(SettledCount));
        OnPropertyChanged(nameof(HasSettled));
        OnPropertyChanged(nameof(SettledText));

        _main.RefreshBadges();

        Selected = Files.Count == 0
            ? null
            : Files[Math.Min(index < 0 ? 0 : index, Files.Count - 1)];

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>確定した分をまとめて編集へ送る。ID確定と入力を分ける設計の受け渡し口。</summary>
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
