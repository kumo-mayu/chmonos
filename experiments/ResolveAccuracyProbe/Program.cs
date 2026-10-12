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
//       [--cache <控えの置き場>] [--offline] [--no-avatars] [--siblings] [--second-page]
//       [--out <第2部の結果.tsv>] [--detail <第3部の結果.tsv>] [--names <名前>...]
//   --second-page は、引き直しでも外れたとき最初の1語の検索の2ページ目も見る（本体の SearchSecondPage。research §19）。
//   第3部の結果の最後の列 secondPage に、2ページ目を引いた本は「2」
//   pairs.json は [{ "file": "...zip", "id": "123", "itemName": "...", "shop": "subdomain", "shopFolder": "...", "itemFolder": "..." }, ...]
//   --query folder は、検索語にファイル名の代わりに親のフォルダ名（itemFolder）を使う（置き方を人が明示したときの案。2026-10-12）。
//   --query folder-clean は、そのフォルダ名から販売の飾り（期間限定無料・8/5まで・○%OFF・末尾の「 - ショップ名」）を落としてから使う
//   --layout は、本体の自動検索（ProposeAsync）に置き方の手掛かり（FolderLayoutHint）を渡す。ショップのサブドメインは正解の商品を除いた
//   手元の商品から引く。--shops <ShopListProbe の控え> を渡せば、そのショップの一覧は取り直さない
//   第3部は、置き方のフォルダ名（2段上＝ショップ、1段上＝商品名）と一致する候補を上へ並べ直した順位も出す
//
// 結果の TSV には正解の商品名とファイル名が入る。友人のデータで測るときは、リポジトリの外に置く。

using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;
using Chmonos.Core.Search;
using Chmonos.Core.Storage;
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

// 引き直しでも外れたとき、最初の1語の検索の2ページ目も見る（本体の FallbackResolver.SearchSecondPage。research §19）
var secondPage = args.Contains("--second-page");
var queryMode = Option("--query");
var folderQuery = queryMode is "folder" or "folder-clean";
var layoutMode = args.Contains("--layout");
var storeItems = layoutMode
    ? Directory.EnumerateFiles(Path.Combine(storeDir, "items"), "*.json").Select(path => JsonStore.Read<ItemRecord>(path)).OfType<ItemRecord>().ToList()
    : [];
var outPath = Option("--out");
var detailPath = Option("--detail");
var cacheDir = Option("--cache") ?? Path.Combine(Path.GetTempPath(), "chmonos-resolve-probe-cache");
var client = new CachingBoothClient(cacheDir, offline: args.Contains("--offline")) { ShopListsDirectory = Option("--shops") };

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

// 兄弟の zip（--siblings）。画面では未確定の一覧の同じフォルダ・同じ種類のファイルを渡すので、ここでも
// 保存先の商品が持つ zip の**元の場所**から、同じフォルダにある zip を兄弟にする（全部が未確定だった状態の再現）。
// 正解の商品IDではまとめない（答えを見て測ることになる）
// 手元に無い場所として扱う（中身を読まない）ため、元のフォルダごとに temp の下の番号のフォルダへ置き換える
var absentRoot = Path.Combine(Path.GetTempPath(), "resolve-accuracy-absent");
var listing = args.Contains("--siblings") ? ListedPaths(storeDir, absentRoot) : null;
string FakePath(string file) => listing is not null && listing.PathOf.TryGetValue(file, out var path)
    ? path
    : Path.Combine(absentRoot, file);
var varyingCache = new Dictionary<string, IReadOnlySet<string>?>(StringComparer.OrdinalIgnoreCase);
IReadOnlySet<string>? VaryingOf(string file)
{
    if (listing is null)
    {
        return null;
    }

    if (!varyingCache.TryGetValue(file, out var varying))
    {
        varyingCache[file] = varying = SiblingTokens.Varying(FakePath(file), listing.All, isAvatarName);
    }

    return varying;
}

Func<string, bool>? NotProductOf(string file) => SiblingTokens.NotProductName(avatars, VaryingOf(file));

