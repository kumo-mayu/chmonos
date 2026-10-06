using System.IO;
using Chmonos.App.Services;
using Chmonos.Core.Models;

namespace Chmonos.App.ViewModels;

/// <summary>主画面：落とされた物の振り分け（技術的負債 4-1：画面のクラスを関心ごとのファイルに分けた。中身は変えていない）</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// ウィンドウに落とされた／貼り付けられたものを振り分ける。
    ///
    /// 受け口をウィンドウ1つにしているのは、**落ちてくるものが2種類しか無い**から。
    /// 画面ごとに受けると、同じものを落としたのに画面によって結果が変わる。
    ///
    /// **勝手に処理を始めない。**ファイルは取り込みの対象に積むだけで、実行は押してから。
    /// 持っていない商品のURLは、外部への通信を伴うので必ず尋ねる。
    /// </summary>
    public async Task HandleDropAsync(IReadOnlyList<string>? paths, string? text, bool hasBitmap = false)
    {
        // 判断は Core 側の規則に任せる。画面を立ち上げずに確かめられるようにするため。
        //
        // 商品ページを開いているときだけ規則が変わる。**足す先が決まっているから**——
        // 決まっていない場所で「この商品の画像に足しますか」と聞いても答えられない
        // 編集画面も同じ（ユーザ判断：画像の追加などは商品ページと同等。落とす・貼るも含む）
        // 未確定の画面でファイルを選んでいるときは、商品ページを「そのファイルの商品ID」として受ける（ユーザ指示 2026-09-17：
        // 前は商品IDの入力欄の上でしか受けず、欄の外に落とすと商品ページへ移っていた）
        var resolve = CurrentViewModel as ResolveViewModel;

        // 改変の詳細も同じ道を通す（B5）。以前は改変の画面が自分で受けていて、
        // 改変を見ている間は zip も BOOTH の URL も落とせなかった
        var modification = CurrentModification;
        var decision = CurrentItemPage is not null
            ? Core.Services.DropRouting.DecideOnItemPage(paths, text, hasBitmap, _services.Store.Items.Exists)
            : modification is not null
                ? Core.Services.DropRouting.DecideOnModification(paths, text, hasBitmap, _services.Store.Items.Exists)
                : resolve is { HasSelection: true }
                    ? Core.Services.DropRouting.DecideOnResolve(paths, text, _services.Store.Items.Exists)
                    : Core.Services.DropRouting.Decide(paths, text, _services.Store.Items.Exists);

        switch (decision.Action)
        {
            case Core.Services.DropAction.UseAsItemId:
                resolve?.AcceptDroppedItemId(decision.ItemId!);
                return;

            case Core.Services.DropAction.AddPhotoToModification when modification is not null:
                if (paths is { Count: > 0 })
                {
                    await modification.AddImageFilesAsync(paths);
                }
                else if (hasBitmap && ReadClipboardImage() is { } photo)
                {
                    await modification.PasteImageAsync(photo);
                }

                return;

            case Core.Services.DropAction.AddImageToItem:
                await AddDroppedImagesAsync(paths, hasBitmap, decision.ImageUrl);
                return;

            case Core.Services.DropAction.AskImageOrItem:
                await AskImageOrItemAsync(decision.ItemId!, paths, hasBitmap, decision.ImageUrl);
                return;

            case Core.Services.DropAction.AskAttachOrImport:
                await AskAttachOrImportAsync(paths!);
                return;

            case Core.Services.DropAction.Import:
                // 画面は移さない（点検 2026-09-23・動線の点検 A2）。前は取り込み画面へ移り、見ていた画面が勝手に替わった。
                // 登録で画面を移さなくした（Q4）のと同じく、下の帯に進み具合と「取り込み画面を開く」を出す
                // 落としたらそのまま始める（#38。設定で切れる）
                var startNow = _services.Settings.StartImportOnDrop;
                Import.AddDroppedPaths(paths!, startImmediately: startNow);
                NoteImportQueued(startNow);
                return;

            case Core.Services.DropAction.OpenItem:
                if (await _services.Store.Items.LoadAsync(decision.ItemId!) is { } owned)
                {
                    ShowItem(owned);
                }

                return;

            case Core.Services.DropAction.OfferToRegister:
                await OfferToRegisterAsync(decision.ItemId!);
                return;

            case Core.Services.DropAction.OpenShop:
                await ShowShopAsync(decision.Shop!);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// 落とした／貼った画像を、いま開いている商品に足す。
    ///
    /// ファイルとクリップボードの絵で、足したあとの流れは同じにしてある。
    /// </summary>
    private async Task AddDroppedImagesAsync(
        IReadOnlyList<string>? paths,
        bool hasBitmap,
        string? imageUrl = null)
    {
        if (CurrentItemPage is not { } item)
        {
            return;
        }

        if (paths is { Count: > 0 })
        {
            await item.AddImageFilesAsync(paths.Where(Core.Services.DropRouting.LooksLikeImage).ToList());
            return;
        }

        if (hasBitmap && ReadClipboardImage() is { } bytes)
        {
            await item.AddImageBytesAsync(bytes);
            await item.ReloadGalleryAsync();
            return;
        }

        // ブラウザからの絵はURLだけで落ちてくる。取りに行く。
        // **BOOTHの画像置き場だけ**（DropRouting が確かめている）で、
        // 人が押した操作なので他の取得より先に出る
        if (imageUrl is not null)
        {
            // 優先度（指名された画像）は CommandHandler の中で掛ける
            var fetched = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.FetchBoothImage(imageUrl));
            if (fetched is not Core.Commands.CommandResult.ImageFetched { Bytes: var bytesFromBooth })
            {
                // 文は取ってきた側が作る（E3：届かなかったのか、もう無いのかで次の一手が違う）
                Services.Notice.Show(
                    fetched is Core.Commands.CommandResult.Failed failure
                        ? failure.Message
                        : "BOOTHから画像を取れませんでした。通信を確かめて、少し待ってからもう一度お試しください。",
                    "画像を追加",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            await item.AddImageBytesAsync(bytesFromBooth);
            await item.ReloadGalleryAsync();
        }
    }

    /// <summary>
    /// BOOTH由来の画像を受け取ったとき。
    ///
    /// **その商品を開きたいのか、この商品の画像に足したいのかは決まらない。**
    /// BOOTHの商品ページから絵をドラッグすると、その絵のURLに商品IDが入っているので、
    /// 落としたものだけからは意図が読めない。ここだけ人に聞く。
    /// </summary>
    private async Task AskImageOrItemAsync(
        string itemId,
        IReadOnlyList<string>? paths,
        bool hasBitmap,
        string? imageUrl = null)
    {
        if (CurrentItemPage is not { } item)
        {
            return;
        }

        var name = await _services.Store.Items.LoadAsync(itemId) is { } known
            ? $"「{known.DisplayName}」"
            : $" {itemId} ";

        // 「はい／いいえ」は本文と対応を覚えないと押せない。ボタンに何が起きるかを名乗らせる（#18・ユーザ指摘）
        var answer = Views.ChoiceDialog.Ask(
            "BOOTHの画像を受け取りました",
            "この画像をどうしますか？",
            $"「商品を開く」\n商品{name}のページへ移ります\n\n"
            + $"「画像として追加」\nいま開いている「{item.Name}」の画像に加えます",
            "商品を開く",
            "画像として追加");

        switch (answer)
        {
            case Views.ChoiceDialogResult.First:
                await OpenOrOfferAsync(itemId);
                return;

            case Views.ChoiceDialogResult.Second:
                await AddDroppedImagesAsync(paths, hasBitmap, imageUrl);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// 商品ページに zip などを落としたとき（ユーザ指示 2026-10-06）。
    ///
    /// **この商品に紐付けたいのか、いつも通り取り込みたいのかは決まらない。**作者が同じ物を新しいIDで出し直すと、
    /// 手掛かりは古いIDを指すので、取り込むと古い商品か未確定へ行く。前はいつも取り込みに積んでいた。
    /// 画像を落としたときの聞き方（<see cref="AskImageOrItemAsync"/>）と同じに、ボタンに何が起きるかを名乗らせる。
    /// </summary>
    private async Task AskAttachOrImportAsync(IReadOnlyList<string> paths)
    {
        if (CurrentItemPage is not { } item)
        {
            return;
        }

        var answer = ChoiceQuestion.Ask(AttachOrImportQuestion(item.Name, paths));
        switch (answer)
        {
            case Views.ChoiceDialogResult.First:
                await AttachDroppedAsync(item, paths);
                return;

            case Views.ChoiceDialogResult.Second:
                var startNow = _services.Settings.StartImportOnDrop;
                Import.AddDroppedPaths(paths, startImmediately: startNow);
                NoteImportQueued(startNow);
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// 「この商品に紐付ける」を選んだ後。**混ざっていた画像は、商品の画像にしたいのか、配布物として紐付けたいのかが決まらない**
    /// （ユーザ判断 2026-10-06・L80 の 14：BOOTH のダウンロード形式に画像が含まれることがあるので紐付ける道は残し、画像として追加するかも聞く）。
    /// 画像が何枚あっても1回だけ聞き、全部に同じ答えを当てる（1枚ずつ聞くと、画像を何枚も落とした人に何度も窓が出る）。
    /// キャンセルは何もしない——zip だけ紐付けて画像を黙って捨てると、落とした物の一部が消えたように見える
    /// </summary>
    private async Task AttachDroppedAsync(ItemViewModel item, IReadOnlyList<string> paths)
    {
        var images = paths.Where(Core.Services.DropRouting.LooksLikeImage).ToList();
        if (images.Count == 0)
        {
            await item.AttachFilesAsync(paths);
            return;
        }

        switch (ChoiceQuestion.Ask(ImagesOnAttachQuestion(item.Name, images)))
        {
            case Views.ChoiceDialogResult.First:
                await item.AttachFilesAsync(paths.Except(images).ToList());
                await item.AddImageFilesAsync(images);
                return;

            case Views.ChoiceDialogResult.Second:
                await item.AttachFilesAsync(paths);
                return;

            // キャンセルは画像だけを飛ばす。ほかのファイルは1つ目の問いで「紐付ける」と答えてもらっているので、
            // 画像の問いをやめたことでそちらまで取り消さない（ユーザ判断 2026-10-06）
            default:
                var others = paths.Except(images).ToList();
                if (others.Count > 0)
                {
                    await item.AttachFilesAsync(others);
                }

                return;
        }
    }

    /// <summary>紐付けるファイルに混ざっていた画像を、商品の画像にするかファイルとして紐付けるかの問い。</summary>
    internal static ChoiceRequest ImagesOnAttachQuestion(string itemName, IReadOnlyList<string> images)
        => new(
            "画像も受け取りました",
            images.Count == 1 ? $"画像「{Path.GetFileName(images[0])}」をどうしますか？" : $"画像 {images.Count} 件をどうしますか？",
            $"「画像として追加」\n「{itemName}」の画像に加えます。ほかのファイルは紐付けます。\n\n"
            + "「ファイルとして紐付ける」\nほかのファイルと一緒に、この商品のファイルに加えます。\n\n"
            + "「キャンセル」\n画像は加えません。ほかのファイルは紐付けます。",
            "画像として追加",
            "ファイルとして紐付ける");

    /// <summary>商品ページに落としたファイルを、紐付けるか取り込むかの問い。</summary>
    internal static ChoiceRequest AttachOrImportQuestion(string itemName, IReadOnlyList<string> paths)
        => new(
            "ファイルを受け取りました",
            paths.Count == 1 ? $"「{Path.GetFileName(paths[0])}」をどうしますか？" : $"{paths.Count} 件のファイルをどうしますか？",
            $"「この商品に紐付ける」\nいま開いている「{itemName}」のファイルに加えます。\n\n"
            + "「取り込む」\nいつもの取り込みに加えます。ファイルの情報から商品を探します。",
            "この商品に紐付ける",
            "取り込む");

    /// <summary>手元にあれば開き、無ければ登録するか尋ねる。落としたURLと同じ扱い。</summary>
    private async Task OpenOrOfferAsync(string itemId)
    {
        if (await _services.Store.Items.LoadAsync(itemId) is { } owned)
        {
            ShowItem(owned);
            return;
        }

        await OfferToRegisterAsync(itemId);
    }

    /// <summary>
    /// クリップボードの絵をPNGの生データにする。
    ///
    /// スクリーンショットは**ファイルではなく絵そのもの**で置かれるので、
    /// パス経由では受け取れない。ここで一度PNGに固めてから、
    /// 足す側でBOOTHと同じ圧縮を通す。
    /// </summary>
    private static byte[]? ReadClipboardImage()
    {
        try
        {
            if (System.Windows.Clipboard.GetImage() is not { } source)
            {
                return null;
            }

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception exception)
            when (exception is System.Runtime.InteropServices.ExternalException or NotSupportedException)
        {
            // 他のアプリがクリップボードを掴んでいることがある。次に押せば入る
            return null;
        }
    }

    /// <summary>
    /// 落としたURLの商品を登録している数。続けて落とすと順番に並ぶので、1つの帯に数でまとめる
    /// （登録ごとに帯を足すと画面の下が何段も積み上がる）。
    /// </summary>
    private int _droppedRegistering;

    public bool IsRegisteringDropped => _droppedRegistering > 0;

    public string DroppedRegisteringText => _droppedRegistering > 1
        ? $"{_droppedRegistering} 件の商品を登録しています…"
        : "商品を登録しています…";

    private void BeginDroppedRegistering()
    {
        _droppedRegistering++;
        NotifyDroppedRegistering();
    }

    private void EndDroppedRegistering()
    {
        _droppedRegistering = Math.Max(0, _droppedRegistering - 1);
        NotifyDroppedRegistering();
    }

    private void NotifyDroppedRegistering()
    {
        OnPropertyChanged(nameof(IsRegisteringDropped));
        OnPropertyChanged(nameof(DroppedRegisteringText));
    }

    /// <summary>
    /// 手元に無い商品のURLを受けたとき。
    ///
    /// この経路が、**贈答品や気になっている未購入品を登録する道**にもなる。
    /// ファイルが手元に来ないものは取り込みからは入らないので、ここが唯一の入口。
    /// </summary>
    private async Task OfferToRegisterAsync(string itemId)
    {
        var answer = Services.Notice.Show(
            $"商品 {itemId} はライブラリにありません。\n\n"
                + "BOOTHから情報を取得して、ファイルを持たない商品として登録しますか？",
            "BOOTHのURLを受け取りました",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (answer != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        // 登録は BOOTH の順番を待つ（1本ずつ・取り込み中は今の1本の後ろ）ので、「はい」を押してから数秒かかる。
        // 前は済むまで何も出ず、押せたのかも分からなかった（洗い出し 12。未確定の登録の進み具合 bbefef4 と同じ直し）。
        // 窓を閉じた後は別の画面へ移れるので、どの画面でも見える下の帯に出す
        Core.Commands.CommandResult result;
        BeginDroppedRegistering();
        try
        {
            result = await _services.Commands.ExecuteAsync(new Core.Commands.UiCommand.RegisterItem(itemId));
        }
        catch (Exception exception)
        {
            // 落とした操作は裏で待つので、例外を投げっぱなしにすると帯が「登録しています…」のまま残り、理由も出ない
            Core.Diagnostics.AppLog.Error("落としたURLの商品の登録", exception);
            Services.Notice.Show(
                Core.Services.FailureText.Cause(exception),
                "登録できませんでした",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            return;
        }
        finally
        {
            EndDroppedRegistering();
        }

        if (result is Core.Commands.CommandResult.Failed failure)
        {
            Services.Notice.Show(
                failure.Message,
                "登録できませんでした",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);

            return;
        }

        await ReloadLibraryAsync();

        // 登録は BOOTH の順番を待つので、終わる頃には別の画面を見ていることがある。そこで商品ページへ移ると、
        // 検索を進めていた画面が勝手に替わる（ユーザ指摘 2026-09-15）。移らずに下の帯で開くかを聞く
        if (await _services.Store.Items.LoadAsync(itemId) is { } added)
        {
            NoteRegistered(added);
        }
    }
}
