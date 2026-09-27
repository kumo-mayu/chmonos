namespace BoothAssetManager.App.ViewModels;

/// <summary>
/// 取り込み元・監視フォルダを外した知らせ（ユーザ判断 2026-09-27・動線の洗い出し B4）。
/// 外すのは確かめずに行う（足し直せば戻り、フォルダとファイルには触らない）。その代わり、
/// 外した直後に下の帯で「何を外したか」とその場で戻すボタンを出す（非表示の帯と同じ作法）。
/// 設定と取り込み画面の両方から来るので、帯は主画面が1本だけ持つ。新しく外したら前の知らせは置き換える
/// </summary>
public sealed partial class MainViewModel
{
    private Func<Task>? _undoFolderRemoval;
    private string _folderRemovedText = string.Empty;
    private string _folderRemovedUndoText = string.Empty;
    private RelayCommand? _undoFolderRemovalCommand;
    private RelayCommand? _dismissFolderRemovedCommand;

    public bool HasFolderRemovedNotice => _undoFolderRemoval is not null;

    public string FolderRemovedText => _folderRemovedText;

    /// <summary>戻すボタンの名前。何を戻すかを書く（ui-terms.md：裸の「元に戻す」は使わない）。</summary>
    public string FolderRemovedUndoText => _folderRemovedUndoText;

    public RelayCommand UndoFolderRemovalCommand =>
        _undoFolderRemovalCommand ??= new RelayCommand(() => UndoFolderRemovalAsync().Forget());

    public RelayCommand DismissFolderRemovedCommand =>
        _dismissFolderRemovedCommand ??= new RelayCommand(() => SetFolderRemoved(null, string.Empty, string.Empty));

    /// <summary>外せたあとに呼ぶ。<paramref name="undo"/> は設定へ1件足し直す処理（外した画面が開いていれば行も戻す）。</summary>
    internal void NoteFolderRemoved(string text, string undoText, Func<Task> undo) => SetFolderRemoved(undo, text, undoText);

    private async Task UndoFolderRemovalAsync()
    {
        var undo = _undoFolderRemoval;
        SetFolderRemoved(null, string.Empty, string.Empty);
        if (undo is not null)
        {
            await undo();
        }
    }

    private void SetFolderRemoved(Func<Task>? undo, string text, string undoText)
    {
        _undoFolderRemoval = undo;
        _folderRemovedText = text;
        _folderRemovedUndoText = undoText;
        OnPropertyChanged(nameof(HasFolderRemovedNotice));
        OnPropertyChanged(nameof(FolderRemovedText));
        OnPropertyChanged(nameof(FolderRemovedUndoText));
    }
}
