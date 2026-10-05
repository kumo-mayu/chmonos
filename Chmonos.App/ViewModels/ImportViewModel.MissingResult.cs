using System.Collections.ObjectModel;
using System.IO;
using Chmonos.Core.Services;

namespace Chmonos.App.ViewModels;

/// <summary>「見つからないファイルを探す」の結果の、商品のファイル1行（紐付け直した物・見つからなかった物）。</summary>
public sealed class MissingFileResultRow : ViewModelBase
{
    private string _statusText = string.Empty;

    public required string ItemId { get; init; }

    public required string ItemName { get; init; }

    public required string FileName { get; init; }

    /// <summary>紐付け直した先のフォルダ。見つからなかった行は空。</summary>
    public string FolderText { get; init; } = string.Empty;

    public bool HasFolder => FolderText.Length > 0;

    /// <summary>吹き出しに出す、ファイルの場所（紐付け直した行は新しい場所、見つからなかった行は元の場所）。</summary>
    public string PathTip { get; init; } = string.Empty;

    /// <summary>商品名を押す（商品ページを開く）。</summary>
    public RelayCommand? OpenItemCommand { get; set; }

    /// <summary>商品を開けなかったときの1行（押した行のすぐ下に出す。D6）。</summary>
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
    /// <summary>
    /// これより多ければ、結果の一覧を畳んで出す。監視フォルダの中を丸ごと移した回は数十〜数百件になり、
    /// 開いたまま並べると、下の「見つからない登録フォルダ」（押して選ぶ物）が画面の外へ押し出される。
    /// 5件は見出しと合わせて、幅900でもスクロールせずに読める数。
    /// </summary>
    internal const int ResultFoldOver = 5;

    private bool _isRelinkedExpanded;
    private bool _isNotFoundExpanded;
    private string _missingSearchNotes = string.Empty;

    /// <summary>
    /// 探して紐付け直したファイル（手触りの確認 2026-10-06・メモ73）。
    /// 前は「n 件を紐付け直しました」の1行だけで、どの商品のどのファイルがどこへ移ったかが分からず、
    /// 監視していない場所を足して探した回に結び直っていたのに、見つからなかったように見えた。
    /// </summary>
    public ObservableCollection<MissingFileResultRow> RelinkedFiles { get; } = [];

    /// <summary>探しても見つからなかったファイル。</summary>
    public ObservableCollection<MissingFileResultRow> NotFoundFiles { get; } = [];

    public bool HasRelinkedFiles => RelinkedFiles.Count > 0;

    public bool HasNotFoundFiles => NotFoundFiles.Count > 0;

    public string RelinkedCountText => $"  {RelinkedFiles.Count} 件";

    public string NotFoundCountText => $"  {NotFoundFiles.Count} 件";

    public bool IsRelinkedExpanded
    {
        get => _isRelinkedExpanded;
        set => SetField(ref _isRelinkedExpanded, value);
    }

    public bool IsNotFoundExpanded
    {
        get => _isNotFoundExpanded;
        set => SetField(ref _isNotFoundExpanded, value);
    }

    /// <summary>探せなかった場所・読めなかった物の文（1行に1つ）。1行目の要約に混ぜると長くなり、切れて読めなかった。</summary>
    public string MissingSearchNotes
    {
        get => _missingSearchNotes;
        private set
        {
            if (SetField(ref _missingSearchNotes, value))
            {
                OnPropertyChanged(nameof(HasMissingSearchNotes));
            }
        }
    }

    public bool HasMissingSearchNotes => MissingSearchNotes.Length > 0;

    /// <summary>探し直すたびに並べ直す（前の回の結果は古いので残さない）。結果が無い（失敗・窓を閉じた）なら空にする。</summary>
    internal void ShowMissingFiles(MissingFileSearchResult? result)
    {
        Fill(RelinkedFiles, result?.RelinkedFiles ?? []);
        Fill(NotFoundFiles, result?.NotFoundFiles ?? []);
        MissingSearchNotes = result is null ? string.Empty : string.Join("\n", MissingSearchNoteLines(result));
        IsRelinkedExpanded = RelinkedFiles.Count <= ResultFoldOver;
        IsNotFoundExpanded = NotFoundFiles.Count <= ResultFoldOver;

        OnPropertyChanged(nameof(HasRelinkedFiles));
        OnPropertyChanged(nameof(HasNotFoundFiles));
        OnPropertyChanged(nameof(RelinkedCountText));
        OnPropertyChanged(nameof(NotFoundCountText));
    }

    private void Fill(ObservableCollection<MissingFileResultRow> rows, IReadOnlyList<MissingFileOutcome> outcomes)
    {
        rows.Clear();
        foreach (var row in outcomes
                     .Select(ResultRowOf)
                     .OrderBy(row => row.ItemName, StringComparer.CurrentCulture)
                     .ThenBy(row => row.FileName, StringComparer.CurrentCulture))
        {
            row.OpenItemCommand = new RelayCommand(() => OpenResultItemAsync(row.ItemId, text => row.StatusText = text).Forget());
            rows.Add(row);
        }
    }

    /// <summary>結果の1つを行にする。紐付け直した物は新しい場所の名前とフォルダ、見つからなかった物は元の名前。</summary>
    internal static MissingFileResultRow ResultRowOf(MissingFileOutcome outcome)
    {
        var path = outcome.NewPath ?? outcome.OldPaths.FirstOrDefault() ?? string.Empty;
        var name = Path.GetFileName(path);
        return new MissingFileResultRow
        {
            ItemId = outcome.ItemId,
            ItemName = outcome.ItemName,
            // 取り込みが場所を全部外したファイルは、元の名前が記録に残っていない
            FileName = name.Length > 0 ? name : "名前の分からないファイル",
            FolderText = outcome.NewPath is { } found ? Path.GetDirectoryName(found) ?? string.Empty : string.Empty,
            PathTip = path,
        };
    }

    /// <summary>1行目の要約の下に出す、探せなかった場所・読めなかった物の文（どれも句点で終わる）。</summary>
    internal static IEnumerable<string> MissingSearchNoteLines(MissingFileSearchResult result)
    {
        if (result.Unreachable.Count > 0)
        {
            yield return $"{result.Unreachable.Count} 個のフォルダはつながっていないため探せませんでした。";
        }

        // ドライブは在ってフォルダだけが無い（名前を変えた・移した）。つないでも直らないので、外付けとは分けて言う
        // （見つからない・移動の点検 9・2026-10-05）
        if (result.NotFoundFolders.Count > 0)
        {
            yield return $"{result.NotFoundFolders.Count} 個のフォルダは見つからないため探せませんでした。"
                + "名前を変えたか移したなら、新しい場所を監視フォルダに追加してください。";
        }

        if (UnreadableInSearchText(result.UnreadableFiles, result.UnreadableFolders) is { Length: > 0 } unreadable)
        {
            yield return unreadable + "。";
        }
    }

    /// <summary>結果の行の商品名を押したとき（ファイルの行・登録フォルダの行）。</summary>
    private async Task OpenResultItemAsync(string itemId, Action<string> say)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } record)
        {
            _main.ShowItem(record);
            return;
        }

        // 探した後に商品IDを変えた・管理から外した。黙って何も起きないと壊れたように見える
        say("この商品は見つかりませんでした。商品IDを変えたか、管理対象から除外した可能性があります。");
    }
}
