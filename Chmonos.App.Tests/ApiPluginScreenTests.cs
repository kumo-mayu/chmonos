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

    private static Task Run(Func<TestApp, MainViewModel, HttpClient, ManualTime, Task> body) => Run(async app =>
    {
        for (var index = 1; index <= 4; index++)
        {
            await app.AddItemAsync(Make.Item($"96006{index:00}", $"作り物の商品{index}"));
        }
    }, body);

    /// <param name="prepare">主画面を作る前に保存先へ置く物。</param>
    private static async Task Run(Func<TestApp, Task> prepare, Func<TestApp, MainViewModel, HttpClient, ManualTime, Task> body) => await TestApp.Run(async app =>
    {
        await prepare(app);
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

    /// <summary>最後に流れた画面（まだ何も流れていなければ null）。</summary>
    private static async Task<string?> LastAsync(HttpClient client)
        => (await ScreensAsync(client)) is { Length: > 0 } screens ? Describe(screens[^1]) : null;

    // 画面の中の変化は、係が画面の知らせ（プロパティの名前）を聞いて拾う。名前が外れると黙って流れなくなるので、道ごとに「流れる」を確かめる。
    // どれも、変えた直後はまだ流れず（画面の差し替えではなく画面の中の変化として拾った印）、300ms 進めると流れる

    [Fact]
    public Task フォルダで行を選ぶと_右に出た商品ページが流れる() => Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9600801", "作り物のドレス").WithFiles(Make.File(app.NewFile(@"lib\a\dress.zip"))));
    }, async (app, main, client, time) =>
    {
        main.ShowFolders();
        var folders = Assert.IsType<FolderViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => folders.EmptyText != "読み込んでいます…", "フォルダビューの読み込みが済む");
        while (folders.Rows.FirstOrDefault(row => row.CanExpand && !row.IsExpanded) is { } closed)
        {
            folders.ToggleCommand.Execute(closed);
        }

        folders.Selected = folders.Rows.First(row => row.Name == "dress.zip");
        await app.SettleAsync();
        Assert.IsType<ItemViewModel>(folders.Detail);
        Assert.Equal("folder:-", await LastAsync(client));

        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("folder:item/9600801:embedded", await LastAsync(client));
    });

    [Fact]
    public Task アバターの管理で選ぶと_選んだアバターが流れる() => Run(async app =>
    {
        await app.Store.Avatars.SaveAsync(new AvatarRegistry
        {
            Entries =
            [
                new AvatarRegistryEntry { ItemId = "9600901", BoothName = "作り物のアバター", AvatarOverride = true },
                new AvatarRegistryEntry { ItemId = "9600902", BoothName = "もう1体のアバター", AvatarOverride = true },
            ],
        });
    }, async (app, main, client, time) =>
    {
        main.ShowAvatars();
        var avatars = Assert.IsType<AvatarsViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => !avatars.IsLoading && avatars.Rows.Count > 0, "アバターの読み込みが済む");
        time.Advance(PluginScreenTracker.Delay);
        var before = await LastAsync(client);

        avatars.Selected = avatars.Rows.Single(row => row.ItemId == "9600902");
        Assert.Equal(before, await LastAsync(client));
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("avatar:avatar/9600902:embedded", await LastAsync(client));
    });

    [Fact]
    public Task 改変の画面で右に出た改変と使った物が流れる() => Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9601001", "作り物のアバター"));
        await app.AddItemAsync(Make.Item("9601002", "作り物の衣装"));
    }, async (app, main, client, time) =>
    {
        var created = Assert.IsType<Chmonos.Core.Commands.CommandResult.ModificationCreated>(
            await app.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.CreateModification("9601001", "作り物の改変")));
        await app.Services.Commands.ExecuteAsync(new Chmonos.Core.Commands.UiCommand.AddModificationMember(
            created.Record.Id, new ModificationMember { ItemId = "9601002" }));

        main.ShowModifications(ModificationHubLevel.Modification, new ModificationHubSelection(ModificationHubSelectionKind.Modification, created.Record.Id));
        var hub = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => hub.Detail is ModificationViewModel, "右に改変が出る");
        Assert.Equal("modification:-", await LastAsync(client));
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal($"modification:modification/{created.Record.Id}:embedded", await LastAsync(client));

        main.ShowModifications(ModificationHubLevel.Modification, new ModificationHubSelection(ModificationHubSelectionKind.Member, created.Record.Id, 0));
        var members = Assert.IsType<ModificationHubViewModel>(main.CurrentViewModel);
        await UiThread.Until(() => members.Detail is HubItemDetail, "右に使った物が出る");
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("modification:item/9601002:embedded", await LastAsync(client));
    });

    [Fact]
    public Task 編集で保存して次へ進むと_次の商品が流れる() => Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9601101", "作り物の衣装"));
        await app.AddItemAsync(Make.Item("9601102", "作り物の髪型"));
        await app.ChangeSettingsAsync(settings => settings with { ReturnToSearchWhenEditDone = false });
    }, async (app, main, client, time) =>
    {
        await main.ShowEditAsync(["9601101", "9601102"]);
        await app.SettleAsync();
        var edit = Assert.IsType<EditViewModel>(main.CurrentViewModel);
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("edit:item/9601101", await LastAsync(client));

        edit.SaveAndNextCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal("9601102", edit.ItemPage?.Item.Id);
        Assert.Equal("edit:item/9601101", await LastAsync(client));
        time.Advance(PluginScreenTracker.Delay);
        Assert.Equal("edit:item/9601102", await LastAsync(client));
    });

    [Fact]
    public Task ショップのページを開くと_ショップの鍵が流れる() => Run(async app =>
    {
        await app.AddItemAsync(Make.Item("9601201", "作り物の衣装", shop: "sample-shop"));
    }, async (app, main, client, time) =>
    {
        main.ShowShop(new Chmonos.Core.Services.ShopSummary { Subdomain = "sample-shop", Name = "作り物のショップ", KnownCount = 1, OwnedCount = 1, SpentYen = 0 });
        await app.SettleAsync();

        Assert.Equal("shop:shop/sample-shop", await LastAsync(client));
    });

    [Fact]
    public void 主画面が作る画面の型は_どれも名前を持つ()
    {
        // 主画面が作るが、主画面の画面（CurrentViewModel）には入れない物（通信の様子・サブ画面・窓・画面が持ち回る控え）
        var notScreens = new HashSet<Type>
        {
            typeof(BoothActivityViewModel), typeof(FilePoolViewModel), typeof(LocalItemNameDialogViewModel), typeof(RegisterPastedItemsDialogViewModel),
            typeof(PaneColumn), typeof(EditDraftStore), typeof(ResolveSearchState),
        };
        var constructed = ConstructedTypes.Of(typeof(MainViewModel)).Where(type => typeof(ViewModelBase).IsAssignableFrom(type)).ToList();

        // 集め方が効いている印：検索（作るのは主画面の作り始め）と商品ページ（開くたび・戻るたび）が入る
        Assert.Contains(typeof(SearchViewModel), constructed);
        Assert.Contains(typeof(ItemViewModel), constructed);
        Assert.Contains(typeof(AvatarsViewModel), constructed);

        var unnamed = constructed.Where(type => !notScreens.Contains(type) && PluginScreenTracker.ScreenNameOf(type) == "other").Select(type => type.Name).ToList();
        Assert.True(unnamed.Count == 0, $"名前の無い画面：{string.Join("・", unnamed)}（PluginScreenTracker の一覧か、この試験の「画面ではない」に足す）");
    }
}
