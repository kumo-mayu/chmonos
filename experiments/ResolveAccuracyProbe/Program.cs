// ResolveAccuracyProbe
//
// 自動検索（未確定の「自動検索」）が、正解の分かっているファイルでどれだけ当たるかを測る。
//
// 正解は「取り込みで既に商品へ紐付いたファイル」から取る。Zone.Identifier や zip 内の URL で
// 決まったものが大半なので、**その手掛かりが無かったら**自動検索で辿り着けたか、を見る形になる。
// Brave で落とした・7-Zip で展開した・別PCから持ってきた、のどれでも手掛かりは消える（docs/research/id-resolution.md §6）。
//
// 入力はファイル名と正解IDの組だけ。**ファイルの中身は読まない**（unitypackage の手掛かりは測れない）。
//
//   第1部（通信なし）：登録簿からの候補（RegistryCandidates）と、正解の点数の上限
//   第2部（--search）：BOOTH内検索で正解が上位3件に入るか。外れたら引き直す（本体と同じ RetryQueries）
//   第3部（--propose）：本体と同じ FallbackResolver.ProposeAsync を端から端まで。画面の1位に正解が来るか
//
// **BOOTH の答えは控える**（--cache、既定は %TEMP%\chmonos-resolve-probe-cache）。検索語・商品IDごとに保存し、
// 2回目からは控えを返す。並べ直し・点数の直しは控えだけで試せる（--offline で通信を禁じる）。
// 控えに無いときだけ BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。
//
// 使い方:
//   dotnet run --project experiments/ResolveAccuracyProbe -- <保存先> <pairs.json> --make-pairs
//       保存先の items/*.json から、商品に紐付いた zip の名前と正解の組を書き出す（保存先は読むだけ）
//   dotnet run --project experiments/ResolveAccuracyProbe -- <保存先> <pairs.json> [--search] [--propose <本数|all>]
//       [--cache <控えの置き場>] [--offline] [--no-avatars] [--out <第2部の結果.tsv>] [--detail <第3部の結果.tsv>] [--names <名前>...]
//   pairs.json は [{ "file": "...zip", "id": "123", "itemName": "...", "shop": "subdomain" }, ...]
//
// 結果の TSV には正解の商品名とファイル名が入る。友人のデータで測るときは、リポジトリの外に置く。

using System.Text.Json;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Storage;
using ResolveAccuracyProbe;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length < 2)
{
    Console.WriteLine("使い方: ResolveAccuracyProbe <保存先> <pairs.json> [--make-pairs] [--search] [--propose <本数|all>] [--cache <dir>] [--offline] [--out <tsv>] [--detail <tsv>]");
    return;
}

var storeDir = args[0];
string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

if (args.Contains("--make-pairs"))
{
    MakePairs(storeDir, args[1]);
    return;
}

var doSearch = args.Contains("--search");
var outPath = Option("--out");
var detailPath = Option("--detail");
var cacheDir = Option("--cache") ?? Path.Combine(Path.GetTempPath(), "chmonos-resolve-probe-cache");
var client = new CachingBoothClient(cacheDir, offline: args.Contains("--offline"));

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

// 本体と同じく、ファイル名の中のアバターの名前を検索語から外す（--no-avatars で外さない＝2026-09-29 より前の形）
var avatars = args.Contains("--no-avatars") ? null : AvatarTokens.From(registry, readings);
Func<string, bool>? isAvatarName = avatars is null ? null : avatars.IsAvatarName;

Console.WriteLine($"zip {pairs.Count} 本（重複名を除く）／登録簿 {registry.Entries.Count} 件／控え {cacheDir}\n");

