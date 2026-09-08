using System.Collections.ObjectModel;
using System.IO;
using BoothAssetManager.Core.Commands;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Services;

namespace BoothAssetManager.App.ViewModels;

/// <summary>未確定ファイル1件。一覧に並べる分の情報だけを持つ。</summary>
public sealed class UnresolvedRow
{
    public required UnresolvedFile File { get; init; }

    public required string FileName { get; init; }

    public required string SizeText { get; init; }

    public required string DirectoryText { get; init; }

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
    private readonly List<string> _settledItemIds = [];

    private UnresolvedRow? _selected;
    private string _itemIdInput = string.Empty;
    private ItemPreview? _preview;
    private string _statusText = string.Empty;
    private bool _isBusy;

    public ResolveViewModel(AppServiceContainer services, MainViewModel main)
    {
        _services = services;
        _main = main;

        ProposeCommand = new RelayCommand(() => _ = ProposeAsync(), () => HasSelection && !IsBusy);
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync(ItemIdInput), () => CanPreview);
        UseCandidateCommand = new RelayCommand(parameter => _ = UseCandidateAsync(parameter), parameter => parameter is CandidateRow && !IsBusy);
        AssignCommand = new RelayCommand(() => _ = AssignAsync(), () => HasPreview && HasSelection && !IsBusy);
        ExcludeCommand = new RelayCommand(() => _ = ExcludeAsync(), () => HasSelection && !IsBusy);
        SendSettledToEditCommand = new RelayCommand(SendSettledToEdit, () => _settledItemIds.Count > 0);
        OpenBoothCommand = new RelayCommand(OpenBoothSearch, () => HasSelection);

        Reload();
    }

    public ObservableCollection<UnresolvedRow> Files { get; } = [];

    public ObservableCollection<CandidateRow> Candidates { get; } = [];

    public RelayCommand ProposeCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand UseCandidateCommand { get; }

    public RelayCommand AssignCommand { get; }

    public RelayCommand ExcludeCommand { get; }

    public RelayCommand SendSettledToEditCommand { get; }

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

    public bool CanPreview => !IsBusy && ItemIdInput.Trim().Length > 0;

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

    public string PreviewOwnedNote => "既にライブラリにある商品です。このファイルはそこへ追加されます。";

    public int RemainingCount => Files.Count;

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

    public void Reload()
    {
        var unresolved = _services.Store.Unresolved.Load();

        Files.Clear();
        foreach (var file in unresolved.OrderByDescending(entry => entry.SizeBytes))
        {
            var path = file.Paths.Count > 0 ? file.Paths[0] : string.Empty;
            Files.Add(new UnresolvedRow
            {
                File = file,
                FileName = path.Length > 0 ? Path.GetFileName(path) : file.Hash[..12],
                DirectoryText = path.Length > 0 ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
                SizeText = FormatSize(file.SizeBytes),
            });
        }

        Selected = Files.FirstOrDefault();
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(RemainingText));
    }

    private void OnSelectionChanged()
    {
        ItemIdInput = string.Empty;
        Preview = null;
        StatusText = string.Empty;

        Candidates.Clear();
        foreach (var id in Selected?.File.CandidateItemIds ?? [])
        {
            Candidates.Add(new CandidateRow
            {
                ItemId = id,
                Title = $"商品ID {id}",
                Source = "取り込み時の手掛かり",
            });
        }

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedPaths));
        OnPropertyChanged(nameof(SelectedContents));
        OnPropertyChanged(nameof(HasContents));
        OnPropertyChanged(nameof(ContentsSummary));
        OnPropertyChanged(nameof(ZoneText));
        OnPropertyChanged(nameof(HasZone));
        OnPropertyChanged(nameof(HasStatus));
        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>ファイル名からBOOTH内を検索して候補を出す。通信するので明示的に押させる。</summary>
    private async Task ProposeAsync()
    {
        if (Selected is null || Selected.File.Paths.Count == 0)
        {
            return;
        }

        IsBusy = true;
        StatusText = "BOOTHを検索しています…";
        try
        {
            var result = await _services.Commands.ExecuteAsync(
                new UiCommand.ProposeCandidates(Selected.File.Paths[0]));

            if (result is CommandResult.CandidatesProposed proposed)
            {
                foreach (var candidate in proposed.Candidates)
                {
                    Candidates.Add(ToRow(candidate));
                }

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
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private static CandidateRow ToRow(ResolutionCandidate candidate) => new()
    {
        ItemId = candidate.ItemId,
        Title = candidate.Name ?? $"商品ID {candidate.ItemId}",
        Detail = string.Join("　", new[] { candidate.ShopName, string.Join(" / ", candidate.Reasons) }
            .Where(part => !string.IsNullOrEmpty(part))),
        Source = candidate.IsStrong ? $"検索・確度が高い（{candidate.Score}）" : $"検索（{candidate.Score}）",
        IsStrong = candidate.IsStrong,
    };

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
        var trimmed = itemId.Trim();
        if (trimmed.Length == 0)
        {
            return;
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

        var hash = Selected.File.Hash;
        var itemId = Preview.Id;

        IsBusy = true;
        try
        {
            var result = await _services.Commands.ExecuteAsync(new UiCommand.AssignItemId(hash, itemId));
            if (result is CommandResult.Failed failed)
            {
                StatusText = failed.Message;
                OnPropertyChanged(nameof(HasStatus));
                return;
            }

            // 確定したものはここで溜めて、最後にまとめて編集へ送る
            if (!_settledItemIds.Contains(itemId))
            {
                _settledItemIds.Add(itemId);
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
        if (Selected is null)
        {
            return;
        }

        var answer = System.Windows.MessageBox.Show(
            $"{Selected.FileName} を管理から外します。\n\n"
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
            await _services.Commands.ExecuteAsync(
                new UiCommand.ExcludeFile(Selected.File.Hash, Selected.File.Paths, "未確定画面から除外"));
            AfterSettled();
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

    /// <summary>自分で探したいときのために、ファイル名でBOOTH検索を開く。</summary>
    private void OpenBoothSearch()
    {
        if (Selected is null || Selected.File.Paths.Count == 0)
        {
            return;
        }

        var query = FileNameQuery.ToSearchQuery(Selected.File.Paths[0]);
        var url = Core.Booth.BoothClient.SearchUrl(query.Length > 0 ? query : Selected.FileName);

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
