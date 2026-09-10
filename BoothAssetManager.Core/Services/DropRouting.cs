namespace BoothAssetManager.Core.Services;

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

    /// <summary>画像として扱える拡張子。中身を読むのは足す側の仕事で、ここは振り分けだけ。</summary>
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp"];

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

        // 画像でないファイルが混ざっていたら取り込みを採る。
        // **画像そのものが配布物のこともある**ので（BOOTHのダウンロード形式に画像が含まれる）、
        // 画像だから取り込みではない、とは言えない。zipが混ざっているなら
        // 「取り込みたい」意図の方が強い、という判断だけをする
        if (paths is { Count: > 0 } && images.Count != paths.Count)
        {
            return new DropDecision(DropAction.Import, null);
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
