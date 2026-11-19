using System.Collections.ObjectModel;
using Chmonos.Core.Commands;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>
/// 商品ページ：見つからないファイル・登録フォルダを、この商品の分だけ探す（ユーザ判断 2026-11-20）。
/// 前は見つからない行に「取り込みを開く」があり、取り込み画面へ移るだけで、移した先を取り込むか全商品を探すしかなかった。
/// </summary>
public sealed partial class ItemViewModel
{
    private RelayCommand? _findMissingCommand;
    private bool _isFindingMissing;

    /// <summary>見つからない行の「探す…」。範囲を選ぶ窓（取り込み画面と同じ）を開き、この商品の分だけを探す。</summary>
    public RelayCommand FindMissingCommand => _findMissingCommand ??= new RelayCommand(
        () => FindMissingAsync().Forget(),

        // 取り込みの③が済むまでは中身を変える操作を塞ぐ（外す・付けると同じ）
        () => !_isFindingMissing && !IsEditLocked && !_isAttachingFiles);

    /// <summary>
    /// 探した結果の、見つからない登録フォルダと候補（取り込み画面の結果の欄と同じ行）。候補は見せるだけで、
    /// 人が「この場所にする」を押したときだけ差し替える（推定した値を勝手に入れない）。
    /// </summary>
    public ObservableCollection<MissingFolderRow> MissingFolderCandidates { get; } = [];

    public bool HasMissingFolderCandidates => MissingFolderCandidates.Count > 0;

    private RelayCommand? _useFolderCandidateCommand;

    /// <summary>候補の場所に差し替える。引数は <see cref="FolderCandidateRow"/>。</summary>
    public RelayCommand UseFolderCandidateCommand => _useFolderCandidateCommand ??= new RelayCommand(
        parameter => UseFolderCandidateAsync(parameter as FolderCandidateRow).Forget(),
        parameter => parameter is FolderCandidateRow);

    private async Task FindMissingAsync()
    {
        _isFindingMissing = true;
        RelayCommand.RaiseCanExecuteChanged();
        try
        {
            // 進み具合は裏から遅れて届くことがあるので、結果を出した後の分は捨てる（結果の文を上書きしない）
            var searching = true;
            var progress = new Progress<(int Hashed, string? Detail)>(report => RunOnUiThread(() =>
            {
                if (searching)
                {
                    FilesNotice.Show($"中身を確かめています… {report.Hashed} 件");
                }
            }));
            var result = await MissingSearchFlow.RunAsync(
                _services, _main, [Item.Id], progress, started: () => FilesNotice.Show("見つからないファイルを調べています…"));
            searching = false;
            if (result is null)
            {
                FilesNotice.Clear();
                return;
            }

            ShowFolderCandidates(result is CommandResult.MissingFilesSearched searched ? searched.Result.MissingFolders : []);

            var text = MissingSearchFlow.SummaryOf(result, _services.Settings.WatchedFolders ?? []);
            if (result is CommandResult.MissingFilesSearched { Result.StillMissing: 0 })
            {
                FilesNotice.Show(text);
            }
            else
            {
                FilesNotice.Warn(text);
            }

            // 結び直した場所・見つからなかった日時を行に入れる（札「見つかりません」が消える／残る）
            await AfterAttachAsync([]);
        }
        finally
        {
            _isFindingMissing = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private void ShowFolderCandidates(IReadOnlyList<MissingFolder> folders)
    {
        MissingFolderCandidates.Clear();
        foreach (var folder in folders)
        {
            MissingFolderCandidates.Add(new MissingFolderRow(folder));
        }

        OnPropertyChanged(nameof(HasMissingFolderCandidates));
    }

    private async Task UseFolderCandidateAsync(FolderCandidateRow? row)
    {
        if (row is null)
        {
            return;
        }

        var owner = row.Owner;
        var result = await _services.Commands.ExecuteAsync(
            new UiCommand.RelocateFolder(owner.Folder.ItemId, owner.Folder.Path, row.Path));

        if (result is CommandResult.ItemSaved)
        {
            // 差し替えた行は候補を畳み、結果をその行に出す（取り込み画面の結果の欄と同じ）
            owner.Candidates.Clear();
            owner.StatusText = $"「{row.Path}」に差し替えました。";
            await _main.ReloadLibraryAsync();
            await AfterAttachAsync([]);

            // 登録フォルダの行は在るかをディスクで見て作るので、記録を読み直しただけでは札「見つかりません」が残る
            await FillLocalFolderStateAsync();
            return;
        }

        owner.StatusText = result is CommandResult.Failed failed ? failed.Message : "差し替えられませんでした。";
    }
}
