// ResolveAccuracyProbe
//
// 自動検索（未確定の「候補を探す」）が、正解の分かっているファイルでどれだけ当たるかを測る。
//
// 正解は「取り込みで既に商品へ紐付いたファイル」から取る。Zone.Identifier や zip 内の URL で
// 決まったものが大半なので、**その手掛かりが無かったら**自動検索で辿り着けたか、を見る形になる。
// Brave で落とした・7-Zip で展開した・別PCから持ってきた、のどれでも手掛かりは消える（docs/research/id-resolution.md §6）。
//
// 入力はファイル名と正解IDの組だけ。**ファイルの中身は読まない**（unitypackage の手掛かりは測れない）。
//
//   第1部（通信なし）：登録簿からの候補（RegistryCandidates）と、正解の点数の上限
//   第2部（通信あり）：BOOTH内検索で正解が上位3件に入るか。外れたら AlternateQueries で引き直す
//
// BOOTHへの問い合わせは BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。
// 検索1本／ファイル（＋外れたときの引き直し最大2本）。候補のJSONは取らない。
//
// 使い方:
//   dotnet run --project experiments/ResolveAccuracyProbe -- <保存先> <pairs.json> [--search] [--out <結果.tsv>]
//   pairs.json は [{ "file": "...zip", "id": "123", "itemName": "...", "shop": "subdomain" }, ...]

using System.Text.Json;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Storage;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("使い方: ResolveAccuracyProbe <保存先> <pairs.json> [--search] [--out <結果.tsv>]");
    return;
}

var storeDir = args[0];
var doSearch = args.Contains("--search");
var outIndex = Array.IndexOf(args, "--out");
var outPath = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : null;

