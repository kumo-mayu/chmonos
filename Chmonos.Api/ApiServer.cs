using System.Security.Cryptography;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Api;

/// <summary>
/// 外から使う API を、アプリと同じプロセスで動かす（docs/history/headless-core-2026-10-13.md 段2）。アプリが設定でオンのときに始め、オフで止める。
/// 前は別の実行ファイル（<c>Chmonos.Api.exe</c>）で、アプリの書き込みの門・商品の錠をすり抜けずに書き込みを足せなかった。
/// 同じプロセスなら、商品の記録はアプリの控えと共有し（<see cref="QuerySnapshot.SharesItemCache"/>）、取り込み中の知らせも直に受ける。
/// 外の道具はポートと合言葉を <c>api.json</c> から読む（今までどおり）。
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ApiState _state;
    private readonly StoreSync _sync;
    private readonly AppPaths _paths;
    private readonly string _token;

    private ApiServer(WebApplication app, ApiState state, StoreSync sync, AppPaths paths, int port, string token)
    {
        _app = app;
        _state = state;
        _sync = sync;
        _paths = paths;
        Port = port;
        _token = token;
    }

    /// <summary>待っているポート。</summary>
    public int Port { get; }

    /// <summary>
    /// 読み込んで待ち受けを始め、<c>api.json</c> にポートと合言葉を書く。合言葉は始めるたびには変えない（外の道具が毎回読み直さずに済むように）。
    /// ポートが使われていれば <see cref="IOException"/> を投げる（呼んだ側が「待てません」と言う）。
    /// </summary>
    /// <param name="store">アプリの保存先の一式。商品の記録をアプリの控えと共有する。</param>
    /// <param name="writes">書き込みの口（アプリの命令の口など）。無ければ書き込みの URL は 503 で断る。</param>
    public static async Task<ApiServer> StartAsync(DataStore store, ApiWriteHooks? writes = null, CancellationToken cancellationToken = default)
    {
        var paths = store.Paths;
        var settings = ApiFiles.ReadSettings(paths);
        var endpoint = ApiFiles.ReadEndpoint(paths);
        var token = string.IsNullOrEmpty(endpoint?.Token) ? ApiFiles.NewToken() : endpoint.Token;

        var (app, state, sync) = await ApiHost.BuildAsync(
            new ApiHostOptions(paths.Root, settings.Port, token, settings.AllowedOrigins, Store: store, Writes: writes),
            cancellationToken);
        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        sync.Start();
        ApiFiles.WriteEndpoint(paths, new ApiEndpointRecord
        {
            Port = settings.Port,
            Token = token,
            ProcessId = Environment.ProcessId,
            Version = ApiHost.AppVersion,
            StartedAt = DateTimeOffset.Now,
        });
        return new ApiServer(app, state, sync, paths, settings.Port, token);
    }

    /// <summary>
    /// 合言葉を作り直して <c>api.json</c> に書く（止めてから呼ぶ。次に始めたときから新しい合言葉で答える）。
    /// 前は別の実行ファイルの <c>--new-token</c> だった。外の道具は api.json から読み直す
    /// </summary>
    public static void RenewToken(AppPaths paths)
    {
        var port = ApiFiles.ReadEndpoint(paths)?.Port ?? ApiFiles.ReadSettings(paths).Port;
        ApiFiles.WriteEndpoint(paths, new ApiEndpointRecord { Port = port, Token = ApiFiles.NewToken(), Version = ApiHost.AppVersion });
    }

    /// <summary>
    /// アプリがまとめて書いている（取り込みなど。<paramref name="kind"/>）・書き終えた（null）。その間、API は古い答えを返し続け、終わってから読み直す。
    /// 書いたファイルは見張りで拾うので、知らせるのはファイルを見ても分からないこの2つだけ
    /// </summary>
    public void SetBusy(string? kind) => _sync.SetBusy(kind, _state.Clock());

    /// <summary>待ち受けを止め、<c>api.json</c> を「止まった」にする（合言葉は残す。次に始めたときも同じ合言葉で使えるように）。</summary>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        ApiFiles.MarkStopped(_paths, Port, _token);
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
}
