using System.Collections.ObjectModel;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>見つからない登録フォルダの候補1つ（「この場所にする」で差し替える）。</summary>
public sealed class FolderCandidateRow
{
    public required MissingFolderRow Owner { get; init; }

    public required FolderCandidate Candidate { get; init; }

    public string Path => Candidate.Path;

    /// <summary>何が合ったか。人が選ぶ手掛かり。</summary>
    public string KindText => ImportViewModel.MatchText(Candidate.Kind);

    /// <summary>候補の中身の数。数で合っていない候補（名前だけ同じ）は、記録との違いを見比べられるように出す。</summary>
    public string CountText => ImportViewModel.CountText(Candidate.FileCount, Candidate.TotalBytes);
}

/// <summary>見つからない登録フォルダ1つ（取り込み画面の、探した結果の下に並ぶ）。</summary>
public sealed class MissingFolderRow : MissingResultLine
{
    private string _statusText = string.Empty;

    public MissingFolderRow(MissingFolder folder)
    {
        Folder = folder;
        foreach (var candidate in folder.Candidates)
        {
            Candidates.Add(new FolderCandidateRow { Owner = this, Candidate = candidate });
        }

        if (Candidates.Count == 0)
        {
            _statusText = "探したフォルダの中に、合うフォルダはありませんでした。移した先のフォルダを追加して、もう一度探してください。";
        }
    }

    public MissingFolder Folder { get; }

    public string ItemName => Folder.ItemName;

    /// <summary>商品名を押す（商品ページを開く。結果のファイルの行と同じ）。</summary>
    public RelayCommand? OpenItemCommand { get; set; }

    /// <summary>行の左に出す商品の絵（ファイルの行と同じ。絵は上にそろえ、文と候補は下へ伸びる）。</summary>
    public ResultThumbnail? Picture { get; set; }

    public string Path => Folder.Path;

    public string CountText => ImportViewModel.CountText(Folder.FileCount, Folder.TotalBytes);

    public ObservableCollection<FolderCandidateRow> Candidates { get; } = [];

    /// <summary>候補が無い・差し替えた・差し替えられなかったときの1行（押した行のすぐ下に出す。D6）。</summary>
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (SetField(ref _statusText, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => StatusText.Length > 0;
}

public sealed partial class ImportViewModel
{
    private RelayCommand? _useFolderCandidate;

    /// <summary>
    /// 探した結果の、見つからない登録フォルダと候補（見つからない・移動の点検 10-A）。
    /// 候補は見せるだけで、人が「この場所にする」を押したときだけ差し替える（推した値を勝手に入れない）。
    /// </summary>
    public IReadOnlyList<MissingFolderRow> MissingFolders => _missingFolders;

    private IReadOnlyList<MissingFolderRow> _missingFolders = [];

    public bool HasMissingFolders => MissingFolders.Count > 0;

    /// <summary>候補の場所に差し替える。引数は <see cref="FolderCandidateRow"/>。</summary>
    public RelayCommand UseFolderCandidateCommand => _useFolderCandidate ??= new RelayCommand(
        parameter => UseFolderCandidateAsync(parameter as FolderCandidateRow).Forget(),
        parameter => parameter is FolderCandidateRow);

    /// <summary>
    /// 探し直すたびに並べ直す（前の回の候補は古いので残さない）。結果の欄の一覧に、ファイルの2つの見出しと同じ形の見出しで並ぶ（メモ74）。
    /// </summary>
    internal void ShowMissingFolders(IReadOnlyList<MissingFolder> folders)
    {
        var rows = new List<MissingFolderRow>(folders.Count);
        foreach (var folder in folders)
        {
            var row = new MissingFolderRow(folder);
            row.OpenItemCommand = new RelayCommand(() => OpenResultItemAsync(folder.ItemId, text => row.StatusText = text).Forget());
            row.Picture = PictureOf(folder.ItemId, folder.ItemName);
            rows.Add(row);
        }

        _missingFolders = rows;
        FoldersHead.Count = rows.Count;
        FillResultLines(() => IsMissingFoldersExpanded = rows.Count <= ResultFoldOver);

        OnPropertyChanged(nameof(MissingFolders));
        OnPropertyChanged(nameof(HasMissingFolders));
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
            // 差し替えた行は候補を畳み、結果をその行に出す。商品のカードの「見つかりません」の印と検索の写しも直す
            owner.Candidates.Clear();
            owner.StatusText = $"「{row.Path}」に差し替えました。";
            await _main.ReloadLibraryAsync();
            return;
        }

        owner.StatusText = result is CommandResult.Failed failed ? failed.Message : "差し替えられませんでした。";
    }

    /// <summary>候補の合い方の言い方。</summary>
    internal static string MatchText(FolderMatchKind kind) => kind switch
    {
        FolderMatchKind.NameAndContents => "名前・ファイル数・サイズが同じ",
        FolderMatchKind.Contents => "ファイル数・サイズが同じ",
        _ => "名前が同じ",
    };

    /// <summary>フォルダの中身の数。数えられなかったときは空。</summary>
    internal static string CountText(int? fileCount, long? totalBytes)
        => fileCount is { } count && totalBytes is { } bytes
            ? $"{count} ファイル・{DisplayText.Size(bytes)}"
            : string.Empty;
}
