using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ViewShot;

/// <summary>
/// 通しの場面（<c>Scenes.Tour.cs</c>）を、窓の大きさ×明暗×データの量（×表示の大きさ）で撮り、横に並べて比べる索引を作る（2026-11-14）。
///
/// <code>
/// ViewShot tour [--out 空のフォルダ] [--zip 置き場.zip] [--sizes 900x600,1280x800,…] [--zooms 125,150] [--only 名前の頭,…] [--jobs n] [--timeout 秒]
/// </code>
///
/// なぜ catalog と別にしたか：catalog は「1つの状態を見たい所で切り出す」網羅で、329 場面を明暗で撮る。
/// こちらは窓全体を使う人の量のデータで描き、同じ画面を大きさと量で並べて見る。1場面を 10 枚以上描くので、混ぜると catalog が数倍重くなる。
///
/// 既定の大きさ（幅×高さ。高さは題の帯を除いた中身）：
/// - 900×600：窓の最小の幅（<c>MainWindow.MinimumWidth</c>）。高さは最小 560 に近い、小さいノート PC で縦も詰まった姿
/// - 1280×800：13〜14 型のノート PC を最大化した姿に近い
/// - 1440×900：窓の既定（<c>MainWindow.xaml</c>）。初めて起動したときの姿
/// - 1920×1080：フル HD のモニターで最大化した姿（題の帯とタスクバーの分はここでは引かない。中身が少し広い側で見る）
/// - 2560×1400：WQHD のモニターで最大化した姿（作者の PC の主の画面が 2560×1440）
/// 表示の大きさ（<c>--zooms</c>、設定の「表示の大きさ」）は、多いデータの場面だけを窓の既定の大きさで撮り足す（全部に掛けると枚数が倍々になるため）
/// </summary>
internal static class Tour
{
    private static readonly string[] DefaultSizes = ["900x600", "1280x800", "1440x900", "1920x1080", "2560x1400"];

    private static readonly int[] DefaultZooms = [125, 150];

    /// <summary>表示の大きさを撮り足すときの窓の大きさ（窓の既定）</summary>
    private const string ZoomSize = "1440x900";

    /// <summary>画面ごとのフォルダ。場面の名前の「量」の後ろの頭で当てる（長い方を先に）</summary>
    private static readonly (string Folder, string Title, string[] Prefixes)[] Groups =
    [
        ("01-search", "検索", ["search"]),
        ("03-item", "商品ページ", ["item"]),
        ("04-edit", "編集", ["edit"]),
        ("05-import", "取り込み", ["import"]),
        ("06-resolve", "未確定", ["resolve"]),
        ("07-shops", "ショップ", ["shops", "shop"]),
        ("08-folder", "フォルダ", ["folders"]),
        ("09-avatars", "アバター", ["avatars"]),
        ("10-modifications", "改変", ["modification"]),
        ("11-tags", "タグの管理", ["tags"]),
        ("12-attributes", "属性の管理", ["attributes"]),
        ("13-inbox", "通知", ["inbox"]),
        ("14-stats", "統計", ["stats"]),
        ("15-settings", "設定", ["settings"]),
    ];

    private static readonly string[] Densities = ["empty", "few", "many"];

    private static readonly Dictionary<string, string> DensityTitles = new() { ["empty"] = "空", ["few"] = "少ない", ["many"] = "多い" };

