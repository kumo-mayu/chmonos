namespace BoothAssetManager.Core.Models;

/// <summary>
/// 画面に出す文字の作り方を1箇所に集めたもの。
///
/// **同じ概念が二箇所にあると、片方だけ直して食い違う。**実際に起きている：
/// 容量の整形は7つのViewModelに写され、購入の種類は
/// 「自分用」と「購入」に割れ、名前・ショップ・分類の決め方は
/// 商品ページと編集画面で別々に書かれて中身がずれた。
///
/// ここに集めると、**二箇所が食い違えない形**になり、Coreのテスト網にも入る。
/// ViewModelにはテストが無いので、この差は大きい。
/// </summary>
public static class DisplayText
{
    private static readonly string[] SizeUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>容量。1024で繰り上げ、小数は1桁まで（「188.1 MB」）。</summary>
    public static string Size(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < SizeUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {SizeUnits[unit]}";
    }

    /// <summary>
    /// 購入の種類を**名詞として**出す（「自分用」）。
    /// JSONに書いてある語と同じにする——人がJSONを開いて読む前提なので、
    /// 画面と保存で語が違うと同じものだと分からなくなる。
    /// </summary>
    public static string PurchaseKindLabel(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Received => "貰った",
        PurchaseKind.Given => "贈った",
        _ => "自分用",
    };

    /// <summary>
    /// 購入の種類を**文の中に入る形**で出す（「¥480 で買った」）。
    ///
    /// 「自分用」は文に入らないので動詞にする。3つとも過去形で揃える——
    /// 「¥480 で購入」と「¥0 で貰った」が混ざると、同じ欄なのに語調が変わる。
    /// </summary>
    public static string PurchaseKindVerb(PurchaseKind kind) => kind switch
    {
        PurchaseKind.Received => "貰った",
        PurchaseKind.Given => "贈った",
        _ => "買った",
    };

    /// <summary>
    /// 種類の行の名前。**null は「どの種類も指していない」購入。**
    /// BOOTHから取れない商品には種類が1件も無く、
    /// 種類ごとの販売終了でも指す先が消える。
    ///
    /// **番号をそのまま出さない。**以前は <c>variation {id}</c> を返していて、
    /// 英語の内部の言葉が画面に出ていた。番号は人が読んで意味が取れないので、
    /// 名前が引けないことだけを言う（BOOTH側から消えた種類がこれになる）。
    /// </summary>
    public static string VariationLabel(long? variationId)
        => variationId is not null ? "名前の分からない種類" : "種類を選ばない購入";

    /// <summary>
    /// **ユーザが入れたものを優先する**という決まり。名前・ショップ・分類で共通。
    /// 空白だけの入力は「入れていない」として扱う。
    /// </summary>
    public static string? Prefer(string? userValue, string? observed)
        => string.IsNullOrWhiteSpace(userValue) ? observed : userValue.Trim();

    /// <summary>
    /// 画面に出す商品名。ユーザの入力 → BOOTHの名前 → 商品ID の順。
    ///
    /// 最後が商品IDなのは、名前の場所を空にしないため。
    /// 仮IDの商品で名前を消すと <c>local-3f9c1b7e</c> が出るが、
    /// それは「名前が無い」ことが見えている方が正しい。
    /// </summary>
    public static string ItemName(string? userValue, string? boothName, string itemId)
        => Prefer(userValue, boothName) is { Length: > 0 } name ? name : itemId;

    /// <summary>ショップ名。ユーザの入力 → BOOTHのショップ名。</summary>
    public static string ShopName(string? userValue, string? boothShopName)
        => Prefer(userValue, boothShopName) ?? string.Empty;

    /// <summary>
    /// 分類。親が分かれば「親 / 子」、分からなければ子だけ。
    /// ユーザには子の名前しか入れさせないので、親は呼ぶ側が表から引いて渡す。
    /// </summary>
    public static string CategoryText(string? childName, string? parentName)
    {
        if (string.IsNullOrWhiteSpace(childName))
        {
            return string.Empty;
        }

        var child = childName.Trim();
        return string.IsNullOrWhiteSpace(parentName) ? child : $"{parentName.Trim()} / {child}";
    }
}