var pairs = JsonSerializer.Deserialize<List<Pair>>(
        File.ReadAllText(args[1]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
    .Where(pair => pair.File.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    .GroupBy(pair => pair.File, StringComparer.OrdinalIgnoreCase)
    .Select(group => group.First())
    .ToList();

var registry = JsonStore.Read<AvatarRegistry>(Path.Combine(storeDir, "avatar-registry.json")) ?? new AvatarRegistry();
var registryIds = registry.Entries.Select(entry => entry.ItemId).ToHashSet(StringComparer.Ordinal);

var assets = Path.Combine(AppContext.BaseDirectory, "assets");
var bridge = new SearchBridge(new JapaneseDictionary(
    Path.Combine(assets, "JMdict_e.gz"),
    Path.Combine(Path.GetTempPath(), "resolve-accuracy-bridge.cache")));
var readings = new KanjiReadings(Path.Combine(assets, "kanjidic2.xml.gz"));

Console.WriteLine($"zip {pairs.Count} 本（重複名を除く）／登録簿 {registry.Entries.Count} 件\n");

// ── 第1部：通信なし ──
var inRegistry = 0;
var registryTop1 = 0;
var registryAny = 0;
var registryWrongOnly = 0;
var nameRelated = 0;
var readingRelated = 0;
var emptyQuery = 0;
var rows = new List<Row>();

foreach (var pair in pairs)
{
    var query = FileNameQuery.ToSearchQuery(pair.File);
    if (query.Length == 0)
    {
        emptyQuery++;
    }

    var isInRegistry = registryIds.Contains(pair.Id);
    if (isInRegistry)
    {
        inRegistry++;
    }

    var fromRegistry = RegistryCandidates.For(pair.File, registry.Entries, bridge, readings);
    var registryRank = fromRegistry.Select(candidate => candidate.ItemId).ToList().IndexOf(pair.Id);
    if (registryRank == 0)
    {
        registryTop1++;
    }

    if (registryRank >= 0)
    {
        registryAny++;
    }
    else if (fromRegistry.Count > 0)
    {
        registryWrongOnly++;
    }

    // 正解の候補が検索に出てきたとして、名前側の裏付けが付くか
    var related = pair.ItemName is not null && FileNameQuery.LooksRelated(pair.ItemName, query);
    if (related)
    {
        nameRelated++;
    }

    var reading = pair.ItemName is null
        ? null
        : ReadingMatch.Find(query, pair.ItemName, bridge, readings, FileNameQuery.UndividedTokens(pair.File));
    if (reading is not null)
    {
        readingRelated++;
    }

    rows.Add(new Row
    {
        Pair = pair,
        Query = query,
        InRegistry = isInRegistry,
        RegistryRank = registryRank,
        RegistryWrong = fromRegistry.Where(candidate => candidate.ItemId != pair.Id).Select(candidate => $"{candidate.Name}({candidate.MatchedOn})").ToList(),
        NameRelated = related,
        Reading = reading,
    });
}

Console.WriteLine("第1部（通信なし）");
Console.WriteLine($"  検索語が作れない            : {emptyQuery}");
Console.WriteLine($"  正解が登録簿に載っている    : {inRegistry}");
Console.WriteLine($"  登録簿の候補に正解（1位）   : {registryTop1}");
Console.WriteLine($"  登録簿の候補に正解（3件内） : {registryAny}");
Console.WriteLine($"  登録簿の候補が外ればかり    : {registryWrongOnly}");
Console.WriteLine($"  商品名がファイル名と一致(+2): {nameRelated}");
Console.WriteLine($"  読みで一致(+2)              : {readingRelated}");

if (doSearch)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var client = new BoothClient(http, new AppSettings());
    Console.WriteLine($"\n第2部（通信あり）BOOTHへの間隔: {client.CurrentIntervalMs}ms（1本ずつ）");

    var requests = 0;
    var done = 0;

    foreach (var row in rows)
    {
        done++;
        if (row.Query.Length == 0)
        {
            continue;
        }

        requests++;
        var (rank, count, reranked) = await RankOfAsync(client, row.Query, row.Pair.Id, row.Pair.File);
        row.SearchRank = rank;
        row.SearchCount = count;
        row.RerankRank = reranked;

        // 本体と同じく、並べ直した後で上位3件に来なければ引き直す
        if (reranked is < 0 or >= 3)
        {
            foreach (var alternate in FallbackResolver.RetryQueries(row.Pair.File, row.Query, bridge))
            {
                requests++;
                var (_, _, altRank) = await RankOfAsync(client, alternate, row.Pair.Id, row.Pair.File);
                row.Alternates.Add($"{alternate}:{(altRank < 0 ? "-" : (altRank + 1).ToString())}");
                if (altRank is >= 0 and < 3)
                {
                    row.AlternateHit = true;
                    break;
                }
            }
        }

        if (done % 20 == 0)
        {
            Console.WriteLine($"  {done}/{rows.Count}（問い合わせ {requests} 本）");
        }
    }

    var searched = rows.Where(row => row.Query.Length > 0).ToList();
    Console.WriteLine($"  問い合わせ: {requests} 本");
    Console.WriteLine($"  1位           : {searched.Count(row => row.SearchRank == 0)}");
    Console.WriteLine($"  2〜3位        : {searched.Count(row => row.SearchRank is 1 or 2)}");
    Console.WriteLine($"  4位以下       : {searched.Count(row => row.SearchRank >= 3)}");
    Console.WriteLine($"  出てこない    : {searched.Count(row => row.SearchRank < 0)}");
    Console.WriteLine($"  0件           : {searched.Count(row => row.SearchCount == 0)}");
    Console.WriteLine($"  並べ直し後の上位3件: {searched.Count(row => row.RerankRank is >= 0 and < 3)}");
    Console.WriteLine($"  引き直しで当たる: {searched.Count(row => row.AlternateHit)}");
    var reached = searched.Count(row => row.RerankRank is >= 0 and < 3 || row.AlternateHit || row.RegistryRank >= 0);
    Console.WriteLine($"  どれかで上位3件に届く（登録簿を含む）: {reached}/{rows.Count}");
}

// ── 第3部（通信あり）：実際の候補検索を端から端まで通す ──
//
// 第2部は「検索結果に正解が出るか」だけを見ている。こちらは点数付けまで含めて、
// **画面の1位に正解が来るか／外れが「確度が高い」を名乗らないか**を見る。
// 候補ごとに商品JSONを取るので1ファイルあたり4本前後かかる。標本は等間隔に抜く。
// ファイルは手元に無いので unitypackage の手掛かりは空になる（＝手掛かりが消えた状態の再現）。
var proposeIndex = Array.IndexOf(args, "--propose");
if (proposeIndex >= 0)
{
    var sampleSize = proposeIndex + 1 < args.Length && int.TryParse(args[proposeIndex + 1], out var n) ? n : 30;
    var extraNames = args.SkipWhile(arg => arg != "--names").Skip(1).TakeWhile(arg => !arg.StartsWith("--")).ToList();

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var client = new BoothClient(http, new AppSettings());
    var resolver = new FallbackResolver(client, bridge, readings);

    var step = Math.Max(1, pairs.Count / sampleSize);
    var sample = pairs.Where((_, i) => i % step == 0).Take(sampleSize).ToList();

    var top1 = 0;
    var listed = 0;
    var strongRight = 0;
    var strongWrong = 0;
    var empty = 0;

    Console.WriteLine($"\n第3部（端から端まで）標本 {sample.Count} 本");
    foreach (var pair in sample)
    {
        var fake = Path.Combine(Path.GetTempPath(), "resolve-accuracy-absent", pair.File);
        var candidates = await resolver.ProposeAsync(fake);
        var index = candidates.Select(candidate => candidate.ItemId).ToList().IndexOf(pair.Id);

        if (candidates.Count == 0)
        {
            empty++;
        }

        if (index == 0)
        {
            top1++;
        }

        if (index >= 0)
        {
            listed++;
        }

        if (candidates.Count > 0 && candidates[0].IsStrong)
        {
            if (index == 0)
            {
                strongRight++;
            }
            else
            {
                strongWrong++;
            }
        }

        var head = candidates.Count == 0 ? "候補なし" : $"1位[{candidates[0].Score}点]{(index == 0 ? "○" : "×")} {candidates[0].Name}";
        Console.WriteLine($"  {(index < 0 ? "×" : index == 0 ? "○" : "△")} {pair.File} → {head}");
    }

    Console.WriteLine($"  1位が正解: {top1}/{sample.Count}　候補に正解: {listed}　候補なし: {empty}");
    Console.WriteLine($"  1位が「確度が高い」: 正解 {strongRight} ／ 外れ {strongWrong}");

    foreach (var name in extraNames)
    {
        var fake = Path.Combine(Path.GetTempPath(), "resolve-accuracy-absent", name);
        var candidates = await resolver.ProposeAsync(fake);
        Console.WriteLine($"\n  ■ {name}（正解不明）");
        foreach (var candidate in candidates)
        {
            Console.WriteLine($"    [{candidate.Score}点]{(candidate.IsStrong ? "◎" : "・")} {candidate.ItemId} {candidate.Name} ／ {string.Join("・", candidate.Reasons)}");
        }
    }
}

if (outPath is not null)
{
    using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
    writer.WriteLine("file\tid\titemName\tquery\tsearchRank\tsearchCount\talternates\tregistryRank\tregistryWrong\tnameRelated\treading");
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join('\t',
            row.Pair.File, row.Pair.Id, row.Pair.ItemName, row.Query,
            row.SearchRank?.ToString() ?? "", row.SearchCount?.ToString() ?? "",
            string.Join(" ", row.Alternates), row.RegistryRank, string.Join(" / ", row.RegistryWrong),
            row.NameRelated, row.Reading ?? ""));
    }
}

