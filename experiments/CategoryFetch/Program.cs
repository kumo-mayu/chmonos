// CategoryFetch
//
// BOOTHのカテゴリ表を1度だけ取ってきて、同梱できる形に落とす。
//
// **同梱するのは、全員が同じ静的な表を別々に取りに行くのを避けるため。**
// 1000人が使えば1000回、同じ内容のために問い合わせが飛ぶ。同梱すれば0回。
//
// **表は1ページに丸ごと載っている。**BOOTHのトップページの絞り込みが
// `category-options` 属性に「親 → 子」の木をJSONで持っているので、**1リクエストで足りる。**
// 一覧を辿って集める必要はない。
//
// 通信は BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;

/// <summary>トップページが持っている形。<c>pc</c> が親の名前。</summary>
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());

Console.WriteLine($"BOOTHへの間隔: {client.CurrentIntervalMs}ms（1本ずつ）");
Console.WriteLine("取りに行くのは1ページだけ。\n");

var result = await client.GetBrowsePageAsync("https://booth.pm/ja");
if (!result.IsSuccess || result.Value is null)
{
    Console.WriteLine($"取れませんでした: {result.Status} {result.Error}");
    return 1;
}

var html = result.Value;
Console.WriteLine($"トップページ: {html.Length:N0} 文字");

// 同じ属性が複数あるので、いちばん長いものを採る（短い方は一部だけ持っている）
var blob = Regex.Matches(html, @"category-options=""([^""]*)""")
    .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
    .OrderByDescending(text => text.Length)
    .FirstOrDefault();

if (blob is null)
{
    Console.WriteLine("category-options が見つかりませんでした。ページの作りが変わった可能性があります。");
    return 1;
}

Console.WriteLine($"category-options: {blob.Length:N0} 文字");

var parents = JsonSerializer.Deserialize<List<ParentOption>>(blob, options);
if (parents is null || parents.Count == 0)
{
    Console.WriteLine("読めませんでした。");
    return 1;
}

// 親のIDはリンクの data-product-list に出ている（market_browse_207 のような形）。
// 子には付いていないので、持つのは名前だけにする——
// 絞り込みは子の名前だけの平坦な一覧なので、それで足りる
var parentIds = Regex.Matches(
        html,
        @"market_browse_(\d+)""[^>]*href=""https://booth\.pm/ja/browse/([^""]+)""")
    .ToDictionary(
        match => Uri.UnescapeDataString(match.Groups[2].Value),
        match => int.Parse(match.Groups[1].Value),
        StringComparer.Ordinal);

var table = new CategoryTable
{
    FetchedAt = DateTimeOffset.Now,
    Source = "https://booth.pm/ja",
    Parents = parents
        .Select(parent => new CategoryParent
        {
            Name = parent.Pc,
            Id = parentIds.TryGetValue(parent.Pc, out var id) ? id : null,
            Children = parent.Children.Select(child => child.Label).ToList(),
        })
        .ToList(),
};

Console.WriteLine($"\n親 {table.Parents.Count} 件 / 子 {table.Parents.Sum(parent => parent.Children.Count)} 件\n");

foreach (var parent in table.Parents)
{
    var idText = parent.Id is { } id ? $" ({id})" : string.Empty;
    Console.WriteLine($"{parent.Name}{idText}");
    Console.WriteLine($"    {string.Join(" / ", parent.Children)}");
}

// 手元の商品が実際に使っているカテゴリが、この表に載っているかを確かめる
var known = new[] { "3Dキャラクター", "3D衣装", "3Dテクスチャ", "3Dツール・システム", "3D装飾品" };
var all = table.Parents.SelectMany(parent => parent.Children).ToHashSet(StringComparer.Ordinal);
var missing = known.Where(name => !all.Contains(name)).ToList();

Console.WriteLine($"\n手元の商品のカテゴリ {known.Length} 件のうち、表に無いもの: "
    + (missing.Count == 0 ? "なし" : string.Join(" / ", missing)));

var outPath = args.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine(AppContext.BaseDirectory, "booth-categories.json");
await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(table, options));
Console.WriteLine($"\n書き出しました: {outPath}");

// 商品JSONと同じ形で持てることの確認
_ = new BoothCategory { Id = 208, Name = "3Dキャラクター", ParentName = "3Dモデル" };
return 0;

/// <summary>トップページの絞り込みが持っている形。</summary>
internal sealed class ParentOption
{
    public string Pc { get; set; } = string.Empty;

    public List<ChildOption> Children { get; set; } = [];
}

internal sealed class ChildOption
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>同梱する形。</summary>
internal sealed class CategoryTable
{
    public DateTimeOffset FetchedAt { get; set; }

    public string Source { get; set; } = string.Empty;

    public List<CategoryParent> Parents { get; set; } = [];
}

internal sealed class CategoryParent
{
    public string Name { get; set; } = string.Empty;

    /// <summary>BOOTHの市場カテゴリID。親にしか出ていないので子は持たない。</summary>
    public int? Id { get; set; }

    public List<string> Children { get; set; } = [];
}
