using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ViewShot;

/// <summary>
/// 全部の場面を明るい色と暗い色で撮り、画面ごとのフォルダに分け、見て回る索引（index.html）を付けて zip にまとめる（2026-10-05）。
///
/// <code>
/// ViewShot catalog [--out フォルダ] [--zip 置き場.zip] [--only 名前の頭,…] [--changed [基準]] [--from 前の回の置き場] [--dry] [--jobs n] [--timeout 秒] [--quiet ms]
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
        "resolve-broken-zip", "import-result-unreadable", "import-running", "import-row-reveal", "import-missing-results", "import-missing-folders", "stats", "shops-cards", "shop-header", "inbox-rows",
        "card-info", "card-info-off", "avatars-detail", "folder-cards", "tag-manage-cards", "attribute-manage-cards", "settings-long-paths", "edit-chips",
    ];

    private const double NarrowWidth = 900;

    private sealed record SceneResult(Scene Scene, string Folder, IReadOnlyList<string> Images, IReadOnlyList<string> Warnings, IReadOnlyList<string> Notes, string? Error, TimeSpan Elapsed)
    {
        /// <summary>撮り直さず、前の回（<c>--from</c>）の画像を写した</summary>
        public bool Reused { get; init; }

        /// <summary>子が書いた内訳（ms。<see cref="Timing.Line"/>）。描けずに終わった場面は空</summary>
        public IReadOnlyDictionary<string, long> Times { get; init; } = new Dictionary<string, long>();

        public long Time(string key) => Times.TryGetValue(key, out var value) ? value : 0;
    }

    public static int Run(string[] args)
    {
        string? outDir = null;
        string? zipPath = null;
        var only = new List<string>();
        var jobs = DefaultJobs;
        var timeout = TimeSpan.FromSeconds(240);
        var changed = false;
        string? changedBase = null;
        string? from = null;
        string? quiet = null;
        var dry = false;
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
                case "--quiet":
                    quiet = Next();
                    break;
                case "--dry":
                    dry = true;
                    break;
                case "--changed":
                    changed = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        changedBase = args[++i];
                    }

                    break;
                case "--from":
                    from = Path.GetFullPath(Next());
                    if (!File.Exists(Path.Combine(from, ResultsFile)))
                    {
                        throw new ArgumentException($"前の回の記録（{ResultsFile}）がありません：{from}");
                    }

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

        var candidates = Scenes.All
            .Where(scene => !Skipped.Any(prefix => scene.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        var scenes = candidates
            .Where(scene => only.Count == 0 || only.Any(prefix => scene.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        if (changed)
        {
            var selection = CatalogChanged.Select(CatalogChanged.ChangedFiles(changedBase), candidates);
            Console.WriteLine($"触ったファイルから選んだ場面（基準 {changedBase ?? "master との分かれ目"}）：");
            foreach (var reason in selection.Reasons)
            {
                Console.WriteLine($"  {reason}");
            }

            if (selection.Ignored.Count > 0)
            {
                Console.WriteLine($"  画面に関わらないとみなしたファイル {selection.Ignored.Count} 件（{string.Join("・", selection.Ignored.Take(5))}{(selection.Ignored.Count > 5 ? "…" : string.Empty)}）");
            }

            if (!selection.Folders.Contains(CatalogChanged.All))
            {
                scenes = scenes.Where(scene => selection.Folders.Contains(FolderOf(scene.Name)) || selection.SceneNames.Contains(scene.Name)).ToList();
            }
        }

        if (dry)
        {
            // 撮らずに、選んだ場面だけを出す（--changed の選び方を確かめる）
            Console.WriteLine($"{scenes.Count} 場面：{string.Join(" ", scenes.Select(scene => scene.Name))}");
            return 0;
        }

        if (scenes.Count == 0 && from is null)
        {
            throw new ArgumentException(changed
                ? "触ったファイルに関わる場面がありません（基準のコミットを渡すか、--only で選んでください）。"
                : "撮る場面がありません（--only の名前の頭を確かめてください）。");
        }

        Directory.CreateDirectory(outDir);
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("自分の実行ファイルの場所が分かりません。");
        var clock = Stopwatch.StartNew();
        var results = new ConcurrentBag<SceneResult>();
        var done = 0;

        // 前の回の記録。重い場面から先に始める（最後に重い場面が1本だけ残って、ほかの本が遊ぶのを避ける）
        var previous = from is null ? [] : LoadResults(from);
        var expected = previous.ToDictionary(entry => entry.Name, entry => entry.ElapsedMs, StringComparer.Ordinal);
        scenes = scenes.OrderByDescending(scene => expected.TryGetValue(scene.Name, out var ms) ? ms : HeavyGuess(scene)).ToList();

        Console.WriteLine($"{scenes.Count} 場面を明るい色と暗い色で撮ります（{jobs} 本ずつ）→ {outDir}");
        if (from is not null)
        {
            var shooting = scenes.Select(scene => scene.Name).ToHashSet(StringComparer.Ordinal);
            var reused = 0;
            foreach (var scene in candidates.Where(scene => !shooting.Contains(scene.Name)))
            {
                if (previous.FirstOrDefault(entry => entry.Name == scene.Name) is not { } entry)
                {
                    continue;
                }

                // 画面のフォルダの分け方が変わっていても、今の分け方の所へ写す
                var folder = FolderOf(scene.Name);
                Directory.CreateDirectory(Path.Combine(outDir, folder));
                foreach (var image in entry.Images)
                {
                    File.Copy(Path.Combine(from, entry.Folder, image), Path.Combine(outDir, folder, image));
                }

                results.Add(new SceneResult(scene, folder, entry.Images, entry.Warnings, entry.Notes, entry.Error, TimeSpan.FromMilliseconds(entry.ElapsedMs))
                {
                    Times = entry.Times,
                    Reused = true,
                });
                reused++;
            }

            Console.WriteLine($"前の回（{from}）から {reused} 場面の画像を写しました（撮り直していない）");
        }

        Parallel.ForEach(
            Partitioner.Create(scenes, EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = jobs },
            scene =>
            {
                var result = Shoot(self, scene, outDir, timeout, quiet);
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
        File.WriteAllText(Path.Combine(outDir, "times.tsv"), TimesTable(ordered), new UTF8Encoding(false));
        SaveResults(outDir, ordered);

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

    /// <summary>前の回の記録（<c>--from</c> で読む）。画像の一覧・注意・時間を、撮り直さない場面の分だけ写すのに使う</summary>
    private const string ResultsFile = "results.json";

    private sealed record SavedResult(
        string Name, string Folder, List<string> Images, List<string> Warnings, List<string> Notes, string? Error, long ElapsedMs, Dictionary<string, long> Times);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void SaveResults(string outDir, IReadOnlyList<SceneResult> results)
        => File.WriteAllText(
            Path.Combine(outDir, ResultsFile),
            JsonSerializer.Serialize(
                results.Select(result => new SavedResult(
                    result.Scene.Name, result.Folder, [.. result.Images], [.. result.Warnings], [.. result.Notes], result.Error,
                    (long)result.Elapsed.TotalMilliseconds, new Dictionary<string, long>(result.Times))).ToList(),
                Json),
            new UTF8Encoding(false));

    private static List<SavedResult> LoadResults(string from)
        => JsonSerializer.Deserialize<List<SavedResult>>(File.ReadAllText(Path.Combine(from, ResultsFile)), Json) ?? [];

    /// <summary>
    /// 並べる本数の既定。CPU の論理数の 3/4（2026-10-05 に論理 16 の PC で測った：4 本 9.5 分・8 本 3.4 分・12 本 2.9 分・16 本 2.7 分）。
    /// 場面の時間の多くは描画が止まるのを待つ間で CPU は空くので、論理数の近くまで伸びる。16 本では1場面ずつが遅くなり（合計 2061→2608 秒）、
    /// 流す位置が決まりきらずに絵が変わる場面が出た（tag-manage-cards の送りの棒）ので、PC をほかの作業にも残す 3/4 にした
    /// </summary>
    private static int DefaultJobs => Math.Max(2, Environment.ProcessorCount * 3 / 4);

    /// <summary>前の回の時間が無いときの重さの見当。重い場面を先に始め、最後に1本だけ残るのを避ける</summary>
    private static long HeavyGuess(Scene scene) => scene.Name.StartsWith("card-info", StringComparison.Ordinal) ? 30_000 : 5_000;

    private static string FolderOf(string name)
        => Groups.First(group => group.Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).Folder;

    private static SceneResult Shoot(string self, Scene scene, string outDir, TimeSpan timeout, string? quiet)
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

        if (quiet is not null)
        {
            start.ArgumentList.Add("--quiet");
            start.ArgumentList.Add(quiet);
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
        var times = new Dictionary<string, long>();
        var inLog = false;
        foreach (var line in output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r')))
        {
            var parts = line.Split('\t');
            if (parts[0] == Timing.Tag)
            {
                foreach (var pair in parts.Skip(1).Select(part => part.Split('=')))
                {
                    times[pair[0]] = long.Parse(pair[1], CultureInfo.InvariantCulture);
                }
            }
            else if (parts[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
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

        return new SceneResult(scene, folder, images, warnings, notes, error, clock.Elapsed) { Times = times };
    }

    private static string Summary(IReadOnlyList<SceneResult> results, TimeSpan elapsed)
    {
        var text = new StringBuilder();
        var failed = results.Where(result => result.Error is not null).ToList();
        var warned = results.Where(result => result.Error is null && result.Warnings.Count > 0).ToList();
        var reused = results.Count(result => result.Reused);
        text.Append(CultureInfo.InvariantCulture, $"場面 {results.Count}・画像 {results.Sum(result => result.Images.Count)} 枚・{elapsed.TotalMinutes:0.0} 分・失敗 {failed.Count}・注意あり {warned.Count}");
        text.AppendLine(reused > 0 ? $"・撮った {results.Count - reused}・前の回から写した {reused}" : string.Empty);
        foreach (var result in failed)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  失敗：{result.Scene.Name}：{result.Error!.ReplaceLineEndings(" ")[..Math.Min(200, result.Error.ReplaceLineEndings(" ").Length)]}");
        }

        foreach (var result in warned)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  注意：{result.Scene.Name}：{string.Join(" / ", result.Warnings.Take(2))}");
        }

        text.Append(Breakdown(results));
        return text.ToString();
    }

    /// <summary>
    /// 時間の内訳。場面ごとの合計（並べて走らせるので、足すと全体の時間の「本数」倍ほどになる）と、重い場面。
    /// 何に時間を使っているかが分からないと、速くする所を思い込みで選ぶことになる（2026-10-05 に 8 分かかって初めて測った）
    /// </summary>
    private static string Breakdown(IReadOnlyList<SceneResult> results)
    {
        var text = new StringBuilder();
        var timed = results.Where(result => result.Times.Count > 0 && !result.Reused).ToList();
        if (timed.Count == 0)
        {
            return string.Empty;
        }

        double Sum(Func<SceneResult, double> pick) => timed.Sum(pick) / 1000.0;
        var wall = Sum(result => result.Elapsed.TotalMilliseconds);
        var startup = Sum(result => result.Time("startup"));
        var app = Sum(result => result.Time("app"));
        var build = Sum(result => result.Time("build"));
        var buildSettle = Sum(result => result.Time("buildSettle"));
        var settle = Sum(result => result.Time("settle"));
        var render = Sum(result => result.Time("render"));
        var save = Sum(result => result.Time("save"));
        var total = Sum(result => result.Time("total"));
        var shoot = total - app - build;
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"時間の内訳（{timed.Count} 場面の合計。秒。並べて走らせるので、足すと全体の時間より長い）");
        text.AppendLine(CultureInfo.InvariantCulture, $"  場面ごとのプロセス（親から見た時間）  {wall,8:0.0}");
        text.AppendLine(CultureInfo.InvariantCulture, $"    起動（.NET・WPF の読み込み）        {startup,8:0.0}");
        text.AppendLine(CultureInfo.InvariantCulture, $"    切り離し・App の組み立て            {app,8:0.0}");
        text.AppendLine(CultureInfo.InvariantCulture, $"    場面を組む                          {build,8:0.0}（うち落ち着くまでの待ち {buildSettle:0.0}）");
        text.AppendLine(CultureInfo.InvariantCulture, $"    色・幅ごとに撮る                    {shoot,8:0.0}（うち待ち {settle - buildSettle:0.0}）");
        text.AppendLine(CultureInfo.InvariantCulture, $"    終わりの片付け・プロセスの終了      {wall - startup - total,8:0.0}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  待ちの中の描画（画素を比べるための描画を含む） {render:0.0}・書き出し {save:0.0}・待った回数 {timed.Sum(result => result.Time("settles"))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"    うち作り物を書く {Sum(result => result.Time("seed")):0.0}・一式を組む {Sum(result => result.Time("start")):0.0}・窓の中身を作る {Sum(result => result.Time("window")):0.0}・載せて落ち着くまで {Sum(result => result.Time("present")):0.0}");

        // 落ち着いたとみなす長さ（Stage.QuietSpan）の根拠。これより長く止まった後に変わった場面があれば、短くしすぎている
        var broken = timed.OrderByDescending(result => result.Time("brokenQuiet")).Take(5).ToList();
        text.AppendLine("  止まって見えた後に変わった、いちばん長い間（ms）：" + string.Join("・", broken.Select(result => $"{result.Scene.Name} {result.Time("brokenQuiet")}")));
        text.AppendLine();
        text.AppendLine("重い場面（親から見た秒・組む・うち待ち・撮る・待ちの回数）");
        foreach (var result in timed.OrderByDescending(result => result.Elapsed).Take(25))
        {
            var sceneShoot = result.Time("total") - result.Time("app") - result.Time("build");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"  {result.Elapsed.TotalSeconds,6:0.0}  組む {result.Time("build") / 1000.0,5:0.0}（待ち {result.Time("buildSettle") / 1000.0:0.0}）・撮る {sceneShoot / 1000.0,5:0.0}・{result.Time("settles"),2} 回  {result.Scene.Name}");
        }

        return text.ToString();
    }

    /// <summary>場面ごとの内訳を表で（表計算で開いて並べ替える）。</summary>
    private static string TimesTable(IReadOnlyList<SceneResult> results)
    {
        string[] keys = ["startup", "app", "seed", "start", "window", "present", "build", "buildSettle", "settle", "settles", "render", "renders", "save", "total", "brokenQuiet"];
        var text = new StringBuilder();
        text.AppendLine("scene\twall\t" + string.Join('\t', keys) + "\timages\tfailed");
        foreach (var result in results)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{result.Scene.Name}\t{(long)result.Elapsed.TotalMilliseconds}\t{string.Join('\t', keys.Select(key => result.Time(key)))}\t{result.Images.Count}\t{(result.Error is null ? 0 : 1)}");
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
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"note\">{(result.Reused ? "前の回の画像（撮り直していない）・" : string.Empty)}{result.Elapsed.TotalSeconds:0.0} 秒（組む {result.Time("build") / 1000.0:0.0}・うち待ち {result.Time("buildSettle") / 1000.0:0.0}・待ち {result.Time("settles")} 回）</div>");
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
