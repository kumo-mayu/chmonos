// ItemStatusProbe
//
// 商品IDの一覧を BOOTH に聞き、今どうなっているか（公開・無い・R-18・販売終了）を確かめる。
// 未確定に残った行の原因を調べるため（取り込みが「BOOTH に無い」と言われたのか、別の理由か）。
//
// 通信は BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。
// 第三者のデータを扱うことがあるので、画面には件数だけを出し、ID ごとの結果は指定したファイルに書く（リポジトリの外に置く）。
//
// 使い方：ItemStatusProbe <ID を1行ずつ書いたファイル> <結果を書く JSON のパス>

using System.Text.Json;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length != 2)
{
    Console.WriteLine("使い方: ItemStatusProbe <IDの一覧> <結果のJSON>");
    return 1;
}

var ids = File.ReadAllLines(args[0]).Select(line => line.Trim()).Where(line => line.Length > 0).Distinct().ToList();
using var http = BoothClient.CreateHttpClient();
var client = new BoothClient(http, new AppSettings());
Console.WriteLine($"{ids.Count} 件を聞く（間隔 {client.CurrentIntervalMs}ms・1本ずつ）");

var results = new List<object>();
foreach (var id in ids)
{
    var answer = await client.GetItemJsonAsync(id);
    bool? adult = null, endOfSale = null, soldOut = null;
    if (answer.IsSuccess && answer.Value is { } json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        adult = root.TryGetProperty("is_adult", out var a) && a.ValueKind == JsonValueKind.True;
        endOfSale = root.TryGetProperty("is_end_of_sale", out var e) && e.ValueKind == JsonValueKind.True;
        soldOut = root.TryGetProperty("is_sold_out", out var s) && s.ValueKind == JsonValueKind.True;
    }

    results.Add(new { id, status = answer.Status.ToString(), adult, endOfSale, soldOut, error = answer.Error });
}

File.WriteAllText(args[1], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
foreach (var group in results.GroupBy(result => JsonSerializer.Serialize(result).Contains("\"Success\"")
             ? $"Success（R-18 {results.Count(r => JsonSerializer.Serialize(r).Contains("\"adult\":true"))}）"
             : JsonSerializer.Serialize(result).Split("\"status\":\"")[1].Split('"')[0]))
{
    Console.WriteLine($"  {group.Key}: {group.Count()}");
}

return 0;
