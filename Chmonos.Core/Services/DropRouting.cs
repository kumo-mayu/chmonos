namespace Chmonos.Core.Services;

/// <summary>落ちてきた／貼られたものに対して何をするか。</summary>
public enum DropAction
{
    /// <summary>受け取る理由が無い。何もしない。</summary>
    Ignore,

    /// <summary>取り込みの対象に積む。**実行は押してから。**</summary>
    Import,

    /// <summary>その商品を開く。手元にあるので通信は要らない。</summary>
    OpenItem,

    /// <summary>手元に無い商品。取得して登録するかを尋ねる。</summary>
    OfferToRegister,

    /// <summary>ショップのURLだった。そのショップの画面を開く（手持ちが見える方）。</summary>
    OpenShop,

    /// <summary>画像だった。いま開いている商品の画像に足す。</summary>
    AddImageToItem,

    /// <summary>
    /// BOOTH由来の画像だった。**その商品を開きたいのか、この商品の画像に足したいのかが
    /// 落としたものからは決まらない。**そこだけ人に聞く。
    /// </summary>
    AskImageOrItem,

    /// <summary>未確定の画面で、選んでいるファイルの商品IDとして入れて確かめる（ユーザ指示 2026-09-17：画面のどこに落としても）。</summary>
    UseAsItemId,

    /// <summary>改変の詳細を開いているとき、画像を**その改変の写真**に足す（B5）。</summary>
    AddPhotoToModification,

    /// <summary>
    /// 商品ページに zip などを落とした。**いま開いている商品に結ぶのか、いつも通り取り込むのかが決まらない**ので聞く
    /// （ユーザ指示 2026-10-06。作者が同じ物を新しいIDで出し直すと、取り込みでは古い商品へ行く）。
    /// </summary>
    AskAttachOrImport,
}

public readonly record struct DropDecision(
    DropAction Action,
    string? ItemId,
    string? Shop = null,

    /// <summary>BOOTHの画像そのものが落ちてきた場合のURL。取りに行くのは呼ぶ側。</summary>
    string? ImageUrl = null);

/// <summary>
/// ウィンドウに落とされた／貼られたものの行き先を決める。
///
/// **落ちてくるものは2種類しかない**（ファイルかBOOTHのURL）ので、規則も短い。
/// 画面ごとに受けると同じものを落としたのに結果が変わるので、規則は1つに保つ。
///
/// 判断だけをここに置いてあるのは、画面を立ち上げずに確かめられるようにするため。
/// 実際の動き（画面を切り替える・尋ねる）は呼ぶ側が持つ。
/// </summary>
public static class DropRouting
{
    /// <param name="isKnown">その商品IDを既にライブラリが持っているか。</param>
    public static DropDecision Decide(
        IReadOnlyList<string>? paths,
        string? text,
        Func<string, bool> isKnown)
    {
        // ファイルが先。URLとファイルが同時に来ることは無いが、来たならファイルを採る
        // （ファイルは実体で、URLは参照。実体の方が意図がはっきりしている）
        if (paths is { Count: > 0 })
        {
            return new DropDecision(DropAction.Import, null);
        }

        if (BoothItemId.Parse(text) is { } itemId)
        {
            return isKnown(itemId)
                ? new DropDecision(DropAction.OpenItem, itemId)
                : new DropDecision(DropAction.OfferToRegister, itemId);
        }

        // 商品が見つからなければショップを見る。ショップの画面があるので、
        // 「そのショップの手持ち」へ送れる（外のBOOTHへ飛ばすより役に立つ）
        if (BoothItemId.ParseShop(text) is { } shop)
        {
            return new DropDecision(DropAction.OpenShop, null, shop);
        }

        return new DropDecision(DropAction.Ignore, null);
    }

    /// <summary>
    /// 未確定の画面でファイルを選んでいるとき。商品ページは**開かずに、選んだファイルの商品IDとして入れる**——
    /// この画面で商品ページを落とすのは「このファイルはこの商品」と言うためで、持っている商品でも開いて画面を移ると作業が途切れる。
    /// ファイルは今まで通り取り込みに積む。ショップなど商品でない物も今まで通り。
    /// </summary>
    public static DropDecision DecideOnResolve(
        IReadOnlyList<string>? paths,
        string? text,
        Func<string, bool> isKnown)
    {
        if (paths is not { Count: > 0 } && BoothItemId.Parse(text) is { } itemId)
        {
            return new DropDecision(DropAction.UseAsItemId, itemId);
        }

        return Decide(paths, text, isKnown);
    }

