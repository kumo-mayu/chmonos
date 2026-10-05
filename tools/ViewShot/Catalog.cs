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
/// 全部の場面を明るい色と暗い色で撮り、画面ごとのフォルダに分け、見て回る索引（index.html）を付けて zip にまとめる（2026-10-05）。
///
/// <code>
/// ViewShot catalog [--out フォルダ] [--zip 置き場.zip] [--only 名前の頭,…] [--jobs 4] [--timeout 秒]
/// </code>
///
/// 網羅の表（どの画面のどの状態をどの場面が撮るか）は <c>docs/dev/ui-shots.md</c>。
/// 場面は今までどおり1つずつ別のプロセスで描く（<see cref="Program"/> の RunMany と同じ理由：保存先はプロセスごとに1つ）。
/// 失敗した場面があっても止めない。全体を見て回りたいときに、1つの失敗で残りが撮れないと困る
/// </summary>
internal static class Catalog
{
    /// <summary>
    /// 画面ごとのフォルダ。上から順に名前の頭で当てる（「folder-item-」は商品ページ、「folder-」はフォルダビュー、のように長い方を先に）。
    /// フォルダ名は zip を別の PC で開いても化けないよう英字にし、画面の名前は索引に出す
    /// </summary>
    private static readonly (string Folder, string Title, string[] Prefixes)[] Groups =
    [
        ("01-search", "検索", ["search-", "card-", "catalog-search-", "empty-search", "calendar"]),
        ("02-search-modules", "検索の条件（モジュール）", ["catalog-module-"]),
        ("03-item", "商品ページ", ["item-", "folder-item-", "modification-item-", "catalog-item-"]),
        ("04-edit", "編集", ["edit-", "catalog-edit-"]),
        ("05-import", "取り込み", ["import-", "empty-import"]),
        ("06-resolve", "未確定", ["resolve-", "empty-resolve"]),
        ("07-shops", "ショップ", ["shops-", "shop-", "empty-shops"]),
        ("08-folder", "フォルダビュー", ["folder-", "empty-folder"]),
        ("09-avatars", "アバター", ["avatars-", "suggest-avatar-", "empty-avatars"]),
        ("10-modifications", "改変", ["modification-", "hub-", "pick-member-", "pick-modification-", "suggest-modification-", "empty-hub-"]),
        ("11-tags", "タグの管理", ["tag-manage-", "empty-tag-manage"]),
        ("12-attributes", "属性の管理", ["attribute-manage-", "empty-attribute-manage"]),
        ("13-inbox", "通知", ["inbox-", "empty-inbox"]),
        ("14-stats", "統計", ["stats", "catalog-stats-", "empty-stats"]),
        ("15-settings", "設定", ["settings-"]),
        ("16-nav-bands", "ナビと帯・知らせの帯", ["nav-", "band-", "catalog-nav-", "catalog-notice-"]),
        ("17-dialogs", "小窓・初回の窓", ["notice-", "catalog-dialog-", "first-run-"]),
        ("18-parts", "部品・その他", [""]),
    ];

    /// <summary>
    /// 撮らない場面：重さを測るための場面（見た目は同じ画面のほかの場面で撮っている。2000件・300件を組むので1つで数十秒かかる）
    /// </summary>
    private static readonly string[] Skipped = ["perf-", "card-info-perf", "drag-edge-scroll-measure", "shop-300"];

    /// <summary>
    /// 窓の最小の幅（900）でも撮る場面。主な画面の代表と、空の表示（空の文は狭い幅ではみ出したことがある。2026-10-02 メモ6-④）。
    /// 全部を2つの幅で撮ると時間も枚数も倍になるので、並びが幅で変わる画面の代表だけにする
    /// </summary>
    private static readonly HashSet<string> NarrowToo =
    [
        "search-cards", "search-first-open", "catalog-search-no-hits", "item-page", "catalog-item-local-only",
        "resolve-broken-zip", "import-result-unreadable", "stats", "shops-cards", "shop-header", "inbox-rows",
        "avatars-detail", "folder-cards", "tag-manage-cards", "attribute-manage-cards", "settings-long-paths", "edit-chips",
    ];

    private const double NarrowWidth = 900;

