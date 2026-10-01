using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Chmonos.Core.Commands;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;

namespace Chmonos.App.Services;

/// <summary>
/// YouTube の動画のタイトルを取る（ユーザ指示 2026-09-14：取れるならリンクした絵とタイトルを出す）。
///
/// 鍵の要らない oEmbed（<c>youtube.com/oembed</c>）を使う。BOOTH への問い合わせではないのでゲートは通さないが、
/// 同じ作法にする：**1本ずつ**、アプリの名前で名乗る（ブラウザを名乗らない）、**商品ページの動画の欄を開いたときだけ**。
///
/// **取れたタイトルは控え（video-titles.json）に残し、30日以内なら聞き直さない**（ユーザ判断 2026-09-14：何の動画か分からないので控える）。
/// 30日を過ぎたら聞き直し、非公開・削除で取れなくなっていれば控えを消す（YouTube の開発者ポリシー III.E.4・<see cref="VideoTitleBook"/>）。
/// 通信の失敗は覚えない（次に開いたときにもう一度試す）。取れなくても、リンクは出せる。
/// </summary>
public sealed class YouTubeInfo(JsonFileStore<List<VideoTitleRecord>> store, CommandHandler commands)
{
    private static readonly HttpClient Http = CreateClient();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>このアプリを閉じるまでの答え（取れた題・取れなかったこと）。控えを読み直さずに済ませる。</summary>
    private readonly ConcurrentDictionary<string, string?> _titles = new(StringComparer.Ordinal);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Core.Booth.BoothClient.UserAgent);
        return client;
    }

    /// <returns>タイトル。非公開・削除・通信の失敗で取れなければ null。</returns>
    public async Task<string?> TitleAsync(string videoId, string url)
    {
        if (_titles.TryGetValue(videoId, out var known))
        {
            return known;
        }

        await _gate.WaitAsync();
        try
        {
            if (_titles.TryGetValue(videoId, out known))
            {
                return known;
            }

            // 読むだけなので控えは直に読む（ディスクは画面のスレッドの外で）
            var stored = VideoTitleBook.FreshTitle(await Task.Run(store.Load), videoId, DateTimeOffset.Now);
            if (stored is not null)
            {
                _titles[videoId] = stored;
                return stored;
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
            else if (response.StatusCode is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                     or HttpStatusCode.Forbidden or HttpStatusCode.NotFound))
            {
                // 混んでいる・向こうの不具合（429・5xx）は通信の失敗と同じ。覚えも消しもしない
                return null;
            }

            // 返事があった物は覚える（非公開・削除は 401・404 で返る。何度聞いても同じ）。
            // 控えにも書く——取れなかったなら、30日を過ぎた古い題を残さないよう消す
            _titles[videoId] = title;
            await RememberAsync(videoId, title);
            return title;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Error($"動画のタイトルを取る（{videoId}）", exception);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>控えに書く。書けなくても題は出せるので、ログに残して進む。</summary>
    private async Task RememberAsync(string videoId, string? title)
    {
        try
        {
            if (await commands.ExecuteAsync(new UiCommand.RememberVideoTitle(videoId, title)) is CommandResult.Failed failed)
            {
                AppLog.Error($"動画のタイトルを控える（{videoId}）", new InvalidOperationException(failed.Message));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"動画のタイトルを控える（{videoId}）", exception);
        }
    }
}
