using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BoothAssetManager.App.Services;
using BoothAssetManager.App.ViewModels;
using BoothAssetManager.Core.Models;

namespace ViewShot;

/// <summary>
/// 窓を出さずに、画面や部品を PNG に描く台。
///
/// <code>
/// ViewShot list
/// ViewShot shot &lt;場面&gt;[,&lt;場面&gt;…] | --all  [--theme light|dark|both] [--width 900,1280] [--height 800]
///                                          [--scale 1,1.5] [--zoom 125] [--crop x,y,幅,高さ] [--full] [--out フォルダ] [--jobs 3]
/// ViewShot diff &lt;前.png&gt; &lt;後.png&gt; [--out 並べた画像.png] [--tolerance 0]
/// ViewShot diff &lt;前のフォルダ&gt; &lt;後のフォルダ&gt;
/// ViewShot peers &lt;場面&gt;
/// </code>
///
/// 何が確かめられて何が確かめられないかは <c>.claude/skills/ui-check/SKILL.md</c>「窓を出さずに描く」。
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 呼んだ側（PowerShell・bash）が何であっても日本語が化けないように
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        try
        {
            return args.FirstOrDefault() switch
            {
                "list" => List(),
                "shot" => ShotCommand(args[1..]),
                "peers" => ShotCommand(args[1..], peers: true),
                "diff" => DiffCommand(args[1..]),
                _ => Usage(),
            };
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    private static int Usage()
    {
        Console.WriteLine(
            """
            窓を出さずに、画面や部品を PNG に描く。

              ViewShot list
              ViewShot shot <場面>[,<場面>…] | --all [--theme light|dark|both] [--width 900,1280] [--height 800]
                            [--scale 1,1.5] [--zoom 125] [--crop x,y,幅,高さ] [--full] [--out フォルダ] [--jobs 3]
              ViewShot diff <前.png> <後.png> [--out 並べた画像.png] [--tolerance 0]
              ViewShot diff <前のフォルダ> <後のフォルダ>
              ViewShot peers <場面>      読み上げ・自動操作の窓口の木を文字で書き出す（名前・型・押せるか）

            画像は既定で %TEMP%\chmonos-shots\view\ に置く（リポジトリの外）。
            """);
        return 2;
    }

    private static int List()
    {
        foreach (var scene in Scenes.All)
        {
            var size = scene.Width is { } width
                ? $"{width}x{(scene.Height is { } height ? height.ToString(CultureInfo.InvariantCulture) : "中身")}"
                : "中身に合わせる";
            Console.WriteLine($"{scene.Name,-28} {size,-12} {scene.Title}");
        }

        return 0;
    }

    // ---- shot ----

    private static int ShotCommand(string[] args, bool peers = false)
    {
        var names = new List<string>();
        var all = false;
        var themes = new List<ColorThemeMode> { ColorThemeMode.Light };
        var widths = new List<double>();
        double? height = null;
        var scales = new List<double> { 1.0 };
        var zoom = DisplayZoom.DefaultPercent;
        Rect? crop = null;
        var full = false;
        var jobs = 3;

        // 一時フォルダは、切り離す（Isolation.Enter）前の本来の場所で決める
        var outDir = Path.Combine(Path.GetTempPath(), "chmonos-shots", "view");

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません。");
            switch (args[i])
            {
                case "--all":
                    all = true;
                    break;
                case "--theme":
                    themes = Next() switch
                    {
                        "light" => [ColorThemeMode.Light],
                        "dark" => [ColorThemeMode.Dark],
                        "both" => [ColorThemeMode.Light, ColorThemeMode.Dark],
                        var other => throw new ArgumentException($"--theme は light・dark・both のどれか（{other}）。"),
                    };
                    break;
                case "--width":
                    widths = Numbers(Next());
                    break;
                case "--height":
                    height = Numbers(Next())[0];
                    break;
                case "--scale":
                    scales = Numbers(Next());
                    break;
                case "--zoom":
                    zoom = (int)Numbers(Next())[0];
                    break;
                case "--crop":
                    var box = Numbers(Next());
                    crop = box.Count == 4
                        ? new Rect(box[0], box[1], box[2], box[3])
                        : throw new ArgumentException("--crop は x,y,幅,高さ。");
                    break;
                case "--full":
                    full = true;
                    break;
                case "--out":
                    outDir = Path.GetFullPath(Next());
                    break;
                case "--jobs":
                    jobs = Math.Max(1, (int)Numbers(Next())[0]);
                    break;
                default:
                    names.AddRange(args[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
            }
        }

        var scenes = all ? Scenes.All.ToList() : names.Select(Scenes.Find).ToList();
        if (scenes.Count == 0)
        {
            throw new ArgumentException("場面の名前か --all を渡してください。名前は ViewShot list で出ます。");
        }

        var options = new ShotOptions
        {
            Themes = themes,
            Widths = widths,
            Width = widths.Count > 0 ? widths[0] : null,
            Height = height,
            Scales = scales,
            ZoomPercent = zoom,
            Crop = crop,
            Full = full,
            OutDir = outDir,
            Jobs = jobs,
            Peers = peers,
        };

        if (peers && scenes.Count != 1)
        {
            throw new ArgumentException("peers は場面を1つだけ渡してください（木を続けて書き出すと、どの場面の物か分からなくなる）。");
        }

        return scenes.Count == 1 ? RunOne(scenes[0], options) : RunMany(scenes, args, options);
    }

    private static List<double> Numbers(string text)
        => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new ArgumentException($"数ではありません：{part}"))
            .ToList();

    /// <summary>
    /// 場面ごとに別のプロセスで描く。保存先はプロセスが最初に決めたら変えられない（<c>AppPaths.Default</c>）ので、
    /// 場面ごとにまっさらな保存先を持たせるには、プロセスを分けるしかない。分ければ並べて走らせられる
    /// </summary>
    private static int RunMany(IReadOnlyList<Scene> scenes, string[] args, ShotOptions options)
    {
        var clock = Stopwatch.StartNew();
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("自分の実行ファイルの場所が分かりません。");

        // 場面の名前と --all・--jobs を除いた残り（色・幅・倍率・置き場）を、そのまま子へ渡す
        var passed = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--all" || !args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (args[i] == "--jobs")
            {
                i++;
                continue;
            }

            passed.Add(args[i]);
            if (args[i] != "--full" && i + 1 < args.Length)
            {
                passed.Add(args[++i]);
            }
        }

        var failed = 0;
        var gate = new object();
        Parallel.ForEach(
            scenes,
            new ParallelOptions { MaxDegreeOfParallelism = options.Jobs },
            scene =>
            {
                var start = new ProcessStartInfo(self)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("shot");
                start.ArgumentList.Add(scene.Name);
                foreach (var argument in passed)
                {
                    start.ArgumentList.Add(argument);
                }

                using var child = Process.Start(start)!;
                var errors = child.StandardError.ReadToEndAsync();
                var output = child.StandardOutput.ReadToEnd();
                child.WaitForExit();

                lock (gate)
                {
                    Console.Write(output);
                    if (child.ExitCode != 0)
                    {
                        failed++;
                        Console.Error.WriteLine($"{scene.Name}：描けませんでした（終了コード {child.ExitCode}）");
                        Console.Error.Write(errors.Result);
                    }
                }
            });

        Console.WriteLine($"{scenes.Count} 場面・{clock.Elapsed.TotalSeconds:0.0} 秒（{options.Jobs} 本ずつ）{(failed > 0 ? $"・失敗 {failed}" : string.Empty)}");
        return failed > 0 ? 1 : 0;
    }

    private static int RunOne(Scene scene, ShotOptions options)
    {
        var clock = Stopwatch.StartNew();
        Isolation.Enter(scene.Name);

        // アプリの資源（色の表・標準の部品の見た目・App.xaml の既定）を読む。起動の処理は走らない——
        // WPF は Run を呼ばなくても、コンストラクタで積んだ OnStartup を下の Dispatcher.Run で走らせるが、
        // App の側が「アプリ本体として起動されたときだけ進める」と分けている（App.IsLaunchedAsApp）
        var app = new BoothAssetManager.App.App();
        app.InitializeComponent();

        // 窓を作って捨てるたびに「最後の窓が閉じた」で終わりにされないように
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var exit = 1;
        var dispatcher = Dispatcher.CurrentDispatcher;
        dispatcher.UnhandledException += (_, arguments) =>
        {
            Console.Error.WriteLine($"{scene.Name}：画面の処理で失敗しました。\n{arguments.Exception}");
            arguments.Handled = true;
            exit = 1;
            dispatcher.InvokeShutdown();
        };

        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                exit = await RunSceneAsync(scene, options, clock);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"{scene.Name}：{exception.Message}\n{exception}");
                exit = 1;
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });

        Dispatcher.Run();
        Isolation.Leave();

        // 裏のスレッド（辞書の索引・通信の後始末）が残っていても、描き終えたら終わる
        Console.Out.Flush();
        Environment.Exit(exit);
        return exit;
    }

    private static async Task<int> RunSceneAsync(Scene scene, ShotOptions options, Stopwatch clock)
    {
        using var stage = new Stage();
        stage.SetScale(options.Scales[0]);

        // 色の表。サービス一式を組む前でも当てられる（アプリも、窓を1つも出さないうちに当てている）
        AppTheme.Current.Mode = options.Themes[0];
        stage.SyncColorTable();

        var context = new SceneContext(stage, options, scene);

        // 知らせの窓は出さない。出ようとしたことは結果に書く（場面の途中で失敗しているかもしれない）
        Notice.Intercept = request =>
        {
            context.Notices.Add($"「{request.Caption}」{request.Text}");
            return request.DefaultResult != MessageBoxResult.None
                ? request.DefaultResult
                : request.Button is MessageBoxButton.OK ? MessageBoxResult.OK
                : request.Button is MessageBoxButton.YesNo ? MessageBoxResult.No
                : MessageBoxResult.Cancel;
        };

        var shot = await scene.Build(context);
        if (shot.Still is { } still)
        {
            // 場面が自分で描いたコマ。待って描き直すと、見たかった途中の姿が消える
            var cut = options.Crop is { } box ? Stage.Crop(still, stage.ToPixels(box)) : still;
            var stillPath = Path.Combine(
                options.OutDir, scene.Name + (options.Themes[0] == ColorThemeMode.Dark ? "-dark" : "-light") + ".png");
            Stage.Save(cut, stillPath);
            Console.WriteLine($"{stillPath}\t{cut.PixelWidth}x{cut.PixelHeight}\t場面が描いたコマ");
            Console.WriteLine($"  {scene.Name}：全部で {clock.Elapsed.TotalSeconds:0.0} 秒");
            context.Dispose();
            return 0;
        }

        if (!ReferenceEquals(stage.Content, shot.Root))
        {
            await context.PresentAsync(shot.Root);
        }

        if (options.Peers)
        {
            await stage.SettleAsync();
            PeerTree.Write(shot.Focus?.Invoke() ?? shot.Root, Console.Out);
            context.Dispose();
            return 0;
        }

        var built = clock.Elapsed;
        var widths = options.Widths.Count > 0 ? options.Widths.Cast<double?>().ToList() : [null];

        foreach (var scale in options.Scales)
        {
            stage.SetScale(scale);
            foreach (var width in widths)
            {
                stage.Resize(width ?? scene.Width, options.Height ?? scene.Height);
                foreach (var theme in options.Themes)
                {
                    var lap = Stopwatch.StartNew();
                    AppTheme.Current.Mode = theme;
                    stage.SyncColorTable();

                    // 見たい所が、送らないと見えない所（画面の下の方）にあれば、送って見せる。
                    // 幅や倍率を替えると並びが変わるので、描くたびに探し直す
                    FrameworkElement? Find() => options.Full || options.Crop is not null ? null : shot.Focus?.Invoke();
                    var focus = Find();
                    focus?.BringIntoView();
                    var settled = await stage.SettleAsync();
                    if (focus is null && (focus = Find()) is not null)
                    {
                        focus.BringIntoView();
                        settled = await stage.SettleAsync();
                    }

                    var image = stage.Render();
                    var region = options.Crop is { } box
                        ? stage.ToPixels(box)
                        : focus is not null ? stage.BoundsOf(focus, shot.FocusMargin) : null;
                    if (region is { } cut)
                    {
                        image = Stage.Crop(image, cut);
                    }

                    var lostFocus = shot.Focus is not null && !options.Full && options.Crop is null && region is null;

                    var name = scene.Name
                        + (theme == ColorThemeMode.Dark ? "-dark" : "-light")
                        + (width is { } given ? $"-w{given:0}" : string.Empty)
                        + (Math.Abs(scale - 1.0) > 0.001 ? $"-s{scale * 100:0}" : string.Empty)
                        + (options.ZoomPercent != DisplayZoom.DefaultPercent ? $"-z{options.ZoomPercent}" : string.Empty)
                        + ".png";
                    var path = Path.Combine(options.OutDir, name);
                    Stage.Save(image, path);

                    Console.WriteLine(
                        $"{path}\t{image.PixelWidth}x{image.PixelHeight}\t{lap.ElapsedMilliseconds} ms"
                        + (settled ? string.Empty : "\t落ち着かなかった（動き続ける部品がある。打ち切って描いた）")
                        + (lostFocus ? "\t見たい所が見つからなかったので、全体を出した" : string.Empty));
                }
            }
        }

        foreach (var notice in context.Notices)
        {
            Console.WriteLine($"  知らせの窓が出ようとした（出さずに既定の答えを返した）：{notice.ReplaceLineEndings(" ")}");
        }

        // アプリは裏の作業の失敗を画面に出さず、ログに残す。場面の途中で何かが失敗していたら、絵は描けていても状態が違うかもしれない
        foreach (var line in ReadAppLog())
        {
            Console.WriteLine(line);
        }

        Console.WriteLine($"  {scene.Name}：組むまで {built.TotalSeconds:0.0} 秒・全部で {clock.Elapsed.TotalSeconds:0.0} 秒");
        context.Dispose();
        return 0;
    }

    private static IEnumerable<string> ReadAppLog()
    {
        var log = BoothAssetManager.Core.Storage.AppPaths.Default.LogFile;
        string[] lines;
        try
        {
            if (!File.Exists(log))
            {
                return [];
            }

            // 書く側が開いたままでも読めるように、共有を許して開く
            using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (IOException)
        {
            return ["  アプリのログを読めませんでした（場面の途中の失敗は確かめられていない）"];
        }

        return lines.Length == 0
            ? []
            : [$"  アプリのログに {lines.Length} 行（場面の途中の失敗かもしれない）：", .. lines.Take(5).Select(line => $"    {line}")];
    }

    // ---- diff ----

    private static int DiffCommand(string[] args)
    {
        var paths = new List<string>();
        string? outPath = null;
        var tolerance = 0;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} の値がありません。");
            switch (args[i])
            {
                case "--out":
                    outPath = Path.GetFullPath(Next());
                    break;
                case "--tolerance":
                    tolerance = (int)Numbers(Next())[0];
                    break;
                default:
                    paths.Add(args[i]);
                    break;
            }
        }

        if (paths.Count != 2)
        {
            throw new ArgumentException("diff には、前と後の画像（かフォルダ）を2つ渡してください。");
        }

        if (Directory.Exists(paths[0]) && Directory.Exists(paths[1]))
        {
            return DiffFolders(paths[0], paths[1], outPath, tolerance);
        }

        var result = ImageDiff.Compare(paths[0], paths[1], tolerance);
        Console.WriteLine(result.Describe());
        if (!result.IsSame && outPath is not null)
        {
            ImageDiff.SaveSideBySide(paths[0], paths[1], outPath, tolerance);
            Console.WriteLine($"並べた画像（前・後・違う所は赤）：{outPath}");
        }

        return result.IsSame ? 0 : 1;
    }

    /// <summary>同じ名前の画像どうしを比べる（直す前に1回、直した後に1回描いて、2つのフォルダを渡す）。</summary>
    private static int DiffFolders(string before, string after, string? outDir, int tolerance)
    {
        var different = 0;
        var names = Directory.EnumerateFiles(before, "*.png").Select(Path.GetFileName)
            .Union(Directory.EnumerateFiles(after, "*.png").Select(Path.GetFileName))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var name in names)
        {
            var first = Path.Combine(before, name!);
            var second = Path.Combine(after, name!);
            if (!File.Exists(first) || !File.Exists(second))
            {
                different++;
                Console.WriteLine($"{name}\t{(File.Exists(first) ? "後に無い" : "前に無い")}");
                continue;
            }

            var result = ImageDiff.Compare(first, second, tolerance);
            Console.WriteLine($"{name}\t{result.Describe()}");
            if (!result.IsSame)
            {
                different++;
                if (outDir is not null)
                {
                    ImageDiff.SaveSideBySide(first, second, Path.Combine(outDir, name!), tolerance);
                }
            }
        }

        Console.WriteLine($"{names.Count} 枚のうち、違うのは {different} 枚");
        return different > 0 ? 1 : 0;
    }
}
