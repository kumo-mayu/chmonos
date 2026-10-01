// QueryVariantProbe
//
// ResolveAccuracyProbe で「検索の上位3件に正解が出なかった」ファイルについて、
// 2つの直し方がどれだけ効くかを測る。
//
//   ① 検索語を整える：版番号・配布形態の語・作者の略号・【…対応】の括弧・#タグを落とし、
//      日本語と英字の境目でも割る。BOOTH内検索はスペースをANDで読むので、
//      余計な1語（版番号の残り「1 02」など）が混ざるだけで0〜1件になる
//   ② 検索結果の60件を並べ直す：検索ページの商品カードには名前とショップ（data-product-name /
//      data-product-brand）が載っている。**通信を増やさずに**、ファイル名との一致・
//      シリーズ番号・ショップで並べ替えられる（連番のシリーズ物は番号を捨てると十数位に沈む）
//
// 正解のファイル名は第三者のライブラリから取るので、この試験の入力も出力もリポジトリに入れない。
//
// 公平のため、元々当たっていたファイルのうち検索語が変わるものも最大40本試す（整えて悪化しないか）。
//
// BOOTHへの問い合わせは BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。
//
// 使い方: QueryVariantProbe <resolve-search.tsv> [--out <結果.tsv>]

using System.Net;
using System.Text.RegularExpressions;
using Chmonos.Core.Booth;
using Chmonos.Core.Models;
using Chmonos.Core.Resolution;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var lines = File.ReadAllLines(args[0]).Skip(1).Select(line => line.Split('\t')).ToList();
var outIndex = Array.IndexOf(args, "--out");
var outPath = outIndex >= 0 ? args[outIndex + 1] : null;

var rows = lines.Select(cols => new Row
{
    File = cols[0],
    Id = cols[1],
    ItemName = cols[2],
    Query = cols[3],
    Rank = int.TryParse(cols[4], out var rank) ? rank : -1,
    AltHit = Regex.IsMatch(cols[6], @":[123](\s|$)"),
}).ToList();

var misses = rows.Where(row => !(row.Rank is >= 0 and < 3) && !row.AltHit).ToList();
var controls = rows.Where(row => row.Rank is >= 0 and < 3 && QueryCleaner.Clean(row.File) != row.Query).Take(40).ToList();

// 通信せずに、整えた語だけを見る。整え方の誤りで問い合わせを無駄にしないため
if (args.Contains("--dry"))
{
    foreach (var row in misses.Concat(controls))
    {
        var cleaned = QueryCleaner.Clean(row.File);
        Console.WriteLine($"{row.File} | 元「{row.Query}」→「{cleaned}」 1語「{QueryCleaner.Longest(cleaned)}」 番号[{string.Join(",", QueryCleaner.SeriesNumbers(row.File))}]");
    }

    return;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());
Console.WriteLine($"外れ {misses.Count} 本／対照（元は当たり・語が変わる）{controls.Count} 本");

var requests = 0;
var done = 0;
foreach (var row in misses.Concat(controls))
{
    row.Cleaned = QueryCleaner.Clean(row.File);
    row.Longest = QueryCleaner.Longest(row.Cleaned);

    // 整えた語（変わらなければ元の語）で引き、並べ直しも同じ結果で測る
    var cards = await SearchAsync(row.Cleaned.Length > 0 ? row.Cleaned : row.Query);
    row.CleanRank = IndexOf(cards, row.Id);
    row.RerankRank = IndexOf(Rerank(cards, row.File), row.Id);

    // それでも上位3件に来なければ、いちばん特徴のある1語だけで引く
    if (!(row.CleanRank is >= 0 and < 3) && !(row.RerankRank is >= 0 and < 3)
        && row.Longest.Length > 0 && row.Longest != row.Cleaned)
    {
        var longestCards = await SearchAsync(row.Longest);
        row.LongestRank = IndexOf(longestCards, row.Id);
        row.LongestRerankRank = IndexOf(Rerank(longestCards, row.File), row.Id);
    }

    if (++done % 20 == 0)
    {
        Console.WriteLine($"  {done}/{misses.Count + controls.Count}（問い合わせ {requests} 本）");
    }
}

Report("外れていた分", misses);
Report("対照（元は当たり）", controls);

if (outPath is not null)
{
    using var writer = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false));
    writer.WriteLine("file\tid\titemName\tquery\trank\tcleaned\tcleanRank\trerankRank\tlongest\tlongestRank\tlongestRerankRank");
    foreach (var row in misses.Concat(controls))
    {
        writer.WriteLine(string.Join('\t', row.File, row.Id, row.ItemName, row.Query, row.Rank, row.Cleaned,
            row.CleanRank, row.RerankRank, row.Longest, row.LongestRank?.ToString() ?? "", row.LongestRerankRank?.ToString() ?? ""));
    }
}

void Report(string title, List<Row> set)
{
    bool Top3(int? rank) => rank is >= 0 and < 3;
    Console.WriteLine($"\n{title}: {set.Count} 本　問い合わせ累計 {requests} 本");
    Console.WriteLine($"  元の語で上位3件      : {set.Count(row => Top3(row.Rank))}");
    Console.WriteLine($"  整えた語で上位3件    : {set.Count(row => Top3(row.CleanRank))}");
    Console.WriteLine($"  整えた語＋並べ直し   : {set.Count(row => Top3(row.RerankRank))}");
    Console.WriteLine($"  ＋1語だけで引き直し  : {set.Count(row => Top3(row.RerankRank) || Top3(row.LongestRank) || Top3(row.LongestRerankRank))}");
    Console.WriteLine($"  60件の中には居た     : {set.Count(row => row.CleanRank >= 0 || row.LongestRank >= 0)}");
}

