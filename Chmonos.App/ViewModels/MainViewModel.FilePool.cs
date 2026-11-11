namespace Chmonos.App.ViewModels;

/// <summary>主画面：ファイルの割り振りのサブ画面（plan 30）を1つだけ持つ。決め事は docs/spec/ui-subwindows.md。</summary>
public sealed partial class MainViewModel
{
    private FilePoolViewModel? _filePool;

    /// <summary>開いているファイルの割り振り（無ければ null）。試験が中身を見る。</summary>
    internal FilePoolViewModel? FilePool => _filePool;

    /// <summary>
    /// 選んだ商品の間でファイルを割り振るサブ画面を開く。**開くのは1つだけ**：開いていれば選んだ商品を入れ替えて前に出す
    /// （同じ選び方なら入れ替えずに前に出すだけ）。
    /// </summary>
    public void ShowFilePool(IReadOnlyList<string> itemIds)
    {
        if (itemIds.Count == 0)
        {
            return;
        }

        if (_filePool is { } open)
        {
            open.Reopen(itemIds);
            return;
        }

        var model = new FilePoolViewModel(_services, this, itemIds);
        _filePool = model;
        model.Closed += () =>
        {
            if (ReferenceEquals(_filePool, model))
            {
                _filePool = null;
            }
        };
        model.Open();
    }

    /// <summary>
    /// サブ画面で付け外しした商品を、主の窓に映す（API が書いたときと同じ道：検索の写しとカード・開いている商品ページ・ナビの数）。
    /// サブ画面は主の窓の画面を差し替えないので、主の側から読み直さないと古いまま残る。
    /// </summary>
    internal void NoteItemsChangedBySubWindow(IReadOnlyList<string> ids)
    {
        NoteSweptItemsAsync(ids).Forget();
        NoteOpenItemChangedOutsideAsync(ids).Forget();
        RefreshBadges();
    }
}
