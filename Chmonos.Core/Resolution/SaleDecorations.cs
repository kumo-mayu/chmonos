using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Chmonos.Core.Resolution;

/// <summary>
/// 商品名の販売の飾り（「【期間限定無料】」「8/5(水)まで」「○%OFF」）を落とし、フォルダ名と商品名を比べる（<c>docs/spec/folder-layout.md</c>）。
///
/// ブラウザの拡張機能などは、落とした時点のページの題をフォルダ名にする。題には期限付きの飾りが付いていることがあり、作者は後で外す。
/// 2人目の友人の写しで、飾りの付いたフォルダ157件中133件が今の商品名に戻り、飾りの無い472件で落としすぎたのは3件だった（research §21）。
/// 残った24件は自由な宣伝の言葉（「夏休み後半戦」など）で、決まりでは拾いきれない。
/// </summary>
public static partial class SaleDecorations
{
    /// <summary>
    /// 名前のどこにあっても落とす飾り：期限・値段が数字や「期間」「時間」と組になった言い回し。商品名の中身には出にくい。
    /// 「無料」「フリー」「記念」などの1語は、括弧の中か名前の頭と尻にあるときだけ落とす（<see cref="WordRegex"/>）。
    /// どこでも落とすと「フリース帽子」が「ス帽子」、「記念碑セット」が「碑セット」になった（点検33）
    /// </summary>
    private static readonly Regex[] Sale =
    [
        DateUntilRegex, SanitizedDateUntilRegex, LimitedRegex, PercentOffRegex, PercentJapaneseRegex, YenOffRegex,
    ];

    // 8/5(水)まで・10月14日 23:59まで
    [GeneratedRegex(@"\d{1,2}\s*[/月]\s*\d{1,2}\s*日?\s*(?:[(（]?[月火水木金土日][)）]?)?\s*(?:\d{1,2}\s*[:時]\s*\d{0,2}\s*分?)?\s*(?:まで|迄|~|～)")]
    private static partial Regex DateUntilRegex { get; }

    // Windows で使えない「/」が消えた日付（8/5 → 85）
    [GeneratedRegex(@"\d{1,4}\s*(?:[(（][月火水木金土日][)）])?\s*(?:まで|迄)")]
    private static partial Regex SanitizedDateUntilRegex { get; }

    // 「限定」は前に期間か後ろに値段の語があるときだけ（「限定カラー」のような名前の語を残す）
    [GeneratedRegex(@"(?:\d+\s*(?:時間|日間|日|週間|ヶ月|か月)\s*(?:期間|数量|時間|日数)?|期間|数量)\s*限定\s*(?:無料|価格|セール|公開|配布|販売|特価|割引)?|限定\s*(?:無料|価格|セール|特価|割引)")]
    private static partial Regex LimitedRegex { get; }

    [GeneratedRegex(@"\d+\s*%\s*off", RegexOptions.IgnoreCase)]
    private static partial Regex PercentOffRegex { get; }

    [GeneratedRegex(@"\d+\s*%\s*(?:オフ|引き|割引)")]
    private static partial Regex PercentJapaneseRegex { get; }

    [GeneratedRegex(@"\d+\s*円\s*(?:引き|off)", RegexOptions.IgnoreCase)]
    private static partial Regex YenOffRegex { get; }

    /// <summary>飾りの1語。括弧の中か、名前の頭と尻（区切りで離れている）にあるときだけ落とす。</summary>
    private const string Words = @"(?:無料|フリー|free|sale|セール中?|値下げ中?|割引中?|特価|記念|配布中|公開中|配布|新作)";

    private const string Separators = @"[\s\-_~～・|｜!！★☆♪✨/]";

    /// <summary>
    /// 飾りの語と区切りだけでできていて、飾りの語を1つ以上含むか（括弧の中身に当てる）。
    /// 数字だけの括弧（「帽子(1)」）は番号なので残す（落とすと番号違いの商品が同じ名前になった。点検34）
    /// </summary>
    [GeneratedRegex(@"^(?=.*" + Words + @")(?:" + Separators + @"|\d|" + Words + @")*$", RegexOptions.IgnoreCase)]
    private static partial Regex OnlyWordsRegex { get; }

