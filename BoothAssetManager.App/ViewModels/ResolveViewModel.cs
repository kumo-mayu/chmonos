using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定ファイル1件。一覧に並べる分の情報だけを持つ。</summary>
public sealed class UnresolvedRow : ViewModelBase
{
    private bool _isSelected;

    public required UnresolvedFile File { get; init; }

    /// <summary>一括操作の対象。一覧の選択（＝今見ているもの）とは別に持つ。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                SelectionChanged?.Invoke();
            }
        }
    }

    public event Action? SelectionChanged;

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required string DirectoryText { get; init; }

    /// <summary>配布物を展開した中身とみなせるか。多くの場合そのまま管理から外したい。</summary>
    public bool IsArchiveContent { get; init; }

    /// <summary>そう判断した理由。押し付けにならないよう根拠を見せる。</summary>
    public string? ContentReason { get; init; }

    /// <summary>展開物の根とみなしたフォルダ。まとめて扱う単位。</summary>
    public string? ProductFolder { get; init; }

    /// <summary>
    /// 展開元のzip。エクスプローラーの「すべて展開」が中のファイルに残した記録から分かる
    /// （zipを消した後でも、展開先を別のドライブへ移した後でも残る）。分からなければ null。
    /// </summary>
    public ArchiveOrigin? Origin { get; init; }

    public bool HasOrigin => Origin is not null;

    /// <summary>
    /// 一覧で束ねる単位。元zipが分かれば元zip、分からなければフォルダ。
    /// zipは配布された単位そのもので、中身はたいてい1商品。フォルダは展開の仕方次第で
    /// 1つのzipが何か所にも割れる（友人のデータで元zip 12 本のうち 6 本が複数のフォルダに割れていた）
    /// </summary>
    public string GroupKey => Origin?.ArchiveName ?? DirectoryText;

    /// <summary>元zipの束は畳んでおく。基本はzip単位で扱い、1件ずつ見たいときだけ開く。</summary>
    public bool StartsExpanded => Origin is null;

    /// <summary>取り込み時に拾えた候補の数。0件（手掛かりなし）と複数件（曖昧）がある。</summary>
    public int CandidateCount => File.CandidateItemIds.Count;

    public bool HasCandidates => CandidateCount > 0;

    public string CandidateText => CandidateCount switch
    {
        0 => "手掛かりなし",
        1 => "候補 1 件",
        _ => $"候補 {CandidateCount} 件（曖昧）",
    };
}

/// <summary>提示する候補1件。どこから来た候補なのかを添える。</summary>
public sealed class CandidateRow
{
    public required string ItemId { get; init; }

    public required string Title { get; init; }

    public string? Detail { get; init; }

    /// <summary>この候補の出どころ（取り込み時の手掛かり／検索）。</summary>
    public required string Source { get; init; }

    public bool IsStrong { get; init; }

    /// <summary>
    /// この候補の商品ページをブラウザで開く。
    /// 候補を出している以上、それが目当てのものか確かめる手段が要る。
    /// </summary>
    public RelayCommand? OpenBoothCommand { get; set; }
}

