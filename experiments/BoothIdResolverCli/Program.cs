using System.Text;
using BoothZipInspector;

namespace BoothIdResolver;

/// <summary>
/// ZIPファイルをドラッグ＆ドロップ（または引数指定）すると、
/// BOOTH商品ページのURLを表示してクリップボードへコピーする。
/// ネットワーク通信は行わない。判定にはZone.IdentifierとZIP内テキストのみを使う。
/// ウィンドウを閉じる（またはCtrl+C）まで、何度でも続けて使える。
/// </summary>
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

        if (args.Length > 0)
        {
            RunOnce(PathNormalizer.Normalize(string.Join(' ', args)));
        }

        while (!_cancelled)
        {
            Console.WriteLine();
            Console.WriteLine("ZIPファイルをこのウィンドウへドラッグ＆ドロップして、Enterを押してください（終了するにはウィンドウを閉じてください）：");

            var rawPath = Console.ReadLine();
            if (rawPath is null)
            {
                // 入力がリダイレクトされている等でこれ以上読めない場合は終了する。
                break;
            }

            if (_cancelled)
            {
                break;
            }

            RunOnce(PathNormalizer.Normalize(rawPath));
        }

        return _cancelled ? 130 : 0;
    }

    private static void RunOnce(string path)
    {
        var validationError = Validate(path);
        if (validationError is not null)
        {
            Console.Error.WriteLine(validationError);
            return;
        }

        var zone = ZoneIdentifierReader.Read(path);

        ZipInspectionResult zipResult;
        try
        {
            zipResult = ZipInspector.Inspect(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"ZIPファイルを読み取れませんでした: {ex.Message}");
            return;
        }

        var candidates = IdResolver.Resolve(zone, zipResult.Clues);

        if (candidates.Count == 0)
        {
            Console.WriteLine("BOOTH商品IDが見つかりませんでした。");
            return;
        }

        if (candidates.Count > 1)
        {
            Console.WriteLine("複数の候補が見つかったため、自動選択しませんでした。");
            foreach (var c in candidates)
            {
                Console.WriteLine($"  {IdResolver.ToItemUrl(c.ItemId)}  (発見元: {c.Source})");
            }
            return;
        }

        var url = IdResolver.ToItemUrl(candidates[0].ItemId);
        Console.WriteLine(url);
        Console.WriteLine($"(発見元: {candidates[0].Source})");

        if (ClipboardWriter.TrySetText(url))
        {
            Console.WriteLine("クリップボードにコピーしました。");
        }
        else
        {
            Console.WriteLine("クリップボードへのコピーに失敗しました。");
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
}
