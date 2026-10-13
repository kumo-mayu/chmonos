using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Chmonos.App.Services;
using Chmonos.App.Tests.Support;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Core.Storage;
using Chmonos.Query;

namespace Chmonos.App.Tests;

/// <summary>
/// 外から使う API の「Unity へ送る」（docs/history/headless-core-2026-10-13.md 段3）。Unity は動かさず、エディタの一覧と送る部品を作り物に差し替える。
/// Unity を実際に動かす確かめは、ユーザが「試して」と言ったときだけ。名前・ID・パスは作り物
/// </summary>
public sealed class ApiUnitySendTests
{
    private const string ItemId = "9600201";

    /// <summary>登録したフォルダに unitypackage を置いた商品（在るファイルだけが送る物に数えられるので、試験のフォルダに本当に置く）。</summary>
    private static ItemRecord ItemWith(TestApp app, params string[] packages)
    {
        // 先に全部作る（配列の Select に Last を当てると、最後の1つしか作らない）
        var made = packages.Select(package => app.NewFile(System.IO.Path.Combine("衣装", package))).ToList();
        var folder = System.IO.Path.GetDirectoryName(made[^1])!;
        var item = Make.Item(ItemId, "作り物の衣装").WithFiles();
        return item with { Local = item.Local with { LocalFolders = [new LocalFolderRecord { Path = folder, UnityPackages = packages }] } };
    }

    /// <summary>作り物の送る部品：送り先と送った物を控え、決めた結果を返す。</summary>
    private sealed class FakeUnity
    {
        public List<OpenUnityEditor> Editors { get; } = [];

        public List<(int ProcessId, string Entry)> Sent { get; } = [];

        public bool Busy { get; set; }

        public Func<UnityPackageEntry, UnityQueueOutcome> Outcome { get; set; } = package => new UnityQueueOutcome(package, Opened: true, Problem: null);

        public ApiUnitySender SenderFor(AppServiceContainer services) => new(
            services,
            () => Editors,
            editor => editor.ProjectPath,
            (processId, packages, _) =>
            {
                lock (Sent)
                {
                    Sent.AddRange(packages.Select(package => (processId, package.EntryPath)));
                }

                return Task.FromResult<IReadOnlyList<UnityQueueOutcome>>(packages.Select(Outcome).ToList());
            },
            () => Busy);
    }

    private static async Task<int> CodeStatusAsync(Func<object> act, string code)
    {
        var exception = Assert.Throws<QueryException>(act);
        Assert.Equal(code, exception.Code);
        await Task.CompletedTask;
        return exception.Status;
    }

    [Fact]
    public Task 送る物と送り先が1つずつなら_それを送り_取り込めたら使ったの足跡を付ける() => TestApp.Run(async app =>
    {
        var item = ItemWith(app, "Outfit.unitypackage");
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity();
        unity.Editors.Add(new OpenUnityEditor(4242, "作り物のプロジェクト", @"C:\作り物\Project"));
        var sender = unity.SenderFor(app.Services);

        var (started, finished) = sender.Start(item, new UnitySendRequest(null, null, null));
        Assert.Equal(("sending", @"C:\作り物\Project"), (started.State, started.Project));

        var done = await finished;
        Assert.Equal("imported", done.State);
        Assert.Equal((4242, "Outfit.unitypackage"), Assert.Single(unity.Sent));
        Assert.Same(done, sender.Last);
        await app.SettleAsync();
        Assert.NotNull(app.Store.Recent.Load().Entries.Single(entry => entry.ItemId == ItemId).UsedAt);

        // 何をしたかはログに1行（結果の種類だけ。商品 ID・包みの名前は書かない）
        var noted = System.IO.File.ReadAllLines(app.Services.Paths.LogFile).Where(line => line.Contains("外から使う API", StringComparison.Ordinal)).ToList();
        Assert.Contains(noted, line => line.Contains("Unity へ送る → imported", StringComparison.Ordinal));
        Assert.DoesNotContain(noted, line => line.Contains(ItemId, StringComparison.Ordinal) || line.Contains("Outfit", StringComparison.Ordinal));
    });

