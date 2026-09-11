// 名簿の表示名の付け方を比べる評価台（#54）。本体は読むだけ。
//
// 正解は、試験データの store-clean の表示名（#58 で読めない66体を人が付け直した版）。
// 友人のデータは第三者のものなので、名前の出る結果は試験データの置き場所にだけ書き、画面には数だけ出す。
//
// 使い方: dotnet run --project experiments/AvatarNameBench [試験データのフォルダ]
using System.Text;
using System.Text.Json;
using BoothAssetManager.Core.Services;

Console.OutputEncoding = Encoding.UTF8;
var dir = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoothAssetManager-eval");

var raw = Load(Path.Combine(dir, "store", "avatar-registry.json"));
var clean = Load(Path.Combine(dir, "store-clean", "avatar-registry.json")).ToDictionary(entry => entry.Id);

var rows = raw
    .Where(entry => entry.Category == "3Dキャラクター" && clean.ContainsKey(entry.Id))
    .Select(entry => new Row(
        entry.Id,
        entry.BoothName,
        Gold: clean[entry.Id].DisplayName,
        Stored: entry.DisplayName,
        Shortened: AvatarText.ShortenName(entry.BoothName, clean[entry.Id].Aliases),
        Proposed: AvatarText.DisplayNameFrom(entry.BoothName, clean[entry.Id].Aliases)))
    .ToList();

Console.WriteLine($"アバター {rows.Count} 体（正解を付け直した物 {rows.Count(row => row.Stored != row.Gold)} 体）");
Console.WriteLine();
Console.WriteLine("付け方                  正解と一致  正解の頭を含む  読める   読めない  同じ名前の組");
Report("保存されていた表示名", row => row.Stored);
Report("今の付け方（最短の呼び名）", row => row.Shortened);
Report("新しい付け方", row => row.Proposed);

// 読めなかった物と、正解と食い違った物を、試験データの置き場所へ書く（人の目で確かめる）
var unreadable = rows.Where(row => !Readable(row.Proposed, row.BoothName)).ToList();
var differs = rows.Where(row => Readable(row.Proposed, row.BoothName) && !HasGoldHead(row.Proposed, row.Gold)).ToList();
File.WriteAllLines(
    Path.Combine(dir, "names-proposed-miss.tsv"),
    new[] { "種類\titemId\tboothName\tproposed\tgold" }
        .Concat(unreadable.Select(row => $"読めない\t{row.Id}\t{row.BoothName}\t{row.Proposed}\t{row.Gold}"))
        .Concat(differs.Select(row => $"正解と違う\t{row.Id}\t{row.BoothName}\t{row.Proposed}\t{row.Gold}")),
    new UTF8Encoding(false));
Console.WriteLine();
Console.WriteLine($"新しい付け方で読めない {unreadable.Count} 体・読めるが正解の頭を含まない {differs.Count} 体 → names-proposed-miss.tsv");

// 移行の見分け：保存されている表示名が「以前の付け方で自動に付いた物」と分かるか。
// 分かった物は消して新しい付け方に任せ、分からない物は手で付けた名前として残す
var rawById = raw.ToDictionary(entry => entry.Id);
// 本体の AvatarNames.ManualName と同じ見分け方
bool LooksAuto(Row row)
{
    var entry = rawById[row.Id];
    return row.Stored.Length == 0
        || row.Stored == entry.Id
        || row.Stored == entry.BoothName
        || AvatarText.IsNotAName(row.Stored)
        || row.Stored == AvatarText.ShortenName(entry.BoothName)
        || row.Stored == AvatarText.ShortenName(entry.BoothName, entry.Aliases);
}

var recognized = rows.Count(LooksAuto);
Console.WriteLine($"移行：保存されていた表示名のうち、自動で付いた物と見分けられる {recognized} / {rows.Count} 体");

// 見分けられずに「手で付けた名前」として残る物。読めない名前が紛れていないか、人の目で確かめる
var kept = rows.Where(row => !LooksAuto(row)).ToList();
File.WriteAllLines(
    Path.Combine(dir, "names-migration-kept.tsv"),
    new[] { "itemId\tboothName\tstored（残る名前）\tproposed（自動なら）\tgold\t正解と違う" }
        .Concat(kept.Select(row => $"{row.Id}\t{row.BoothName}\t{row.Stored}\t{row.Proposed}\t{row.Gold}\t{(row.Stored != row.Gold ? "違う" : "")}")),
    new UTF8Encoding(false));
