// ZipOriginProbe
//
// 未確定を元zipで束ねたとき（#60）に、
//   ① 束がいくつになるか（今のフォルダ束ねと比べて）
//   ② 元zipの名前で自動検索すると何が出るか（今の「フォルダ名で引く」と比べて）
// を見る。
//
// 入力は2通り。
//   --unresolved <unresolved.json> : 保存済みの未確定。保存された ReferrerUrl で束ねる（本体の画面と同じ道）
//   --scan <フォルダ>              : 実際のファイル。Zone.Identifier を読んで記録を作る（本体の取り込みの走査と同じ）
// 第三者のデータを入れることがあるので、入力も出力もリポジトリに入れない。
//
// BOOTHへの問い合わせは BoothClient を通すので、必ず1本ずつ・1.5秒空けて出る。
// 自動検索は1束につき本体と同じ ProposeAsync を1回（候補ごとに商品JSONを取るので4本前後）。
//
// 使い方: ZipOriginProbe (--unresolved <json> | --scan <フォルダ>) [--search] [--compare]

using System.Text.Json;
using BoothAssetManager.Core.Booth;
using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Resolution;
using BoothAssetManager.Core.Scanning;
using BoothAssetManager.Core.Search;

Console.OutputEncoding = System.Text.Encoding.UTF8;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var files = new List<UnresolvedFile>();
string? scanRoot = null;

if (Option("--unresolved") is { } json)
{
    files.AddRange(JsonSerializer.Deserialize<List<UnresolvedFile>>(
        File.ReadAllText(json), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
}

if (Option("--scan") is { } root)
{
    scanRoot = Path.TrimEndingDirectorySeparator(root);
    foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        files.Add(new UnresolvedFile
        {
            Hash = path,
            Paths = [path],
            SizeBytes = 0,
            ModifiedAtUtc = DateTimeOffset.UnixEpoch,
            FirstSeenAt = DateTimeOffset.UnixEpoch,

            // 本体は取り込みの走査で読んで記録に持つ。画面は保存した値だけで束ねるので、ここでも記録を作るときに読む
            ZoneReferrerUrl = BoothZipInspector.ZoneIdentifierReader.Read(path).ReferrerUrl,
        });
    }
}

if (files.Count == 0)
{
    Console.WriteLine("使い方: ZipOriginProbe (--unresolved <json> | --scan <フォルダ>) [--search] [--compare]");
    return;
}

var rows = files.Select(file => (File: file, Origin: UnresolvedOrigin.For(file))).ToList();

var zipGroups = rows.Where(row => row.Origin is not null)
    .GroupBy(row => row.Origin!.ArchiveName, StringComparer.OrdinalIgnoreCase)
    .OrderByDescending(group => group.Count())
    .ToList();
var leftovers = rows.Where(row => row.Origin is null).ToList();
var folderOnly = rows.GroupBy(row => Path.GetDirectoryName(row.File.Paths[0]), StringComparer.OrdinalIgnoreCase).Count();
var leftoverFolders = leftovers.GroupBy(row => Path.GetDirectoryName(row.File.Paths[0]), StringComparer.OrdinalIgnoreCase).Count();
var split = zipGroups.Count(group => group
    .Select(row => Path.GetDirectoryName(row.File.Paths[0]))
    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);

Console.WriteLine($"ファイル {files.Count} 件");
Console.WriteLine($"  今（フォルダで束ねる）      : {folderOnly} 束");
Console.WriteLine($"  元zipで束ねる              : {zipGroups.Count} 束（{rows.Count - leftovers.Count} 件）＋ 元zipが分からない {leftovers.Count} 件はフォルダで {leftoverFolders} 束");
Console.WriteLine($"  フォルダで束ねると割れる元zip: {split} / {zipGroups.Count}");
Console.WriteLine();

/// 今の画面が引く対象の近似。取り込み元の直下のフォルダ（RegisterTargetFolder と同じ規則）。
/// 保存済みの入力では取り込み元が分からないので、ファイルの親フォルダにする
string CurrentTarget(UnresolvedFile file)
{
    var path = file.Paths[0];
    if (scanRoot is not null && path.StartsWith(scanRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        var first = path[(scanRoot.Length + 1)..].Split(Path.DirectorySeparatorChar)[0];
        return Path.Combine(scanRoot, first);
    }

    return Path.GetDirectoryName(path) ?? path;
}

foreach (var group in zipGroups)
{
    var origin = group.First().Origin!;
    var current = CurrentTarget(group.First().File);
    Console.WriteLine($"{group.Count(),4} 件  {origin.ArchiveName}");
    Console.WriteLine($"        zip名の検索語   : {FileNameQuery.ToSearchQuery(origin.ArchivePath)}　（zipが今もある: {File.Exists(origin.ArchivePath)}）");
    Console.WriteLine($"        今の検索語      : {FileNameQuery.ToSearchQuery(current)}　（{Path.GetFileName(current)}）");
}

if (!args.Contains("--search"))
{
    return;
}

var assets = Path.Combine(AppContext.BaseDirectory, "assets");
var bridge = new SearchBridge(new JapaneseDictionary(
    Path.Combine(assets, "JMdict_e.gz"),
    Path.Combine(Path.GetTempPath(), "zip-origin-bridge.cache")));
var readings = new KanjiReadings(Path.Combine(assets, "kanjidic2.xml.gz"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var client = new BoothClient(http, new AppSettings());
var resolver = new FallbackResolver(client, bridge, readings);

Console.WriteLine($"\n自動検索（BOOTHへの間隔 {client.CurrentIntervalMs}ms・1本ずつ）");

async Task Show(string label, string target)
{
    var candidates = await resolver.ProposeAsync(target);
    Console.WriteLine($"    {label}（{FileNameQuery.ToSearchQuery(target)}）: {candidates.Count} 件");
    foreach (var candidate in candidates.Take(3))
    {
        Console.WriteLine($"      {(candidate.IsStrong ? "◎" : "・")} {candidate.Score,3}  {candidate.Name}  [{candidate.ItemId}]");
    }
}

foreach (var group in zipGroups)
{
    var origin = group.First().Origin!;
    Console.WriteLine($"\n{origin.ArchiveName}");
    await Show("zip名", origin.ArchivePath);

    if (args.Contains("--compare"))
    {
        await Show("今", CurrentTarget(group.First().File));
    }
}
