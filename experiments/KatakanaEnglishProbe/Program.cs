// KatakanaEnglishProbe
//
// 日英変換（カタカナの語 → 英語・2026-09-16）を検索に入れる前に、当たりがどれだけ増え、
// 関係の無い物がどれだけ混ざるかを測る（ユーザ判断 Q18「精度を測って報告して追加してください」）。
// 本体の橋渡し（SearchBridge・同梱の JMdict）をそのまま使う。通信はしない。
//
// 物差し：
// - 探しそうな語：試験データの BOOTH タグのうち、カタカナだけの語（2文字以上）。人が付けた語なので、打たれ得る。
// - 当たり：商品名に語を含む商品（検索の既定の対象の中心）。
// - 増えた当たり：英語の候補で商品名に新しく当たった商品（元の語では当たらなかった物）。
// - 関係のある当たりの目安：その商品の BOOTH タグか説明文に、元のカタカナの語がある。
//   人が1件ずつ「探している物か」を付ける代わり。タグや説明に語が無くても関係のある物はあり得るので、目安は低めに出る。
//
// 試験データ（第三者のライブラリ）はリポジトリに入れない。**出すのは数だけ**（商品名・語は出さない）。
//
// 使い方: KatakanaEnglishProbe <store\items> <JMdict_e.gz>

using System.Globalization;
using System.Text;
using System.Text.Json;
using BoothAssetManager.Core.Search;

Console.OutputEncoding = Encoding.UTF8;
var itemsDir = args[0];
var dictionaryPath = args[1];

var items = new List<(string Name, string Tags, string Description)>();
foreach (var file in Directory.EnumerateFiles(itemsDir, "*.json"))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(file));
    if (!doc.RootElement.TryGetProperty("booth", out var booth))
    {
        continue;
    }

    var names = new List<string>();
    if (booth.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
    {
        names.Add(name.GetString()!);
    }

    if (doc.RootElement.TryGetProperty("local", out var local)
        && local.TryGetProperty("displayName", out var displayName)
        && displayName.ValueKind == JsonValueKind.String)
    {
        names.Add(displayName.GetString()!);
    }

    var tags = new List<string>();
    if (booth.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
    {
        tags.AddRange(tagArray.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String).Select(tag => tag.GetString()!));
    }

    var description = new StringBuilder();
    if (booth.TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String)
    {
        description.Append(text.GetString());
    }

    if (booth.TryGetProperty("h2Sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
    {
        foreach (var section in sections.EnumerateArray())
        {
            if (section.TryGetProperty("text", out var body) && body.ValueKind == JsonValueKind.String)
            {
                description.Append('\n').Append(body.GetString());
            }
        }
    }

    items.Add((Norm(string.Join('\n', names)), Norm(string.Join('\n', tags)), Norm(description.ToString())));
}

static string Norm(string text) => text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
static bool IsKatakanaWord(string word)
    => new StringInfo(word).LengthInTextElements >= 2 && word.All(c => c is >= 'ァ' and <= 'ヶ' or 'ー');

// 探しそうな語：カタカナだけのタグ（重複を除く）
var words = items
    .SelectMany(item => item.Tags.Split('\n'))
    .Where(IsKatakanaWord)
    .Distinct(StringComparer.Ordinal)
    .ToList();

var cacheDir = Path.Combine(Path.GetTempPath(), "katakana-english-probe");
Directory.CreateDirectory(cacheDir);
var bridge = new SearchBridge(new JapaneseDictionary(dictionaryPath, Path.Combine(cacheDir, "search-bridge.cache")));
var onlyJapaneseToEnglish = new BridgeOptions(Romaji: false, Kanji: false, EnglishToJapanese: false, JapaneseToEnglish: true);

Console.WriteLine($"商品 {items.Count} 件・探しそうな語（カタカナだけのタグ）{words.Count} 語");

// 当て方の違い：部分一致（検索の今の当て方）／英単語の区切りで当てる／短い英語（4文字未満）は使わない。
// 短い英語が別の単語の途中に当たる（top が stop に当たる）ことが、関係の無い当たりの主な出どころかを見る
var matchers = new (string Label, Func<string, string, bool> Hit, int MinLength)[]
{
    ("部分一致", (text, candidate) => text.Contains(candidate, StringComparison.Ordinal), 1),
    ("英単語の区切りで当てる", WholeWord, 1),
    ("英単語の区切り＋4文字以上", WholeWord, 4),
};

static bool WholeWord(string text, string candidate)
{
    for (var start = text.IndexOf(candidate, StringComparison.Ordinal); start >= 0;
         start = text.IndexOf(candidate, start + 1, StringComparison.Ordinal))
    {
        var before = start == 0 || !char.IsAsciiLetter(text[start - 1]);
        var end = start + candidate.Length;
        var after = end >= text.Length || !char.IsAsciiLetter(text[end]);
        if (before && after)
        {
            return true;
        }
    }

    return false;
}

foreach (var (label, hit, minLength) in matchers)
foreach (var depth in new[] { 1, 3 })
{
    var withCandidates = 0;
    var gained = 0;
    var added = 0;
    var relevant = 0;
    var candidateCount = 0;
    var baseHits = 0;

    foreach (var word in words)
    {
        var candidates = bridge.Expand(word, onlyJapaneseToEnglish)
            .Take(depth)
            .Select(c => Norm(c.Text))
            .Where(candidate => candidate.Length >= minLength)
            .ToList();
        var before = items.Where(item => item.Name.Contains(word, StringComparison.Ordinal)).ToList();
        baseHits += before.Count;
        if (candidates.Count == 0)
        {
            continue;
        }

        withCandidates++;
        candidateCount += candidates.Count;

        var after = items
            .Where(item => !item.Name.Contains(word, StringComparison.Ordinal)
                && candidates.Any(candidate => hit(item.Name, candidate)))
            .ToList();

        if (after.Count > 0)
        {
            gained++;
        }

        added += after.Count;
        relevant += after.Count(item => item.Tags.Contains(word, StringComparison.Ordinal)
            || item.Description.Contains(word, StringComparison.Ordinal));
    }

    Console.WriteLine();
    Console.WriteLine($"── {label}・英語を {depth} 語まで使う ──");
    Console.WriteLine($"英語の候補が出た語: {withCandidates} / {words.Count}（候補は平均 {(withCandidates == 0 ? 0 : (double)candidateCount / withCandidates):F1} 語）");
    Console.WriteLine($"元の語での当たり（商品名）: 延べ {baseHits} 件");
    Console.WriteLine($"当たりが増えた語: {gained} 語");
    Console.WriteLine($"増えた当たり: 延べ {added} 件");
    Console.WriteLine($"  うち、タグか説明文に元の語がある（関係のある目安）: {relevant} 件（{(added == 0 ? 0 : 100.0 * relevant / added):F0}%）");
}