    private static readonly Regex ImageName = new(@"^(?<scene>tour-[a-z0-9-]+?)-(?<theme>light|dark)-w(?<w>\d+)-h(?<h>\d+)(?:-z(?<z>\d+))?\.png$", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        string? outDir = null;
        string? zipPath = null;
        var sizes = DefaultSizes.ToList();
        var zooms = DefaultZooms.ToList();
        var only = new List<string>();
        var jobs = Catalog.DefaultJobs;
        var timeout = TimeSpan.FromSeconds(300);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません。");
            List<string> Split(string text) => [.. text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            switch (args[i])
            {
                case "--out":
                    outDir = Path.GetFullPath(Next());
                    break;
                case "--zip":
                    zipPath = Path.GetFullPath(Next());
                    break;
                case "--sizes":
                    sizes = Split(Next());
                    break;
                case "--zooms":
                    var text = Next();
                    zooms = text == "none" ? [] : Split(text).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToList();
                    break;
                case "--only":
                    only.AddRange(Split(Next()));
                    break;
                case "--jobs":
                    jobs = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                case "--timeout":
                    timeout = TimeSpan.FromSeconds(int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"tour の引数ではありません：{args[i]}");
            }
        }

        outDir ??= Path.Combine(Path.GetTempPath(), "chmonos-shots", "tour-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        zipPath ??= outDir.TrimEnd('\\', '/') + ".zip";
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
        {
            throw new ArgumentException($"置き場が空ではありません：{outDir}\n別の --out を渡すか、消してから流してください。");
        }

        var scenes = Scenes.All
            .Where(scene => scene.Name.StartsWith(Catalog.TourPrefix, StringComparison.Ordinal))
            .Where(scene => only.Count == 0 || only.Any(prefix => scene.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        if (scenes.Count == 0)
        {
            throw new ArgumentException("撮る場面がありません（--only の名前の頭を確かめてください）。");
        }

        var missing = Scenes.MissingTourStores(scenes.Select(scene => scene.Name));
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"写しがありません：{string.Join("・", missing)}\n"
                + "作り方は .claude/skills/ui-check/sandboxes.md の showcase・tour-dense の行（tools/SandboxGen の init → showcase）。"
                + "別の場所の写しを使うなら CHMONOS_TOUR_FEW・CHMONOS_TOUR_MANY に渡してください。");
        }

        // 1本の仕事＝1場面を全部の大きさで、か、1場面を1つの表示の大きさで（子のプロセスは表示の大きさを1つしか持てない）
        var work = scenes.Select(scene => (Scene: scene, Extra: (IReadOnlyList<string>)["--size", string.Join(',', sizes)]))
            .Concat(zooms.SelectMany(zoom => scenes.Where(scene => DensityOf(scene.Name) == "many")
                .Select(scene => (Scene: scene, Extra: (IReadOnlyList<string>)["--size", ZoomSize, "--zoom", zoom.ToString(CultureInfo.InvariantCulture)]))))
            .ToList();

        Directory.CreateDirectory(outDir);
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("自分の実行ファイルの場所が分かりません。");
        var clock = Stopwatch.StartNew();
        var results = new ConcurrentBag<Catalog.SceneResult>();
        var done = 0;
        Console.WriteLine($"{scenes.Count} 場面・{work.Count} 本の仕事を撮ります（大きさ {string.Join(" ", sizes)}・表示の大きさ {(zooms.Count == 0 ? "なし" : string.Join(" ", zooms.Select(zoom => zoom + "%")))}・{jobs} 本ずつ）→ {outDir}");
        Parallel.ForEach(
            Partitioner.Create(work, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            job =>
            {
                var result = Catalog.Shoot(self, job.Scene, outDir, timeout, quiet: null, folder: FolderOf(job.Scene.Name), extra: job.Extra);
                results.Add(result);
                var count = Interlocked.Increment(ref done);
                Console.WriteLine(
                    $"[{count}/{work.Count}] {job.Scene.Name}{(job.Extra.Count > 2 ? $"（{job.Extra[3]}%）" : string.Empty)}："
                    + $"{(result.Error is null ? $"{result.Images.Count} 枚" : "失敗")}・{result.Elapsed.TotalSeconds:0.0} 秒{(result.Warnings.Count > 0 ? "・注意あり" : string.Empty)}");
            });

        var all = results.ToList();
        File.WriteAllText(Path.Combine(outDir, "index.html"), Index(all, sizes, zooms, clock.Elapsed), new UTF8Encoding(false));
        var summary = Summary(all, clock.Elapsed);
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary, new UTF8Encoding(false));
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(outDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);
        Console.WriteLine();
        Console.Write(summary);
        Console.WriteLine($"索引：{Path.Combine(outDir, "index.html")}");
        Console.WriteLine($"zip：{zipPath}（{new FileInfo(zipPath).Length / 1024.0 / 1024.0:0.0} MB）");
        return all.Any(result => result.Error is not null) ? 1 : 0;
    }

    /// <summary>「tour-many-item-avatars-end」→「many」</summary>
    private static string DensityOf(string name) => name[Catalog.TourPrefix.Length..].Split('-')[0];

    /// <summary>「tour-many-item-avatars-end」→「item-avatars-end」（量を除いた、画面と流した位置）</summary>
    private static string ViewOf(string name) => name[(Catalog.TourPrefix.Length + DensityOf(name).Length + 1)..];

    private static string FolderOf(string name)
    {
        var view = ViewOf(name);
        return Groups.FirstOrDefault(group => group.Prefixes.Any(prefix => view.StartsWith(prefix, StringComparison.Ordinal))).Folder ?? "99-other";
    }

    private static string Summary(IReadOnlyList<Catalog.SceneResult> results, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        var failed = results.Where(result => result.Error is not null).ToList();
        var warned = results.Where(result => result.Error is null && result.Warnings.Count > 0).ToList();
        text.AppendLine(CultureInfo.InvariantCulture, $"仕事 {results.Count}・画像 {results.Sum(result => result.Images.Count)} 枚・{elapsed.TotalMinutes:0.0} 分・失敗 {failed.Count}・注意あり {warned.Count}");
        foreach (var result in failed)
        {
            var error = result.Error!.ReplaceLineEndings(" ");
            text.AppendLine(CultureInfo.InvariantCulture, $"  失敗：{result.Scene.Name}：{error[..Math.Min(300, error.Length)]}");
        }

        foreach (var result in warned)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  注意：{result.Scene.Name}：{string.Join(" / ", result.Warnings.Distinct().Take(3))}");
        }

        return text.ToString();
    }