// 1本の中身を見る（語ごとにアバターの名前とみなしたか・並べ直しの上位）。控えにある検索だけを見る
if (Option("--explain") is { } explain)
{
    var query = FileNameQuery.ToSearchQuery(explain, isAvatarName);
    Console.WriteLine($"検索語: {query}");
    foreach (var token in FileNameQuery.Tokens(explain))
    {
        Console.WriteLine($"  {token}: 名前={avatars?.IsAvatarName(token)} → {string.Join(",", avatars?.AvatarsNamedBy(token) ?? [])}");
    }

    var page = await client.SearchAsync(query);
    if (page.IsSuccess && page.Value is not null)
    {
        foreach (var card in FallbackResolver.Rerank(FallbackResolver.ExtractSearchCards(page.Value), explain, avatars).Take(5))
        {
            Console.WriteLine($"  {card.ItemId} {card.Name} → {string.Join(",", avatars?.AvatarsIn(card.Name) ?? [])}");
        }
    }

    return;
}

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
    var query = FileNameQuery.ToSearchQuery(pair.File, isAvatarName);
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
    var before = client.NetworkRequests;
    var queries = 0;
    var done = 0;
    Console.WriteLine("\n第2部（検索の当たり）");

    foreach (var row in rows)
    {
        done++;
        if (row.Query.Length == 0)
        {
            continue;
        }

        queries++;
        var ranked = await RankOfAsync(client, row.Query, row.Pair.Id, row.Pair.File, avatars);
        row.SearchRank = ranked.Rank;
        row.SearchCount = ranked.Count;
        row.RerankRank = ranked.Reranked;
        row.Top = ranked.Top;

        // 本体と同じく、並べ直した後で上位3件に来なければ引き直す
        if (ranked.Reranked is < 0 or >= 3)
        {
            foreach (var alternate in FallbackResolver.RetryQueries(row.Pair.File, row.Query, bridge, avatars))
            {
                queries++;
                var alt = await RankOfAsync(client, alternate, row.Pair.Id, row.Pair.File, avatars);
                row.Alternates.Add($"{alternate}:{(alt.Reranked < 0 ? "-" : (alt.Reranked + 1).ToString())}({alt.Count})");
                if (alt.Reranked is >= 0 and < 3)
                {
                    row.AlternateHit = true;
                    break;
                }
            }
        }

        if (done % 20 == 0)
        {
            Console.WriteLine($"  {done}/{rows.Count}（検索 {queries} 本・うち通信 {client.NetworkRequests - before} 本）");
        }
    }

    var searched = rows.Where(row => row.Query.Length > 0).ToList();
    Console.WriteLine($"  検索: {queries} 本（うち BOOTH へ出たもの {client.NetworkRequests - before} 本・控えに無く飛ばした {client.Misses} 本）");
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

// ── 第3部：実際の候補検索を端から端まで通す ──
//
// 第2部は「検索結果に正解が出るか」だけを見ている。こちらは点数付けまで含めて、
// **画面の1位に正解が来るか／外れが「確度が高い」を名乗らないか**を見る。
// 候補ごとに商品JSONを取るので1ファイルあたり4本前後かかる（控えにあれば0本）。標本は等間隔に抜く。
// ファイルは手元に無いので unitypackage の手掛かりは空になる（＝手掛かりが消えた状態の再現）。
var propose = Option("--propose");
if (propose is not null)
{
    var before = client.NetworkRequests;
    var resolver = new FallbackResolver(client, bridge, readings, avatars is null ? null : () => registry);

    var sample = propose == "all"
        ? pairs
        : SampleEvenly(pairs, int.TryParse(propose, out var n) ? n : 30);

    var top1 = 0;
    var listed = 0;
    var strongRight = 0;
    var strongWrong = 0;
    var empty = 0;
    var details = new List<string>();

    Console.WriteLine($"\n第3部（端から端まで）標本 {sample.Count} 本");
    foreach (var pair in sample)
    {
        var fake = Path.Combine(Path.GetTempPath(), "resolve-accuracy-absent", pair.File);
        var candidates = (await resolver.ProposeAsync(fake)).Candidates;
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

        details.Add(string.Join('\t',
            pair.File, pair.Id, pair.ItemName, FileNameQuery.ToSearchQuery(pair.File, isAvatarName), index,
            string.Join(" | ", candidates.Select(candidate =>
                $"{(candidate.ItemId == pair.Id ? "○" : "")}{candidate.Score}点 {candidate.Name} [{string.Join("・", candidate.Reasons)}]"))));
    }

    Console.WriteLine($"  1位が正解: {top1}/{sample.Count}　候補に正解: {listed}　候補なし: {empty}");
    Console.WriteLine($"  1位が「確度が高い」: 正解 {strongRight} ／ 外れ {strongWrong}");
    Console.WriteLine($"  BOOTH へ出た問い合わせ: {client.NetworkRequests - before} 本・控えに無く飛ばした {client.Misses} 本");

    if (detailPath is not null)
    {
        File.WriteAllLines(detailPath, ["file\tid\titemName\tquery\tindex\tcandidates", .. details], new System.Text.UTF8Encoding(false));
    }

    foreach (var name in args.SkipWhile(arg => arg != "--names").Skip(1).TakeWhile(arg => !arg.StartsWith("--")))
    {
        var fake = Path.Combine(Path.GetTempPath(), "resolve-accuracy-absent", name);
        var candidates = (await resolver.ProposeAsync(fake)).Candidates;
        Console.WriteLine($"\n  ■ {name}（正解不明）");
        foreach (var candidate in candidates)
        {
            Console.WriteLine($"    [{candidate.Score}点]{(candidate.IsStrong ? "◎" : "・")} {candidate.ItemId} {candidate.Name} ／ {string.Join("・", candidate.Reasons)}");
        }
    }
}