/// <summary>
/// 未確定画面。BoothIDが決まらなかったファイルに、IDを与えるか管理から外す。
///
/// ID確定だけをまとめて先に片付ける形にしている（設計メモの Resolve → Edit）。
/// 1件ごとにID確定とメタデータ入力を交互にやらないのは、
/// 調べる作業と主観で決める作業とで頭の使い方が違うため。
/// 確定したものはこの画面で溜めておき、最後にまとめて編集へ送る。
/// </summary>
public sealed class ResolveViewModel : ViewModelBase
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
        ProposeCommand = new RelayCommand(() => _ = ProposeAsync(), () => HasSelection);
        // 取得中は押せないようにする。他のボタンには入っていて、ここだけ抜けていた
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync(ItemIdInput), () => CanPreview && !IsBusy);
        UseCandidateCommand = new RelayCommand(parameter => _ = UseCandidateAsync(parameter), parameter => parameter is CandidateRow);
        AssignCommand = new RelayCommand(() => _ = AssignAsync(), () => HasPreview && HasSelection && !IsBusy);
        ExcludeCommand = new RelayCommand(() => _ = ExcludeAsync(), () => HasSelection && !IsBusy);
        UseLocalNameCommand = new RelayCommand(
            parameter => { if (parameter is string name) { LocalNameInput = name; } },
            parameter => parameter is string);
        RegisterLocalCommand = new RelayCommand(
            () => _ = RegisterLocalAsync(),
            () => HasSelection && !IsBusy && !string.IsNullOrWhiteSpace(LocalNameInput));
        SendSettledToEditCommand = new RelayCommand(SendSettledToEdit, () => _settledItemIds.Count > 0);
        OpenLastSettledCommand = new RelayCommand(() => _ = OpenLastSettledAsync(), () => _settledItemIds.Count > 0);
        OpenBoothCommand = new RelayCommand(OpenBoothSearch, () => HasSelection);

        SelectFolderCommand = new RelayCommand(SelectFolder, parameter => parameter is string);
        SelectGroupCommand = new RelayCommand(SelectGroup, parameter => parameter is string);
        ClearGroupCommand = new RelayCommand(() => ActiveGroup = null, () => HasActiveGroup);
        // 取り込みで未確定が増えたときに読み直す。画面ごと作り直すのが一番確実
        ReloadCommand = new RelayCommand(_main.ShowResolve);
        SelectAllCommand = new RelayCommand(SelectAll);
        SelectArchiveContentCommand = new RelayCommand(SelectArchiveContent, () => HasArchiveContent);
        RegisterFolderCommand = new RelayCommand(() => _ = RegisterFolderAsync(), () => CanRegisterFolder);
        ClearChecksCommand = new RelayCommand(ClearChecks);
        ExcludeCheckedCommand = new RelayCommand(() => _ = ExcludeCheckedAsync(), () => HasChecked && !IsBusy);
        AssignCheckedCommand = new RelayCommand(() => _ = AssignCheckedAsync(), () => HasChecked && HasPreview && !IsBusy);

        _ = ReloadAsync();
    }

    public RelayCommand SelectFolderCommand { get; }

    /// <summary>元zipの束をまとめて1つの対象にする。</summary>
    public RelayCommand SelectGroupCommand { get; }

    /// <summary>束をやめて、選んでいる1件だけを扱う。</summary>
    public RelayCommand ClearGroupCommand { get; }

    /// <summary>「取り込み中に n 件増えました」を押したときの読み直し。</summary>
    public RelayCommand ReloadCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectArchiveContentCommand { get; }

    public RelayCommand RegisterFolderCommand { get; }

    public RelayCommand ClearChecksCommand { get; }

    public RelayCommand ExcludeCheckedCommand { get; }

    public RelayCommand AssignCheckedCommand { get; }

    public int CheckedCount => Files.Count(row => row.IsSelected);

    public bool HasChecked => CheckedCount > 0;

    public string CheckedText => $"{CheckedCount} 件を選択中";

    public string AssignCheckedText => $"選択した {CheckedCount} 件をこのIDで確定";

    public string ExcludeCheckedText => $"選択した {CheckedCount} 件を管理から外す";

    private void OnCheckedChanged()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(CheckedText));
        OnPropertyChanged(nameof(AssignCheckedText));
        OnPropertyChanged(nameof(ExcludeCheckedText));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 同じフォルダのものをまとめて選ぶ。
    /// 1つのアーカイブを展開した中身が並んでいることが多く、
    /// それらは1件ずつ判断する必要がないため。
    /// </summary>
    private void SelectFolder(object? parameter)
    {
        if (parameter is not string directory)
        {
            return;
        }

        foreach (var row in Files.Where(row =>
            string.Equals(row.DirectoryText, directory, StringComparison.OrdinalIgnoreCase)))
        {
            row.IsSelected = true;
        }
    }

    private string? _activeGroup;

    /// <summary>
    /// まとめて扱っている元zipの束。null なら選んだ1件だけを扱う。
    /// 確定・管理から外すがこの束の全件に効く。元zipが単位の基本で、
    /// 1件ずつ扱いたいときは束を開いて行を選ぶ（行を選ぶと束は外れる）。
    /// </summary>
    public string? ActiveGroup
    {
        get => _activeGroup;
        private set
        {
            if (SetField(ref _activeGroup, value))
            {
                OnPropertyChanged(nameof(HasActiveGroup));
                OnPropertyChanged(nameof(ActiveGroupText));
                OnPropertyChanged(nameof(AssignOutcomeText));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasActiveGroup => ActiveGroup is not null;

    public string ActiveGroupText => ActiveGroup is null
        ? string.Empty
        : $"元zip「{ActiveGroup}」の {ActiveRows.Count} 件をまとめて扱っています";

    /// <summary>確定・管理から外すの対象。束を選んでいればその全件、でなければ選んだ1件。</summary>
    private IReadOnlyList<UnresolvedRow> ActiveRows => ActiveGroup is { } key
        ? Files.Where(row => row.HasOrigin && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase)).ToList()
        : Selected is null ? [] : [Selected];

    private void SelectGroup(object? parameter)
    {
        if (parameter is not string key)
        {
            return;
        }

        var first = Files.FirstOrDefault(row =>
            row.HasOrigin && string.Equals(row.GroupKey, key, StringComparison.OrdinalIgnoreCase));
        if (first is null)
        {
            return;
        }

        // 行を選ぶと束は外れるので、先に代表の行を選んでから束を立てる。
        // 代表の行は手掛かりの表示と検索の対象に使う（検索は元zipの名前で引くので、どの行でも同じ）
        Selected = first;
        ActiveGroup = key;
    }

    private void SelectAll()
    {
        foreach (var row in Files)
        {
            row.IsSelected = true;
        }
    }

    private void ClearChecks()
    {
        foreach (var row in Files.Where(row => row.IsSelected))
        {
            row.IsSelected = false;
        }
    }

    private async Task ExcludeCheckedAsync()
    {
        var targets = Files.Where(row => row.IsSelected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var sample = string.Join("\n", targets.Take(8).Select(row => $"・{row.FileName}"));
        if (targets.Count > 8)
        {
            sample += $"\n…ほか {targets.Count - 8} 件";
        }

        var answer = System.Windows.MessageBox.Show(
            $"{targets.Count} 件を管理から外します。\n\n{sample}\n\n"
            + "ファイル自体は消しません。次回以降のスキャンで未確定に出てこなくなります。",
            "まとめて管理から外す",
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
            foreach (var row in targets)
            {
                await _services.Commands.ExecuteAsync(
                    new UiCommand.ExcludeFile(row.File.Hash, row.File.Paths, "未確定画面からまとめて除外"));
            }

            RemoveRows(targets);
            StatusText = $"{targets.Count} 件を管理から外しました。";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    /// <summary>
    /// 選んだ複数のファイルを同じ商品IDへ確定する。
    /// 1商品に複数のファイル（本体zipと差分、psdなど）が付くことは普通にある。
    /// 2件目以降はローカルのitemへ追加されるだけで、BOOTHへは行かない。
    /// </summary>
    private async Task AssignCheckedAsync()
    {
        var targets = Files.Where(row => row.IsSelected).ToList();
        if (targets.Count == 0 || Preview is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"{targets.Count} 件を「{Preview.Name}」（ID {Preview.Id}）のファイルとして確定します。\n\n"
            + "同じ商品のファイルであることを確認してください。",
            "まとめて確定",
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
            var settled = new List<UnresolvedRow>();
            foreach (var row in targets)
            {
                var result = await _services.Commands.ExecuteAsync(
                    new UiCommand.AssignItemId(row.File.Hash, Preview.Id));

                if (result is not CommandResult.Failed)
                {
                    settled.Add(row);
                }
            }

            if (!_settledItemIds.Contains(Preview.Id))
            {
                _settledItemIds.Add(Preview.Id);
            }

            RemoveRows(settled);
            StatusText = settled.Count == targets.Count
                ? $"{settled.Count} 件を確定しました。"
                : $"{settled.Count} / {targets.Count} 件を確定しました（残りは失敗）。";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

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

    public System.Windows.GridLength ListColumnWidth => IsEmbedded
        ? new System.Windows.GridLength(0)
        : new System.Windows.GridLength(330);

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

    /// <summary>判定はフォルダ単位で同じになるので、フォルダをキーに覚えておく。</summary>
    private ArchiveContentJudgement JudgeCached(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (_judgements.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        var judgement = ArchiveContentDetector.Judge(path);
        _judgements[directory] = judgement;
        return judgement;
    }

    /// <summary>展開物とみなせるものの件数。0なら案内も出さない。</summary>
    public int ArchiveContentCount => Files.Count(row => row.IsArchiveContent);

    public bool HasArchiveContent => ArchiveContentCount > 0;

    public string ArchiveContentText
    {
        get
        {
            var rows = Files.Where(row => row.IsArchiveContent).ToList();
            if (rows.Count == 0)
            {
                return string.Empty;
            }

            var folders = rows
                .Select(row => Path.GetFileName(row.ProductFolder ?? string.Empty))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var where = folders.Count switch
            {
                0 => string.Empty,
                1 => $"（{folders[0]} の中）",
                _ => $"（{string.Join("・", folders.Take(3))}{(folders.Count > 3 ? " ほか" : string.Empty)} の中）",
            };

            return $"配布物を展開した中身とみなせるものが {rows.Count} 件あります{where}。";
        }
    }

    /// <summary>1件でも理由を見せる。まとめて外す前に何を根拠にしたかが分かるように。</summary>
    public string ArchiveContentReason =>
        Files.FirstOrDefault(row => row.IsArchiveContent)?.ContentReason ?? string.Empty;

    /// <summary>展開物とみなしたものだけを選ぶ。外すかどうかは見てから決めてもらう。</summary>
    private void SelectArchiveContent()
    {
        foreach (var row in Files.Where(row => row.IsArchiveContent))
        {
            row.IsSelected = true;
        }
    }

    /// <summary>
    /// 登録の対象にするフォルダ。
    /// 取り込み元フォルダの直下の子を選ぶ（zipが展開されたときの単位と一致するため）。
    /// 取り込み元が分からなければ、目印のあるフォルダをそのまま使う。
    /// </summary>
    public string? RegisterTargetFolder
    {
        get
        {
            var row = Selected ?? Files.FirstOrDefault(entry => entry.IsArchiveContent);
            if (row?.ProductFolder is not { } marker || row.File.Paths.Count == 0)
            {
                return null;
            }

            var path = row.File.Paths[0];

            foreach (var root in _services.Settings.ImportFolders)
            {
                var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
                if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rest = path[(normalizedRoot.Length + 1)..];
                var firstSegment = rest.Split(Path.DirectorySeparatorChar)[0];
                return Path.Combine(normalizedRoot, firstSegment);
            }

            return ClimbSingleChildFolders(marker);
        }
    }

    /// <summary>
    /// 取り込み元が分からないときの当て。
    /// 「そのフォルダしか入っていない親」が続く限り遡る。
    /// zipを展開すると rurune_v1.1.3/rurune のように1段包まれることが多く、
    /// 配布の単位は外側だから。中に他のものが混ざった時点で止める。
    /// </summary>
    private static string ClimbSingleChildFolders(string folder)
    {
        var current = folder;

        for (var depth = 0; depth < 4; depth++)
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent))
            {
                return current;
            }

            try
            {
                var entries = Directory.EnumerateFileSystemEntries(parent).Take(2).ToList();
                if (entries.Count != 1)
                {
                    return current;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return current;
            }

            current = parent;
        }

        return current;
    }

    public string RegisterTargetName => Path.GetFileName(RegisterTargetFolder ?? string.Empty);

    public bool CanRegisterFolder => RegisterTargetFolder is not null && HasPreview && !IsBusy;

    public string RegisterFolderText => RegisterTargetName.Length > 0
        ? $"「{RegisterTargetName}」をこの商品として登録"
        : "このフォルダをこの商品として登録";

    /// <summary>
    /// フォルダを商品に紐付ける。zipを落とし直せない場合の受け皿。
    /// 紐付けると配下がスキャン対象から外れるので、未確定も一緒に片付く。
    /// </summary>
    private async Task RegisterFolderAsync()
    {
        if (RegisterTargetFolder is not { } folder || Preview is null)
        {
            return;
        }

        var (count, bytes) = RegisteredFolderSet.Measure(folder);

        var answer = System.Windows.MessageBox.Show(
            $"次のフォルダを「{Preview.Name}」（ID {Preview.Id}）として登録します。\n\n"
            + $"{folder}\n{count} ファイル / {Core.Models.DisplayText.Size(bytes)}\n\n"
            + "以降このフォルダの中はスキャンしなくなり、未確定にも出てこなくなります。\n"
            + "フォルダを移動するとリンクが切れるので、その場合は登録し直してください。",
            "フォルダを商品として登録",
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
                new UiCommand.RegisterFolder(Preview.Id, folder));

            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            if (!_settledItemIds.Contains(Preview.Id))
            {
                _settledItemIds.Add(Preview.Id);
            }

            // itemの中身が変わったので、持ち回っているライブラリも読み直す。
            // これをしないと商品ページに登録したフォルダが出てこない
            await _main.ReloadLibraryAsync();

            await ReloadAsync();
            StatusText = $"「{RegisterTargetName}」を登録しました。配下の未確定は一覧から外れます。";
            OnPropertyChanged(nameof(HasStatus));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Reload()
    {
        var unresolved = _services.Store.Unresolved.Load().Where(file => _scope?.Invoke(file) ?? true).ToList();
        _judgements.Clear();

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
            .ThenByDescending(entry => entry.File.SizeBytes)
            .ToList();

        foreach (var (file, origin) in withOrigin)
        {
            var path = file.Paths.Count > 0 ? file.Paths[0] : string.Empty;

            // 展開物の中身かどうかを見ておく。フォルダ単位で同じ結果になるので、
            // 1件ごとにディスクを叩き直さないようキャッシュする
            var judgement = path.Length > 0 ? JudgeCached(path) : ArchiveContentJudgement.NotContent;

            var row = new UnresolvedRow
            {
                File = file,
                FileName = path.Length > 0 ? Path.GetFileName(path) : file.Hash[..12],
                DirectoryText = path.Length > 0 ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
                SizeText = Core.Models.DisplayText.Size(file.SizeBytes),
                IsArchiveContent = judgement.IsContent,
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
        OnPropertyChanged(nameof(ArchiveContentCount));
        OnPropertyChanged(nameof(HasArchiveContent));
        OnPropertyChanged(nameof(ArchiveContentText));
        OnPropertyChanged(nameof(ArchiveContentReason));
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
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(LocalIdPreview));
        OnPropertyChanged(nameof(HasLocalNameSuggestions));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 手元のアバター登録簿から候補を足す。**通信は増えない。**
    ///
    /// 登録簿は未所持の商品の名前まで持っているので、BOOTHが404を返すファイルでも
    /// 名前から辿り着けることがある。別名（「くうた対応」など）は
    /// **まさにファイル名に現れる形**で溜まっている。
    ///
    /// **欄は分けない。**（Q7）候補が2箇所に出ると、ユーザは
    /// 「どちらを先に見るべきか」を判断させられる。押した先で真実を出せばよい——
    /// 「BOOTHでは見つかりません。手元の記録では『くうた』です」。
    /// </summary>
    private void AddRegistryCandidates()
    {
        if (Selected?.File.Paths.FirstOrDefault() is not { } path)
        {
            return;
        }

        var registry = _services.Store.Avatars.Load().Entries;
        var already = Candidates.Select(row => row.ItemId).ToHashSet(StringComparer.Ordinal);

        foreach (var candidate in Core.Resolution.RegistryCandidates.For(
            path, registry, _services.Bridge, _services.KanjiReadings))
        {
            // 名前は「BOOTHに無い商品として登録する」側の候補にも回す。
            // 当たった項目が非公開なら、その名前こそが手元に残っている唯一の名前
            if (!LocalNameSuggestions.Contains(candidate.Name))
            {
                LocalNameSuggestions.Add(candidate.Name);
            }

            if (!already.Add(candidate.ItemId))
            {
                continue;
            }

            var detail = $"「{candidate.MatchedOn}」で一致";
            if (candidate.NeverFetched)
            {
                detail += "　BOOTHからは情報を取れていません";
            }

            Candidates.Add(WithBooth(new CandidateRow
            {
                ItemId = candidate.ItemId,
                Title = candidate.Name,
                Detail = detail,
                Source = "手元のアバター登録簿",
            }));
        }
    }

    /// <summary>ファイル名からBOOTH内を検索して候補を出す。通信するので明示的に押させる。</summary>
    /// <summary>
    /// 検索の手掛かりにするパス。
    ///
    /// 展開物の中身は、ファイル名（cloth.psd など）で引いても商品には辿り着かない。
    /// 元zipが分かればその名前で、分からなければ展開元とみなしたフォルダの名前で引く。
    /// zip名は配布者が付けた名前そのもので、フォルダ名は展開した人が変えていることがある。
    /// zipが今もその場所にあれば、中の unitypackage も手掛かりとして読まれる。
    /// バナーからも候補カードからも同じ対象になるようにここへ集約する。
    /// </summary>
    public string? SearchTargetPath => Selected is null || Selected.File.Paths.Count == 0
        ? null
        : Selected.Origin is { } origin
            ? origin.ArchivePath
            : Selected.IsArchiveContent && RegisterTargetFolder is { } folder
                ? folder
                : Selected.File.Paths[0];

    public string SearchTargetText => SearchTargetPath is null
        ? string.Empty
        : SelectedOriginText is not null
            ? $"元のzipの名前「{Selected!.Origin!.ArchiveName}」で探します（展開したときに Windows が残した記録から分かりました）"
        : Selected?.IsArchiveContent == true
            ? $"フォルダ名「{Path.GetFileName(SearchTargetPath)}」で探します（ファイル名では商品に辿り着かないため）"
            : $"ファイル名「{Path.GetFileName(SearchTargetPath)}」で探します";

    private async Task ProposeAsync()
    {
        if (SearchTargetPath is not { } searchTarget)
        {
            return;
        }

        IsBusy = true;
        StatusText = string.Empty;
        SearchCurrent = 0;
        SearchTotal = 0;
        SearchPhase = "準備しています";

        // 1件ずつ間隔を空けて取りに行くので十数秒かかることがある。
        // 何をどこまでやっているかを出さないと、止まったように見える。
        var progress = new Progress<ResolveProgress>(report => RunOnUiThread(() =>
        {
            SearchPhase = report.Phase;
            SearchCurrent = report.Current;
            SearchTotal = report.Total;
        }));

        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ProposeCandidates(searchTarget, progress));

            if (result is CommandResult.CandidatesProposed proposed)
            {
                foreach (var candidate in proposed.Candidates)
                {
                    Candidates.Add(ToRow(candidate));
                }

                SearchPhase = string.Empty;
                StatusText = proposed.Candidates.Count == 0
                    ? "候補は見つかりませんでした。商品IDを直接入れるか、管理から外してください。"
                    : $"候補を {proposed.Candidates.Count} 件見つけました。";
            }
            else if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
            }
        }
        finally
        {
            IsBusy = false;
            IsSearching = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    // --- 自動検索の進み具合 ---

    private string _searchPhase = string.Empty;
    private int _searchCurrent;
    private int _searchTotal;
    private bool _isSearching;

    /// <summary>今どの段階かの文言。</summary>
    public string SearchPhase
    {
        get => _searchPhase;
        private set
        {
            if (SetField(ref _searchPhase, value))
            {
                IsSearching = value.Length > 0;
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    public int SearchCurrent
    {
        get => _searchCurrent;
        private set
        {
            if (SetField(ref _searchCurrent, value))
            {
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    /// <summary>0なら件数の分からない段階。バーは伸び縮みだけさせる。</summary>
    public int SearchTotal
    {
        get => _searchTotal;
        private set
        {
            if (SetField(ref _searchTotal, value))
            {
                OnPropertyChanged(nameof(HasSearchTotal));
                OnPropertyChanged(nameof(SearchProgressText));
            }
        }
    }

    public bool HasSearchTotal => SearchTotal > 0;

    public bool IsSearching
    {
        get => _isSearching;
        private set => SetField(ref _isSearching, value);
    }

    public string SearchProgressText => SearchTotal > 0
        ? $"{SearchPhase}　{SearchCurrent + 1} / {SearchTotal}"
        : SearchPhase;

    private static CandidateRow ToRow(ResolutionCandidate candidate) => WithBooth(new CandidateRow
    {
        ItemId = candidate.ItemId,
        Title = candidate.Name ?? $"商品ID {candidate.ItemId}",
        Detail = string.Join("　", new[] { candidate.ShopName, string.Join(" / ", candidate.Reasons) }
            .Where(part => !string.IsNullOrEmpty(part))),
        Source = candidate.IsStrong ? $"検索・確度が高い（{candidate.Score}）" : $"検索（{candidate.Score}）",
        IsStrong = candidate.IsStrong,
    });

    /// <summary>候補にBOOTHを開くコマンドを付ける。候補を出す以上、確かめる手段が要る。</summary>
    private static CandidateRow WithBooth(CandidateRow row)
    {
        row.OpenBoothCommand = new RelayCommand(() => OpenInBrowser(Core.Booth.BoothClient.ItemPageUrl(row.ItemId)));
        return row;
    }

    private static void OpenInBrowser(string url)
    {
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

    private async Task UseCandidateAsync(object? parameter)
    {
        if (parameter is CandidateRow candidate)
        {
            ItemIdInput = candidate.ItemId;
            await PreviewAsync(candidate.ItemId);
        }
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
            $"{what} を管理から外します。\n\n"
            + "ファイル自体は消しません。次回以降のスキャンで未確定に出てこなくなります。",
            "管理から外す",
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
                StatusText = $"{targets.Count} 件を管理から外しました。";
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
        _ = _main.ShowEditAsync(ids);
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