async Task<List<Card>> SearchAsync(string query)
{
    requests++;
    var result = await client.SearchAsync(query);
    return result.IsSuccess && result.Value is not null ? Card.Parse(result.Value) : [];
}

static int IndexOf(List<Card> cards, string id) => cards.FindIndex(card => card.Id == id);

// 検索カードだけで点を付ける。JSONを取らないので通信は増えない
static List<Card> Rerank(List<Card> cards, string file)
{
    var tokens = QueryCleaner.Tokens(file);
    var numbers = QueryCleaner.SeriesNumbers(file);
    var raw = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

    return cards
        .Select((card, position) =>
        {
            var score = tokens.Count(token => FileNameQuery.LooksRelated(card.Name, token)) * 2;

            // シリーズの番号。「練習用ポーズ集13」の13が商品名に語として出るか
            if (numbers.Any(number => Regex.IsMatch(card.Name, $@"(?<![0-9]){number}(?![0-9])")))
            {
                score += 3;
            }

            // ファイル名の頭や尻にショップのサブドメインが入っていることが多い（sampleflow, samplecat）
            if (card.Brand.Length >= 3 && raw.Contains(card.Brand.ToLowerInvariant()))
            {
                score += 3;
            }

            return (card, score, position);
        })
        .OrderByDescending(entry => entry.score)
        .ThenBy(entry => entry.position)
        .Select(entry => entry.card)
        .ToList();
}

sealed record Card(string Id, string Name, string Brand)
{
    private static readonly Regex CardRegex = new(@"<li[^>]*class=""item-card[^""]*""[^>]*>", RegexOptions.Compiled);

    public static List<Card> Parse(string html)
    {
        var cards = new List<Card>();
        foreach (Match tag in CardRegex.Matches(html))
        {
            var id = Attr(tag.Value, "data-product-id");
            if (id.Length > 0)
            {
                cards.Add(new Card(id, Attr(tag.Value, "data-product-name"), Attr(tag.Value, "data-product-brand")));
            }
        }

        return cards;
    }

    private static string Attr(string tag, string name)
    {
        var match = Regex.Match(tag, name + @"=""([^""]*)""");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }
}

/// <summary>検索語の整え方の試作。本体の FileNameQuery に入れる前に効果を測るためのもの。</summary>
static class QueryCleaner
{
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "append", "addon", "update", "fullset", "fullpack", "full", "set", "pack", "unitypackage", "unity",
        "vrc", "vrchat", "sample", "trial", "readme", "installer", "license", "material", "materials", "psd",
        "lite", "free", "ver", "version", "fix", "fixed", "ma", "quest", "pc", "android", "prefab", "texture",
        "無料", "改", "改変用", "修正版", "調整版", "更新", "最新", "配布", "full_pack",
    };

    public static List<string> Tokens(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        name = Regex.Replace(name, @"\.unitypackage$", string.Empty, RegexOptions.IgnoreCase);

        // 括弧の中は【マリシア対応】（2）のような付け足しが多い。中身が名前だけのときに備えて、
        // 全部消えてしまうなら括弧だけ外す
        var stripped = Regex.Replace(name, @"[【\[［(（〈《「『][^】\]］)）〉》」』]*[】\]］)）〉》」』]", " ");
        if (Regex.Replace(stripped, @"[\s_\-.]", string.Empty).Length == 0)
        {
            stripped = Regex.Replace(name, @"[【\[［(（〈《「『】\]］)）〉》」』]", " ");
        }

        stripped = Regex.Replace(stripped, @"#\S+", " ");

        // 日本語と英字の境目、英字の大文字小文字の境目で割る
        stripped = Regex.Replace(stripped, @"(?<=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}ー])(?=[A-Za-z0-9])|(?<=[A-Za-z0-9])(?=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])", " ");
        stripped = Regex.Replace(stripped, @"(?<=[a-z])(?=[A-Z])", " ");

        return Regex.Split(stripped, @"[\s_\-.　・＿~〜+＋,&]+")
            .Where(token => token.Length > 0)
            .Where(token => !Noise.Contains(token))
            // 版番号・連番・x.x.x・v1・r2 は商品名ではない
            .Where(token => !Regex.IsMatch(token, @"^(v|ver|r)?\d+[a-z]?$|^x+$", RegexOptions.IgnoreCase))
            // 1〜2文字の大文字は作者の略号（WH_ / QW_ / S_）であることが多い
            .Where(token => !Regex.IsMatch(token, @"^[A-Z]{1,2}$"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string Clean(string file) => string.Join(' ', Tokens(file));

    /// <summary>いちばん特徴のある1語。日本語は2文字以上、英字は4文字以上で最長のもの。</summary>
    public static string Longest(string cleaned)
        => cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.All(char.IsAscii) ? token.Length >= 4 : token.Length >= 2)
            .OrderByDescending(token => token.All(char.IsAscii) ? token.Length : token.Length * 2)
            .FirstOrDefault() ?? string.Empty;

    /// <summary>シリーズ番号の候補。版番号（1.2.0 の各桁）ではなく、語に直接付いた番号を拾う。</summary>
    public static List<string> SeriesNumbers(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return Regex.Matches(name, @"(?<=[^\d._vV])(\d{1,3})(?=$|[_\s(（【])")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
    }
}

sealed class Row
{
    public required string File { get; init; }

    public required string Id { get; init; }

    public required string ItemName { get; init; }

    public required string Query { get; init; }

    public int Rank { get; init; }

    public bool AltHit { get; init; }

    public string Cleaned { get; set; } = string.Empty;

    public string Longest { get; set; } = string.Empty;

    public int CleanRank { get; set; } = -1;

    public int RerankRank { get; set; } = -1;

    public int? LongestRank { get; set; }

    public int? LongestRerankRank { get; set; }
}
