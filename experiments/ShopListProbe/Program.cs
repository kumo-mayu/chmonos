// ShopListProbe
//
// ショップの商品一覧（<サブドメイン>.booth.pm/items?page=N）を全ページ取り、商品IDと商品名の一覧を控えに書く。
// 置き方（ショップ名\商品名\ファイル）を人が明示したとき、フォルダ名をショップの一覧と照らせば ID が決まるかを測るため
// （2026-10-12。BOOTH 全体の検索だと、ファイル名向けの検索語の整え方や記号入りの名前で外れる）。照らすのは別の台本。
//
// 一覧の各商品は <li class="js-mount-point-shop-item-card" data-item="{…}"> の JSON に id と name を持つ。
// 商品の無いページか、次のページへのリンクが無いページで止める。
//
// 通信は BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。取ったショップは控え（<控え>/<サブドメイン>.json）にあれば取り直さない。
//
// 使い方：ShopListProbe <サブドメインを1行ずつ書いたファイル> <控えの置き場>

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length != 2)
{
    Console.WriteLine("使い方: ShopListProbe <サブドメインの一覧> <控えの置き場>");
    return 1;
}

var subdomains = File.ReadAllLines(args[0]).Select(line => line.Trim().ToLowerInvariant())
    .Where(line => Regex.IsMatch(line, "^[a-z0-9][a-z0-9-]*$")).Distinct().ToList();
Directory.CreateDirectory(args[1]);
using var http = BoothClient.CreateHttpClient();
var client = new BoothClient(http, new AppSettings());
var cardRegex = new Regex("data-item=\"([^\"]*)\"");
var requests = 0;
var failed = 0;

foreach (var subdomain in subdomains)
{
    var cachePath = Path.Combine(args[1], subdomain + ".json");
    if (File.Exists(cachePath))
    {
        continue;
    }

    var items = new List<ShopItem>();
    var complete = true;

    // 1ショップの上限：大きなショップでも取り切れる数（1ページ数十件 × 50）。暴走したときの止め
    for (var page = 1; page <= 50; page++)
    {
        requests++;
        var answer = await client.GetTextUntilAsync($"https://{subdomain}.booth.pm/items?page={page}", _ => false, 4 * 1024 * 1024);
        if (!answer.IsSuccess || answer.Value is not { } html)
        {
            complete = false;
            break;
        }

        var cards = cardRegex.Matches(html)
            .Select(match => JsonDocument.Parse(WebUtility.HtmlDecode(match.Groups[1].Value)).RootElement)
            .Where(card => card.TryGetProperty("id", out _) && card.TryGetProperty("name", out _))
            .Select(card => new ShopItem(card.GetProperty("id").ToString(), card.GetProperty("name").GetString() ?? string.Empty))
            .ToList();
        items.AddRange(cards);
        if (cards.Count == 0 || !html.Contains($"page={page + 1}", StringComparison.Ordinal))
        {
            break;
        }
    }

    if (!complete)
    {
        failed++;
        continue;
    }

    File.WriteAllText(cachePath, JsonSerializer.Serialize(items.DistinctBy(item => item.Id).ToList()));
}

Console.WriteLine($"ショップ {subdomains.Count}・取ったページ {requests}・取れなかったショップ {failed}");
return 0;

sealed record ShopItem(string Id, string Name);
