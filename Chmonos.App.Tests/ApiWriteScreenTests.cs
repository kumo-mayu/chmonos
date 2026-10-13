using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 外から使う API の書き込みを、アプリの命令の口で通し、画面へ映す（docs/history/headless-core-2026-10-13.md 段3）。
/// 本物の命令の口（CommandHandler）を通る。名前・ID は作り物
/// </summary>
public sealed class ApiWriteScreenTests
{
    private const string ItemId = "9600101";

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(HttpClient Client, int Port)> StartApiAsync(TestApp app)
    {
        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true, Port = port });
        await app.Services.Api.ApplyAsync();
        Assert.Equal(Services.ApiRunState.Running, app.Services.Api.State);

        var token = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token;
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, port);
    }

    private static Task<HttpResponseMessage> PutFavoriteAsync(HttpClient client)
        => client.PutAsync($"/v1/items/{ItemId}/favorite", new StringContent("""{"value":true}""", Encoding.UTF8, "application/json"));

    [Fact]
    public Task APIで付けたお気に入りは_アプリの命令で書かれ_検索の一覧に映る() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の帽子"));
        var main = await app.StartAsync();
        var (client, _) = await StartApiAsync(app);
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await PutFavoriteAsync(client)).StatusCode);

            Assert.True((await app.Store.Items.LoadAsync(ItemId))!.Local.IsFavorite);
            await UiThread.Until(
                () => main.Search.SnapshotItems().Single(item => item.Id == ItemId).Local.IsFavorite,
                "検索の写しにお気に入りが映る");
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 編集画面で開いている商品には_APIから書けない() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の帽子"));
        var main = await app.StartAsync();
        await main.ShowEditAsync([ItemId]);
        var (client, _) = await StartApiAsync(app);
        try
        {
            Assert.Equal(HttpStatusCode.Conflict, (await PutFavoriteAsync(client)).StatusCode);
            Assert.False((await app.Store.Items.LoadAsync(ItemId))!.Local.IsFavorite);
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });
}