    [Fact]
    public Task 選べないときは送らずに理由を返す() => TestApp.Run(async app =>
    {
        var item = ItemWith(app, "A.unitypackage", "B.unitypackage");
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity();
        var sender = unity.SenderFor(app.Services);

        // エディタが開いていない
        Assert.Equal(409, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest("A.unitypackage", null, null)), "unityNotOpen"));

        // 送る物が2つあるのに指定が無い・無い物を指定した
        unity.Editors.Add(new OpenUnityEditor(1, "一", @"C:\作り物\One"));
        Assert.Equal(400, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest(null, null, null)), "unityPackageAmbiguous"));
        Assert.Equal(404, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest("C.unitypackage", null, null)), "unityPackageNotFound"));

        // エディタが2つあるのに送り先の指定が無い・開いていないプロジェクトを指定した
        unity.Editors.Add(new OpenUnityEditor(2, "二", @"C:\作り物\Two"));
        Assert.Equal(409, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest("A.unitypackage", null, null)), "unityTargetAmbiguous"));
        Assert.Equal(409, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest("A.unitypackage", null, @"C:\作り物\Three")), "unityProjectNotOpen"));

        // もう送っている途中
        unity.Busy = true;
        Assert.Equal(409, await CodeStatusAsync(() => sender.Start(item, new UnitySendRequest("A.unitypackage", null, @"C:\作り物\Two")), "unityBusy"));
        Assert.Empty(unity.Sent);

        // 指定すれば、そのプロジェクトを開いているエディタへ、その物を送る（末尾の区切りと大文字小文字は見ない）
        unity.Busy = false;
        await sender.Start(item, new UnitySendRequest("b.unitypackage", null, @"c:\作り物\two\")).Finished;
        Assert.Equal((2, "B.unitypackage"), Assert.Single(unity.Sent));
    });

    [Fact]
    public Task 送れなければ失敗を返し_使ったの足跡は付けない() => TestApp.Run(async app =>
    {
        var item = ItemWith(app, "Outfit.unitypackage");
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity { Outcome = package => new UnityQueueOutcome(package, Opened: false, Problem: "作り物の失敗") };
        unity.Editors.Add(new OpenUnityEditor(7, "作り物", @"C:\作り物\Project"));

        var done = await unity.SenderFor(app.Services).Start(item, new UnitySendRequest(null, null, null)).Finished;

        Assert.Equal(("failed", "作り物の失敗"), (done.State, done.Problem));
        await app.SettleAsync();
        Assert.DoesNotContain(app.Store.Recent.Load().Entries, entry => entry.ItemId == ItemId && entry.UsedAt is not null);
    });

    [Fact]
    public Task HTTPからは_202で送っていると返り_終わりは最後の送りで引ける() => TestApp.Run(async app =>
    {
        var item = ItemWith(app, "Outfit.unitypackage");
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity();
        unity.Editors.Add(new OpenUnityEditor(4242, "作り物のプロジェクト", @"C:\作り物\Project"));
        app.Services.ApiUnity = unity.SenderFor(app.Services);

        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true, Port = port });
        await app.Services.Api.ApplyAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token);
        try
        {
            var editors = JsonDocument.Parse(await client.GetStringAsync("/v1/unity/editors")).RootElement;
            Assert.Equal(4242, editors[0].GetProperty("processId").GetInt32());

            var response = await client.PostAsync($"/v1/items/{ItemId}/send-to-unity", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("sending", JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("state").GetString());

            await UiThread.Until(() => app.Services.ApiUnity.Last?.State == "imported", "送り終える");
            var last = JsonDocument.Parse(await client.GetStringAsync("/v1/unity/last-send")).RootElement;
            Assert.Equal("imported", last.GetProperty("state").GetString());
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });
}
