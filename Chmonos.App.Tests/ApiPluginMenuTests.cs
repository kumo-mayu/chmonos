using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Controls;
using Chmonos.Api;
using Chmonos.App.Tests.Support;
using Chmonos.App.ViewModels;
using Chmonos.App.Views;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.App.Tests;

/// <summary>
/// ほかのソフトの項目を画面に出す（plan 44 の②・docs/spec/plugins-menu.md）：メニューの組み立て（押せない理由・「ほかのソフト」を出さない時・
/// 区切りの出し入れ・「_」）、押したら列に積んで帯で「送りました」、結果で帯の文を差し替える（古い結果では上書きしない）、
/// 複数選択の帯のボタンは1つ、許し直しの窓の行と拒否の数え方。本物の API を同じプロセスで動かす。名前・ID は作り物
/// </summary>
public sealed class ApiPluginMenuTests
{
    private const string Token = "plugin-menu-token-0123456789";

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>作り物の商品を並べ、項目を宣言済みのソフトを控えに書いて、API を始める。</summary>
    private static async Task<(MainViewModel Main, HttpClient Client)> StartAsync(TestApp app, string name = "作り物_道具", IApiClientApprovals? approvals = null)
    {
        for (var index = 1; index <= 3; index++)
        {
            // 1件目だけファイルを持たない（条件「持っている物だけ」に合わない商品）
            var item = Make.Item($"96007{index:00}", $"作り物の商品{index}");
            await app.AddItemAsync(index == 1 ? item.WithFiles() : item);
        }

        // 許しの口は API を作る前に差し込む（アプリを始めると API の係が作られる）
        app.Services.ApprovalsForTest = approvals;

        JsonStore.Write(app.Services.Paths.ApiClientsFile, new ApiClientList
        {
            Clients =
            [
                new ApiClientRecord
                {
                    Id = "tool", Name = name,
                    TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token))).ToLowerInvariant(),
                    Menu =
                    [
                        new PluginMenuItemRecord { Id = "send", Label = "送る_1", Where = ["item", "selection"], Needs = ["name"] },
                        new PluginMenuItemRecord { Id = "owned-only", Label = "持っている物だけ", Where = ["item"], When = new PluginMenuWhen { Owned = true } },
                    ],
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

    private static async Task Run(Func<TestApp, MainViewModel, HttpClient, Task> body, string name = "作り物_道具", IApiClientApprovals? approvals = null) => await TestApp.Run(async app =>
    {
        var (main, client) = await StartAsync(app, name, approvals);
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

    private static Task PollAsync(HttpClient client) => client.GetStringAsync("/v1/plugin/events?after=0&wait=0");

    private static MenuItem Child(MenuItem parent, int index) => (MenuItem)parent.Items[index]!;

    [Fact]
    public Task 応答していないソフトは灰色で理由を言い_取りに来たら押せる() => Run(async (app, main, client) =>
    {
        var menus = app.Services.PluginMenus;
        var itemId = main.Search.ListItems[0].Item.Id;

        // 出すかの見直しは、API が始まった知らせを画面のスレッドへ渡してから
        await app.SettleAsync();
        Assert.True(menus.HasItemMenus);
        Assert.True(menus.HasSelectionMenus);

        var root = PluginMenuItems.Root(menus, menus.Build(PluginMenus.WhereItem, [itemId]), PluginMenus.WhereItem, [itemId])!;
        Assert.Equal(PluginMenuItems.RootHeader, root.Header);
        Assert.False(root.IsEnabled);
        Assert.Equal("作り物_道具が応答していません。", root.ToolTip);

        await PollAsync(client);
        root = PluginMenuItems.Root(menus, menus.Build(PluginMenus.WhereItem, [itemId]), PluginMenus.WhereItem, [itemId])!;
        Assert.True(root.IsEnabled);
        var software = Child(root, 0);

        // ソフトが付けた「_」はアクセスキーにしない（二つ重ねて、そのまま見せる）
        Assert.Equal("作り物__道具", software.Header);
        Assert.Equal("送る__1", Child(software, 0).Header);
        Assert.True(Child(software, 0).IsEnabled);

        // ファイルを持っていない作り物の商品なので、条件に合わず灰色
        Assert.False(Child(software, 1).IsEnabled);
        Assert.Equal("ファイルを持っている商品で使えます。", Child(software, 1).ToolTip);
        Assert.Equal("PluginMenu.Item", AutomationProperties.GetAutomationId(Child(software, 1)));
    });

    [Fact]
    public Task カードの右クリックには区切りと一緒に足し_開き直すと作り直す() => Run(async (app, main, client) =>
    {
        await PollAsync(client);
        var menus = app.Services.PluginMenus;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "非表示にする" });

        PluginMenuItems.AttachTo(menu, menus, "9600701");
        PluginMenuItems.AttachTo(menu, menus, "9600701");
        Assert.Equal(3, menu.Items.Count);
        Assert.IsType<Separator>(menu.Items[1]);
        Assert.Equal(PluginMenuItems.RootHeader, ((MenuItem)menu.Items[2]!).Header);

        // ソフトが無い（係が無い・商品が無い）ときは、区切りごと外す
        PluginMenuItems.AttachTo(menu, null, "9600701");
        Assert.Single(menu.Items);
    });

    [Fact]
    public Task 押したら列に積み帯で送ったと言い_結果で文を差し替え_古い結果では上書きしない() => Run(async (app, main, client) =>
    {
        await PollAsync(client);
        var menus = app.Services.PluginMenus;
        var ids = new[] { main.Search.ListItems[0].Item.Id };
        var software = Assert.Single(menus.Build(PluginMenus.WhereItem, ids));

        await menus.PressAsync(software, software.Items[0], PluginMenus.WhereItem, ids);
        Assert.True(menus.HasNotice);
        Assert.Equal("作り物_道具に送りました。", menus.NoticeText);
        Assert.False(menus.NoticeFailed);

        var answer = JsonDocument.Parse(await client.GetStringAsync("/v1/plugin/events?after=0&wait=0")).RootElement;
        var first = answer.GetProperty("events")[0];
        Assert.Equal("menu", first.GetProperty("kind").GetString());
        Assert.Equal("作り物の商品1", first.GetProperty("menu").GetProperty("items")[0].GetProperty("name").GetString());

        // 2回目を押した後に、1回目の結果が来ても帯は変えない
        await menus.PressAsync(software, software.Items[0], PluginMenus.WhereItem, ids);
        var firstSeq = first.GetProperty("seq").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await PostResultAsync(client, firstSeq, true, "古い結果")).StatusCode);
        await app.SettleAsync();
        Assert.Equal("作り物_道具に送りました。", menus.NoticeText);

        Assert.Equal(HttpStatusCode.OK, (await PostResultAsync(client, firstSeq + 1, false, "開けませんでした")).StatusCode);
        await app.SettleAsync();
        Assert.Equal("作り物_道具：開けませんでした", menus.NoticeText);
        Assert.True(menus.NoticeFailed);

        menus.DismissNoticeCommand.Execute(null);
        Assert.False(menus.HasNotice);
    });

