using System.IO;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using BoothAssetManager.App.Services;
using BoothAssetManager.Core.Models;

namespace BoothAssetManager.App.ViewModels;

/// <summary>検索画面：カードの操作（開く・コピー・非表示・お気に入り）（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class SearchViewModel
{
    /// <summary>
    /// カードの右クリックから使う操作。
    /// UI要素をカードに増やさずに済ませたいので、出口はここへ集める。
    /// </summary>
    public RelayCommand OpenBoothCommand { get; }

    public RelayCommand OpenShopCommand { get; }

    /// <summary>商品ページのURLをコピーする。人に教えるときに要る。</summary>
    public RelayCommand CopyLinkCommand { get; }

    public RelayCommand EditItemCommand { get; }

    public RelayCommand RevealCommand { get; }

    public RelayCommand HideItemCommand { get; }

    /// <summary>
    /// 検索のカードと同じ中身のカードを作る（フォルダビューの右側で使う・ユーザ指示 2026-09-14「検索画面同等の UI」）。
    /// 札（所持・未編集・取り込み中・見つからない）の決め方を1か所に保つ
    /// </summary>
    /// <remarks>
    /// 渡された記録ではなく、この画面の最新の写しから作る。フォルダビューは開いたときの写しを持っているので、
    /// そのまま作るとその後に付けた星などが古いまま出る（ユーザ指摘 2026-09-14）
    /// </remarks>
    public ItemCardViewModel CreateCard(ItemRecord item)
        => ToCard(_allItems.Find(current => current.Id == item.Id) ?? item);

    /// <summary>
    /// 右クリックのメニューが渡してきた行を、カードとして受け取る。
    /// **カードそのもの**（検索・ショップ・フォルダ）か、**カードを持つ行**（改変のリスト表示・<see cref="IHasItemCard"/>）。
    /// 手元に無い商品の行はカードが無いので null（押せない）
    /// </summary>
    internal static ItemCardViewModel? AsCard(object? parameter)
        => parameter as ItemCardViewModel ?? (parameter as IHasItemCard)?.Card;

    /// <summary>商品ページをブラウザで開く。中クリックからも呼ぶ。</summary>
    public void OpenBooth(ItemCardViewModel? card)
    {
        if (card is not null)
        {
            // BOOTHに無い商品には送り先が無い（押しても何も起きないのが正しい）
            if (Core.Booth.BoothClient.PageUrlFor(card.Item) is { } url)
            {
                Shell.OpenUrl(url);
            }
        }
    }

    /// <summary>
    /// 商品ページのURLをクリップボードへ。
    ///
    /// 人に商品を教えるときに要る。**落とす／貼るの逆向き**で、
    /// このアプリ同士なら受け取った側がそのまま貼って登録できる。
    /// </summary>
    private static void CopyLink(ItemCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        if (Core.Booth.BoothClient.PageUrlFor(card.Item) is not { } url)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(url);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
        }
    }

    /// <summary>ショップはアプリ内の画面へ送る（外のBOOTHではなく、手持ちが見える方）。</summary>
    private void OpenShop(ItemCardViewModel? card)
    {
        var subdomain = card?.Item.Booth.Shop?.Subdomain;
        if (!string.IsNullOrWhiteSpace(subdomain) && _main is not null)
        {
            _main.ShowShopAsync(subdomain).Forget();
        }
    }

    private async Task EditItemAsync(ItemCardViewModel? card)
    {
        if (card is not null && _main is not null)
        {
            await _main.ShowEditAsync([card.Item.Id]);
        }
    }

    /// <summary>
    /// 手元のファイルをエクスプローラで開く。**2つ以上あれば選ばせる**（ユーザ指示 2026-09-19：
    /// 前は最初の1件を黙って開いていて、別のファイルが開いても気付けなかった）
    /// </summary>
    private static void Reveal(ItemCardViewModel? card)
    {
        if (card is not null)
        {
            ItemFileActions.RevealAsync(card.Item).Forget();
        }
    }

    // ---- 右クリックの「開く」「Unity」（ユーザ指示 2026-09-19：Unity が無く、一時展開も見えなかった） ----
    // 中身は商品ページの「開く ▾」「Unity ▾」と同じ道（ItemFileActions・ItemUnityActions）。
    // ファイルや unitypackage が2つ以上あれば、どれにするかを先に選ばせる

    /// <summary>zip を選んで一時フォルダへ展開して開く。</summary>
    public RelayCommand CardUnpackCommand { get; }

    public RelayCommand CardSendToUnityCommand { get; }

    public RelayCommand CardSendToUnityWithRecordCommand { get; }

    public RelayCommand CardSelectInUnityCommand { get; }

    private async Task CardUnityAsync(ItemCardViewModel? card, string title, string okText,
        Func<Core.Services.UnityPackageEntry, Task> run)
    {
        if (card is null)
        {
            return;
        }

        if (await ItemFileActions.PickPackageAsync(card.Item, title, okText) is { } package)
        {
            await run(package);
        }
    }

    /// <summary>
    /// カードには結果を出す行が無いので、選択・改変に足した結果は窓で言う。
    /// **できなかったことは ⚠ で出す**（D8：既定のまま ℹ で出していて、失敗が情報の顔をしていた）。
    /// </summary>
    private static void Tell(string title, string text, bool failed)
    {
        // 「調べています…」のような途中の文と、空の文（入っていなかった・続きを言う合図）は窓にしない
        if (text.Length == 0 || text.EndsWith('…'))
        {
            return;
        }

        // 選択の結果は Unity を手前に出した後に来るので、主の窓を戻してから言う（Unity の後ろに隠れていた）
        FrontNotice.Show(text, title, System.Windows.MessageBoxButton.OK,
            failed ? System.Windows.MessageBoxImage.Warning : System.Windows.MessageBoxImage.Information);
    }

    /// <summary>
    /// 検索とショップの件数から外す。設定画面から戻せるので確認は挟まない。
    /// </summary>
    /// <summary>
    /// お気に入りの星を切り替える（#70・ユーザ指示「searchのitem要素で空いている下の方に星のトグル」）。
    ///
    /// 星だけを名指しして書く。カードが抱えているのは前回の読み込み時の写しで、
    /// 丸ごと書き戻すとその間に取り込みや検出が入れた項目まで古い値に戻る。
    /// 一覧ごと読み直さないのは、星1つのためにスクロール位置や並びを崩さないため。
    /// </summary>
    public async Task ToggleFavoriteAsync(ItemCardViewModel card)
    {
        var next = !card.IsFavorite;
        card.IsFavorite = next;

        var result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SaveItemLocal(
            card.Item.Id, card.Item.Local with { IsFavorite = next }, LocalOwners.Favorite));

        if (result is Core.Commands.CommandResult.Failed)
        {
            // 書けなかったら戻す。付いたように見えて次に開くと消えている、を起こさない
            card.IsFavorite = !next;
            return;
        }

        // 写し・この画面のカード・「お気に入り」の絞り込みをまとめて直す（商品ページの星と同じ道）。
        // 押されたカードがフォルダビューの物だと、この画面のカードは別の物なので、前は検索に戻っても古い星が出ていた（ユーザ指摘 2026-09-14）
        NoteFavoriteChanged(card.Item.Id, next);
    }

    private async Task HideItemAsync(ItemCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        // カードが抱えているのは前回の読み込み時の写しなので、非表示だけを名指しして書く。
        // 丸ごと書き戻すと、その間に取り込みや検出が入れた項目まで古い値に戻る
        await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.SaveItemLocal(
            card.Item.Id,
            card.Item.Local with { IsHidden = true },
            LocalOwners.Visibility));

        await ReloadAsync();
    }
}
