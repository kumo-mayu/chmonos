using System.Text;
using BoothAssetManager.Core.Booth;

namespace BoothAssetManager.Cli;

/// <summary>
/// データ層を実データで検証するための道具。GUIができるまでの足場で、配布物ではない。
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "h2" => await RunH2Async(args),
                "sample" => await RunSampleAsync(args),
                "scan" => await RunScanAsync(args),
                "resolve" => await RunResolveAsync(args),
                "avatars" => await RunAvatarsAsync(args),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"エラー: {exception.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("使い方:");
        Console.WriteLine("  scan <フォルダ> [<フォルダ> ...]        取り込みを実行する（BOOTHへ通信します）");
        Console.WriteLine("  resolve <ファイル> [...]                未確定ファイルの候補を出す（BOOTHへ通信します）");
        Console.WriteLine("  avatars [--offline]                     対応アバターを検出する（BOOTHへ通信します）");
        Console.WriteLine("  h2 <商品ページのHTMLファイル>            説明文のセクション抽出を確認する");
        Console.WriteLine("  sample <商品JSON> [商品ページHTML]      保存されるJSONの形を確認する");
        Console.WriteLine();
        Console.WriteLine("保存先: " + Core.Storage.AppPaths.Default.Root);
    }


    /// <summary>
    /// 対応アバターの検出を実データで確かめる。
    /// --offline を付けるとBOOTHへ問い合わせず、手元の材料だけで走る。
    /// </summary>
    private static async Task<int> RunAvatarsAsync(string[] args)
    {
        var offline = args.Contains("--offline", StringComparer.OrdinalIgnoreCase);

        var paths = Core.Storage.AppPaths.Default;
        var store = new Core.Storage.DataStore(paths);
        var settings = store.Settings.Load();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        Core.Booth.IBoothClient? client = offline ? null : new BoothClient(httpClient, settings);
        var service = new Core.Services.AvatarService(store, settings, client);

        var progress = new SynchronousProgress<Core.Services.AvatarDetectProgress>(report =>
        {
            if (report.Done % 25 == 0 || report.Done == report.Total)
            {
                Console.WriteLine($"  {report.Phase}: {report.Done}/{report.Total}");
            }
        });

        var result = await service.DetectAsync(progress);

        Console.WriteLine();
        Console.WriteLine($"走査した商品      : {result.ItemsScanned}");
        Console.WriteLine($"書き換えた商品    : {result.ItemsUpdated}");
        Console.WriteLine($"アバター          : {result.AvatarsFound}");
        Console.WriteLine($"アバターでなかった: {result.NonAvatars}");
        Console.WriteLine($"共通素体グループ  : {result.BaseGroupsFound}");
        Console.WriteLine($"BOOTHへの問い合わせ: {result.Requests}");
        Console.WriteLine($"保留（通信失敗）  : {result.Unresolved}");
        Console.WriteLine();

        foreach (var avatar in await service.LoadAsync())
        {
            var owned = avatar.IsOwned ? "所有" : "　　";
            Console.WriteLine($"  {owned} {avatar.Entry.DisplayName} "
                + $"(直接 {avatar.DirectCount} / 素体経由 {avatar.ViaBaseCount})");
        }

        foreach (var group in await service.LoadBasesAsync())
        {
            Console.WriteLine($"  素体 {group.Group.Name}: アバター {group.MemberCount} / 名指し商品 {group.ItemCount}");
        }

        return 0;
    }
    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"不明なコマンド: {command}");
        PrintUsage();
        return 1;
    }

    /// <summary>
    /// 実フォルダを取り込む。3フェーズの進捗を出しながら、items/ と images/ を作る。
    /// 途中で Ctrl+C を押しても、再実行すれば済んだ分は飛ばして続きから進む。
    /// </summary>
    private static async Task<int> RunScanAsync(string[] args)
    {
        var folders = args.Skip(1).Where(Directory.Exists).ToList();
        if (folders.Count == 0)
        {
            Console.Error.WriteLine("存在するフォルダを1つ以上指定してください。");
            return 1;
        }

        var paths = Core.Storage.AppPaths.Default;
        using var instanceLock = Core.Storage.SingleInstanceLock.TryAcquire(paths);
        if (instanceLock is null)
        {
            Console.Error.WriteLine("既に起動しています。");
            return 1;
        }

        var store = new Core.Storage.DataStore(paths);
        var settings = store.Settings.Load();

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var client = new BoothClient(httpClient, settings);
        var images = new Core.Images.ImagePipeline(client, paths, settings);
        var pipeline = new Core.Scanning.ImportPipeline(store, client, images, settings);
        var handler = new Core.Commands.CommandHandler(pipeline, new Core.Services.ItemService(store, client, images, settings));

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine();
            Console.WriteLine("中断しています…（ここまでの結果は保存されます）");
        };

        Console.WriteLine($"保存先: {paths.Root}");
        Console.WriteLine($"対象  : {string.Join(", ", folders)}");
        Console.WriteLine();

        var lastPhase = (Core.Scanning.ImportPhase?)null;

        // Progress<T> は報告をスレッドプールへ投げるため、フェーズの切り替わり判定が競合して
        // 同じ見出しが何度も出てしまう。ここでは順序が要るので同期的に呼ぶ実装を使う。
        var progress = new SynchronousProgress<Core.Scanning.ImportProgress>(report =>
        {
            if (lastPhase != report.Phase)
            {
                lastPhase = report.Phase;
                Console.WriteLine();
                Console.WriteLine(report.Phase switch
                {
                    Core.Scanning.ImportPhase.Scanning => "1. ファイルを走査",
                    Core.Scanning.ImportPhase.Resolving => "2. BoothIDを解決",
                    _ => "3. BOOTHから取得（1件ずつ間隔を空けます）",
                });
            }

            var total = report.Total > 0 ? $"/{report.Total}" : string.Empty;
            Console.Write($"\r  {report.Current}{total}  {Truncate(report.Detail, 60)}".PadRight(90));
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await handler.ExecuteAsync(
                new Core.Commands.UiCommand.ScanFolders(folders),
                progress,
                cancellation.Token);

            Console.WriteLine();
            Console.WriteLine();

            if (result is Core.Commands.CommandResult.Imported imported)
            {
                PrintSummary(imported.Summary, stopwatch.Elapsed);
                return 0;
            }

            Console.Error.WriteLine(result is Core.Commands.CommandResult.Failed failed ? failed.Message : "失敗しました。");
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("中断しました。再実行すると続きから進みます。");
            return 130;
        }
    }

    /// <summary>
    /// 手掛かりが無いファイルに対して、検索と検証で候補を出す。
    /// Zone.Identifier もZIP内URLも無い既存ライブラリ向けのフォールバック。
    /// </summary>
    private static async Task<int> RunResolveAsync(string[] args)
    {
        var files = args.Skip(1).Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            Console.Error.WriteLine("存在するファイルを1つ以上指定してください。");
            return 1;
        }

        var settings = new Core.Storage.DataStore(Core.Storage.AppPaths.Default).Settings.Load();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var resolver = new Core.Resolution.FallbackResolver(new BoothClient(httpClient, settings));

        var strong = 0;
        foreach (var file in files)
        {
            var query = Core.Resolution.FileNameQuery.ToSearchQuery(file);
            Console.WriteLine($"■ {Path.GetFileName(file)}");
            Console.WriteLine($"  検索語: {query}");

            var candidates = await resolver.ProposeAsync(file);
            if (candidates.Count == 0)
            {
                Console.WriteLine("  候補なし");
                Console.WriteLine();
                continue;
            }

            foreach (var candidate in candidates)
            {
                var mark = candidate.IsStrong ? "◎" : "・";
                Console.WriteLine($"  {mark} [{candidate.Score,2}点] {candidate.ItemId}  {candidate.Name}");
                Console.WriteLine($"        ショップ: {candidate.ShopName} ({candidate.ShopSubdomain})");
                foreach (var reason in candidate.Reasons)
                {
                    Console.WriteLine($"        - {reason}");
                }
            }

            if (candidates[0].IsStrong)
            {
                strong++;
            }

            Console.WriteLine();
        }

        Console.WriteLine($"裏付けの取れた候補が出たファイル: {strong} / {files.Count}");
        return 0;
    }

    private static void PrintSummary(Core.Scanning.ImportSummary summary, TimeSpan elapsed)
    {
        Console.WriteLine("── 結果 ──");
        Console.WriteLine($"  走査したファイル      : {summary.FilesScanned}");
        Console.WriteLine($"  ハッシュを計算        : {summary.FilesHashed}");
        Console.WriteLine($"  キャッシュを再利用    : {summary.FilesReusedFromCache}");
        Console.WriteLine($"  除外                  : {summary.FilesExcluded}");
        Console.WriteLine($"  展開先として除外      : {summary.FilesSkippedAsUnpacked}");
        Console.WriteLine($"  新規に取得したitem    : {summary.ItemsAdded}");
        Console.WriteLine($"  取得済みだったitem    : {summary.ItemsAlreadyKnown}");
        Console.WriteLine($"  未確定                : {summary.UnresolvedFiles}");
        Console.WriteLine($"  404（非公開の疑い）   : {summary.NotFound}");
        Console.WriteLine($"  一時エラー            : {summary.TemporaryFailures}");
        Console.WriteLine($"  取得した画像          : {summary.ImagesDownloaded}");
        Console.WriteLine($"  所要時間              : {elapsed:mm\\:ss}");

        if (summary.UnpackedFolders.Count == 0)
        {
            return;
        }

        var totalBytes = summary.UnpackedFolders.Sum(folder => folder.TotalBytes);
        Console.WriteLine();
        Console.WriteLine($"── 展開先とみなしたフォルダ（{summary.UnpackedFolders.Count} 件 / 計 {ToHumanReadable(totalBytes)}）──");
        foreach (var folder in summary.UnpackedFolders.OrderByDescending(folder => folder.TotalBytes).Take(10))
        {
            Console.WriteLine($"  {Path.GetFileName(folder.Path),-34} {folder.FileCount,4} ファイル  {ToHumanReadable(folder.TotalBytes),9}");
            Console.WriteLine($"    展開元: {Path.GetFileName(folder.ArchivePath)}");
        }

        Console.WriteLine();
        Console.WriteLine("  ※ これらはアーカイブを展開したものなので取り込んでいません。");
        Console.WriteLine("     アーカイブを残すなら削除して容量を戻せます（削除機能はUI側で確認を挟んで実装予定）。");
    }

    private static string ToHumanReadable(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string Truncate(string? text, int length)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= length ? text : "…" + text[^(length - 1)..];
    }

    /// <summary>
    /// 保存済みの商品JSON（と任意で商品ページHTML）から、実際にディスクへ書かれる形を組み立てて表示する。
    /// 「JSONを直接開いて読めるか」を目で確かめるための道具。
    /// </summary>
    private static async Task<int> RunSampleAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("商品JSONのパスを指定してください。");
            return 1;
        }

        var json = await File.ReadAllTextAsync(args[1]);

        IReadOnlyList<Core.Models.H2Section> sections = [];
        if (args.Length >= 3 && File.Exists(args[2]))
        {
            sections = H2SectionExtractor.Extract(await File.ReadAllTextAsync(args[2])).Sections;
        }

        var itemId = BoothItemMapper.ReadItemId(json) ?? "unknown";
        var item = new Core.Models.ItemRecord
        {
            Id = itemId,
            Booth = BoothItemMapper.Map(json, DateTimeOffset.Now, sections),
            Local = new Core.Models.LocalBlock
            {
                AppTags = [new Core.Models.AppTagAssignment { Top = "小物", Subs = ["ギミック"] }],
                Attributes = new Dictionary<string, int> { ["かっこいい"] = 70 },
                Memo = "動作確認済み",
                AcquiredAt = new DateOnly(2026, 8, 14),
                LocalFiles =
                [
                    new Core.Models.LocalFileRecord
                    {
                        Hash = "545E12139F484E6030EFA0E416A49EBF81108E288FF846A87D398383A4ACE478",
                        Paths = [@"D:\storage\VRChat_clothes\example.zip", @"E:\backup\example.zip"],
                        SizeBytes = 114597377,
                        Contents = ["example/README.txt", "example/example.unitypackage"],
                    },
                ],
                Purchases =
                [
                    new Core.Models.Purchase
                    {
                        VariationId = 12826082,
                        NameSnapshot = null,
                        Price = 2500,
                    },
                ],
            },
        };

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"booth-sample-{itemId}.json");
        Core.Storage.JsonStore.Write(temporaryPath, item);

        Console.WriteLine(await File.ReadAllTextAsync(temporaryPath));
        Console.WriteLine();
        Console.WriteLine($"論理容量   : {item.LogicalSizeBytes:N0} バイト（重複を1回だけ計上）");
        Console.WriteLine($"実占有量   : {item.ActualDiskBytes:N0} バイト（2箇所ぶん）");
        Console.WriteLine($"所持       : {item.IsDownloaded}");
        Console.WriteLine($"出力先     : {temporaryPath}");

        return 0;
    }

    /// <summary>保存済みの商品ページHTMLに対して、セクション抽出の結果を目で確認する。</summary>
    private static async Task<int> RunH2Async(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("HTMLファイルのパスを指定してください。");
            return 1;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"ファイルが見つかりません: {path}");
            return 1;
        }

        var html = await File.ReadAllTextAsync(path);
        var result = H2SectionExtractor.Extract(html);

        Console.WriteLine($"ファイル      : {Path.GetFileName(path)}");
        Console.WriteLine($"説明の本文    : {(result.HasDescriptionBody ? "あり" : "なし")}");
        Console.WriteLine($"セクション数  : {result.Sections.Count}");
        Console.WriteLine();

        foreach (var section in result.Sections)
        {
            var marker = H2SectionExtractor.IsUpdateHistoryHeading(section.Heading) ? " [更新履歴]" : string.Empty;
            Console.WriteLine($"■ {section.Heading}{marker}");
            if (section.NormalizedHeading != section.Heading)
            {
                Console.WriteLine($"  正規化後: {section.NormalizedHeading}");
            }

            var preview = section.Text.Length > 80 ? section.Text[..80] + "…" : section.Text;
            Console.WriteLine($"  本文: {preview.Replace("\n", " / ")}");
            Console.WriteLine();
        }

        return 0;
    }
}
