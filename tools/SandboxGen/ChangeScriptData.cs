using Chmonos.Core.Models;

/// <summary>
/// 台本の1版ぶん。商品ページ（JSON と HTML）の今の姿。説明文の節は HTML の h2 とその後ろの1行1要素の本文
/// （アプリの読み方：子要素を改行でつなぐ）。節の無い商品は <see cref="Description"/> の改行つきの文だけで比べられる
/// </summary>
internal sealed record ChangeVersion(
    string NameSuffix = "",
    int? Price = null,
    bool EndOfSale = false,
    int ExtraVariations = 0,
    string[]? Description = null,
    (string Heading, string[] Lines)[]? Sections = null)
{
    private static readonly string[] DefaultDescription = ["確かめ用の作り物の商品です。"];

    public string ToJson(ItemRecord item)
    {
        var booth = item.Booth;
        var price = Price ?? booth.Variations[0].Price;
        var variations = Enumerable.Range(0, 1 + ExtraVariations).Select(n => new Dictionary<string, object?>
        {
            ["id"] = booth.Variations[0].Id + n * 1000,
            ["name"] = n == 0 ? "通常版" : $"追加版 {n}",
            ["price"] = price,
            ["status"] = "addable_to_cart",
            ["type"] = "digital",
        }).ToList();
        var json = new Dictionary<string, object?>
        {
            ["id"] = long.Parse(item.Id),
            ["name"] = booth.Name + NameSuffix,
            ["description"] = string.Join("\n", Description ?? DefaultDescription),
            ["is_adult"] = false,
            ["is_end_of_sale"] = EndOfSale,
            ["is_sold_out"] = false,
            ["published_at"] = booth.PublishedAt?.ToString("o"),
            ["price"] = $"¥ {price:N0}",
            ["wish_lists_count"] = booth.WishListsCount,
            ["url"] = booth.Url,
            ["tags"] = booth.Tags,
            ["category"] = new Dictionary<string, object?>
            {
                ["id"] = booth.Category?.Id,
                ["name"] = booth.Category?.Name,
                ["parent"] = new Dictionary<string, object?> { ["name"] = booth.Category?.ParentName },
            },
            ["shop"] = new Dictionary<string, object?> { ["name"] = booth.Shop?.Name, ["subdomain"] = booth.Shop?.Subdomain, ["url"] = booth.Shop?.Url },
            ["images"] = booth.Images.Select(image => new Dictionary<string, object?> { ["original"] = image.OriginalUrl }).ToList(),
            ["variations"] = variations,
        };
        return System.Text.Json.JsonSerializer.Serialize(json);
    }

    public string ToHtml()
    {
        var sections = (Sections ?? []).Select(section =>
            $"<section class=\"shop__text\"><h2>{System.Net.WebUtility.HtmlEncode(section.Heading)}</h2>"
            + string.Concat(section.Lines.Select(line => $"<p>{System.Net.WebUtility.HtmlEncode(line)}</p>"))
            + "</section>");
        var shortText = System.Net.WebUtility.HtmlEncode(string.Join(" ", Description ?? DefaultDescription));
        return $"<html><body><section class=\"main-info-column\"><div class=\"js-market-item-detail-description\">{shortText}</div>"
            + string.Concat(sections) + "</section></body></html>";
    }
}

/// <summary>
/// changes の台本。商品ごとに版の並び（先頭が前の版、続きが順に当てる変更）。見出しは今の見分けに当たる形（【…】・★…★）
/// </summary>
internal static class ChangeScriptData
{
    private const string Avatars = "【対応アバター】";
    private const string Notes = "【注意事項】";
    private const string History = "★更新履歴★";
    private const string Usage = "★ご利用について★";
    private static readonly (string, string[]) Body = ("■ 内容", ["作り物のファイル一式"]);

    public static readonly ChangeVersion[][] Scripts =
    [
        // 1：行を足す・消す・書き換える（同じ見出しの中）。更新履歴の見出しの変化は強い知らせ
        [
            new(Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 乙", "作り物アバター 丙"]), (History, ["v1.0 公開"]), Body]),
            new(Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 乙（Quest 対応）", "作り物アバター 丁"]), (History, ["v1.1 揺れ物の動きを修正", "v1.0 公開"]), Body]),
        ],
        // 2：見出しごと足す・消す
        [
            new(Sections: [(Notes, ["再配布は禁止です", "改変は個人利用のみ可です"]), Body]),
            new(Sections: [(Usage, ["個人・法人での利用が可能です", "クレジット表記は不要です", "R18 の作品での利用は不可です"]), Body]),
        ],
        // 3：名前と価格だけが変わる
        [
            new(Sections: [Body]),
            new(NameSuffix: "【新版】", Price: 3000, Sections: [Body]),
        ],
        // 4：販売終了
        [
            new(Sections: [(Avatars, ["作り物アバター 甲"]), Body]),
            new(EndOfSale: true, Sections: [(Avatars, ["作り物アバター 甲"]), Body]),
        ],
        // 5：見出しの無い説明文（本文全体を行で比べる）
        [
            new(Description: ["確かめ用の作り物の商品です。", "対応：作り物アバター 甲", "対応：作り物アバター 乙", "同梱：説明書"]),
            new(Description: ["確かめ用の作り物の商品です。", "対応：作り物アバター 甲", "対応：作り物アバター 丙", "同梱：説明書", "同梱：予備のテクスチャ"]),
        ],
        // 6：同じ商品に2回続けて変わる（未読の知らせを重ねる確かめ）
        [
            new(Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 乙"]), (History, ["v1.0 公開"]), Body]),
            new(NameSuffix: "（改訂）", Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 乙", "作り物アバター 丙"]), (History, ["v1.1 丙に対応", "v1.0 公開"]), Body]),
            new(NameSuffix: "（改訂）", Price: 2500, Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 丙（調整済み）"]), (History, ["v1.2 乙の対応を終了", "v1.1 丙に対応", "v1.0 公開"]), Body]),
        ],
        // 7：種類（バリエーション）が増え、本文にも1行足す
        [
            new(Sections: [(Avatars, ["作り物アバター 甲"]), Body]),
            new(ExtraVariations: 1, Sections: [(Avatars, ["作り物アバター 甲", "作り物アバター 乙"]), Body]),
        ],
    ];
}