    private sealed record SceneResult(Scene Scene, string Folder, IReadOnlyList<string> Images, IReadOnlyList<string> Warnings, IReadOnlyList<string> Notes, string? Error, TimeSpan Elapsed);

    public static int Run(string[] args)
    {
        string? outDir = null;
        string? zipPath = null;
        var only = new List<string>();
        var jobs = 4;
        var timeout = TimeSpan.FromSeconds(240);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません。");
            switch (args[i])
            {
                case "--out":
                    outDir = Path.GetFullPath(Next());
                    break;
                case "--zip":
                    zipPath = Path.GetFullPath(Next());
                    break;
                case "--only":
                    only.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "--jobs":
                    jobs = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                case "--timeout":
                    timeout = TimeSpan.FromSeconds(int.Parse(Next(), CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException($"catalog の引数ではありません：{args[i]}");
            }
        }

        outDir ??= Path.Combine(Path.GetTempPath(), "chmonos-shots", "catalog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        zipPath ??= outDir.TrimEnd('\\', '/') + ".zip";
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
        {
            // 前の回の画像が残っていると、索引に載らない古い画像が zip に混ざる。消すかは人が決める
            throw new ArgumentException($"置き場が空ではありません：{outDir}\n別の --out を渡すか、消してから流してください。");
        }

        if (Path.GetFullPath(zipPath).StartsWith(Path.GetFullPath(outDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("zip は置き場の外に作ってください（中に作ると、自分自身を詰めようとする）。");
        }

        var scenes = Scenes.All
            .Where(scene => !Skipped.Any(prefix => scene.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(scene => only.Count == 0 || only.Any(prefix => scene.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        if (scenes.Count == 0)
        {
            throw new ArgumentException("撮る場面がありません（--only の名前の頭を確かめてください）。");
        }

        Directory.CreateDirectory(outDir);
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("自分の実行ファイルの場所が分かりません。");
        var clock = Stopwatch.StartNew();
        var results = new ConcurrentBag<SceneResult>();
        var done = 0;

        Console.WriteLine($"{scenes.Count} 場面を明るい色と暗い色で撮ります（{jobs} 本ずつ）→ {outDir}");
        Parallel.ForEach(
            scenes,
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            scene =>
            {
                var result = Shoot(self, scene, outDir, timeout);
                results.Add(result);
                var count = Interlocked.Increment(ref done);
                Console.WriteLine(
                    $"[{count}/{scenes.Count}] {scene.Name}：{(result.Error is null ? $"{result.Images.Count} 枚" : "失敗")}"
                    + $"・{result.Elapsed.TotalSeconds:0.0} 秒{(result.Warnings.Count > 0 ? "・注意あり" : string.Empty)}");
            });

        var ordered = results.OrderBy(result => result.Folder, StringComparer.Ordinal)
            .ThenBy(result => Scenes.All.ToList().IndexOf(result.Scene))
            .ToList();
        File.WriteAllText(Path.Combine(outDir, "index.html"), Index(ordered, clock.Elapsed), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), Summary(ordered, clock.Elapsed), new UTF8Encoding(false));

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        // PNG はもう縮んでいるので、詰め直しに時間をかけない
        ZipFile.CreateFromDirectory(outDir, zipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        Console.WriteLine();
        Console.Write(Summary(ordered, clock.Elapsed));
        Console.WriteLine($"索引：{Path.Combine(outDir, "index.html")}");
        Console.WriteLine($"zip：{zipPath}（{new FileInfo(zipPath).Length / 1024.0 / 1024.0:0.0} MB）");
        return ordered.Any(result => result.Error is not null) ? 1 : 0;
    }

    private static string FolderOf(string name)
        => Groups.First(group => group.Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).Folder;

    private static SceneResult Shoot(string self, Scene scene, string outDir, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        var folder = FolderOf(scene.Name);
        var start = new ProcessStartInfo(self)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "shot", scene.Name, "--theme", "both", "--out", Path.Combine(outDir, folder) })
        {
            start.ArgumentList.Add(argument);
        }

        if (NarrowToo.Contains(scene.Name) || (scene.Name.StartsWith("empty-", StringComparison.Ordinal) && scene.Width is not null))
        {
            start.ArgumentList.Add("--width");
            start.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"{scene.Width ?? 1280},{NarrowWidth}"));
        }

        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var errors = child.StandardError.ReadToEndAsync();
        string? error = null;
        if (!child.WaitForExit(timeout))
        {
            // 落ち着かない場面で止まったままにしない。ほかの場面は撮り続ける
            child.Kill(entireProcessTree: true);
            error = $"{timeout.TotalSeconds:0} 秒で終わらなかったので止めた";
        }

        child.WaitForExit();
        var images = new List<string>();
        var warnings = new List<string>();
        var notes = new List<string>();
        var inLog = false;
        foreach (var line in output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')))
        {
            var parts = line.Split('\t');
            if (parts[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                images.Add(Path.GetFileName(parts[0]));
                warnings.AddRange(parts.Skip(3));
            }
            else if (line.StartsWith("  ", StringComparison.Ordinal) && !line.Contains("組むまで", StringComparison.Ordinal)
                && !line.Contains("全部で", StringComparison.Ordinal))
            {
                // 台が出す注意（状態が思った物になっていないかもしれない）と、場面が自分で書く測った値（流れの位置・件数）を分ける。
                // 測った値まで注意に数えると、本当の注意が埋もれる（初めの通しで 29 場面の「注意」のうち 26 が測った値だった）
                var isWarning = line.Contains("知らせの窓が出ようとした", StringComparison.Ordinal)
                    || line.Contains("アプリのログ", StringComparison.Ordinal)
                    || (line.StartsWith("    ", StringComparison.Ordinal) && inLog);
                inLog = line.Contains("アプリのログに", StringComparison.Ordinal) || (inLog && line.StartsWith("    ", StringComparison.Ordinal));
                (isWarning ? warnings : notes).Add(line.Trim());
            }
        }

        if (error is null && child.ExitCode != 0)
        {
            var text = errors.Result.Trim();
            error = $"終了コード {child.ExitCode}：" + (text.Length > 1200 ? text[..1200] + "…" : text);
        }

        return new SceneResult(scene, folder, images, warnings, notes, error, clock.Elapsed);
    }

    private static string Summary(IReadOnlyList<SceneResult> results, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        var failed = results.Where(result => result.Error is not null).ToList();
        var warned = results.Where(result => result.Error is null && result.Warnings.Count > 0).ToList();
        text.AppendLine(CultureInfo.InvariantCulture, $"場面 {results.Count}・画像 {results.Sum(result => result.Images.Count)} 枚・{elapsed.TotalMinutes:0.0} 分・失敗 {failed.Count}・注意あり {warned.Count}");
        foreach (var result in failed)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  失敗：{result.Scene.Name}：{result.Error!.ReplaceLineEndings(" ")[..Math.Min(200, result.Error.ReplaceLineEndings(" ").Length)]}");
        }

        foreach (var result in warned)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  注意：{result.Scene.Name}：{string.Join(" / ", result.Warnings.Take(2))}");
        }

        return text.ToString();
    }

    private static readonly Regex WidthMark = new(@"-w(\d+)", RegexOptions.Compiled);

    private static string Label(string file)
    {
        var theme = file.Contains("-dark", StringComparison.Ordinal) ? "暗い色" : "明るい色";
        var width = WidthMark.Match(file);
        return width.Success ? $"{theme}・幅 {width.Groups[1].Value}" : theme;
    }

    private static string Index(IReadOnlyList<SceneResult> results, TimeSpan elapsed)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        var html = new StringBuilder();
        html.Append(
            """
            <!doctype html>
            <html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Chmonos の画面の一覧</title>
            <style>
            :root { --bg:#f4f4f5; --fg:#1d1d20; --muted:#6b6b73; --card:#fff; --line:#d9d9de; --bad:#c62828; --warn:#a15c00; }
            @media (prefers-color-scheme: dark) { :root { --bg:#18181b; --fg:#ececf0; --muted:#a0a0aa; --card:#232327; --line:#3a3a40; --bad:#ff6b6b; --warn:#f0b050; } }
            body { margin:0; background:var(--bg); color:var(--fg); font-family:"Yu Gothic UI","Meiryo",sans-serif; }
            header { position:sticky; top:0; background:var(--bg); border-bottom:1px solid var(--line); padding:10px 16px; z-index:1; }
            header nav a { margin-right:12px; color:inherit; white-space:nowrap; font-size:13px; }
            header .controls { margin-top:6px; font-size:13px; color:var(--muted); }
            main { padding:0 16px 40px; }
            h2 { margin:28px 0 8px; font-size:20px; }
            .scene { background:var(--card); border:1px solid var(--line); border-radius:6px; padding:10px 12px; margin:10px 0; }
            .scene h3 { margin:0; font-size:14px; font-family:Consolas,monospace; }
            .scene p { margin:4px 0 8px; font-size:13px; color:var(--muted); }
            .scene.failed { border-color:var(--bad); }
            .bad { color:var(--bad); font-weight:bold; } .warn { color:var(--warn); font-size:12px; } .note { color:var(--muted); font-size:12px; }
            .shots { display:flex; flex-wrap:wrap; gap:10px; }
            figure { margin:0; } figcaption { font-size:12px; color:var(--muted); }
            figure img { max-width:340px; max-height:260px; border:1px solid var(--line); display:block; background:#888; }
            pre { white-space:pre-wrap; font-size:12px; }
            body.no-light .t-light, body.no-dark .t-dark { display:none; }
            </style></head><body>
            """);
        html.Append("<header><strong>Chmonos の画面の一覧</strong>　");
        html.Append(E($"場面 {results.Count}・画像 {results.Sum(result => result.Images.Count)} 枚・失敗 {results.Count(result => result.Error is not null)}・{elapsed.TotalMinutes:0.0} 分・{DateTime.Now:yyyy-MM-dd HH:mm}"));
        html.Append("<nav>");
        foreach (var group in Groups)
        {
            var count = results.Count(result => result.Folder == group.Folder);
            if (count > 0)
            {
                html.Append(CultureInfo.InvariantCulture, $"<a href=\"#{group.Folder}\">{E(group.Title)}（{count}）</a>");
            }
        }

        html.Append(
            """
            </nav><div class="controls">
            <label><input type="checkbox" checked onchange="document.body.classList.toggle('no-light', !this.checked)"> 明るい色</label>
            <label><input type="checkbox" checked onchange="document.body.classList.toggle('no-dark', !this.checked)"> 暗い色</label>
            　絞る：<input id="q" type="search" placeholder="場面の名前・説明" oninput="filter(this.value)">
            　画像を押すと原寸で開く
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
            foreach (var result in members)
            {
                html.Append(CultureInfo.InvariantCulture, $"<section class=\"scene{(result.Error is null ? string.Empty : " failed")}\" data-text=\"{E((result.Scene.Name + " " + result.Scene.Title).ToLowerInvariant())}\">");
                html.Append(CultureInfo.InvariantCulture, $"<h3>{E(result.Scene.Name)}</h3><p>{E(result.Scene.Title)}</p>");
                if (result.Error is not null)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<div class=\"bad\">描けなかった</div><pre>{E(result.Error)}</pre>");
                }

                foreach (var warning in result.Warnings)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<div class=\"warn\">注意：{E(warning)}</div>");
                }

                foreach (var note in result.Notes)
                {
                    html.Append(CultureInfo.InvariantCulture, $"<div class=\"note\">{E(note)}</div>");
                }

                html.Append("<div class=\"shots\">");
                foreach (var image in result.Images)
                {
                    var path = $"{group.Folder}/{Uri.EscapeDataString(image)}";
                    var theme = image.Contains("-dark", StringComparison.Ordinal) ? "t-dark" : "t-light";
                    html.Append(CultureInfo.InvariantCulture, $"<figure class=\"{theme}\"><a href=\"{path}\" target=\"_blank\"><img loading=\"lazy\" src=\"{path}\" alt=\"{E(result.Scene.Name + " " + Label(image))}\"></a><figcaption>{E(Label(image))}</figcaption></figure>");
                }

                html.Append("</div></section>");
            }
        }

        html.Append(
            """
            </main><script>
            function filter(q) {
              q = q.trim().toLowerCase();
              document.querySelectorAll('.scene').forEach(s => { s.style.display = !q || s.dataset.text.includes(q) ? '' : 'none'; });
            }
            </script></body></html>
            """);
        return html.ToString();
    }
}
