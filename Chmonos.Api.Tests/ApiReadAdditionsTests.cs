using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Microsoft.AspNetCore.Builder;

namespace Chmonos.Api.Tests;

/// <summary>
/// 2026-10-14 に足した読む道（説明文・画像の形式と大きさ・batch の include の配列・送れる unitypackage で絞る）を HTTP で叩く。
/// 使い手の試作が書いた不足（Web の API-GAPS G7・Unity の API-GAPS 3-1・3-2・3-9）から。保存先は作り物の写し、名前・ID は作り物
/// </summary>
public sealed class ApiReadAdditionsTests : IAsyncLifetime
{
    private const string Token = "test-token-0123456789abcdef";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "chmonos-api-tests", Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var store = new DataStore(new AppPaths(_root));
        await store.Items.SaveAsync(new ItemRecord
        {
            Id = "9600001",
            Booth = new BoothBlock
            {
                Name = "作り物の夏服",
                Description = "作り物の短い説明",
                H2Sections = [new H2Section { Heading = "利用規約", Text = "作り物の規約の文" }],
            },
            Local = new LocalBlock
            {
                LocalFiles =
                [
                    new LocalFileRecord
                    {
                        Hash = new string('A', 64), Paths = [@"D:\files\summer.zip"], SizeBytes = 1,
                        UnityPackages = [new UnityPackageSummary { Entry = "summer.unitypackage", Roots = ["Assets/Summer"] }],
                    },
                ],
            },
        });
        await store.Items.SaveDescriptionHtmlAsync("9600001", "<h2>利用規約</h2><p>作り物の規約の文</p>");
        await store.Items.SaveAsync(new ItemRecord { Id = "9600002", Booth = new BoothBlock { Name = "作り物の冬服" } });

        var images = Path.Combine(_root, "images", "9600001");
        Directory.CreateDirectory(images);
        using (var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(64, 48))
        {
            await SixLabors.ImageSharp.ImageExtensions.SaveAsWebpAsync(image, Path.Combine(images, "0011223344.webp"));
        }

        await File.WriteAllBytesAsync(Path.Combine(images, "5566778899.webp"), [1, 2, 3]);

        (_app, _, _) = await ApiHost.BuildAsync(new ApiHostOptions(_root, 0, Token, []));
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{new Uri(_app.Urls.Single()).Port}") };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task 説明文は_短い説明と見出しごとの文と表示用のHTMLを返す()
    {
        var answer = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/items/9600001/description"));
        Assert.Equal("作り物の短い説明", answer.GetProperty("summary").GetString());
        Assert.Equal("利用規約", answer.GetProperty("sections")[0].GetProperty("heading").GetString());
        Assert.Equal("作り物の規約の文", answer.GetProperty("sections")[0].GetProperty("text").GetString());
        Assert.Equal("<h2>利用規約</h2><p>作り物の規約の文</p>", answer.GetProperty("html").GetString());

        var empty = await JsonOf(await SendAsync(HttpMethod.Get, "/v1/items/9600002/description"));
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("html").ValueKind);

        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/items/9999999/description"), HttpStatusCode.NotFound, "itemNotFound");
    }

    [Fact]
    public async Task 画像は頼まれたときだけ_PNGやJPEGに書き直して縮める()
    {
        var png = await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?format=png&size=16");
        Assert.Equal(HttpStatusCode.OK, png.StatusCode);
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
        var info = SixLabors.ImageSharp.Image.Identify(await png.Content.ReadAsByteArrayAsync());
        Assert.Equal((16, 12), (info.Width, info.Height));

        var jpeg = await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?format=jpeg");
        Assert.Equal("image/jpeg", jpeg.Content.Headers.ContentType?.MediaType);

        // 省けば今までどおり、元のファイルをそのまま
        var original = await SendAsync(HttpMethod.Get, "/v1/images/9600001/5566778899.webp");
        Assert.Equal("image/webp", original.Content.Headers.ContentType?.MediaType);
        Assert.Equal([1, 2, 3], await original.Content.ReadAsByteArrayAsync());

        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?format=gif"), HttpStatusCode.BadRequest, "invalidFormat");
        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?size=8"), HttpStatusCode.BadRequest, "invalidSize");
        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?size=4096"), HttpStatusCode.BadRequest, "invalidSize");
        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/images/9600001/0011223344.webp?size=abc"), HttpStatusCode.BadRequest, "invalidSize");
        await AssertProblem(await SendAsync(HttpMethod.Get, "/v1/images/9600001/5566778899.webp?format=png"), HttpStatusCode.UnprocessableEntity, "imageUnreadable");
    }
}
