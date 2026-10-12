using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// 外から使う API（別の実行ファイル）へ、まとめて書く操作（取り込み）の始まりと終わりを知らせる（docs/spec/local-api.md「同期」）。
/// 書いたファイルは API が見張って拾うので、知らせるのはファイルを見ても分からないこの2つだけ。その間、API は古い答えを返し続ける。
///
/// **アプリの書き込みを API の都合で遅らせない**：知らせは待たずに送り、API が動いていなければ（api.json が無い・つながらない）何もしない。
/// 始まりと終わりの順が入れ替わらないよう、1本ずつ送る。
/// </summary>
public sealed class ApiNotifier(AppPaths paths)
{
    /// <summary>API は同じ PC の中なので、つながるならすぐ答える。待つのは短く。</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly SemaphoreSlim _order = new(1, 1);

    /// <summary>まとめて書く操作が始まった（<paramref name="kind"/>）・終わった（null）。</summary>
    public void NoteBusy(string? kind) => SendAsync(kind).Forget();

    internal async Task SendAsync(string? kind)
    {
        await _order.WaitAsync();
        try
        {
            var endpoint = System.IO.File.Exists(paths.ApiFile) ? JsonStore.Read<ApiEndpointRecord>(paths.ApiFile) : null;
            if (endpoint is not { ProcessId: not null, Token.Length: > 0 })
            {
                return;
            }

            var body = kind is null ? """{"state":"end"}""" : $$"""{"state":"start","kind":"{{kind}}"}""";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{endpoint.Port}/internal/busy")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
            using var _ = await Http.SendAsync(request);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.IO.IOException or System.Text.Json.JsonException)
        {
            // API が止まっている・落ちて api.json だけ残っている。知らせが届かなくても、API は見張りで追いつく（終わりの知らせが来なければ上限で待つのをやめる）
        }
        finally
        {
            _order.Release();
        }
    }
}