    private static string Index(IReadOnlyList<Catalog.SceneResult> results, IReadOnlyList<string> sizes, IReadOnlyList<int> zooms, TimeSpan elapsed)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);

        // 画像を（場面・色・大きさ・表示の大きさ）で引けるようにする
        var images = results.SelectMany(result => result.Images.Select(image => (Result: result, File: image, Match: ImageName.Match(image))))
            .Where(entry => entry.Match.Success)
            .ToList();
        string? Find(string scene, string theme, string size, int? zoom)
        {
            var hit = images.FirstOrDefault(entry => entry.Match.Groups["scene"].Value == scene && entry.Match.Groups["theme"].Value == theme
                && $"{entry.Match.Groups["w"].Value}x{entry.Match.Groups["h"].Value}" == size
                && (zoom is null ? !entry.Match.Groups["z"].Success : entry.Match.Groups["z"].Value == zoom.Value.ToString(CultureInfo.InvariantCulture)));
            return hit.File is null ? null : $"{hit.Result.Folder}/{hit.File}";
        }

        var html = new StringBuilder();
        html.Append(
            """
            <!doctype html>
            <html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Chmonos の画面を通しで見る</title>
            <style>
            :root { --bg:#f4f4f5; --fg:#1d1d20; --muted:#6b6b73; --card:#fff; --line:#d9d9de; --bad:#c62828; --warn:#a15c00; }
            @media (prefers-color-scheme: dark) { :root { --bg:#18181b; --fg:#ececf0; --muted:#a0a0aa; --card:#232327; --line:#3a3a40; --bad:#ff6b6b; --warn:#f0b050; } }
            body { margin:0; background:var(--bg); color:var(--fg); font-family:"Yu Gothic UI","Meiryo",sans-serif; }
            header { position:sticky; top:0; background:var(--bg); border-bottom:1px solid var(--line); padding:10px 16px; z-index:1; font-size:13px; }
            header nav a { margin-right:12px; color:inherit; white-space:nowrap; }
            header .controls { margin-top:6px; color:var(--muted); }
            header .controls label { margin-right:8px; white-space:nowrap; }
            main { padding:0 16px 40px; }
            h2 { margin:28px 0 8px; font-size:20px; }
            .view { background:var(--card); border:1px solid var(--line); border-radius:6px; padding:10px 12px; margin:10px 0; overflow-x:auto; }
            .view h3 { margin:0 0 6px; font-size:15px; }
            .view h3 code { font-size:12px; color:var(--muted); font-weight:normal; margin-left:8px; }
            table { border-collapse:collapse; }
            th, td { padding:4px 6px; vertical-align:top; text-align:left; }
            th { font-size:12px; color:var(--muted); font-weight:normal; white-space:nowrap; }
            td img { width:var(--thumb, 240px); border:1px solid var(--line); display:block; background:#888; }
            td.missing { color:var(--muted); font-size:12px; }
            .note { color:var(--muted); font-size:12px; max-width:240px; }
            .warn { color:var(--warn); font-size:12px; } .bad { color:var(--bad); font-size:12px; font-weight:bold; }
            .hide { display:none; }
            </style>
            <script>
            // 大きさ・色・量の列と行を、選んだ物だけ出す（全部出すと横に長すぎる）
            function apply() {
              document.querySelectorAll('[data-size]').forEach(e => e.classList.toggle('hide', !document.getElementById('size-' + e.dataset.size).checked));
              document.querySelectorAll('[data-row]').forEach(e => {
                const [density, theme] = e.dataset.row.split(' ');
                e.classList.toggle('hide', !document.getElementById('d-' + density).checked || !document.getElementById('t-' + theme).checked);
              });
            }
            function thumb(v) { document.body.style.setProperty('--thumb', v + 'px'); }
            </script></head><body>
            """);
        html.Append(CultureInfo.InvariantCulture, $"<header><strong>Chmonos の画面を通しで見る</strong>　{E($"画像 {results.Sum(result => result.Images.Count)} 枚・失敗 {results.Count(result => result.Error is not null)}・{elapsed.TotalMinutes:0.0} 分・{DateTime.Now:yyyy-MM-dd HH:mm}")}<nav>");
        foreach (var group in Groups.Where(group => results.Any(result => result.Folder == group.Folder)))
        {
            html.Append(CultureInfo.InvariantCulture, $"<a href=\"#{group.Folder}\">{E(group.Title)}</a>");
        }

        html.Append("</nav><div class=\"controls\">窓の大きさ：");
        foreach (var size in sizes)
        {
            html.Append(CultureInfo.InvariantCulture, $"<label><input type=\"checkbox\" id=\"size-{size}\" checked onchange=\"apply()\"> {size.Replace("x", "×", StringComparison.Ordinal)}</label>");
        }

        html.Append("　量：");
        foreach (var density in Densities)
        {
            html.Append(CultureInfo.InvariantCulture, $"<label><input type=\"checkbox\" id=\"d-{density}\" checked onchange=\"apply()\"> {DensityTitles[density]}</label>");
        }

        html.Append(
            """
            　色：<label><input type="checkbox" id="t-light" checked onchange="apply()"> 明るい</label><label><input type="checkbox" id="t-dark" checked onchange="apply()"> 暗い</label>
            　縮小：<input type="range" min="160" max="900" value="240" oninput="thumb(this.value)">
            　画像を押すと原寸で開く。表示の大きさ（設定）を変えた絵は、多い量の行の下に「125%」などで並ぶ（窓は既定の 1440×900）
            </div></header><main>
            """);

        foreach (var group in Groups)
        {
            var members = results.Where(result => result.Folder == group.Folder).ToList();
            if (members.Count == 0)
            {
                continue;
            }

            html.Append(CultureInfo.InvariantCulture, $"<h2 id=\"{group.Folder}\">{E(group.Title)}</h2>");
            // 量を除いた画面（流した位置を含む）ごとに1つの表。場面の登録順に並べる
            var views = Scenes.All.Select(scene => scene.Name).Where(name => members.Any(result => result.Scene.Name == name))
                .Select(ViewOf).Distinct().ToList();
            foreach (var view in views)
            {
                var title = members.First(result => ViewOf(result.Scene.Name) == view).Scene.Title;
                title = title[(title.IndexOf('：', StringComparison.Ordinal) + 1)..];
                html.Append(CultureInfo.InvariantCulture, $"<section class=\"view\"><h3>{E(title)}<code>{E(view)}</code></h3><table><tr><th></th>");
                foreach (var size in sizes)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<th data-size=\"{size}\">{size.Replace("x", "×", StringComparison.Ordinal)}</th>");
                }

                html.Append("</tr>");
                foreach (var density in Densities)
                {
                    var scene = $"{Catalog.TourPrefix}{density}-{view}";
                    var runs = members.Where(result => result.Scene.Name == scene).ToList();
                    if (runs.Count == 0)
                    {
                        continue;
                    }

                    foreach (var theme in new[] { "light", "dark" })
                    {
                        html.Append(CultureInfo.InvariantCulture, $"<tr data-row=\"{density} {theme}\"><th>{DensityTitles[density]}・{(theme == "light" ? "明" : "暗")}");
                        if (theme == "light")
                        {
                            // 場面が書いた件数（狙った量が出たか）と、注意・失敗
                            foreach (var note in runs.SelectMany(run => run.Notes).Distinct().Where(note => !note.Contains("流せる欄が無かった", StringComparison.Ordinal)).Take(3))
                            {
                                html.Append(CultureInfo.InvariantCulture, $"<div class=\"note\">{E(note)}</div>");
                            }

                            if (runs.Any(run => run.Notes.Any(note => note.Contains("流せる欄が無かった", StringComparison.Ordinal))))
                            {
                                html.Append("<div class=\"note\">流せる欄が無い大きさがある（全部が窓に収まった）</div>");
                            }

                            foreach (var run in runs.Where(run => run.Error is not null))
                            {
                                html.Append(CultureInfo.InvariantCulture, $"<div class=\"bad\">失敗：{E(run.Error![..Math.Min(200, run.Error.Length)])}</div>");
                            }

                            foreach (var warning in runs.SelectMany(run => run.Warnings).Distinct().Take(2))
                            {
                                html.Append(CultureInfo.InvariantCulture, $"<div class=\"warn\">{E(warning)}</div>");
                            }
                        }

                        html.Append("</th>");
                        foreach (var size in sizes)
                        {
                            AppendCell(html, Find(scene, theme, size, null), size);
                        }

                        html.Append("</tr>");
                        if (density != "many")
                        {
                            continue;
                        }

                        foreach (var zoom in zooms)
                        {
                            // 表示の大きさは窓の既定の大きさの列にだけ置く（ほかの列は空）
                            html.Append(CultureInfo.InvariantCulture, $"<tr data-row=\"{density} {theme}\"><th>多い・{(theme == "light" ? "明" : "暗")}・{zoom}%</th>");
                            foreach (var size in sizes)
                            {
                                if (size == ZoomSize)
                                {
                                    AppendCell(html, Find(scene, theme, size, zoom), size);
                                }
                                else
                                {
                                    html.Append(CultureInfo.InvariantCulture, $"<td data-size=\"{size}\"></td>");
                                }
                            }

                            html.Append("</tr>");
                        }
                    }
                }

                html.Append("</table></section>");
            }
        }

        html.Append("</main></body></html>");
        return html.ToString();

        static void AppendCell(StringBuilder html, string? path, string size)
        {
            if (path is null)
            {
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"missing\" data-size=\"{size}\">（無し）</td>");
                return;
            }

            var href = WebUtility.HtmlEncode(path);
            html.Append(CultureInfo.InvariantCulture, $"<td data-size=\"{size}\"><a href=\"{href}\" target=\"_blank\"><img loading=\"lazy\" src=\"{href}\" alt=\"\"></a></td>");
        }
    }
}