    private static Task<HttpResponseMessage> PostResultAsync(HttpClient client, long seq, bool ok, string message)
        => client.PostAsync($"/v1/plugin/events/{seq}/result", new StringContent(JsonSerializer.Serialize(new { ok, message }), Encoding.UTF8, "application/json"));

    [Fact]
    public Task 応答していないときに押すと送らずに帯で理由を言う() => Run(async (app, main, client) =>
    {
        var menus = app.Services.PluginMenus;
        var ids = new[] { main.Search.ListItems[0].Item.Id };
        var software = Assert.Single(menus.Build(PluginMenus.WhereItem, ids));

        await menus.PressAsync(software, software.Items[0], PluginMenus.WhereItem, ids);
        Assert.Equal("作り物_道具が応答していません。", menus.NoticeText);
        Assert.True(menus.NoticeFailed);
        Assert.Empty(JsonDocument.Parse(await client.GetStringAsync("/v1/plugin/events?after=0&wait=0")).RootElement.GetProperty("events").EnumerateArray());
    });

    [Fact]
    public Task 取り消したら_ほかのソフト自体を出さない() => Run(async (app, main, client) =>
    {
        var menus = app.Services.PluginMenus;
        await app.Services.Commands.ExecuteAsync(new Core.Commands.UiCommand.ChangeApiClients(list => list with { Clients = [] }));
        app.Services.Api.ReloadClients();
        await app.SettleAsync();

        Assert.False(menus.HasItemMenus);
        Assert.False(menus.HasSelectionMenus);
        Assert.Null(PluginMenuItems.Root(menus, menus.Build(PluginMenus.WhereItem, ["9600701"]), PluginMenus.WhereItem, ["9600701"]));
    });