    /// <summary>頭の飾りの語（後ろが区切りか終わり）。</summary>
    [GeneratedRegex(@"^" + Separators + @"*" + Words + @"(?=" + Separators + @"|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingWordRegex { get; }

    /// <summary>尻の飾りの語（前が区切り）。</summary>
    [GeneratedRegex(@"(?<=" + Separators + @")" + Words + Separators + @"*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingWordRegex { get; }

    /// <summary>括弧ひと組（中に括弧を含まない）。</summary>
    [GeneratedRegex(@"[【\[［(（〈《]([^【\[［(（〈《】\]］)）〉》]*)[】\]］)）〉》]")]
    private static partial Regex BracketRegex { get; }

    /// <summary>中身が飾りの区切りだけになった括弧。</summary>
    [GeneratedRegex(@"[【\[［(（〈《][\s\-_~～・|｜!！]*[】\]］)）〉》]")]
    private static partial Regex EmptyBracketsRegex { get; }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex { get; }

    /// <summary>
    /// 飾りを落とす。括弧と飾りを交互に、変わらなくなるまで落とす（「【8/5(水)まで無料】」は中の曜日だけ先に落ちて、外の括弧が残るため）。
    /// <paramref name="shopName"/> を渡せば、末尾の「 - ショップ名」（商品ページの題に付く）も落とす。何も残らなければ元の名前を返す。
    /// </summary>
    public static string Strip(string name, string? shopName = null)
    {
        var text = name;

        // 4回で足りる：落とすたびに括弧が1重ずつ外れる。飾りの括弧が3重になった名前は見ていない
        for (var round = 0; round < 4; round++)
        {
            var before = text;
            foreach (var sale in Sale)
            {
                text = sale.Replace(text, " ");
            }

            // 中身が飾りの語だけの括弧を落とす（「【無料】」「【FREE】」）
            text = BracketRegex.Replace(text, match => OnlyWordsRegex.IsMatch(match.Groups[1].Value) ? " " : match.Value);
            text = EmptyBracketsRegex.Replace(text, " ");
            text = LeadingWordRegex.Replace(text, " ");
            text = TrailingWordRegex.Replace(text, " ");
            if (text == before)
            {
                break;
            }
        }

        var dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (shopName is not null && dash > 0)
        {
            var tail = Loose(text[(dash + 3)..]);
            var shop = Loose(shopName);
            if (tail.Length > 0 && shop.Length > 0 && (shop.Contains(tail, StringComparison.Ordinal) || tail.Contains(shop, StringComparison.Ordinal)))
            {
                text = text[..dash];
            }
        }

        var cleaned = SpacesRegex.Replace(text, " ").Trim(' ', '-', '_', '~', '～', '・', '|', '｜', '!', '！');
        return cleaned.Length == 0 ? name : cleaned;
    }

    /// <summary>
    /// フォルダ名と商品名が同じ物を指すか。全角半角・大小・空白と記号を揃え、飾りを落として比べる。
    /// 片方が他方を含む（4字以上）も同じと見る：フォルダ名は Windows で使えない字が消え、長い名前は切られている
    /// （拡張機能の一つは120字で切る）。4字未満を含むで当てると、短い名前の商品が何にでも当たる
    /// </summary>
    public static bool SameName(string? itemName, string? folderName, string? shopName = null)
    {
        if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var item = Loose(Strip(itemName));
        var folder = Loose(Strip(folderName, shopName));
        if (item.Length == 0 || folder.Length == 0)
        {
            return false;
        }

        if (item == folder || Loose(itemName) == Loose(folderName))
        {
            return true;
        }

        var (shorter, longer) = item.Length <= folder.Length ? (item, folder) : (folder, item);
        return shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal);
    }

    /// <summary>ショップ名か、サブドメインと同じか（大小・空白と記号を揃える）。</summary>
    public static bool SameShop(string? shopName, string? shopSubdomain, string? folderName)
    {
        var folder = Loose(folderName);
        return folder.Length > 0
            && (Loose(shopName) == folder || string.Equals(shopSubdomain, folderName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>NFKC で揃え、小文字にして、文字と数字だけを残す。</summary>
    public static string Loose(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune) || category == UnicodeCategory.LetterNumber || category == UnicodeCategory.OtherNumber)
            {
                builder.Append(rune.ToString());
            }
        }

        return builder.ToString();
    }
}
