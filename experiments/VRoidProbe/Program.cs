// VRoidProbe
//
// 「BOOTHの VRoid カテゴリをアバターとして扱うべきか」を実データで確かめる。
//
// アバターの判定は今 category が 3Dキャラクター かどうかだけで決めている。
// VRoid も 3Dモデル の子カテゴリなので、そこに**アバター本体**が並んでいるなら
// 取りこぼしていることになる。逆に**VRoid用の衣装やテクスチャ**が主なら、
// 足すと衣装がアバターとして登録されて一覧が汚れる。
//
// 見るのは「何が並んでいるか」。名前だけでは決められないので、
// 数件は商品JSONまで取ってcategoryとタグを確かめる。
//
// 通信は BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。

using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

Console.OutputEncoding = System.Text.Encoding.UTF8;

/// <summary>商品JSONまで取って中を見る数。多くしても判断は変わらない。</summary>
const int Sampled = 6;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());

Console.WriteLine($"BOOTHへの間隔: {client.CurrentIntervalMs}ms（1本ずつ）\n");

var page = await client.GetBrowsePageAsync("https://booth.pm/ja/browse/VRoid");
if (!page.IsSuccess || page.Value is null)
{
    Console.WriteLine($"一覧を取れませんでした: {page.Status} {page.Error}");
    return 1;
}

Console.WriteLine($"VRoid の一覧: {page.Value.Length:N0} 文字");

var ids = FallbackResolver.ExtractSearchResultIds(page.Value);
Console.WriteLine($"並んでいる商品: {ids.Count} 件　先頭 {Sampled} 件の中身を見る\n");

// 名前は一覧のHTMLに全部載っている。**ここを読む分には通信が増えない。**
// 6件のJSONだけでは「人気の端」しか見ていないので、まず全件の名前を見る
var listed = Regex.Matches(page.Value, @"item-card__title-anchor[^>]*>\s*([^<]+?)\s*<")
    .Select(match => System.Net.WebUtility.HtmlDecode(match.Groups[1].Value.Trim()))
    .Where(name => name.Length > 0)
    .ToList();

if (listed.Count == 0)
{
    // 作りが変わったら、名前を拾えていないことを黙って隠さない
    var dumped = Path.Combine(Path.GetTempPath(), "booth-vroid.html");
    File.WriteAllText(dumped, page.Value);
    Console.WriteLine($"名前を拾えませんでした。そのまま置きました: {dumped}");
}

Console.WriteLine($"名前を読めたもの: {listed.Count} 件\n");
foreach (var name in listed)
{
    Console.WriteLine($"  {name}");
}

Console.WriteLine($"\n先頭 {Sampled} 件の中身を見る\n");

var counts = new Dictionary<string, int>(StringComparer.Ordinal);

foreach (var id in ids.Take(Sampled))
{
    var json = await client.GetItemJsonAsync(id);
    if (!json.IsSuccess || json.Value is null)
    {
        Console.WriteLine($"  {id}　取れませんでした（{json.Status}）");
        continue;
    }

    using var document = JsonDocument.Parse(json.Value);
    var root = document.RootElement;

    var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
    var category = root.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.Object
        ? c.TryGetProperty("name", out var cn) ? cn.GetString() : null
        : null;
    var parent = root.TryGetProperty("category", out var c2) && c2.ValueKind == JsonValueKind.Object
        ? c2.TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.Object
            ? p.TryGetProperty("name", out var pn) ? pn.GetString() : null
            : null
        : null;

    var tags = root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
        ? t.EnumerateArray()
            .Select(tag => tag.TryGetProperty("name", out var tn) ? tn.GetString() : null)
            .Where(text => text is not null)
            .Take(8)
            .ToList()
        : [];

    counts[category ?? "(なし)"] = counts.TryGetValue(category ?? "(なし)", out var current) ? current + 1 : 1;

    Console.WriteLine($"  {id}　{name}");
    Console.WriteLine($"      category: {parent} / {category}");
    Console.WriteLine($"      tags: {string.Join(" ", tags)}");
}

Console.WriteLine("\n---- categoryの内訳 ----");
foreach (var (category, count) in counts.OrderByDescending(pair => pair.Value))
{
    Console.WriteLine($"  {category}: {count} 件");
}


// 名前とIDは同じ順で並んでいる。**アバター本体に見えるものだけ**を選んで中を見る——
// 「本体がVRoidカテゴリに入っているのか、3Dキャラクターに入っているのか」が判断の分かれ目
var suspects = listed
    .Select((name, index) => (Name: name, Id: index < ids.Count ? ids[index] : null))
    .Where(pair => pair.Id is not null)
    .Where(pair => Words.Body.Any(word => pair.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
    .ToList();

Console.WriteLine($"\n本体に見えるもの: {suspects.Count} 件\n");
foreach (var (name, id) in suspects)
{
    Console.WriteLine($"  {id}　{name}");
}

Console.WriteLine();
foreach (var (name, id) in suspects)
{
    var suspect = await client.GetItemJsonAsync(id!);
    if (!suspect.IsSuccess || suspect.Value is null)
    {
        Console.WriteLine($"  {id}　取れませんでした（{suspect.Status}）");
        continue;
    }

    using var doc = JsonDocument.Parse(suspect.Value);
    var cat = doc.RootElement.TryGetProperty("category", out var ce) && ce.ValueKind == JsonValueKind.Object
        ? ce.TryGetProperty("name", out var cne) ? cne.GetString() : null
        : null;

    Console.WriteLine($"  {id}　{cat}　{name}");
}
Console.WriteLine("\n判断の材料：VRoid カテゴリの商品が実際に何なのか（本体か、本体向けの衣装か）。");
return 0;

/// <summary>アバター本体らしさを示す語。ここに引っかかったものだけ中を見る。</summary>
internal static class Words
{
    public static readonly string[] Body =
        ["model", "モデル", "素体", "アバター", "avatar", "VRM", "doll", "Cat"];
}
