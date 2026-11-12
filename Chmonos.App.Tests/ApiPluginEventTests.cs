using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 画面の操作からソフトへの出来事（plan 44 の①・docs/spec/plugins.md）：商品ページを開いた・選択の変化（まとめてから）・取り込みの終わり。
/// 本物の API を同じプロセスで動かし、ソフトのトークンで取りに行く。購読していなければ何も積まない。名前・ID は作り物
/// </summary>
public sealed class ApiPluginEventTests
{
    private const string Token = "plugin-watcher-token-0123456789";

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>作り物の商品を並べ、見張りを全部許したソフトを控えに書いて、API を始める。</summary>
    private static async Task<(MainViewModel Main, HttpClient Client)> StartAsync(TestApp app, int count = 4)
    {
        for (var index = 1; index <= count; index++)
        {
            await app.AddItemAsync(Make.Item($"96005{index:00}", $"作り物の商品{index}"));
        }

        JsonStore.Write(app.Services.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                new ApiClientRecord
                {
                    Id = "watcher", Name = "作り物の見張り", Scopes = ApiScopes.Watches,
                    TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token))).ToLowerInvariant(),
                },
            ],
        });
        var main = await app.StartAsync();
        await app.SettleAsync();

        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, Port = port });
        await app.Services.Api.ApplyAsync();
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return (main, client);
    }

    private static async Task Run(Func<TestApp, MainViewModel, HttpClient, Task> body) => await TestApp.Run(async app =>
    {
        var (main, client) = await StartAsync(app);
        try
        {
            await body(app, main, client);
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    private static async Task SubscribeAsync(HttpClient client, params string[] kinds)
    {
        var response = await client.PutAsync(
            "/v1/plugin/subscriptions", new StringContent(JsonSerializer.Serialize(new { kinds }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement[]> EventsAsync(HttpClient client, long after = 0)
    {
        var answer = JsonDocument.Parse(await client.GetStringAsync($"/v1/plugin/events?after={after}")).RootElement;
        return [.. answer.GetProperty("events").EnumerateArray()];
    }

    [Fact]
    public Task 購読していなければ_選択の印も立てず何も積まない() => Run(async (app, main, client) =>
    {
        var cards = main.Search.ListItems;
        main.Search.SelectByClick(cards[0], control: true, shift: false);
        main.ShowItem(cards[1].Item);

        Assert.False(app.Services.PluginSelection.IsPending);
        await SubscribeAsync(client, "itemOpened", "selectionChanged");
        Assert.Empty(await EventsAsync(client));
    });

    [Fact]
    public Task 選択の変化はまとめて1つにし_同じ選択は流し直さない() => Run(async (app, main, client) =>
    {
        await SubscribeAsync(client, "selectionChanged");
        var search = main.Search;
        var cards = search.ListItems;

        // 続けて選んだ2回は、時間が切れたときに1つで流す（試験は時計を待たずに切る）
        search.SelectByClick(cards[0], control: true, shift: false);
        search.SelectByClick(cards[2], control: true, shift: false);
        Assert.True(app.Services.PluginSelection.IsPending);
        Assert.Empty(await EventsAsync(client));

        app.Services.PluginSelection.Flush();
        var events = await EventsAsync(client);
        var selection = Assert.Single(events).GetProperty("selectionChanged");
        Assert.Equal("search", selection.GetProperty("screen").GetString());
        Assert.Equal([cards[0].Item.Id, cards[2].Item.Id], selection.GetProperty("itemIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal(2, selection.GetProperty("count").GetInt32());
        Assert.False(selection.GetProperty("truncated").GetBoolean());

        // 外して選び直し、同じ選択に戻った：流し直さない
        search.SelectByClick(cards[2], control: true, shift: false);
        search.SelectByClick(cards[2], control: true, shift: false);
        app.Services.PluginSelection.Flush();
        Assert.Single(await EventsAsync(client));

        // 全部外したら、空の選択を流す
        search.ClearSelection();
        app.Services.PluginSelection.Flush();
        var cleared = (await EventsAsync(client, after: 1)).Single().GetProperty("selectionChanged");
        Assert.Equal(0, cleared.GetProperty("count").GetInt32());
    });

    [Fact]
    public Task 商品ページを開いたら知らせ_開き直しでは知らせない() => Run(async (app, main, client) =>
    {
        await SubscribeAsync(client, "itemOpened");
        var item = main.Search.ListItems[1].Item;

        main.ShowItem(item);
        main.ReplaceItem(item);

        var opened = Assert.Single(await EventsAsync(client));
        Assert.Equal("itemOpened", opened.GetProperty("kind").GetString());
        Assert.Equal(item.Id, opened.GetProperty("itemOpened").GetProperty("itemId").GetString());
    });

    [Fact]
    public Task 取り込みが終わったら_結果の種類と件数だけを知らせる() => Run(async (app, main, client) =>
    {
        await SubscribeAsync(client, "importFinished");
        var file = app.NewFile("作り物の帽子.unitypackage");

        await app.Services.Imports.StartOrStackAsync(new Services.ImportRequest(() => [file], [file]));
        await app.SettleAsync();

        var finished = Assert.Single(await EventsAsync(client)).GetProperty("importFinished");
        Assert.Equal("imported", finished.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Number, finished.GetProperty("unresolvedFiles").ValueKind);

        // 載せるのは件数だけ（パス・名前は載せない）
        Assert.DoesNotContain("作り物の帽子", finished.GetRawText());
    });
}