Console.WriteLine($"zip {pairs.Count} 本（重複名を除く）／登録簿 {registry.Entries.Count} 件／控え {cacheDir}\n");
if (listing is not null)
{
    Console.WriteLine($"一覧 {listing.All.Count} 本／変わる語を外す: {pairs.Count(pair => VaryingOf(pair.File) is { Count: > 0 })} 本\n");
}

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
    var query = FileNameQuery.ToSearchQuery(pair.File, NotProductOf(pair.File));
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
        var varying = VaryingOf(row.Pair.File);
        var ranked = await RankOfAsync(client, row.Query, row.Pair.Id, row.Pair.File, avatars, varying);
        row.SearchRank = ranked.Rank;
        row.SearchCount = ranked.Count;
        row.RerankRank = ranked.Reranked;
        row.Top = ranked.Top;

        // 本体と同じく、並べ直した後で上位3件に来なければ引き直す
        if (ranked.Reranked is < 0 or >= 3)
        {
            var firstWord = FileNameQuery.MostDistinctiveToken(row.Pair.File, NotProductOf(row.Pair.File));
            var firstWordFilled = false;
            foreach (var alternate in FallbackResolver.RetryQueries(row.Pair.File, row.Query, bridge, avatars, varying))
            {
                queries++;
                var alt = await RankOfAsync(client, alternate, row.Pair.Id, row.Pair.File, avatars, varying);
                row.Alternates.Add($"{alternate}:{(alt.Reranked < 0 ? "-" : (alt.Reranked + 1).ToString())}({alt.Count})");
                firstWordFilled |= string.Equals(alternate, firstWord, StringComparison.OrdinalIgnoreCase) && alt.Count >= 60;
                if (alt.Reranked is >= 0 and < 3)
                {
                    row.AlternateHit = true;
                    break;
                }
            }

            // 本体の SearchSecondPage と同じく、最初の1語の検索が60件埋まっていれば2ページ目を見る
            if (secondPage && !row.AlternateHit && firstWordFilled)
            {
                queries++;
                var alt = await RankOfAsync(client, firstWord, row.Pair.Id, row.Pair.File, avatars, varying, page: 2);
                row.Alternates.Add($"{firstWord}#2:{(alt.Reranked < 0 ? "-" : (alt.Reranked + 1).ToString())}({alt.Count})");
                row.AlternateHit = alt.Reranked is >= 0 and < 3;
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
    var resolver = new FallbackResolver(client, bridge, readings, avatars is null ? null : () => registry)
    {
        SearchSecondPage = secondPage,
    };

    var sample = propose == "all"
        ? pairs
        : SampleEvenly(pairs, int.TryParse(propose, out var n) ? n : 30);

    var top1 = 0;
    var listed = 0;
    var strongRight = 0;
    var strongWrong = 0;
    var empty = 0;
    var missedPairs = 0;
    var missedTop1 = 0;
    var layoutTop1 = 0;
    var layoutSureRight = 0;
    var layoutSureWrong = 0;
    var layoutStrongRight = 0;
    var layoutStrongWrong = 0;
    var details = new List<string>();

    Console.WriteLine($"\n第3部（端から端まで）標本 {sample.Count} 本");
    foreach (var pair in sample)
    {
        var fake = FakePath(folderQuery && pair.ItemFolder is { Length: > 0 } folder
            ? (queryMode == "folder-clean" ? Layout.StripDecorations(folder, pair.ShopFolder) : folder) + ".zip"
            : pair.File);
        var missesBefore = client.Misses;
        var secondBefore = client.SecondPageRequests;
        FolderLayoutHint? hint = null;
        if (layoutMode)
        {
            var subdomain = storeItems.Where(item => item.Id != pair.Id).Select(item => item.Booth.Shop)
                .FirstOrDefault(shop => shop?.Subdomain is { Length: > 0 } && SaleDecorations.SameShop(shop.Name, shop.Subdomain, pair.ShopFolder))?.Subdomain;
            hint = new FolderLayoutHint(pair.ItemFolder, pair.ShopFolder, subdomain);
        }

        var candidates = (await resolver.ProposeAsync(fake, listed: listing?.All, layout: hint)).Candidates;
        if (candidates.Count > 0 && candidates[0].LayoutPoints >= 3)
        {
            if (candidates[0].ItemId == pair.Id)
            {
                layoutSureRight++;
            }
            else
            {
                layoutSureWrong++;
            }
        }
        if (client.Misses > missesBefore)
        {
            missedPairs++;
            missedTop1 += candidates.Count > 0 && candidates[0].ItemId == pair.Id ? 1 : 0;
        }
        var index = candidates.Select(candidate => candidate.ItemId).ToList().IndexOf(pair.Id);

        // 置き方のフォルダ名で並べ直す（商品名の一致を2、ショップの一致を1。同じなら元の順）
        var laid = candidates
            .Select((candidate, order) => (candidate, order, bonus: Layout.Bonus(candidate, pair)))
            .OrderByDescending(entry => entry.bonus).ThenBy(entry => entry.order)
            .ToList();
        var layoutIndex = laid.FindIndex(entry => entry.candidate.ItemId == pair.Id);
        layoutTop1 += layoutIndex == 0 ? 1 : 0;
        if (laid.Count > 0 && laid[0].bonus == 3)
        {
            // 商品名とショップの両方がフォルダ名と一致した1位（新しい「確度が高い」の案）
            if (layoutIndex == 0)
            {
                layoutStrongRight++;
            }
            else
            {
                layoutStrongWrong++;
            }
        }

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
            pair.File, pair.Id, pair.ItemName, FileNameQuery.ToSearchQuery(pair.File, NotProductOf(pair.File)), index,
            string.Join(" | ", candidates.Select(candidate =>
                $"{(candidate.ItemId == pair.Id ? "○" : "")}{candidate.Score}点 {candidate.Name} [{string.Join("・", candidate.Reasons)}]")),
            client.SecondPageRequests > secondBefore ? "2" : ""));
    }

    Console.WriteLine($"  1位が正解: {top1}/{sample.Count}　候補に正解: {listed}　候補なし: {empty}");
    Console.WriteLine($"  1位が「確度が高い」: 正解 {strongRight} ／ 外れ {strongWrong}");
    Console.WriteLine($"  置き方のフォルダ名で並べ直した1位が正解: {layoutTop1}/{sample.Count}　商品名とショップが両方一致した1位: 正解 {layoutStrongRight} ／ 外れ {layoutStrongWrong}");
    if (layoutMode)
    {
        Console.WriteLine($"  本体（--layout）：置き方の点が3以上の1位 正解 {layoutSureRight} ／ 外れ {layoutSureWrong}　ショップの一覧を求めた {client.ShopPageRequests} 本");
    }
    Console.WriteLine($"  BOOTH へ出た問い合わせ: {client.NetworkRequests - before} 本・控えに無く飛ばした {client.Misses} 本");
    Console.WriteLine($"  控えに無い問い合わせを含んだ本数: {missedPairs}（うち1位が正解 {missedTop1}）");

    if (detailPath is not null)
    {
        File.WriteAllLines(detailPath, ["file\tid\titemName\tquery\tindex\tcandidates\tsecondPage", .. details], new System.Text.UTF8Encoding(false));
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

Console.WriteLine($"\nBOOTH へ出た問い合わせ（合計）: {client.NetworkRequests} 本／控えから: {client.CacheHits} 本／2ページ目の検索: {client.SecondPageRequests} 本");

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
    CachingBoothClient client, string query, string itemId, string file, AvatarTokens? avatars, IReadOnlySet<string>? varying, int page = 1)
{
    var result = await client.SearchAsync(query, page);
    if (!result.IsSuccess || result.Value is null)
    {
        return (-1, -1, -1, "");
    }

    var ids = FallbackResolver.ExtractSearchResultIds(result.Value).ToList();
    var cards = FallbackResolver.ExtractSearchCards(result.Value);
    var reranked = cards.Count > 0 ? FallbackResolver.Rerank(cards, file, avatars, varying).ToList() : [];
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
                    var itemFolder = Path.GetDirectoryName(filePath);
                    pairs.Add(new Pair
                    {
                        ItemFolder = itemFolder is null ? null : Path.GetFileName(itemFolder),
                        ShopFolder = itemFolder is null || Path.GetDirectoryName(itemFolder) is not { } shopFolder ? null : Path.GetFileName(shopFolder),
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

/// <summary>
/// 全部の zip が未確定だったときの一覧を再現する。保存先の商品が持つ zip の**元の場所**から組み（読むだけ）、
/// 元のフォルダを temp の下の番号のフォルダに置き換える（同じフォルダの物は同じ番号）。
/// 正解の商品IDは使わない（画面の一覧で手に入るのは場所と名前だけ）。
/// 同じ名前の zip が2つのフォルダにあるときは、最初に見たフォルダの物を測る対象にする。
/// </summary>
static Listing ListedPaths(string storeDir, string absentRoot)
{
    var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var all = new List<string>();
    var pathOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    foreach (var path in Directory.EnumerateFiles(Path.Combine(storeDir, "items"), "*.json").Order(StringComparer.Ordinal))
    {
        if (JsonStore.Read<ItemRecord>(path) is not { } item)
        {
            continue;
        }

        foreach (var filePath in item.Local.OwnedFiles.SelectMany(file => file.Paths))
        {
            if (!filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || Path.GetDirectoryName(filePath) is not { } folder)
            {
                continue;
            }

            if (!folders.TryGetValue(folder, out var fakeFolder))
            {
                folders[folder] = fakeFolder = Path.Combine(absentRoot, folders.Count.ToString("D3"));
            }

            var fake = Path.Combine(fakeFolder, Path.GetFileName(filePath));
            all.Add(fake);
            pathOf.TryAdd(Path.GetFileName(filePath), fake);
        }
    }

    return new Listing(all, pathOf);
}

sealed record Listing(IReadOnlyList<string> All, Dictionary<string, string> PathOf);

sealed class Pair
{
    public required string File { get; init; }

    public required string Id { get; init; }

    public string? ItemName { get; init; }

    public string? Shop { get; init; }

    /// <summary>ファイルの2段上のフォルダの名前（置き方が「ショップ\商品名\ファイル」なら、ショップ名）。</summary>
    public string? ShopFolder { get; init; }

    /// <summary>ファイルの親のフォルダの名前（置き方が「ショップ\商品名\ファイル」なら、商品名）。</summary>
    public string? ItemFolder { get; init; }
}

/// <summary>置き方のフォルダ名と候補を照らす（測るための案。本体には入れていない）。</summary>
static class Layout
{
    public static int Bonus(ResolutionCandidate candidate, Pair pair)
        => (Same(candidate.Name, pair.ItemFolder) ? 2 : 0)
            + (Same(candidate.ShopName, pair.ShopFolder) || Same(candidate.ShopSubdomain, pair.ShopFolder) ? 1 : 0);

    /// <summary>
    /// 全角半角・大小・空白と記号を揃えて、同じか、短い方（4字以上）が長い方に含まれるか。
    /// フォルダ名は Windows で使えない字が消え、長い名前は切られていることがあるので、含むも一致と見る
    /// </summary>
    public static bool Same(string? name, string? folder)
    {
        var a = Loose(name);
        var b = Loose(folder);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a == b)
        {
            return true;
        }

        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        return shorter.Length >= 4 && longer.Contains(shorter, StringComparison.Ordinal);
    }

    // 販売の飾り：期限・値段の言葉。商品名の中身には出にくい語だけにする（友人の写しで、飾りの付いた157件中133件が今の商品名に戻り、
    // 飾りの無い472件で落としすぎたのは3件）
    private static readonly System.Text.RegularExpressions.Regex[] Sale =
    [
        new(@"\d{1,2}\s*[/月]\s*\d{1,2}\s*日?\s*(?:[(（]?[月火水木金土日][)）]?)?\s*(?:\d{1,2}\s*[:時]\s*\d{0,2}\s*分?)?\s*(?:まで|迄|~|～)"),
        new(@"\d{1,4}\s*(?:[(（][月火水木金土日][)）])?\s*(?:まで|迄)"),
        new(@"(?:\d+\s*(?:時間|日間|日|週間|ヶ月|か月))?\s*(?:期間|数量|時間|日数)?\s*限定\s*(?:無料|価格|セール|公開|配布|販売|特価|割引)?"),
        new(@"\d+\s*%\s*off", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"\d+\s*%\s*(?:オフ|引き|割引)"),
        new(@"\d+\s*円\s*(?:引き|off)", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"(?:無料|フリー)\s*(?:配布|公開|版)?"),
        new(@"\bfree\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"\bsale\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
        new(@"セール(?:中)?"), new(@"値下げ(?:中)?"), new(@"割引(?:中)?"), new(@"特価"), new(@"記念"),
    ];

    private static readonly System.Text.RegularExpressions.Regex Bracketed = new(@"[【\[［(（〈《][^【\[［(（〈《】\]］)）〉》]*[】\]］)）〉》]");

    private static readonly System.Text.RegularExpressions.Regex EmptyBrackets = new(@"[【\[［(（〈《][\s\-_~～・|｜!！]*[】\]］)）〉》]");

    /// <summary>
    /// 販売の飾りを落とす。括弧と飾りを交互に、変わらなくなるまで落とす（中の曜日だけ先に落ちて外の括弧が残るため）。
    /// 括弧の中の名前は、検索語の整え方（FileNameQuery）が別に扱うので、ここでは飾りだけの括弧を消す
    /// </summary>
    public static string StripDecorations(string value, string? shopFolder)
    {
        var text = value;
        for (var round = 0; round < 4; round++)
        {
            var before = text;
            foreach (var sale in Sale)
            {
                text = sale.Replace(text, " ");
            }

            text = EmptyBrackets.Replace(text, " ");
            if (text == before)
            {
                break;
            }
        }

        // 末尾の「 - ショップ名」（商品ページの題に付く）
        var dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (shopFolder is not null && dash > 0)
        {
            var tail = Loose(text[(dash + 3)..]);
            var shop = Loose(shopFolder);
            if (tail.Length > 0 && (shop.Contains(tail, StringComparison.Ordinal) || tail.Contains(shop, StringComparison.Ordinal)))
            {
                text = text[..dash];
            }
        }

        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '_', '~', '～', '・', '|', '｜', '!', '！');
        return cleaned.Length == 0 ? value : cleaned;
    }

    private static string Loose(string? value)
        => new string((value ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());
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
