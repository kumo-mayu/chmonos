using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chmonos.Api.Tests;

/// <summary>
/// 問い合わせの中身（トークン・検索の言葉・商品 ID・送った本文）をログに残さない（docs/spec/local-api.md の守り）。
/// 2026-11-28 の点検で、ClearProviders を消しても、トークンを標準エラーに書いても落ちる試験が無かった。
/// 標準エラー・標準出力はプロセスで1つだが、この一式は並べずに走らせる（AssemblyInfo.cs）。名前・ID は作り物
/// </summary>
public sealed class ApiLogTests : IAsyncLifetime
{
    private const string Token = "log-test-token-SECRET-0123456789";
    private const string ItemId = "9612345";
    private const string Word = "目印の検索語";
    private const string Marker = "目印の誤り文";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var store = new DataStore(new AppPaths(_root));
        JsonStore.Write(store.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true });
        await store.Items.SaveAsync(new ItemRecord { Id = ItemId, Booth = new BoothBlock { Name = "作り物の帽子" } });

        // 命令の口が中身を含む文で落ちる（500 の道で、例外の文を書き出さない事を見る）
        var hooks = new ApiWriteHooks((_, _) => throw new InvalidOperationException(Marker + " " + ItemId));
        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, [], Watch: false, Store: store, Writes: hooks));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ログの書き出し先を持たない()
    {
        // 既定の書き出し先（コンソール・デバッグ・イベントログ）は、URL（検索の言葉・商品 ID）を書く
        Assert.Empty(_app!.Services.GetServices<ILoggerProvider>());
    }

    [Fact]
    public async Task 読む頼みと500になる頼みで_トークンと中身を書き出さない()
    {
        var error = new StringWriter();
        var output = new StringWriter();
        var oldError = Console.Error;
        var oldOut = Console.Out;
        Console.SetError(error);
        Console.SetOut(output);
        try
        {
            using (var search = Send(HttpMethod.Post, "/v1/search", $$$"""{"where":{"text":"{{{Word}}}"}}"""))
            using (var answer = await _client.SendAsync(search))
            {
                Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            }

            using (var item = Send(HttpMethod.Get, $"/v1/items/{ItemId}?q={Uri.EscapeDataString(Word)}"))
            using (var answer = await _client.SendAsync(item))
            {
                Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
            }

            using (var write = Send(HttpMethod.Put, $"/v1/items/{ItemId}/favorite", """{"value":true}"""))
            using (var answer = await _client.SendAsync(write))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, answer.StatusCode);
            }
        }
        finally
        {
            Console.SetError(oldError);
            Console.SetOut(oldOut);
        }

        // 500 の道は型と場所を書く（何も書かない事で通ってしまわないよう、書き出しを捕まえられている事も見る）
        var written = error + output.ToString();
        Assert.Contains(nameof(InvalidOperationException), written);
        foreach (var secret in new[] { Token, "SECRET", Word, Uri.EscapeDataString(Word), ItemId, Marker, "/v1/search", "favorite" })
        {
            Assert.DoesNotContain(secret, written);
        }
    }

    private static HttpRequestMessage Send(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }
}
