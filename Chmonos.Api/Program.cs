using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Api;

/// <summary>
/// 外から使う API の入口（docs/spec/local-api.md）。使う人が手で起動する（本体と連動させない。ユーザ判断 2026-10-12）。
/// 保存先はアプリと同じ決め方（環境変数 <c>CHMONOS_HOME</c> ＞ <c>location.json</c> ＞ 既定）。読むだけで、書くのは <c>api.json</c> だけ。
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // アプリ本体と同じく、自分の実行ファイルから起動したときだけ、使う人の本当の保存先を開いてよい（StoreLocation.AllowsUserStore）
        StoreLocation.AllowsUserStore = true;

        string root;
        try
        {
            root = StoreLocation.Resolve().Path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"保存先を決められません: {exception.Message}");
            return 1;
        }

        if (!StoreLocation.LooksLikeStore(root))
        {
            Console.Error.WriteLine($"保存先が見つかりません: {root}");
            Console.Error.WriteLine("Chmonos を一度起動してから、もう一度起動してください。");
            return 1;
        }

        var paths = new AppPaths(root);

        // 保存先ごとに1本だけ。開いたまま持つ錠なので、同時に2本起動しても片方だけが取れる（落ちても OS が放す）
        using var instance = SingleInstanceLock.TryAcquire(paths.Root, paths.ApiLockFile);
        var settings = ApiFiles.ReadSettings(paths);
        var endpoint = ApiFiles.ReadEndpoint(paths);
        if (instance is null)
        {
            // 番号は今も動いている API のときだけ出す（同時に起動すると、もう1本が書く前の古い記録を読む）
            Console.Error.WriteLine(endpoint?.ProcessId is { } other && ApiFiles.IsRunningApi(other)
                ? $"この保存先の API は既に動いています（プロセス {other}・ポート {endpoint.Port}）。"
                : "この保存先の API は既に動いています。");
            return 1;
        }

        // 合言葉は起動のたびには変えない（外の道具が毎回読み直さずに済むように）。--new-token で作り直す
        var token = args.Contains("--new-token") || string.IsNullOrEmpty(endpoint?.Token) ? ApiFiles.NewToken() : endpoint.Token;

        WebApplication app;
        try
        {
            (app, _, var sync) = await ApiHost.BuildAsync(new ApiHostOptions(root, settings.Port, token, settings.AllowedOrigins));
            await app.StartAsync();
            sync.Start();
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"ポート {settings.Port} で待てません（他のソフトが使っているかもしれません）: {exception.Message}");
            Console.Error.WriteLine($"{paths.ApiSettingsFile} の port を変えてから、もう一度起動してください。");
            return 1;
        }

        ApiFiles.WriteEndpoint(paths, new ApiEndpointRecord
        {
            Port = settings.Port,
            Token = token,
            ProcessId = Environment.ProcessId,
            Version = ApiHost.AppVersion,
            StartedAt = DateTimeOffset.Now,
        });

        Console.WriteLine($"Chmonos API {ApiHost.AppVersion}: http://127.0.0.1:{settings.Port}/v1/");
        Console.WriteLine($"保存先: {root}");
        Console.WriteLine($"合言葉は {paths.ApiFile} の token です。止めるには Ctrl+C を押します。");

        try
        {
            await app.WaitForShutdownAsync();
        }
        finally
        {
            ApiFiles.MarkStopped(paths, settings.Port, token);
        }

        return 0;
    }
}

/// <summary><c>api.json</c>（API が書く）と <c>api-settings.json</c>（人と設定画面が書く）。</summary>
internal static class ApiFiles
{
    public static ApiSettings ReadSettings(AppPaths paths)
    {
        var settings = JsonStore.Read<ApiSettings>(paths.ApiSettingsFile) ?? new ApiSettings();

        // 手で直した JSON で欠けた配列（null）を空として受ける
        return settings with { AllowedOrigins = settings.AllowedOrigins ?? [] };
    }

    public static ApiEndpointRecord? ReadEndpoint(AppPaths paths)
    {
        try
        {
            return JsonStore.Read<ApiEndpointRecord>(paths.ApiFile);
        }
        catch (System.Text.Json.JsonException)
        {
            // 壊れていれば作り直す（合言葉は新しくなる）
            return null;
        }
    }

    public static void WriteEndpoint(AppPaths paths, ApiEndpointRecord record) => JsonStore.Write(paths.ApiFile, record);

    /// <summary>
    /// 止まったことを残す（合言葉は残す。次に起動したときも同じ合言葉で使えるように）。
    /// **api.json が自分の記録のときだけ**書く：ほかのプロセスが書いた記録を「止まった」で消すと、動いている API が見えなくなる
    /// </summary>
    public static void MarkStopped(AppPaths paths, int port, string token)
    {
        if (ReadEndpoint(paths)?.ProcessId is { } processId && processId != Environment.ProcessId)
        {
            return;
        }

        WriteEndpoint(paths, new ApiEndpointRecord { Port = port, Token = token, Version = ApiHost.AppVersion });
    }

    /// <summary>合言葉。32バイトの乱数（推し量れない長さ）を URL に使える文字で。</summary>
    public static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>そのプロセスが今も動いている API か（落ちて残った api.json の番号を、別のプロセスが使い回していることがある）。</summary>
    public static bool IsRunningApi(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && string.Equals(process.ProcessName, "Chmonos.Api", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