Console.WriteLine($"手で付けた名前として残る {kept.Count} 体のうち、正解と違う（読めない名前が残る）{kept.Count(row => row.Stored != row.Gold)} 体 → names-migration-kept.tsv");

// 新しい付け方で同じ名前になった組。本当に同じ名前のアバターか、人の目で確かめる
var dupGroups = rows.GroupBy(row => AvatarText.Normalize(row.Proposed)).Where(group => group.Count() > 1).ToList();
File.WriteAllLines(
    Path.Combine(dir, "names-proposed-dups.tsv"),
    new[] { "組\titemId\tboothName\tproposed" }
        .Concat(dupGroups.SelectMany((group, number) => group.Select(row => $"{number + 1}\t{row.Id}\t{row.BoothName}\t{row.Proposed}"))),
    new UTF8Encoding(false));
Console.WriteLine($"同じ名前の組 {dupGroups.Count} 組 → names-proposed-dups.tsv");

void Report(string label, Func<Row, string> pick)
{
    var exact = rows.Count(row => Same(pick(row), row.Gold));
    var head = rows.Count(row => HasGoldHead(pick(row), row.Gold));
    var readable = rows.Count(row => Readable(pick(row), row.BoothName));
    var dups = rows.GroupBy(row => AvatarText.Normalize(pick(row))).Count(group => group.Count() > 1);
    Console.WriteLine($"{label,-20} {exact,8} {head,12} {readable,8} {rows.Count - readable,8} {dups,10}");
}

// 正解と完全に同じ（空白と記号の違いは見ない）
static bool Same(string a, string b) => Letters(a) == Letters(b);

// 正解の名前の頭が入っているか。正解は人が付けたもので「梵 soyogi」「灰島-haishima」のような揺れがある
static bool HasGoldHead(string candidate, string gold)
{
    var head = gold.Trim().TrimStart('『', '「', '#', '+', '【')
        .Split([' ', '　', '-', '_', '(', '[', '/', '&', '＆', '『', '』', '〈'], StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault() ?? gold;
    return Letters(candidate).Contains(Letters(head), StringComparison.Ordinal);
}

// 読める：商品名の一部で（作った名前ではない）、名前ではない語が無く、長すぎない。
// 名前が2通りで書かれた商品（「Ciel - シエル -」）はどちらを採っても読めるので、正解との一致とは別に数える
static bool Readable(string candidate, string boothName)
{
    var c = Letters(candidate);
    var booth = Letters(boothName);
    // 「Ciel（シエル）」は名前と読みを別々に商品名の一部か確かめる
    var parts = candidate.Split(['（', '）'], StringSplitOptions.RemoveEmptyEntries).Select(Letters).Where(part => part.Length > 0).ToList();
    return c.Length > 0
        && candidate.Length <= 24
        && parts.All(part => booth.Contains(part, StringComparison.Ordinal))
        && !new[] { "オリジナル", "original", "3dモデル", "vrchat", "アバター", "avatar", "mobile", "ver" }.Any(noise => c.Contains(noise, StringComparison.Ordinal));
}

static string Letters(string text)
    => new(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

static List<Entry> Load(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement.GetProperty("entries").EnumerateArray()
        .Select(element => new Entry(
            element.GetProperty("itemId").GetString() ?? string.Empty,
            Text(element, "boothName"),
            Text(element, "displayName"),
            Text(element, "category"),
            element.TryGetProperty("aliases", out var aliases)
                ? aliases.EnumerateArray().Select(alias => Text(alias, "text")).Where(text => text.Length > 0).ToList()
                : []))
        .ToList();
}

static string Text(JsonElement element, string name)
    => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

sealed record Entry(string Id, string BoothName, string DisplayName, string Category, IReadOnlyList<string> Aliases);

sealed record Row(string Id, string BoothName, string Gold, string Stored, string Shortened, string Proposed);
