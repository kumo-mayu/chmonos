// FallbackSearchProbe
//
// 「取り込みの候補検索を、表記をまたいで引き直す価値があるか」を実測する。
//
// 手掛かりの無いファイルには、ファイル名から作った語でBOOTH内検索をかけている。
// ローマ字のファイル名（Tori_v1_1_1.zip）が日本語の商品（『Bird/鳥』）を指しているとき、
// その検索は当たらないのではないか——という疑いを確かめる。
//
//   1回目：今と同じ、ファイル名から作った語で検索する
//   2回目：1回目で当たらなかったものだけ、読みから作った別表記で検索し直す
//
// **候補のJSONは取らない。**知りたいのは「検索結果に正解が出るか」だけで、
// 点数付けは通信の要らない側で既に測ってある。1ファイルにつき1リクエストに抑える。
//
// BOOTHへの問い合わせは BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。

using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Search;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// 手元のライブラリから取った正解の対応。ファイル名 → 商品ID
var cases = new (string FileName, string ItemId, string ItemName)[]
{
    ("Tori_v1_1_1.zip", "5927710", "オリジナル3Dモデル『Bird/鳥』"),
    ("Sig_Ring_07_ver2.zip", "3565798", "【VRChat想定】指輪モデル_Ⅶ"),
    ("HeartBeatGimmick_v3.0.3.zip", "5316535", "なめらか心音ギミック 3.0"),
    ("SinAvatarPen_v1.2.2.zip", "7881802", "真・アバターペンシステム"),
    ("Bracelet_tamakurage.v1.01.zip", "6871614", "【MA対応】…「タマクラゲ」"),
    ("rurune_v1.1.3.zip", "5957830", "サメっ子オリジナル3Dモデル「rurune」"),
    ("Kuuta_ShapekeyAddon.zip", "6580186", "Shapekey Add-on for Kuuta"),
    ("hotogiya_Kuuta_ver1.03.zip", "4897493", "【くうた-Kuuta-】オリジナル3Dモデル"),

    // 対照：ラテン文字の商品名なので、今でも当たるはず
    ("Kipfel_1.2.0.zip", "5813187", "キプフェル Kipfel"),
    ("Wendy_ver1.01.zip", "7841391", "【オリジナル3Dモデル】Wendy"),
};

/// <summary>候補検索が実際に見るのは上位3件だけ。それより下は「出ていない」のと同じ。</summary>
const int UsedCandidates = 3;

var assets = Path.Combine(AppContext.BaseDirectory, "assets");
var bridge = new SearchBridge(new JapaneseDictionary(
    Path.Combine(assets, "JMdict_e.gz"),
    Path.Combine(Path.GetTempPath(), "fallback-probe-bridge.cache")));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());

Console.WriteLine($"BOOTHへの間隔: {client.CurrentIntervalMs}ms（1本ずつ）");
Console.WriteLine($"1回目: {cases.Length} 件\n");

var missed = new List<(string FileName, string ItemId, string ItemName, string Query)>();
var firstPassHits = 0;

foreach (var (fileName, itemId, itemName) in cases)
{
    var query = FileNameQuery.ToSearchQuery(fileName);
    var rank = await RankOfAsync(query, itemId);

    if (rank >= 0 && rank < UsedCandidates)
    {
        firstPassHits++;
        Console.WriteLine($"○ {fileName,-32} 「{query}」 → {rank + 1}位");
    }
    else
    {
        var where = rank < 0 ? "検索結果に無し" : $"{rank + 1}位（上位{UsedCandidates}件の外）";
        Console.WriteLine($"× {fileName,-32} 「{query}」 → {where}");
        missed.Add((fileName, itemId, itemName, query));
    }
}

Console.WriteLine($"\n1回目で当たった: {firstPassHits}/{cases.Length}");

if (missed.Count == 0)
{
    Console.WriteLine("外れが無いので2回目は要らない。");
    return;
}