    [Fact]
    public Task 複数選択の帯は_選んだ順の商品を渡し_ボタンは1つに畳む() => Run(async (app, main, client) =>
    {
        var search = main.Search;
        var cards = search.ListItems;
        search.SelectByClick(cards[2], control: true, shift: false);
        search.SelectByClick(cards[0], control: true, shift: false);

        Assert.Equal([cards[0].Item.Id, cards[2].Item.Id], search.SelectedItemIdsForPlugins());
        Assert.Same(app.Services.PluginMenus, search.PluginMenus);

        // 帯に置く「ほかのソフト」のボタンは、項目がいくつあっても1つ（帯を長くしない）
        var template = (System.Windows.DataTemplate)new ItemCardResources()["ItemSelectionBar"];
        var bar = (System.Windows.FrameworkElement)template.LoadContent();
        var buttons = Descendants(bar).OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == "SelectionBar.Plugins").ToList();
        Assert.Single(buttons);
        await Task.CompletedTask;
    });

    /// <summary>項目の宣言の許しを、試験が答えるまで待たせる口（「聞いている間」を作る）。</summary>
    private sealed class HeldApprovals : IApiClientApprovals
    {
        private readonly System.Threading.Channels.Channel<TaskCompletionSource<ApiClientDecision>> _asked
            = System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource<ApiClientDecision>>();

        public bool CanAsk => true;

        public Task<ApiClientDecision> AskAsync(ApiClientRequestInfo request, CancellationToken cancellationToken)
            => Task.FromResult(ApiClientDecision.Dismissed);

        public async Task<ApiClientDecision> AskMenuAsync(PluginMenuRequestInfo request, CancellationToken cancellationToken)
        {
            var decision = new TaskCompletionSource<ApiClientDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            await _asked.Writer.WriteAsync(decision, cancellationToken);
            return await decision.Task.WaitAsync(cancellationToken);
        }

        public async Task<TaskCompletionSource<ApiClientDecision>> NextAsync()
            => await _asked.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static Task<HttpResponseMessage> PutMenuAsync(HttpClient client, object items)
        => client.PutAsync("/v1/plugin/menu", new StringContent(JsonSerializer.Serialize(new { items }), Encoding.UTF8, "application/json"));

    private static async Task UntilAsync(TestApp app, Func<bool> done)
    {
        for (var tries = 0; tries < 100 && !done(); tries++)
        {
            await Task.Delay(50);
            await app.SettleAsync();
        }

        Assert.True(done());
    }

    [Fact]
    public Task 設定のつないでいるソフトは_聞き直しを待つ間そう言い_許せば新しい項目を並べ_拒否なら前のまま() => TestApp.Run(async app =>
    {
        var approvals = new HeldApprovals();
        var (main, client) = await StartAsync(app, approvals: approvals);
        try
        {
            var settings = new SettingsViewModel(app.Services, main);
            Assert.Equal(2, settings.ApiClients.Single().Menu.Lines.Count);
            Assert.False(settings.ApiClients.Single().Menu.IsPending);

            // 1つ足す宣言：聞いている間は「待っています」と、前の項目のまま並べる
            var added = new object[]
            {
                new { id = "send", label = "送る_1", where = new[] { "item", "selection" }, needs = new[] { "name" } },
                new { id = "owned-only", label = "持っている物だけ", where = new[] { "item" }, when = new { owned = true } },
                new { id = "new", label = "作り物の新しい項目", where = new[] { "item" } },
            };
            Assert.Equal(HttpStatusCode.Accepted, (await PutMenuAsync(client, added)).StatusCode);
            var asked = await approvals.NextAsync();
            await UntilAsync(app, () => settings.ApiClients.Single().Menu.IsPending);
            Assert.Equal("項目の変更の許可を待っています。許可するまでは今の項目のままです。", settings.ApiClients.Single().Menu.PendingText);
            Assert.Equal(2, settings.ApiClients.Single().Menu.Lines.Count);

            asked.SetResult(new ApiClientDecision(true, []));
            await UntilAsync(app, () => !settings.ApiClients.Single().Menu.IsPending);
            await UntilAsync(app, () => settings.ApiClients.Single().Menu.Lines.Count == 3);

            // 拒否（控えを書かない終わり方）でも「待っています」は消え、許した項目のまま
            Assert.Equal(HttpStatusCode.Accepted, (await PutMenuAsync(client, added.Take(1).Append(new { id = "other", label = "作り物の別の項目", where = new[] { "item" } }).ToArray())).StatusCode);
            asked = await approvals.NextAsync();
            await UntilAsync(app, () => settings.ApiClients.Single().Menu.IsPending);
            asked.SetResult(ApiClientDecision.Refusal);
            await UntilAsync(app, () => !settings.ApiClients.Single().Menu.IsPending);
            Assert.Equal(3, settings.ApiClients.Single().Menu.Lines.Count);
            settings.OnLeaving();
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 設定でメニューから外すと_トークンは使え_項目は出さず_同じ宣言を送れば聞き直す() => TestApp.Run(async app =>
    {
        var approvals = new HeldApprovals();
        var (main, client) = await StartAsync(app, approvals: approvals);
        try
        {
            await PollAsync(client);
            var settings = new SettingsViewModel(app.Services, main);
            settings.ApiClients.Single().Menu.RemoveCommand.Execute(null);
            await UntilAsync(app, () => !app.Services.PluginMenus.HasItemMenus);

            Assert.False(app.Services.PluginMenus.HasSelectionMenus);
            Assert.False(settings.ApiClients.Single().Menu.IsVisible);

            // トークンは残る（読み取りも出来事も今までどおり）。宣言は空
            var menu = JsonDocument.Parse(await client.GetStringAsync("/v1/plugin/menu")).RootElement;
            Assert.Equal(0, menu.GetProperty("items").GetArrayLength());

            // ソフトが同じ宣言を送り直すと、もう一度聞く（最初の宣言と同じ窓）
            var again = new object[] { new { id = "send", label = "送る_1", where = new[] { "item", "selection" }, needs = new[] { "name" } } };
            Assert.Equal(HttpStatusCode.Accepted, (await PutMenuAsync(client, again)).StatusCode);
            var asked = await approvals.NextAsync();
            await UntilAsync(app, () => settings.ApiClients.Single().Menu.IsPending);
            Assert.Equal("メニューの項目の許可を待っています。", settings.ApiClients.Single().Menu.PendingText);
            asked.SetResult(ApiClientDecision.Dismissed);
            settings.OnLeaving();
        }
        finally
        {
            client.Dispose();
            await app.Services.Api.StopAsync();
        }
    });

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    [Fact]
    public void 許し直しの窓は_足す変わる消えるを並べ_渡す情報と場所を言い_マウスの拒否だけ数える()
    {
        var change = new PluginMenuChange(
            [new PluginMenuLine("preview", "プレビュー", null, ["item", "selection"], ["files"], null)],
            [new PluginMenuLine("csv", "CSV", "書き出す", ["selection"], [], null)],
            [new PluginMenuLine("old", "古い項目", null, ["item"], ["name"], null)]);
        var model = new PluginMenuApprovalViewModel(new PluginMenuRequestInfo("作り物の道具", null, null, change, IsFirst: false, RefusalsLeft: 1));

        Assert.Equal("「作り物の道具」がメニューの項目を変えようとしています", model.Heading);
        Assert.Equal("確かめられませんでした", model.ExePathText);
        Assert.Equal("表示する場所：商品の右クリック・複数選択の帯　渡す情報：商品ID・ファイルの場所", model.Added[0].Detail);
        Assert.Equal("書き出す ▸ CSV", model.Changed[0].Label);
        Assert.Equal("表示する場所：商品の右クリック", model.Removed[0].Detail);
        Assert.True(model.LastRefusal);
        Assert.True(model.IsChange);

        // Enter で押した拒否は数えない。マウスで押した拒否だけ数える
        model.NotePointerDown();
        model.NoteKey();
        model.Deny();
        Assert.Same(ApiClientDecision.Dismissed, model.Decide(false));
        model.NotePointerDown();
        model.Deny();
        Assert.Same(ApiClientDecision.Refusal, model.Decide(false));
        Assert.True(model.Decide(true).Approved);

        var first = new PluginMenuApprovalViewModel(new PluginMenuRequestInfo("作り物の道具", @"C:\作り物\Tool.exe", null, change, IsFirst: true));
        Assert.Equal("「作り物の道具」がメニューに項目を追加しようとしています", first.Heading);
        Assert.False(first.IsChange);
        Assert.False(first.LastRefusal);
    }
}
