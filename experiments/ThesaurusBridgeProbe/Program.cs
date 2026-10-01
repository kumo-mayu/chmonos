// ThesaurusBridgeProbe
//
// 曖昧検索の「幅」（#66）：類義語辞書2つ（Sudachi 同義語辞書・日本語 WordNet の確度の高い語）に、
// 今の表記の橋渡し（SearchBridge：ローマ字→読み→カナ・JMdict、英語→JMdict）を重ねると、
// どれだけ広く探せるかを測る。本体の橋渡しをそのまま使う。
//
// 物差しは docs/research/fuzzy-search.md §6 と同じ：探しそうな語を当て、広げた語で新しく当たる商品を数える。
// 1文字の語は部分一致で何にでも当たるので、どの形でも除く。
//
// 類義語辞書のファイルと試験データ（第三者のライブラリ）はリポジトリに入れない。通信はしない。
//
// 使い方: ThesaurusBridgeProbe <store\items> <類義語辞書のフォルダ> [<書き出し先.tsv>]
//
// 書き出し先を渡すと、推しの形（日本語＝類義語2つ、ローマ字・英語＝橋渡し→類義語2つ）で
// 新しく当たった商品を1行ずつ書く（打った語・どの道で広がったか・広がった語・商品ID・商品名）。
// 人が「探している物か」を付けて、関係の無い物がどれだけ混ざるかを数えるため（§9）。
// 商品名が入るので、書き出し先はリポジトリの外にする。

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Search;

Console.OutputEncoding = Encoding.UTF8;
var itemsDir = args[0];
var thesaurusDir = args[1];

