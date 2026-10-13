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
        _services.ApiWritten += write => RunOnUiThread(() => NoteApiWrite(write));
    }

    private void NoteApiWrite(Chmonos.Api.ApiWrite write)
    {
        // 種類ごとに、映している物を作り直す。前は商品の差し替えと件数だけで、「最近」の絞り込みと並び・タグや属性の候補・
        // 開いている商品ページが古いままだった（点検37）
        switch (write.Kind)
        {
            case "items":
                // 見回りの後と同じ：数件ならその商品だけ差し替え、多ければ全件を読み直す。件数も数え直す
                NoteSweptItemsAsync(write.Ids).Forget();
                NoteOpenItemChangedOutsideAsync(write.Ids).Forget();
                break;
            case "recent":
                Search.NoteMaybeRecentChanged();
                RefreshCounts();
                break;
            case "modifications":
                // 改変の画面は、窓が手前に戻ったときと同じく全部を読み直す（記録・アバター・プロジェクト）
                (CurrentViewModel as ModificationHubViewModel)?.NoteWindowActivated();
                RefreshCounts();
                break;
            case "userTags" or "attributes":
                Search.RefreshFacets();
                RefreshCounts();
                break;
            default:
                RefreshCounts();
                break;
        }
    }

    /// <summary>開いている商品ページ（フォルダビュー・改変の画面に組み込んだ物も）の商品が外で書き換わったら、読み直して映す。</summary>
    private async Task NoteOpenItemChangedOutsideAsync(IReadOnlyList<string> ids)
    {
        if ((CurrentItemPage ?? (CurrentViewModel as IEmbeddedItemPageHost)?.EmbeddedItemPage) is not { } page
            || !ids.Contains(page.Item.Id))
        {
            return;
        }

        if (await _services.Store.Items.LoadAsync(page.Item.Id) is { } updated)
        {
            page.NoteChangedOutside(updated);
        }
    }
}