    /// <summary>
    /// 改変の詳細を開いているとき。写真の足し先が決まっているので、**画像だけが落ちてきたら**写真に回す。
    /// それ以外（zip・BOOTHのURL）は今まで通りの規則で決める——
    /// 以前は画面が自分で受けていて、改変を見ている間は zip も URL も落とせなかった（B5）。
    /// </summary>
    public static DropDecision DecideOnModification(
        IReadOnlyList<string>? paths,
        string? text,
        bool hasBitmap,
        Func<string, bool> isKnown)
    {
        if (paths is { Count: > 0 } && paths.All(LooksLikeImage))
        {
            return new DropDecision(DropAction.AddPhotoToModification, null);
        }

        if (paths is not { Count: > 0 } && hasBitmap)
        {
            return new DropDecision(DropAction.AddPhotoToModification, null);
        }

        return Decide(paths, text, isKnown);
    }

    /// <summary>画像として扱える拡張子。中身を読むのは足す側の仕事で、ここは振り分けだけ。</summary>
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

    /// <summary>商品に結べるファイルか。取り込む拡張子と同じ（<see cref="Scanning.FolderScanner.TargetExtensions"/>）。フォルダは結べない。</summary>
    public static bool IsAttachable(string path)
        => Scanning.FolderScanner.TargetExtensions.Contains(Path.GetExtension(path));

    /// <summary>拡張子で画像かを見る。</summary>
    public static bool LooksLikeImage(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// **商品ページを開いているとき**の振り分け。足す先が決まっているので、
    /// 画像を「この商品の画像に足す」へ回せる。
    ///
    /// BOOTHの商品ページから絵をドラッグすると、その絵のURL
    /// （<c>booth.pximg.net/.../i/3565798/....jpg</c>）に**商品IDが入っている**。
    /// つまり「その商品を開きたい」のか「この商品の画像に足したい」のかが
    /// 落としたものからは決まらない。**そこだけ人に聞く。**
    ///
    /// 商品ページ以外では聞かない——足す先が決まっていない場所で聞いても答えられない。
    /// </summary>
    /// <param name="hasBitmap">クリップボードに画像そのものが入っているか（スクリーンショット）。</param>
    public static DropDecision DecideOnItemPage(
        IReadOnlyList<string>? paths,
        string? text,
        bool hasBitmap,
        Func<string, bool> isKnown)
    {
        // 画像でないファイルが混ざっていたら、「この商品に結ぶ」か「取り込む」かを聞く（ユーザ指示 2026-10-06）。
        // 作者が同じ物を新しいIDで出し直すと、手掛かりは古いIDを指すので、取り込むと古い商品か未確定へ行く。
        // 商品ページに落とす人は「これはこの商品の物」と言いたいことがあるが、いつもの取り込みのつもりのこともあり、落とした物からは決まらない。
        // **画像そのものが配布物のこともある**ので（BOOTHのダウンロード形式に画像が含まれる）、混ざった画像も一緒に結ぶ・取り込む。
        // フォルダ・取り込まない種類が混ざっていれば結べない（結ぶのはファイルだけ）ので、今まで通り取り込みに積む
        if (paths is { Count: > 0 } && !paths.All(LooksLikeImage))
        {
            return paths.All(IsAttachable)
                ? new DropDecision(DropAction.AskAttachOrImport, null)
                : new DropDecision(DropAction.Import, null);
        }

        var images = paths?.Where(LooksLikeImage).ToList() ?? [];

        // ブラウザから絵をドラッグすると、ファイルではなくURLだけが落ちてくる。
        // それがBOOTHの画像だと分かれば「足す」の選択肢を出せる
        var imageUrl = FindBoothImageUrl(text);
        var hasImage = images.Count > 0 || hasBitmap || imageUrl is not null;

        if (!hasImage)
        {
            // 画像が来ていないなら、今まで通りの規則で決める
            return Decide(paths, text, isKnown);
        }

        // BOOTH由来か。商品IDが読めるなら、どちらの意図かは決まらない
        return BoothItemId.Parse(text) is { } itemId
            ? new DropDecision(DropAction.AskImageOrItem, itemId, null, imageUrl)
            : new DropDecision(DropAction.AddImageToItem, null, null, imageUrl);
    }


    /// <summary>
    /// BOOTHの画像そのもののURLか。
    ///
    /// **ブラウザからの絵のドラッグはファイルにならない。**落ちてくるのはURLだけで、
    /// それが商品ページのURLと見分けが付かないと「その商品を開く」になってしまう。
    /// 画像のURLだと分かれば、「足す」の選択肢を出せる。
    /// </summary>
    public static bool IsBoothImageUrl(string? text)
        => text is not null && BoothImageUrlRegex.IsMatch(text);

    /// <summary>BOOTHの画像URLを取り出す。文章に混ざっていても拾う。</summary>
    public static string? FindBoothImageUrl(string? text)
        => text is null ? null : BoothImageUrlRegex.Match(text) is { Success: true } match ? match.Value : null;

    /// <summary>
    /// BOOTHの画像置き場。ここ以外は取りに行かない——
    /// **このツールがBOOTH以外へ問い合わせる道を作らない。**
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex BoothImageUrlRegex = new(
        @"https?://booth\.pximg\.net/[^\s""'<>\]\)]+",
        System.Text.RegularExpressions.RegexOptions.Compiled
            | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
