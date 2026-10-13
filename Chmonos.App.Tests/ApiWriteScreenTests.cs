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

            // 本物の経路で「API から来た命令」として渡っている（「人が押した」優先度を掛けない起点）。ログに1行、中身（商品 ID）は書かない
            var noted = System.IO.File.ReadAllLines(app.Services.Paths.LogFile).Where(line => line.Contains("外から使う API", StringComparison.Ordinal)).ToList();
            Assert.Contains(noted, line => line.Contains("SaveItemLocal → ItemSaved", StringComparison.Ordinal));
            Assert.DoesNotContain(noted, line => line.Contains(ItemId, StringComparison.Ordinal));

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

    [Fact]
    public Task 非表示_使ったの足跡_通知の既読と解消を_アプリの命令で書く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の帽子"));
        foreach (var id in new[] { "note-a", "note-b" })
        {
            await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddNotification(new NotificationRecord
            {
                Id = id,
                Kind = NotificationKind.UnpackedFilesImported,
                Title = "作り物の知らせ",
                Detail = "作り物の本文",
                CreatedAt = DateTimeOffset.Now,
            }));
        }

        await app.StartAsync();
        var (client, _) = await StartApiAsync(app);
        try
        {
            StringContent Body(string text) => new(text, Encoding.UTF8, "application/json");

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/v1/items/{ItemId}/hidden", Body("""{"value":true}"""))).StatusCode);
            Assert.True((await app.Store.Items.LoadAsync(ItemId))!.Local.IsHidden);

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/v1/items/{ItemId}/recent", Body("""{"kind":"used"}"""))).StatusCode);
            Assert.NotNull(app.Store.Recent.Load().Entries.Single(entry => entry.ItemId == ItemId).UsedAt);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/v1/items/{ItemId}/recent", Body("""{"kind":"viewed"}"""))).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync("/v1/notifications/note-a/read", Body("""{"value":true}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/v1/notifications/resolve", Body("""{"ids":["note-b"]}"""))).StatusCode);
            var notes = app.Store.Notifications.Load();
            Assert.True(notes.Single(note => note.Id == "note-a").IsRead);
            Assert.True(notes.Single(note => note.Id == "note-b").IsResolved);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync("/v1/notifications/missing/read", Body("""{"value":true}"""))).StatusCode);
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task タグと属性とメモを_一覧の綴りに寄せて_アプリの命令で書く() => TestApp.Run(async app =>
    {
        await app.AddItemAsync(Make.Item(ItemId, "作り物の帽子"));
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddUserTag("Outfit", "Summer"));
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.AddAttribute("Cute"));
        await app.StartAsync();
        var (client, _) = await StartApiAsync(app);
        try
        {
            StringContent Body(string text) => new(text, Encoding.UTF8, "application/json");
            async Task<LocalBlock> LocalAsync() => (await app.Store.Items.LoadAsync(ItemId))!.Local;
            async Task<string?> CodeOf(HttpResponseMessage response)
                => System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

            // 一覧に無い名前は断り、何も書かない
            var unknown = await client.PutAsync($"/v1/items/{ItemId}/user-tags", Body("""{"tags":[{"top":"Hat"}]}"""));
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            Assert.Equal("unknownUserTag", await CodeOf(unknown));
            Assert.Empty((await LocalAsync()).UserTags);

            // 綴りは一覧に寄せる。createMissing なら一覧に足して付ける
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(
                $"/v1/items/{ItemId}/user-tags?createMissing=true", Body("""{"tags":[{"top":"outfit","subs":["summer"]},{"top":"Hat"}]}"""))).StatusCode);
            Assert.Equal(["Outfit:Summer", "Hat:"], (await LocalAsync()).UserTags.Select(tag => $"{tag.Top}:{string.Join(",", tag.Subs)}"));
            Assert.Contains(app.Store.UserTags.Load().Tops, top => top.Name == "Hat");

            // 1つ足す・外す（錠の中で今の値に当てる）
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/v1/items/{ItemId}/user-tags?createMissing=true", Body("""{"top":"Outfit","sub":"Winter"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/v1/items/{ItemId}/user-tags/remove", Body("""{"top":"Outfit","sub":"Summer"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/v1/items/{ItemId}/user-tags/remove", Body("""{"top":"Hat"}"""))).StatusCode);
            Assert.Equal(["Outfit:Winter"], (await LocalAsync()).UserTags.Select(tag => $"{tag.Top}:{string.Join(",", tag.Subs)}"));

            // 属性は名指しした物だけ。0 と未評価（キーが無い）は別。null で外す
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/v1/items/{ItemId}/attributes?createMissing=true", Body("""{"values":{"cute":80,"Sexy":0}}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync($"/v1/items/{ItemId}/attributes", Body("""{"values":{"Sexy":null}}"""))).StatusCode);
            var attributes = (await LocalAsync()).Attributes;
            Assert.Equal(80, attributes["Cute"]);
            Assert.False(attributes.ContainsKey("Sexy"));
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsync($"/v1/items/{ItemId}/attributes", Body("""{"values":{"Cute":101}}"""))).StatusCode);
            Assert.Equal("unknownAttribute", await CodeOf(await client.PatchAsync($"/v1/items/{ItemId}/attributes", Body("""{"values":{"Cool":10}}"""))));

            // メモ（空で消す）。ほかの欄は残る
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/v1/items/{ItemId}/memo", Body("""{"memo":"作り物のメモ"}"""))).StatusCode);
            Assert.Equal("作り物のメモ", (await LocalAsync()).Memo);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync($"/v1/items/{ItemId}/memo", Body("""{"memo":""}"""))).StatusCode);
            Assert.Null((await LocalAsync()).Memo);
            Assert.Equal(["Outfit:Winter"], (await LocalAsync()).UserTags.Select(tag => $"{tag.Top}:{string.Join(",", tag.Subs)}"));

            // 一覧に足す（既にあれば足さない）
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/user-tags", Body("""{"top":"Shoes"}"""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/v1/attributes", Body("""{"name":"Cool"}"""))).StatusCode);
            Assert.Contains(app.Store.UserTags.Load().Tops, top => top.Name == "Shoes");
            Assert.Contains(app.Store.Attributes.Load().Attributes, attribute => attribute.Name == "Cool");
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });
}
