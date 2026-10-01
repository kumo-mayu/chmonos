namespace Chmonos.App.ViewModels;

/// <summary>改変の画面：裏の取得が置いた画像を、右に組み込んだ物へ渡す（洗い出し 6）</summary>
public sealed partial class ModificationHubViewModel : IItemImagesListener
{
    /// <summary>
    /// 組み込んだ商品ページ・改変の画面は主画面から見えない（今の画面はこちら）ので、自分が渡す。
    /// 渡さないと、取得の最中に開いた商品の絵が空のまま残る
    /// </summary>
    void IItemImagesListener.NoteItemImagesSaved(string itemId)
    {
        switch (Detail)
        {
            case HubItemDetail { Page: { } page }:
                page.NoteImagesSaved(itemId);
                break;
            case IItemImagesListener listener:
                listener.NoteItemImagesSaved(itemId);
                break;
        }
    }
}
