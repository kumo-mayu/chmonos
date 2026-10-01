// FetchTimingProbe
//
// ①（商品の情報＝JSON）と②（商品ページ＝HTML）で、1件あたりにかかる時間が違うかを測る。
//
// **なぜ測るか**：取り込み画面の「編集できるまで」は「残りの問い合わせの数 × 1件あたりの実測」で出しており、
// その実測は段をまたいで1本しか持っていない（ImportViewModel の _requestSeconds）。
// ①の間は JSON の実測しか入らないので、②が JSON より重ければ見込みが少なめに出る。
// ただし取り込みの時間のほとんどは間隔（1.5秒以上）を待つ時間なので、
// **どちらの応答も1.5秒未満なら、1件あたりはどちらも1.5秒になり、差は存在しない。**
// 思い込みで直す前に、その差が在るのか無いのかをはっきりさせる。
//
// 測り方は画面と揃える：同じ段を続けて叩いたときの**1件あたりの時間**。
// ゲート待ちは BoothClient の中にあるので外からは切り分けられない。画面が測っているのも同じ「ゲート待ち＋応答」。
//
// 通信は BoothClient を通すので、必ず1本ずつ・1.5秒以上空けて出る。
//
// 使い方: dotnet run --project experiments/FetchTimingProbe -- <保存先> [件数]
//   商品IDは保存先の items から読むだけで、画面にも控えにも出さない（第三者のデータのため）。

using System.Diagnostics;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 1)
{
    Console.WriteLine("使い方: FetchTimingProbe <保存先> [件数]");
    return 1;
}

var store = args[0];
var take = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 15;

var itemsDir = Path.Combine(store, "items");
if (!Directory.Exists(itemsDir))
{
    Console.WriteLine($"items が無い: {itemsDir}");
    return 1;
}

// 数字のIDだけ（local-… は BOOTH に無い）
var ids = Directory.EnumerateFiles(itemsDir, "*.json")
    .Select(Path.GetFileNameWithoutExtension)
    .Where(id => id is not null && id.All(char.IsDigit))
    .Select(id => id!)
    .Order(StringComparer.Ordinal)
    .Take(take)
    .ToList();

if (ids.Count == 0)
{
    Console.WriteLine("BOOTH の商品IDが1件も無い");
    return 1;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());

Console.WriteLine($"BOOTHへの間隔: {client.CurrentIntervalMs}ms（1本ずつ）");
Console.WriteLine($"測る件数: {ids.Count} 件 × 2段 = {ids.Count * 2} リクエスト");
Console.WriteLine("商品IDは出しません。\n");

static string Stat(List<double> v)
    => v.Count == 0 ? "—" : $"平均 {v.Average():F2}s / 中央 {v.Order().ElementAt(v.Count / 2):F2}s / 最長 {v.Max():F2}s";

static string Line(string label, List<double> perItem, List<int> sizes)
{
    var kb = sizes.Count == 0 ? 0 : sizes.Average() / 1024.0;
    return $"{label}{Environment.NewLine}  1件あたり（ゲート待ち＋応答）: {Stat(perItem)}{Environment.NewLine}  1件の大きさ: 平均 {kb:F0} KB";
}

async Task<(List<double> PerItem, List<int> Sizes)> MeasureAsync(
    string label, Func<string, Task<BoothFetchResult<string>>> fetch)
{
    var perItem = new List<double>();
    var sizes = new List<int>();
    var previous = Stopwatch.StartNew();
    var first = true;
    var got = 0;

    foreach (var id in ids)
    {
        var result = await fetch(id);
        var gap = previous.Elapsed.TotalSeconds;
        previous.Restart();

        if (!result.IsSuccess)
        {
            Console.WriteLine($"  取れなかった: {result.Status}");
            continue;
        }

        // 1件目はゲート待ちが無いので、1件あたりには数えない（画面の実測も同じ）
        if (!first) { perItem.Add(gap); }
        first = false;
        got++;
        sizes.Add(result.Value?.Length ?? 0);
        Console.Write($"\r  {label}: {got}/{ids.Count}");
    }

    Console.WriteLine();
    return (perItem, sizes);
}

var json = await MeasureAsync("①JSON", id => client.GetItemJsonAsync(id));
var html = await MeasureAsync("②HTML", id => client.GetItemHtmlAsync(id));

Console.WriteLine();
Console.WriteLine(Line("① 商品の情報（JSON）", json.PerItem, json.Sizes));
Console.WriteLine();
Console.WriteLine(Line("② 商品ページ（HTML）", html.PerItem, html.Sizes));
Console.WriteLine();

if (json.PerItem.Count > 0 && html.PerItem.Count > 0)
{
    var diff = html.PerItem.Average() - json.PerItem.Average();
    Console.WriteLine($"1件あたりの差（②－①）: {diff:+0.00;-0.00;0.00} 秒");
    Console.WriteLine(Math.Abs(diff) < 0.15
        ? "→ 差は間隔にほぼ吸収されている。段ごとに実測を分けても見込みはあまり変わらない。"
        : $"→ 差がある。300件なら「編集できるまで」が {300 * diff / 60:F1} 分ずれる。");
}

return 0;
