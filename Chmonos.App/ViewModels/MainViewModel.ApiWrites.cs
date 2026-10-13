namespace Chmonos.App.ViewModels;

/// <summary>主画面：外から使う API が書いた物を画面へ映す（docs/history/headless-core-2026-10-13.md 段3）。</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// API の書き込みを受ける役を差し込む（作るときに1回。主画面はアプリの間1つ）。
    /// 知らせは API の要求のスレッドから来るので、画面のスレッドへ渡す
    /// </summary>
    private void ListenToApiWrites()
    {
        // 編集画面で開いている商品には、外から書かせない（人が後から保存すると API の分が消える。ユーザ判断 2026-10-13）。
        // 要求のスレッドから聞かれるが、読むのは今の画面と商品の ID だけ（画面のスレッドを待つと、画面が API を待つ所で待ち合う）
        _services.IsItemBeingEdited = itemId => (CurrentViewModel as EditViewModel)?.CurrentItemId == itemId;

        _services.ApiWritten += write => RunOnUiThread(() => NoteApiWrite(write));
    }

    private void NoteApiWrite(Chmonos.Api.ApiWrite write)
    {
        switch (write.Kind)
        {
            case "items":
                // 見回りの後と同じ：数件ならその商品だけ差し替え、多ければ全件を読み直す。件数も数え直す
                NoteSweptItemsAsync(write.Ids).Forget();
                break;
            default:
                RefreshCounts();
                break;
        }
    }
}