// ── 商品名とタグ ──
var items = new List<(string Id, string Text)>();
foreach (var file in Directory.EnumerateFiles(itemsDir, "*.json"))
{
    if (!Path.GetFileNameWithoutExtension(file).All(char.IsAsciiDigit))
    {
        continue;
    }

    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    if (!doc.RootElement.TryGetProperty("booth", out var booth))
    {
        continue;
    }

    var parts = new List<string>();
    if (booth.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
    {
        parts.Add(name.GetString()!);
    }

    if (booth.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
    {
        foreach (var tag in tags.EnumerateArray())
        {
            parts.Add(tag.ValueKind == JsonValueKind.String ? tag.GetString()! :
                tag.TryGetProperty("name", out var tagName) ? tagName.GetString() ?? "" : "");
        }
    }

    items.Add((Path.GetFileName(file), Norm(string.Join(' ', parts))));
}

static string Norm(string text) => text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
HashSet<string> Hits(string word) => items.Where(item => item.Text.Contains(Norm(word), StringComparison.Ordinal)).Select(item => item.Id).ToHashSet();
static bool LongEnough(string word) => new StringInfo(word).LengthInTextElements >= 2;

// ── Sudachi 同義語辞書 ──
var sudachiGroups = new Dictionary<string, List<(string Word, string Control)>>();
var sudachiOf = new Dictionary<string, List<(string Group, string Control)>>();
foreach (var line in File.ReadLines(Path.Combine(thesaurusDir, "sudachi-synonyms.txt")))
{
    var c = line.Split(',');
    if (c.Length < 9 || c[2] == "2" || c[4] == "4" || c[8].Length == 0)
    {
        continue;
    }

    if (!sudachiGroups.TryGetValue(c[0], out var members)) { members = []; sudachiGroups[c[0]] = members; }
    members.Add((c[8], c[2]));
    if (!sudachiOf.TryGetValue(c[8], out var groups)) { groups = []; sudachiOf[c[8]] = groups; }
    groups.Add((c[0], c[2]));
}

IEnumerable<string> Sudachi(string word) =>
    sudachiOf.TryGetValue(word, out var groups)
        ? groups.Where(g => g.Control != "1").SelectMany(g => sudachiGroups[g.Group]).Select(m => m.Word).Where(w => w != word)
        : [];

// ── 日本語 WordNet（確度の高い語だけ） ──
var wnBySynset = new Dictionary<string, HashSet<string>>();
var wnByWord = new Dictionary<string, HashSet<string>>();
using (var gz = new GZipStream(File.OpenRead(Path.Combine(thesaurusDir, "wnjpn-ok.tab.gz")), CompressionMode.Decompress))
using (var reader = new StreamReader(gz, Encoding.UTF8))
{
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        var c = line.Split('\t');
        if (c.Length < 2) continue;
        if (!wnBySynset.TryGetValue(c[0], out var words)) { words = []; wnBySynset[c[0]] = words; }
        words.Add(c[1]);
        if (!wnByWord.TryGetValue(c[1], out var synsets)) { synsets = []; wnByWord[c[1]] = synsets; }
        synsets.Add(c[0]);
    }
}

IEnumerable<string> WordNet(string word) =>
    wnByWord.TryGetValue(word, out var synsets) ? synsets.SelectMany(s => wnBySynset[s]).Where(w => w != word) : [];

IEnumerable<string> Thesaurus(string word) => Sudachi(word).Concat(WordNet(word)).Where(LongEnough).Distinct();

// ── 今の表記の橋渡し ──
var assets = Path.Combine(AppContext.BaseDirectory, "assets");
var dictionary = new JapaneseDictionary(Path.Combine(assets, "JMdict_e.gz"), Path.Combine(Path.GetTempPath(), "thesaurus-bridge-probe.cache"));
var bridge = new SearchBridge(dictionary);

IEnumerable<string> Bridge(string word) => bridge.Expand(word).Select(c => c.Text).Where(LongEnough);

static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー';
static string ToHiragana(string text) => new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

// 日本語の語の書き換え：かな⇄カタカナ、かなの読みから JMdict の表記（めがね→眼鏡）
IEnumerable<string> Kana(string word)
{
    if (word.Length == 0 || !word.All(IsKana))
    {
        yield break;
    }

    var hiragana = ToHiragana(word);
    yield return hiragana;
    yield return RomajiReading.ToKatakana(hiragana);
    foreach (var form in dictionary.ByReading(hiragana))
    {
        yield return form;
    }
}

IEnumerable<string> KanaAll(string word) => Kana(word).Where(w => w != word && LongEnough(w)).Distinct();

// ── 形 ──
var japanese = new (string Name, Func<string, IEnumerable<string>> Expand)[]
{
    ("類義語2つ（前回の推し）", w => Thesaurus(w)),
    ("＋広げた語をかな・カナ・漢字にも", w => Thesaurus(w).Concat(KanaAll(w)).Concat(Thesaurus(w).SelectMany(KanaAll))),
    ("＋書き換えた語にも類義語", w =>
    {
        var first = Thesaurus(w).Concat(KanaAll(w)).ToList();
        var second = KanaAll(w).SelectMany(Thesaurus);
        return first.Concat(second).Concat(first.Concat(second).SelectMany(KanaAll));
    }),
};

var latin = new (string Name, Func<string, IEnumerable<string>> Expand)[]
{
    ("今の橋渡しだけ", w => Bridge(w)),
    ("橋渡し→類義語2つ", w => Bridge(w).Concat(Bridge(w).SelectMany(Thesaurus))),
    ("橋渡し→類義語→かな・カナ・漢字", w =>
    {
        var widened = Bridge(w).Concat(Bridge(w).SelectMany(Thesaurus)).ToList();
        return widened.Concat(widened.SelectMany(KanaAll));
    }),
};

string[] japaneseProbes = ["帽子", "猫耳", "制服", "眼鏡", "メガネ", "靴", "ブーツ", "髪", "髪型", "ネイル", "尻尾", "しっぽ", "羽", "翼",
    "リボン", "ピアス", "イヤリング", "指輪", "リング", "ドレス", "水着", "パーカー", "衣装", "武器", "剣", "銃", "花", "天使",
    "悪魔", "猫", "犬", "うさぎ", "狐", "着物", "浴衣", "眼帯", "マスク", "首輪", "チョーカー", "ヘアピン"];

string[] latinProbes = ["megane", "boushi", "neko", "usagi", "kitsune", "tsubasa", "hane", "shippo", "ribbon", "hat", "shoes", "boots",
    "tail", "wing", "ring", "dress", "glasses", "cat", "rabbit", "fox", "sword", "gun", "flower", "angel", "devil", "mask", "collar",
    "choker", "nail", "hair", "kimono", "yukata", "earring", "pierce", "hoodie", "uniform", "weapon", "swimsuit", "eyepatch", "hairpin"];

Console.WriteLine($"商品 {items.Count} 件／Sudachi の語 {sudachiOf.Count}／WordNet の語 {wnByWord.Count}／JMdict {(dictionary.IsAvailable ? "あり" : "無し")}\n");

Measure("日本語で打った語", japaneseProbes, japanese);
Measure("ローマ字・英語で打った語", latinProbes, latin);

if (args.Length >= 3)
{
    Dump(args[2]);
}

// 推しの形で新しく当たった商品を、どの道で広がったかと一緒に1行ずつ書く。
// 同じ打った語で同じ商品が何度も当たるときは、最初に当たった1行だけにする（数え直さない）
void Dump(string path)
{
    var names = Directory.EnumerateFiles(itemsDir, "*.json")
        .Where(file => Path.GetFileNameWithoutExtension(file).All(char.IsAsciiDigit))
        // メソッドの名前のまま渡すと、鍵の型が「null かもしれない文字列」と推論されて警告になる。
        // 引数が null でなければ戻りも null でない、という宣言は、呼び出しの形で書いたときだけ効く
        .ToDictionary(file => Path.GetFileName(file), file =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("booth", out var booth) && booth.TryGetProperty("name", out var name)
                ? name.GetString() ?? string.Empty
                : string.Empty;
        });

    IEnumerable<(string Route, string Word)> Routes(string probe, bool isLatin)
    {
        if (!isLatin)
        {
            foreach (var word in Sudachi(probe).Where(LongEnough)) yield return ("Sudachi", word);
            foreach (var word in WordNet(probe).Where(LongEnough)) yield return ("WordNet", word);
            yield break;
        }

        foreach (var bridged in Bridge(probe).Distinct())
        {
            yield return ("橋渡し", bridged);
            foreach (var word in Sudachi(bridged).Where(LongEnough)) yield return ("橋渡し→Sudachi", word);
            foreach (var word in WordNet(bridged).Where(LongEnough)) yield return ("橋渡し→WordNet", word);
        }
    }

    using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
    writer.WriteLine("打った語\t道\t広がった語\t商品ID\t商品名\t探している物か");
    var rows = 0;
    foreach (var (probes, isLatin) in new[] { (japaneseProbes, false), (latinProbes, true) })
    {
        foreach (var probe in probes)
        {
            var seen = Hits(probe);
            foreach (var (route, word) in Routes(probe, isLatin))
            {
                if (string.Equals(word, probe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var id in Hits(word).Where(seen.Add))
                {
                    writer.WriteLine($"{probe}\t{route}\t{word}\t{Path.GetFileNameWithoutExtension(id)}\t{names.GetValueOrDefault(id)}\t");
                    rows++;
                }
            }
        }
    }

    Console.WriteLine($"書き出した: {path}（{rows} 行）");
}

void Measure(string title, string[] probes, (string Name, Func<string, IEnumerable<string>> Expand)[] forms)
{
    Console.WriteLine($"=== {title}（{probes.Length} 語）");
    var totals = forms.Select(_ => (Widened: 0, Words: 0, Gain: 0, Big: 0)).ToArray();

    foreach (var probe in probes)
    {
        var baseHits = Hits(probe);
        var line = new StringBuilder($"{probe}（元 {baseHits.Count}件）");
        for (var i = 0; i < forms.Length; i++)
        {
            var words = forms[i].Expand(probe).Where(w => !string.Equals(w, probe, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
            var extra = new HashSet<string>();
            var useful = new List<string>();
            foreach (var word in words)
            {
                var added = Hits(word).Where(id => !baseHits.Contains(id)).ToList();
                if (added.Count > 0)
                {
                    useful.Add($"{word}+{added.Count}");
                    extra.UnionWith(added);
                    // 1語で20件以上増えるのは、たいてい語の一部に当たっている雑音
                    if (added.Count >= 20) totals[i].Big++;
                }
            }

            totals[i].Words += words.Count;
            totals[i].Gain += extra.Count;
            if (words.Count > 0) totals[i].Widened++;
            line.Append($"\n   {forms[i].Name}: {words.Count}語 → 新しく {extra.Count}件  当たった語[{string.Join(" ", useful.Take(8))}]");
        }

        Console.WriteLine(line);
    }

    Console.WriteLine($"── 合計（{title}）");
    for (var i = 0; i < forms.Length; i++)
    {
        Console.WriteLine($"   {forms[i].Name}: 広げられた語 {totals[i].Widened}/{probes.Length}・広がる語 {totals[i].Words}・新しく当たった商品の延べ {totals[i].Gain}・1語で20件以上増えた語 {totals[i].Big}");
    }

    Console.WriteLine();
}
