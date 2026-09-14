using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using BoothAssetManager.Core.Diagnostics;

namespace BoothAssetManager.App.Services;

/// <summary>
/// YouTube の動画のタイトルを取る（ユーザ指示 2026-09-14：取れるならリンクした絵とタイトルを出す）。
///
/// 鍵の要らない oEmbed（<c>youtube.com/oembed</c>）を使う。BOOTH への問い合わせではないのでゲートは通さないが、
/// 同じ作法にする：**1本ずつ**、アプリの名前で名乗る（ブラウザを名乗らない）、**商品ページの動画の欄を開いたときだけ**。
/// 答え（取れた題・非公開や削除で取れなかったこと）はアプリを閉じるまで覚え、同じ動画を二度問い合わせない。
/// 通信の失敗は覚えない（次に開いたときにもう一度試す）。取れなくても、リンクは出せる。
/// </summary>
public static class YouTubeInfo
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<string, string?> Titles = new(StringComparer.Ordinal);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Core.Booth.BoothClient.UserAgent);
        return client;
    }

    /// <returns>タイトル。非公開・削除・通信の失敗で取れなければ null。</returns>
    public static async Task<string?> TitleAsync(string videoId, string url)
    {
        if (Titles.TryGetValue(videoId, out var known))
        {
            return known;
        }

        await Gate.WaitAsync();
        try
        {
            if (Titles.TryGetValue(videoId, out known))
            {
                return known;
            }

            using var response = await Http.GetAsync($"https://www.youtube.com/oembed?format=json&url={Uri.EscapeDataString(url)}");
            string? title = null;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (document.RootElement.TryGetProperty("title", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    title = value.GetString();
                }
            }

            // 返事があった物は覚える（非公開・削除は 401・404 で返る。何度聞いても同じ）
            Titles[videoId] = title;
            return title;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Error($"動画のタイトルを取る（{videoId}）", exception);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