Console.WriteLine($"\n2回目: 外れた {missed.Count} 件を、読みから作った別表記で引き直す\n");

var secondPassHits = 0;

foreach (var (fileName, itemId, itemName, query) in missed)
{
    // 読みの経路だけを使う。ラテン文字のファイル名は日本語商品のローマ字表記であって
    // 英訳ではない（英語の経路は Sin→罪業、Ring→土俵 のような語しか作らなかった）
    var alternates = query
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .SelectMany(token => bridge.Expand(token))
        .Where(candidate => candidate.Via != BridgeRoute.English)
        .Select(candidate => candidate.Text)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    // 辞書から出た表記を先に試す。かなよりも商品名に使われている見込みが高い
    var ordered = alternates
        .OrderByDescending(text => text.Any(c => c is >= '一' and <= '鿿'))
        .ToList();

    if (ordered.Count == 0)
    {
        Console.WriteLine($"－ {fileName,-32} 別表記が作れない → 何もしない（通信も増えない）");
        continue;
    }

    var pick = ordered[0];
    var rank = await RankOfAsync(pick, itemId);

    if (rank >= 0 && rank < UsedCandidates)
    {
        secondPassHits++;
        Console.WriteLine($"○ {fileName,-32} 「{pick}」 → {rank + 1}位　（{itemName}）");
    }
    else
    {
        var where = rank < 0 ? "無し" : $"{rank + 1}位（外）";
        Console.WriteLine($"× {fileName,-32} 「{pick}」 → {where}　候補: {string.Join(" ", ordered.Take(4))}");
    }
}

Console.WriteLine($"\n2回目で拾えた: {secondPassHits}/{missed.Count}");

// ── 3回目：語を「置き換える」形 ──
//
// 2回目は別表記を1語だけ投げていた。BOOTH内検索はスペースをANDで読むので、
// 「くうた」だけで引くと、くうた関連の商品が全部並んで目的の物が埋もれる。
// 他の語を残したまま1語だけ差し替えれば、絞りが効くはず。
Console.WriteLine("\n3回目: 他の語を残したまま、1語だけ別表記に差し替える\n");

var thirdPassHits = 0;
var stillMissed = missed.Where(m => m.Query.Contains(' ')).ToList();

foreach (var (fileName, itemId, itemName, query) in stillMissed)
{
    var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    var found = false;

    for (var i = 0; i < tokens.Count && !found; i++)
    {
        var alternate = bridge.Expand(tokens[i])
            .Where(candidate => candidate.Via != BridgeRoute.English)
            .Select(candidate => candidate.Text)
            .FirstOrDefault();

        if (alternate is null)
        {
            continue;
        }

        var swapped = tokens.ToList();
        swapped[i] = alternate;
        var rewritten = string.Join(' ', swapped);

        var rank = await RankOfAsync(rewritten, itemId);
        if (rank >= 0 && rank < UsedCandidates)
        {
            thirdPassHits++;
            found = true;
            Console.WriteLine($"○ {fileName,-32} 「{rewritten}」 → {rank + 1}位");
        }
        else
        {
            var where = rank < 0 ? "無し" : $"{rank + 1}位（外）";
            Console.WriteLine($"× {fileName,-32} 「{rewritten}」 → {where}");
        }
    }
}

Console.WriteLine($"\n3回目で拾えた: {thirdPassHits}/{stillMissed.Count}");
Console.WriteLine($"合計: {firstPassHits}/{cases.Length} → {firstPassHits + secondPassHits + thirdPassHits}/{cases.Length}");

async Task<int> RankOfAsync(string query, string itemId)
{
    var result = await client.SearchAsync(query);
    if (!result.IsSuccess || result.Value is null)
    {
        Console.WriteLine($"  （検索できませんでした: {result.Error}）");
        return -1;
    }

    var ids = FallbackResolver.ExtractSearchResultIds(result.Value);
    return ids.ToList().IndexOf(itemId);
}
