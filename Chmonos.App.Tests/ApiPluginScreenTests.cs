using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 開いた画面に付いていく（docs/spec/plugins.md「開いた画面に付いていく」）：画面を移るとすぐ screenChanged・右の欄の商品は 300ms まとめる・
/// 同じ画面は流し直さない・GET /v1/plugin/current。本物の API を同じプロセスで動かし、まとめの時間は作り物の時計で進める。名前・ID は作り物
/// </summary>
public sealed class ApiPluginScreenTests
{
    private const string Token = "plugin-screen-token-0123456789";

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task Run(Func<TestApp, MainViewModel, HttpClient, ManualTime, Task> body) => await TestApp.Run(async app =>
    {
        for (var index = 1; index <= 4; index++)
        {
            await app.AddItemAsync(Make.Item($"96006{index:00}", $"作り物の商品{index}"));
        }

        JsonStore.Write(app.Services.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                new ApiClientRecord
                {
                    Id = "follower", Name = "作り物の付き添い", Scopes = ApiScopes.Watches,
                    TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token))).ToLowerInvariant(),
                },
            ],
        });

        var time = new ManualTime();
        app.Services.PluginScreenTime = time;
        var main = await app.StartAsync();
        await app.SettleAsync();

        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = port });
        await app.Services.Api.ApplyAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        try
        {
            var response = await client.PutAsync(
                "/v1/plugin/subscriptions", new StringContent("""{"kinds":["screenChanged"]}""", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await body(app, main, client, time);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    private static async Task<JsonElement[]> ScreensAsync(HttpClient client, long after = 0)
    {
        var answer = JsonDocument.Parse(await client.GetStringAsync($"/v1/plugin/events?after={after}")).RootElement;
        return [.. answer.GetProperty("events").EnumerateArray().Select(item => item.GetProperty("screenChanged"))];
    }

    private static string Describe(JsonElement screen)
        => $"{screen.GetProperty("screen").GetString()}:"
            + (screen.GetProperty("target") is { ValueKind: JsonValueKind.Object } target
                ? $"{target.GetProperty("type").GetString()}/{target.GetProperty("id").GetString()}"
                : "-")
            + (screen.GetProperty("embedded").GetBoolean() ? ":embedded" : string.Empty);

    [Fact]
    public Task 画面を移るとすぐ知らせ_開き直しでは流し直さない() => Run(async (app, main, client, time) =>
    {
        var item = main.Search.ListItems[1].Item;

        main.ShowItem(item);
        main.ReplaceItem(item);
        main.ShowSearch();

        Assert.Equal([$"item:item/{item.Id}", "search:-"], (await ScreensAsync(client)).Select(Describe));
    });

    [Fact]
    public Task 右の欄の商品は300ms待って最後の物だけ知らせる() => Run(async (app, main, client, time) =>
    {
        var cards = main.Search.ListItems;
        main.ShowFolders();
        await app.SettleAsync();
        var folder = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        var shown = (await ScreensAsync(client)).Length;

        // 続けて2つ開く：最後の変化から 300ms 経つまでは流さない
        folder.RightItem.Open(cards[0].Item);
        time.Advance(TimeSpan.FromMilliseconds(200));
        folder.RightItem.Open(cards[2].Item);
        time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(shown, (await ScreensAsync(client)).Length);

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal($"folder:item/{cards[2].Item.Id}:embedded", Describe((await ScreensAsync(client))[^1]));

        // 閉じたことも、同じだけ待って知らせる
        folder.RightItem.Close();
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("folder:-", Describe((await ScreensAsync(client))[^1]));
    });

    [Fact]
    public Task 待っている間に画面を移ったら_前の画面の分は流さない() => Run(async (app, main, client, time) =>
    {
        var cards = main.Search.ListItems;
        main.ShowFolders();
        await app.SettleAsync();
        var folder = Assert.IsType<FolderViewModel>(main.CurrentViewModel);

        folder.RightItem.Open(cards[0].Item);
        main.ShowSearch();
        time.Advance(PluginScreenTracker.Delay);

        var screens = (await ScreensAsync(client)).Select(Describe).ToList();
        Assert.Equal("search:-", screens[^1]);
        Assert.DoesNotContain(screens, screen => screen.Contains(cards[0].Item.Id, StringComparison.Ordinal));
    });

    [Fact]
    public Task 今の画面を読むと_画面と選んだ商品が返る() => Run(async (app, main, client, time) =>
    {
        var cards = main.Search.ListItems;
        main.ShowSearch();
        main.Search.SelectByClick(cards[0], control: true, shift: false);

        var answer = JsonDocument.Parse(await client.GetStringAsync("/v1/plugin/current")).RootElement;
        Assert.Equal("search", answer.GetProperty("screen").GetProperty("screen").GetString());
        Assert.Equal([cards[0].Item.Id], answer.GetProperty("selection").GetProperty("itemIds").EnumerateArray().Select(id => id.GetString()));

        main.ShowItem(cards[1].Item);
        var item = JsonDocument.Parse(await client.GetStringAsync("/v1/plugin/current")).RootElement;
        Assert.Equal($"item:item/{cards[1].Item.Id}", Describe(item.GetProperty("screen")));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("selection").ValueKind);
    });

    [Fact]
    public Task 主画面から開ける画面は_どれも名前を持つ() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9600701", "作り物の商品"));
        var main = await app.StartAsync();
        var item = main.Search.ListItems[0].Item;

        var opens = new (string Screen, Func<Task> Open)[]
        {
            ("search", () => { main.ShowSearch(); return Task.CompletedTask; }),
            ("item", () => { main.ShowItem(item); return Task.CompletedTask; }),
            ("edit", () => main.ShowEditAsync([item.Id])),
            ("import", () => { main.ShowImport(); return Task.CompletedTask; }),
            ("resolve", () => { main.ShowResolve(); return Task.CompletedTask; }),
            ("inbox", () => { main.ShowInbox(); return Task.CompletedTask; }),
            ("shops", () => { main.ShowShops(); return Task.CompletedTask; }),
            ("folder", () => { main.ShowFolders(); return Task.CompletedTask; }),
            ("stats", () => { main.ShowStats(); return Task.CompletedTask; }),
            ("avatar", () => { main.ShowAvatars(); return Task.CompletedTask; }),
            ("modification", () => { main.ShowModifications(); return Task.CompletedTask; }),
            ("settings", () => { main.ShowSettings(); return Task.CompletedTask; }),
            ("tagManage", () => { main.ShowTagManage(); return Task.CompletedTask; }),
            ("attributeManage", () => { main.ShowAttributeManage(); return Task.CompletedTask; }),
        };

        foreach (var (screen, open) in opens)
        {
            await open();
            await app.SettleAsync();
            Assert.Equal(screen, PluginScreenTracker.StateOf(main.CurrentViewModel)?.Screen);
        }
    });
}
