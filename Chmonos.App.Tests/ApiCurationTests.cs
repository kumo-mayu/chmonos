using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// 外から使う API の対応アバターと改変の書き込み（段3c。ユーザ判断 2026-10-13：B）。アプリの本物の命令の口を通す。
/// 名前・ID・パスは作り物
/// </summary>
public sealed class ApiCurationTests
{
    private const string Outfit = "9600401";
    private const string AvatarA = "9600402";
    private const string AvatarB = "9600403";
    private const string Hat = "9600404";
    private const string Body = "作り物素体";

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>衣装1つ・アバター2体（どちらも確認待ちの推定で衣装に付いている）・アバターでない帽子・共通素体1つ。</summary>
    private static async Task<HttpClient> StartAsync(TestApp app)
    {
        var outfit = Make.Item(Outfit, "作り物の衣装");
        outfit = outfit with
        {
            Local = outfit.Local with
            {
                Avatars =
                [
                    new AvatarLink { AvatarItemId = AvatarA, Name = "アバターA", Source = AvatarLinkSource.H2Link },
                    new AvatarLink { AvatarItemId = AvatarB, Name = "アバターB", Source = AvatarLinkSource.H2Link },
                ],
            },
        };
        await app.AddItemAsync(outfit);
        await app.AddItemAsync(Make.Item(AvatarA, "作り物アバターA"));
        await app.AddItemAsync(Make.Item(AvatarB, "作り物アバターB"));
        await app.AddItemAsync(Make.Item(Hat, "作り物の帽子"));
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = AvatarA, BoothName = "作り物アバターA", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = AvatarB, BoothName = "作り物アバターB", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = Hat, BoothName = "作り物の帽子", AvatarOverride = false },
            ],
            BaseGroups = [new AvatarBaseGroup { Name = Body, IsManual = true }],
        });
        await app.StartAsync();

        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true, Port = port });
        await app.Services.Api.ApplyAsync();
        var token = JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token;
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string body)
        => client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string url, string body)
        => client.PutAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<string> CodeOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    private static async Task<AvatarLink> LinkAsync(TestApp app, string avatarId)
        => (await app.Store.Items.LoadAsync(Outfit))!.Local.Avatars.Single(link => link.AvatarItemId == avatarId);

    private static async Task Run(Func<TestApp, HttpClient, Task> body) => await TestApp.Run(async app =>
    {
        var client = await StartAsync(app);
        try
        {
            await body(app, client);
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 確認待ちは名指しした物だけ確認済みになる() => Run(async (app, client) =>
    {
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/items/{Outfit}/avatars/confirm", $$"""{"avatarIds":["{{AvatarA}}"]}""")).StatusCode);

        Assert.True((await LinkAsync(app, AvatarA)).Confirmed);
        Assert.False((await LinkAsync(app, AvatarB)).Confirmed);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, $"/v1/items/{Outfit}/avatars/confirm", """{"avatarIds":[]}""")).StatusCode);
    });

    [Fact]
    public Task 消すと消した印が残り_足し直すと確認済みで戻る() => Run(async (app, client) =>
    {
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/items/{Outfit}/avatars/remove", $$"""{"avatarId":"{{AvatarB}}"}""")).StatusCode);
        var removed = await LinkAsync(app, AvatarB);
        Assert.Equal((true, AvatarLinkSource.Manual), (removed.Rejected, removed.Source));

        // 消した物をもう一度消すことはできない（付いていない）
        Assert.Equal("avatarNotLinked", await CodeOf(await PostAsync(client, $"/v1/items/{Outfit}/avatars/remove", $$"""{"avatarId":"{{AvatarB}}"}""")));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/items/{Outfit}/avatars", $$"""{"avatarId":"{{AvatarB}}"}""")).StatusCode);
        var restored = await LinkAsync(app, AvatarB);
        Assert.Equal((false, true), (restored.Rejected, restored.Confirmed));
        Assert.Equal(2, (await app.Store.Items.LoadAsync(Outfit))!.Local.Avatars.Count);
    });

    [Fact]
    public Task アバターでない商品と自分自身は対応アバターにできない() => Run(async (app, client) =>
    {
        Assert.Equal("unknownAvatar", await CodeOf(await PostAsync(client, $"/v1/items/{Outfit}/avatars", $$"""{"avatarId":"{{Hat}}"}""")));
        Assert.Equal("selfLink", await CodeOf(await PostAsync(client, $"/v1/items/{AvatarA}/avatars", $$"""{"avatarId":"{{AvatarA}}"}""")));
    });

    [Fact]
    public Task 共通素体は一覧の綴りで足し_消すと印が残る() => Run(async (app, client) =>
    {
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/items/{Outfit}/avatar-bases", """{"baseName":" 作り物素体 "}""")).StatusCode);
        var added = Assert.Single((await app.Store.Items.LoadAsync(Outfit))!.Local.AvatarBases);
        Assert.Equal((Body, AvatarLinkSource.Manual, true), (added.BaseName, added.Source, added.Confirmed));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/items/{Outfit}/avatar-bases/remove", $$"""{"baseName":"{{Body}}"}""")).StatusCode);
        Assert.True(Assert.Single((await app.Store.Items.LoadAsync(Outfit))!.Local.AvatarBases).Rejected);

        Assert.Equal("unknownBase", await CodeOf(await PostAsync(client, $"/v1/items/{Outfit}/avatar-bases", """{"baseName":"無い素体"}""")));
    });

    [Fact]
    public Task 改変を作り_メモとblueprintと使った物を書き_外して戻す() => Run(async (app, client) =>
    {
        var created = await PostAsync(client, "/v1/modifications", $$"""{"avatarId":"{{AvatarA}}","name":"普段着"}""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        Assert.Equal($"/v1/modifications/{id}", created.Headers.Location?.OriginalString);

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(client, "/v1/modifications", $$"""{"avatarId":"{{AvatarA}}","name":"普段着"}""")).StatusCode);
        Assert.Equal("unknownAvatar", await CodeOf(await PostAsync(client, "/v1/modifications", $$"""{"avatarId":"{{Hat}}","name":"帽子の改変"}""")));
        Assert.DoesNotContain(app.Store.Avatars.Load().Entries, entry => entry.ItemId == Hat && entry.AvatarOverride == true);

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, $"/v1/modifications/{id}/memo", """{"memo":"夏用"}""")).StatusCode);
        Assert.Equal("invalidBlueprintId", await CodeOf(await PutAsync(client, $"/v1/modifications/{id}/blueprint-id", """{"blueprintId":"avtr_xyz"}""")));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, $"/v1/modifications/{id}/blueprint-id",
            """{"blueprintId":"avtr_01234567-89ab-cdef-0123-456789abcdef"}""")).StatusCode);

        Assert.Equal("unknownFile", await CodeOf(await PostAsync(client, $"/v1/modifications/{id}/members", $$"""{"itemId":"{{Outfit}}","fileHash":"00ff"}""")));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/modifications/{id}/members", $$"""{"itemId":"{{Outfit}}"}""")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/modifications/{id}/members", $$"""{"itemId":"{{Outfit}}","package":"Outfit.unitypackage"}""")).StatusCode);

        // 同じ商品が2行あるので、絞らずに外そうとすると断る
        Assert.Equal("memberAmbiguous", await CodeOf(await PostAsync(client, $"/v1/modifications/{id}/members/detach", $$"""{"itemId":"{{Outfit}}"}""")));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/modifications/{id}/members/detach",
            $$"""{"itemId":"{{Outfit}}","package":"Outfit.unitypackage"}""")).StatusCode);

        var record = (await app.Store.Modifications.LoadAsync(id))!;
        Assert.Equal(("夏用", "avtr_01234567-89ab-cdef-0123-456789abcdef"), (record.Memo, record.BlueprintId));
        Assert.Equal([false, true], record.Members.Select(member => member.Detached));

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/v1/modifications/{id}/members/restore", $$"""{"itemId":"{{Outfit}}"}""")).StatusCode);
        Assert.All((await app.Store.Modifications.LoadAsync(id))!.Members, member => Assert.False(member.Detached));
    });

    [Fact]
    public Task Unityプロジェクトは_Assetsのあるフォルダだけ結べる() => Run(async (app, client) =>
    {
        var created = await PostAsync(client, "/v1/modifications", $$"""{"avatarId":"{{AvatarB}}","name":"制服"}""");
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var project = Path.GetDirectoryName(app.NewFile(Path.Combine("Project", "Assets", "a.txt")))!;
        project = Path.GetDirectoryName(project)!;

        Assert.Equal("notUnityProject", await CodeOf(await PutAsync(client, $"/v1/modifications/{id}/unity-project",
            JsonSerializer.Serialize(new { path = Path.GetDirectoryName(project) }))));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, $"/v1/modifications/{id}/unity-project", JsonSerializer.Serialize(new { path = project }))).StatusCode);
        Assert.Equal(project, (await app.Store.Modifications.LoadAsync(id))!.UnityProject);

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(client, $"/v1/modifications/{id}/unity-project", """{"path":null}""")).StatusCode);
        Assert.Null((await app.Store.Modifications.LoadAsync(id))!.UnityProject);
    });
}