/// 同じ検索結果から、BOOTHの並びでの順位と、本体と同じ並べ直しの後の順位を数える（通信は1本）
static async Task<(int Rank, int Count, int Reranked)> RankOfAsync(BoothClient client, string query, string itemId, string file)
{
    var result = await client.SearchAsync(query);
    if (!result.IsSuccess || result.Value is null)
    {
        Console.WriteLine($"  （検索できませんでした: {query} / {result.Error}）");
        return (-1, -1, -1);
    }

    var ids = FallbackResolver.ExtractSearchResultIds(result.Value).ToList();
    var cards = FallbackResolver.ExtractSearchCards(result.Value);
    var reranked = cards.Count > 0 ? FallbackResolver.Rerank(cards, file).Select(card => card.ItemId).ToList() : ids;
    return (ids.IndexOf(itemId), ids.Count, reranked.IndexOf(itemId));
}

sealed class Pair
{
    public required string File { get; init; }

    public required string Id { get; init; }

    public string? ItemName { get; init; }

    public string? Shop { get; init; }
}

sealed class Row
{
    public required Pair Pair { get; init; }

    public required string Query { get; init; }

    public bool InRegistry { get; init; }

    public int RegistryRank { get; init; }

    public List<string> RegistryWrong { get; init; } = [];

    public bool NameRelated { get; init; }

    public string? Reading { get; init; }

    public int? SearchRank { get; set; }

    public int? SearchCount { get; set; }

    public int? RerankRank { get; set; }

    public List<string> Alternates { get; } = [];

    public bool AlternateHit { get; set; }
}