Console.WriteLine($"\nBOOTH へ出た問い合わせ（合計）: {client.NetworkRequests} 本／控えから: {client.CacheHits} 本");

if (outPath is not null)
{
    using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
    writer.WriteLine("file\tid\titemName\tshop\tquery\tsearchRank\trerankRank\tsearchCount\ttop\talternates\tregistryRank\tregistryWrong\tnameRelated\treading");
    foreach (var row in rows)
    {
        writer.WriteLine(string.Join('\t',
            row.Pair.File, row.Pair.Id, row.Pair.ItemName, row.Pair.Shop, row.Query,
            row.SearchRank?.ToString() ?? "", row.RerankRank?.ToString() ?? "", row.SearchCount?.ToString() ?? "",
            row.Top, string.Join(" ", row.Alternates), row.RegistryRank, string.Join(" / ", row.RegistryWrong),
            row.NameRelated, row.Reading ?? ""));
    }
}

/// 同じ検索結果から、BOOTHの並びでの順位と、本体と同じ並べ直しの後の順位を数える（検索1本）
static async Task<(int Rank, int Count, int Reranked, string Top)> RankOfAsync(
    CachingBoothClient client, string query, string itemId, string file, AvatarTokens? avatars)
{
    var result = await client.SearchAsync(query);
    if (!result.IsSuccess || result.Value is null)
    {
        return (-1, -1, -1, "");
    }

    var ids = FallbackResolver.ExtractSearchResultIds(result.Value).ToList();
    var cards = FallbackResolver.ExtractSearchCards(result.Value);
    var reranked = cards.Count > 0 ? FallbackResolver.Rerank(cards, file, avatars).ToList() : [];
    var rerankedIds = cards.Count > 0 ? reranked.Select(card => card.ItemId).ToList() : ids;
    var top = string.Join(" | ", reranked.Take(3).Select(card => $"{card.Name}@{card.ShopSubdomain}"));
    return (ids.IndexOf(itemId), ids.Count, rerankedIds.IndexOf(itemId), top);
}

/// 等間隔に抜く。前回（2026-09-11）と同じ抜き方にして、数字を比べられるようにする
static List<Pair> SampleEvenly(List<Pair> pairs, int size)
{
    var step = Math.Max(1, pairs.Count / size);
    return pairs.Where((_, i) => i % step == 0).Take(size).ToList();
}

/// 保存先の商品から、紐付いた zip と正解の組を作る。**保存先は読むだけ**。
/// 仮のID（BOOTHに無い商品）と「この商品から外す」の印の付いたファイルは除く（正解がBOOTHの商品ではない・紐付けが誤り）。
static void MakePairs(string storeDir, string outPath)
{
    var pairs = new List<Pair>();
    foreach (var path in Directory.EnumerateFiles(Path.Combine(storeDir, "items"), "*.json").Order(StringComparer.Ordinal))
    {
        if (JsonStore.Read<ItemRecord>(path) is not { } item || item.IsLocalOnly)
        {
            continue;
        }

        foreach (var file in item.Local.OwnedFiles)
        {
            foreach (var filePath in file.Paths)
            {
                if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    pairs.Add(new Pair
                    {
                        File = Path.GetFileName(filePath),
                        Id = item.Id,
                        ItemName = item.Booth.Name ?? item.Local.DisplayName,
                        Shop = item.ShopSubdomain,
                    });
                }
            }
        }
    }

    File.WriteAllText(outPath, JsonSerializer.Serialize(pairs, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    }), new System.Text.UTF8Encoding(false));

    Console.WriteLine($"組 {pairs.Count} 件（zip の名前の重複を除くと {pairs.Select(pair => pair.File).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 本）→ {outPath}");
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

    public string Top { get; set; } = "";

    public List<string> Alternates { get; } = [];

    public bool AlternateHit { get; set; }
}
