using System.IO.Compression;
using System.Text;
using BoothZipInspector.Models;

namespace BoothZipInspector;

public static class Program
{
    private static volatile bool _cancelled;

    public static int Main(string[] args)
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cancelled = true;
            Console.WriteLine();
            Console.WriteLine("中断しました。");
        };

        string rawPath;
        if (args.Length > 0)
        {
            rawPath = string.Join(' ', args);
        }
        else
        {
            Console.WriteLine("ZIPファイルをこのウィンドウへドラッグ＆ドロップして、Enterを押してください：");
            rawPath = Console.ReadLine() ?? string.Empty;
        }

        if (_cancelled)
        {
            return 130;
        }

        var path = PathNormalizer.Normalize(rawPath);
        int exitCode;

        var validationError = Validate(path);
        if (validationError is not null)
        {
            Console.Error.WriteLine(validationError);
            exitCode = 1;
        }
        else
        {
            try
            {
                exitCode = Run(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"想定外のエラーが発生しました: {ex.GetType().Name}: {ex.Message}");
                exitCode = 1;
            }
        }

        WaitForKeyBeforeExit();
        return exitCode;
    }

    private static void WaitForKeyBeforeExit()
    {
        if (_cancelled)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine("終了するには何かキーを押してください...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            // 入出力がリダイレクトされている場合はキー入力を待てないため無視する。
        }
    }

    private static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "ファイルパスが指定されていません。";
        }

        if (Directory.Exists(path))
        {
            return $"ディレクトリが指定されました。ZIPファイルを指定してください: {path}";
        }

        if (!File.Exists(path))
        {
            return $"ファイルが見つかりません: {path}";
        }

        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return $"ZIP以外の拡張子です。.zipファイルを指定してください: {path}";
        }

        return null;
    }

    private static int Run(string path)
    {
        FileBasicInfo? fileInfo = null;
        try
        {
            fileInfo = FileInspector.Inspect(path);
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"アクセス権がありません: {path}");
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ファイルを開けませんでした（使用中の可能性があります）: {path}");
            Console.Error.WriteLine($"詳細: {ex.Message}");
            return 1;
        }

        PrintFileSection(fileInfo);

        var zoneInfo = ZoneIdentifierReader.Read(path);
        PrintZoneIdentifierSection(zoneInfo);

        ZipInspectionResult? zipResult;
        try
        {
            zipResult = ZipInspector.Inspect(path);
        }
        catch (InvalidDataException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ZIPファイルとして開けませんでした（壊れている可能性があります）: {path}");
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ZIPファイルへのアクセス権がありません: {path}");
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"ZIPファイルを開けませんでした（使用中の可能性があります）: {path}");
            Console.Error.WriteLine($"詳細: {ex.Message}");
            return 1;
        }

        PrintZipSection(zipResult.Summary);
        PrintBoothCluesSection(zipResult.Clues);
        return 0;
    }

    private static void PrintFileSection(FileBasicInfo info)
    {
        Console.WriteLine("=== ファイル ===");
        Console.WriteLine($"パス: {info.FullPath}");
        Console.WriteLine($"ファイル名: {info.FileName}");
        Console.WriteLine($"拡張子: {info.Extension}");
        Console.WriteLine($"サイズ: {FileInspector.ToHumanReadableSize(info.SizeBytes)} ({info.SizeBytes:N0} bytes)");
        Console.WriteLine($"作成日時: {info.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"最終更新日時: {info.ModifiedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"SHA-256: {info.Sha256Hex}");
        Console.WriteLine();
    }

    private static void PrintZoneIdentifierSection(ZoneIdentifierInfo info)
    {
        Console.WriteLine("=== ダウンロード元情報 ===");
        if (!info.Found)
        {
            Console.WriteLine("Zone.Identifier: 見つかりませんでした");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"ZoneId: {info.ZoneId ?? "見つかりませんでした"}");
        Console.WriteLine($"ReferrerUrl: {info.ReferrerUrl ?? "見つかりませんでした"}");
        Console.WriteLine($"HostUrl: {info.HostUrl ?? "見つかりませんでした"}");
        Console.WriteLine($"BOOTH商品ID: {info.BoothItemId ?? "見つかりませんでした"}");
        Console.WriteLine();
    }

    private static void PrintZipSection(ZipSummary summary)
    {
        Console.WriteLine("=== ZIP ===");
        Console.WriteLine($"エントリ数: {summary.EntryCount}");
        Console.WriteLine($"非圧縮時合計: {FileInspector.ToHumanReadableSize(summary.TotalUncompressedSize)} ({summary.TotalUncompressedSize:N0} bytes)");
        Console.WriteLine($"圧縮後合計: {FileInspector.ToHumanReadableSize(summary.TotalCompressedSize)} ({summary.TotalCompressedSize:N0} bytes)");
        Console.WriteLine();

        Console.WriteLine("[ファイル一覧]");
        if (summary.Files.Count == 0)
        {
            Console.WriteLine("ファイルが見つかりませんでした");
        }
        else
        {
            foreach (var file in summary.Files)
            {
                var uncompressed = FileInspector.ToHumanReadableSize(file.UncompressedSize);
                var compressed = FileInspector.ToHumanReadableSize(file.CompressedSize);
                Console.WriteLine($"{file.RelativePath} | {uncompressed} -> {compressed}");
            }
        }

        Console.WriteLine();
    }

    private static void PrintBoothCluesSection(IReadOnlyList<BoothClue> clues)
    {
        Console.WriteLine("=== BOOTH手掛かり ===");
        if (clues.Count == 0)
        {
            Console.WriteLine("見つかりませんでした");
            return;
        }

        foreach (var clue in clues.Where(c => c.Kind == BoothClueKind.ItemUrl))
        {
            Console.WriteLine($"商品ID: {clue.ItemId}");
            Console.WriteLine($"URL: {clue.Url}");
            Console.WriteLine($"発見元: {clue.SourcePath}");
            Console.WriteLine();
        }

        foreach (var clue in clues.Where(c => c.Kind == BoothClueKind.ShopUrl))
        {
            Console.WriteLine("ショップURL: " + clue.Url);
            Console.WriteLine($"発見元: {clue.SourcePath}");
            Console.WriteLine();
        }

        foreach (var clue in clues.Where(c => c.Kind == BoothClueKind.OtherBoothUrl))
        {
            Console.WriteLine("その他のURL: " + clue.Url);
            Console.WriteLine($"発見元: {clue.SourcePath}");
            Console.WriteLine();
        }
    }
}
