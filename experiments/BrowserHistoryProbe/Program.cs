// BrowserHistoryProbe
// Chromium系ブラウザ（Brave / Chrome / Edge）の History DB にある「ダウンロード履歴」から、
// BOOTH の配布CDN URL  https://s{n}.booth.pm/<shop-uuid>/f/<商品ID>/<配布ID>/<ファイル名>
// を読み取り、ローカルZIPと突き合わせて商品IDを得る実験ツール。
//
// 使い方:
//   dotnet run --project experiments/BrowserHistoryProbe -- [--history <HistoryファイルのPath>] [--dir <ZIPのあるフォルダ>] [--all-booth]
//     --history 省略時は Brave → Chrome → Edge の既定プロファイルを順に探す
//     --dir     省略時はファイル名照合をせず、--all-booth 相当の一覧を出す
//     --all-booth  BOOTH由来のダウンロード行をすべて表示（ファイル名照合なし）
//
// 安全策:
//   * History は必ずスクラッチへコピーしてから ReadOnly で開く（実行中ブラウザのDBは触らない）。終了時にコピーを削除する。
//   * 署名付きURLのクエリ文字列は表示しない。BOOTH以外のダウンロードは表示しない。
//
// 実測（本リポジトリの調査時）: 履歴が残る期間の 11/17 件で商品IDを直接取得できた（正解と全件一致）。
// 制約: 履歴の保持期間・消去・別PC・別プロファイルでは得られない。ファイルを移動/改名していると target_path では追えないため、
//       ファイル名＋バイト数で照合する。

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

Console.OutputEncoding = Encoding.UTF8;

string? historyPath = null;
string? dir = null;
var allBooth = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--history": historyPath = args[++i]; break;
        case "--dir": dir = args[++i]; break;
        case "--all-booth": allBooth = true; break;
    }
}

historyPath ??= FindDefaultHistory();
if (historyPath is null || !File.Exists(historyPath))
{
    Console.Error.WriteLine("History DB が見つかりません。--history で指定してください。");
    return 1;
}
Console.WriteLine($"history: {historyPath}");

// ローカルZIPの索引（ファイル名 → サイズ）
var local = new Dictionary<string, List<(string path, long size)>>(StringComparer.OrdinalIgnoreCase);
if (dir is not null)
{
    foreach (var p in Directory.EnumerateFiles(dir, "*.zip", SearchOption.AllDirectories))
    {
        var name = Path.GetFileName(p);
        if (!local.TryGetValue(name, out var list)) local[name] = list = new();
        list.Add((p, new FileInfo(p).Length));
    }
    Console.WriteLine($"local zips: {local.Values.Sum(l => l.Count)} under {dir}");
}

// スナップショットを取ってから開く
var snapshot = Path.Combine(Path.GetTempPath(), $"history-probe-{Guid.NewGuid():N}.db");
File.Copy(historyPath, snapshot, overwrite: true);
try
{
    var cs = new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly }.ToString();
    using var con = new SqliteConnection(cs);
    con.Open();

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"select d.id, d.target_path, d.received_bytes,
        datetime(d.start_time/1000000-11644473600,'unixepoch','localtime'),
        d.tab_url, d.state
        from downloads d order by d.start_time";
    using var r = cmd.ExecuteReader();

    var cdn = new Regex(@"^https://s\d+\.booth\.pm/(?<shop>[0-9a-f-]{36})/f/(?<item>\d+)/(?<dl>\d+)/(?<file>[^?]+)", RegexOptions.IgnoreCase);
    var shown = 0;
    var resolvedLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    while (r.Read())
    {
        var id = r.GetInt64(0);
        var target = r.GetString(1);
        var bytes = r.GetInt64(2);
        var started = r.GetString(3);
        var tabUrl = r.IsDBNull(4) ? "" : r.GetString(4);
        var file = Path.GetFileName(target.Replace('\\', '/'));

        var chain = new List<string>();
        using (var c2 = con.CreateCommand())
        {
            c2.CommandText = "select url from downloads_url_chains where id=@id order by chain_index";
            c2.Parameters.AddWithValue("@id", id);
            using var r2 = c2.ExecuteReader();
            while (r2.Read()) chain.Add(r2.GetString(0));
        }

        var isBooth = tabUrl.Contains("booth.pm") || chain.Any(u => u.Contains("booth.pm"));
        if (!isBooth) continue;

        Match? m = chain.Select(u => cdn.Match(u)).FirstOrDefault(x => x.Success);
        var cdnFile = m is null ? null : Uri.UnescapeDataString(m.Groups["file"].Value);

        // 照合対象を絞る
        var localHit = local.TryGetValue(file, out var l1) ? l1 : (cdnFile is not null && local.TryGetValue(cdnFile, out var l2) ? l2 : null);
        if (!allBooth && dir is not null && localHit is null) continue;

        shown++;
        Console.WriteLine();
        Console.WriteLine($"## {file}   started={started}   bytes={bytes:N0}   state={r.GetInt32(5)}");
        Console.WriteLine($"   tab_url : {Mask(tabUrl)}");
        if (m is not null)
        {
            Console.WriteLine($"   item_id : {m.Groups["item"].Value}   downloadable_id: {m.Groups["dl"].Value}   shop_uuid: {m.Groups["shop"].Value}");
            Console.WriteLine($"   cdn_file: {cdnFile}");
        }
        else
        {
            foreach (var u in chain) Console.WriteLine($"   chain   : {Mask(u)}");
        }
        if (localHit is not null)
        {
            foreach (var (p, size) in localHit)
            {
                var sizeMatch = size == bytes ? "size一致" : $"size不一致({size:N0})";
                Console.WriteLine($"   local   : {p}  [{sizeMatch}]");
                if (size == bytes && m is not null) resolvedLocal.Add(p);
            }
        }
    }

    Console.WriteLine();
    Console.WriteLine($"booth downloads shown: {shown}");
    if (dir is not null)
        Console.WriteLine($"local zips resolved (name+size match, item_id found): {resolvedLocal.Count} / {local.Values.Sum(l => l.Count)}");
}
finally
{
    try { File.Delete(snapshot); } catch { /* ignore */ }
}
return 0;

static string Mask(string url)
{
    var q = url.IndexOf('?');
    return q >= 0 ? url[..q] + "?<query omitted>" : url;
}

static string? FindDefaultHistory()
{
    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    foreach (var rel in new[]
    {
        @"BraveSoftware\Brave-Browser\User Data\Default\History",
        @"Google\Chrome\User Data\Default\History",
        @"Microsoft\Edge\User Data\Default\History",
    })
    {
        var p = Path.Combine(local, rel);
        if (File.Exists(p)) return p;
    }
    return null;
}
