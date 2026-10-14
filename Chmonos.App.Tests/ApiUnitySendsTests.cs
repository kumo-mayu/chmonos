using System.IO;
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
/// 外から使う API の送りの ID・止める道・改変の使ったものをまとめて送る道（API-GAPS 3-6・3-7・3-8）と、Unity が閉じたときの扱い（4-1）。
/// Unity は動かさず、エディタの一覧と送る部品を作り物に差し替える。名前・ID・パスは作り物
/// </summary>
public sealed class ApiUnitySendsTests
{
    private const string Costume = "9600501";
    private const string Shader = "9600502";
    private const string Hair = "9600503";
    private const string Gone = "9600504";
    private const string Avatar = "9600505";
    private const string Project = @"C:\作り物\Project";

    /// <summary>
    /// 作り物の送る部品。送った物を控え、1件ずつ結果を知らせる。<see cref="Gate"/> を置くと、1件目の前で止める合図か開くのを待つ
    /// （止める合図が来たら、本物と同じく残りを「中止しました」で返す）
    /// </summary>
    private sealed class FakeUnity
    {
        public List<OpenUnityEditor> Editors { get; } = [new OpenUnityEditor(4242, "作り物のプロジェクト", Project)];

        public List<IReadOnlyList<string>> Runs { get; } = [];

        public List<int> ProcessIds { get; } = [];

        public TaskCompletionSource? Gate { get; set; }

        public Func<UnityPackageEntry, UnityQueueOutcome> Outcome { get; set; } = package => new UnityQueueOutcome(package, Opened: true, Problem: null);

        public ApiUnitySender SenderFor(AppServiceContainer services) => new(
            services,
            () => Editors,
            editor => editor.ProjectPath,
            async (processId, packages, added, token) =>
            {
                lock (Runs)
                {
                    Runs.Add(packages.Select(package => package.EntryPath).ToList());
                    ProcessIds.Add(processId);
                }

                var outcomes = new List<UnityQueueOutcome>();
                try
                {
                    if (Gate is { } gate)
                    {
                        await gate.Task.WaitAsync(token);
                    }
                }
                catch (OperationCanceledException)
                {
                    return packages.Select(package => new UnityQueueOutcome(package, false, UnityImportQueue.StoppedMessage)).ToList();
                }

                foreach (var package in packages)
                {
                    var outcome = Outcome(package);
                    outcomes.Add(outcome);
                    added(outcomes.Count - 1, outcome);
                }

                return outcomes;
            },
            () => false);
    }

    /// <summary>zip 1つに unitypackage を入れた手元のファイル（中身は空。送る物を数えるのに zip を開くので、本当に zip を作る）。</summary>
    private static LocalFileRecord ZipWith(TestApp app, string name, params string[] packages)
    {
        var path = app.NewFile(Path.Combine("zips", name));
        File.Delete(path);
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var package in packages)
            {
                zip.CreateEntry(package);
            }
        }

        return Make.File(path);
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<HttpClient> StartApiAsync(TestApp app, FakeUnity unity)
    {
        await app.StartAsync();
        app.Services.ApiUnity = unity.SenderFor(app.Services);
        var port = FreePort();
        JsonStore.Write(app.Services.Paths.ApiSettingsFile, new ApiSettings { Enabled = true, AllowWrites = true, Port = port });
        await app.Services.Api.ApplyAsync();
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", JsonStore.Read<ApiEndpointRecord>(app.Services.Paths.ApiFile)!.Token);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string body)
        => client.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ---- 4-1：Unity が閉じたのに「取り込んだ」になる ----

    [Fact]
    public void 取り込んだと決めた瞬間にエディタが閉じていれば_閉じた側にする()
    {
        Assert.Equal((UnityImportState.Waiting, true), UnityImportQueue.Settle(UnityImportState.Imported, () => false));
        Assert.Equal((UnityImportState.Waiting, true), UnityImportQueue.Settle(UnityImportState.AlreadyPresent, () => false));
        Assert.Equal((UnityImportState.Imported, false), UnityImportQueue.Settle(UnityImportState.Imported, () => true));

        // 取り消しは、閉じたときと同じく何も入っていないので見直さない（エディタを見に行かない）
        Assert.Equal((UnityImportState.Cancelled, false), UnityImportQueue.Settle(UnityImportState.Cancelled, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void 送る部品の結果を_状態の値にする()
    {
        var package = new UnityPackageEntry(@"C:\作り物\a.zip", "A.unitypackage", 0);
        Assert.Equal("imported", ApiUnitySender.StateOf(new UnityQueueOutcome(package, true, null)));
        Assert.Equal("cancelled", ApiUnitySender.StateOf(new UnityQueueOutcome(package, true, null, Cancelled: true)));
        Assert.Equal("alreadyPresent", ApiUnitySender.StateOf(new UnityQueueOutcome(package, true, null, AlreadyPresent: true)));

        // 取り込みの窓を出した後に閉じた物・まだ出していなかった物のどちらも、取り込んだとは言わない（前は前者が imported になっていた）
        Assert.Equal("editorClosed", ApiUnitySender.StateOf(new UnityQueueOutcome(package, true, UnityImportQueue.EditorClosedMessage)));
        Assert.Equal("editorClosed", ApiUnitySender.StateOf(new UnityQueueOutcome(package, false, UnityImportQueue.EditorClosedMessage)));
        Assert.Equal("failed", ApiUnitySender.StateOf(new UnityQueueOutcome(package, true, "取り込みが終わるのを待ちきれませんでした")));
        Assert.Equal("stopped", ApiUnitySender.StateOf(new UnityQueueOutcome(package, false, UnityImportQueue.StoppedBeforeWindowMessage)));
        Assert.Equal("failed", ApiUnitySender.StateOf(new UnityQueueOutcome(package, false, "作り物の失敗")));
        Assert.Equal("failed", ApiUnitySender.StateOf(null));
    }

    /// <summary>
    /// 画面の送り（改変・検索の複数選択・商品ページ）が「使った」を付けるかは <see cref="UnityQueueOutcome.Taken"/> で決める。
    /// 取り込みの窓を出したまま Unity ごと閉じた物は入っていないので付けない（2026-10-14。前は窓が開いて Cancel でなければ付けていた）
    /// </summary>
    [Fact]
    public void 取り込まれたと見なすのは_窓が開いて_Cancelでも閉じられたのでもない物()
    {
        var package = new UnityPackageEntry(@"C:\作り物\a.zip", "A.unitypackage", 0);
        Assert.True(new UnityQueueOutcome(package, true, null).Taken);
        Assert.True(new UnityQueueOutcome(package, true, null, AlreadyPresent: true).Taken);
        Assert.False(new UnityQueueOutcome(package, true, null, Cancelled: true).Taken);
        Assert.False(new UnityQueueOutcome(package, true, UnityImportQueue.EditorClosedMessage).Taken);
        Assert.False(new UnityQueueOutcome(package, false, "作り物の失敗").Taken);
    }

    [Fact]
    public void 画面の結果の文は_閉じたときも今までどおり()
    {
        var first = new UnityPackageEntry(@"C:\作り物\a.zip", "A.unitypackage", 0);
        var second = new UnityPackageEntry(@"C:\作り物\a.zip", "B.unitypackage", 0);
        var text = UnityQueueOutcome.Describe(
        [
            new UnityQueueOutcome(first, true, UnityImportQueue.EditorClosedMessage),
            new UnityQueueOutcome(second, false, UnityImportQueue.EditorClosedMessage),
        ]);
        Assert.Equal("1 件をUnityへ順に送りました。1 件は送れませんでした（Unityが閉じられました）。", text);
    }

    [Theory]
    [InlineData("imported", "imported")]
    [InlineData("imported,cancelled,alreadyPresent", "imported")]
    [InlineData("alreadyPresent,cancelled", "alreadyPresent")]
    [InlineData("cancelled,cancelled", "cancelled")]
    [InlineData("imported,failed", "failed")]
    [InlineData("imported,editorClosed,editorClosed", "editorClosed")]
    [InlineData("imported,stopped,stopped", "stopped")]
    [InlineData("skipped,imported", "imported")]
    [InlineData("imported,sending,queued", "sending")]
    public void 送り全体の状態は1件ずつから決める(string steps, string overall)
        => Assert.Equal(overall, UnitySendStates.Overall(steps.Split(',')));

    [Fact]
    public Task Unityが閉じた送りは_使ったの足跡を付けない() => TestApp.Run(async app =>
    {
        var item = Make.Item(Costume, "作り物の衣装");
        item = item.WithFiles(ZipWith(app, "costume.zip", "Costume.unitypackage"));
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity { Outcome = package => new UnityQueueOutcome(package, true, UnityImportQueue.EditorClosedMessage) };

        var done = await unity.SenderFor(app.Services).Start(item, new UnitySendRequest(null, null, null)).Finished;

        Assert.Equal(("editorClosed", UnityImportQueue.EditorClosedMessage), (done.State, done.Problem));
        await app.SettleAsync();
        Assert.DoesNotContain(app.Store.Recent.Load().Entries, entry => entry.ItemId == Costume && entry.UsedAt is not null);
    });

    // ---- 3-7：送りの ID と、頼んだ物をそのまま返す ----

    [Fact]
    public Task 送りにIDを付け_頼んだ物をそのまま返し_IDで引ける() => TestApp.Run(async app =>
    {
        var file = ZipWith(app, "costume.zip", "A/Costume.unitypackage", "B/Extra.unitypackage");
        var item = Make.Item(Costume, "作り物の衣装").WithFiles(file);
        await app.AddItemAsync(item);
        await app.StartAsync();
        var sender = new FakeUnity().SenderFor(app.Services);

        var (started, finished) = sender.Start(item, new UnitySendRequest("b/extra.unitypackage", file.Hash, null));
        Assert.Equal(12, started.SendId.Length);
        Assert.Equal((Costume, "B/Extra.unitypackage", file.Hash, "sending"), (started.ItemId, started.Entry, started.ZipHash, started.State));
        var step = Assert.Single(started.Steps);
        Assert.Equal((Costume, "B/Extra.unitypackage", file.Hash, "sending"), (step.ItemId, step.Entry, step.ZipHash, step.State));

        var done = await finished;
        Assert.Equal(("imported", "imported"), (done.State, Assert.Single(done.Steps).State));
        Assert.Equal(done, sender.Find(started.SendId));
        Assert.Null(sender.Find("000000000000"));
    });

    [Fact]
    public Task 覚えている送りは直近の数件だけ() => TestApp.Run(async app =>
    {
        var item = Make.Item(Costume, "作り物の衣装").WithFiles(ZipWith(app, "costume.zip", "Costume.unitypackage"));
        await app.AddItemAsync(item);
        await app.StartAsync();
        var sender = new FakeUnity().SenderFor(app.Services);

        var ids = new List<string>();
        for (var count = 0; count < ApiUnitySender.Kept + 1; count++)
        {
            var (started, finished) = sender.Start(item, new UnitySendRequest(null, null, null));
            await finished;
            ids.Add(started.SendId);
        }

        Assert.Null(sender.Find(ids[0]));
        Assert.NotNull(sender.Find(ids[1]));
        Assert.Equal(ids[^1], sender.Last!.SendId);
    });

    // ---- 3-6：送るのを止める ----

    [Fact]
    public Task 止めると画面の中止と同じく止まり_終わった送りと知らない送りは断る() => TestApp.Run(async app =>
    {
        var item = Make.Item(Costume, "作り物の衣装").WithFiles(ZipWith(app, "costume.zip", "Costume.unitypackage"));
        await app.AddItemAsync(item);
        await app.StartAsync();
        var unity = new FakeUnity { Gate = new TaskCompletionSource() };
        var sender = unity.SenderFor(app.Services);

        var (started, _) = sender.Start(item, new UnitySendRequest(null, null, null));
        var (current, finished) = sender.Cancel(started.SendId);
        Assert.Equal("sending", current.State);

        var done = await finished;
        Assert.Equal(("stopped", UnityImportQueue.StoppedMessage), (done.State, done.Problem));
        Assert.Equal("stopped", Assert.Single(done.Steps).State);

        Assert.Equal("unitySendEnded", Assert.Throws<QueryException>(() => sender.Cancel(started.SendId)).Code);
        Assert.Equal(404, Assert.Throws<QueryException>(() => sender.Cancel("000000000000")).Status);

        // 止めた後は次を受ける。止めた物には「使った」の足跡を付けない
        unity.Gate = null;
        Assert.Equal("imported", (await sender.Start(item, new UnitySendRequest(null, null, null)).Finished).State);
        await app.SettleAsync();
        Assert.Single(app.Store.Recent.Load().Entries, entry => entry.ItemId == Costume && entry.UsedAt is not null);
    });

    // ---- 3-8：改変の使ったものをまとめて送る ----

    /// <summary>
    /// 改変：0 シェーダー（記録あり）・1 衣装（記録なし・候補2つ）・2 外した行・3 髪（記録なし・1つ）・4 手元に無い商品・5 シェーダーをもう一度（同じ記録）。
    /// </summary>
    private static async Task<(string Id, LocalFileRecord ShaderZip, LocalFileRecord CostumeZip)> MakeModificationAsync(TestApp app)
    {
        var shaderZip = ZipWith(app, "shader.zip", "Shader.unitypackage");
        var costumeZip = ZipWith(app, "costume.zip", "Costume_v1.unitypackage", "Costume_v2.unitypackage");
        await app.AddItemAsync(Make.Item(Shader, "作り物のシェーダー").WithFiles(shaderZip));
        await app.AddItemAsync(Make.Item(Costume, "作り物の衣装").WithFiles(costumeZip));
        await app.AddItemAsync(Make.Item(Hair, "作り物の髪").WithFiles(ZipWith(app, "hair.zip", "Hair.unitypackage")));
        await app.AddItemAsync(Make.Item(Avatar, "作り物のアバター"));
        var now = DateTimeOffset.Now;
        var record = new ModificationRecord
        {
            Id = ModificationId.For(Avatar, "作り物の改変", now),
            AvatarItemId = Avatar,
            Name = "作り物の改変",
            CreatedAt = now,
            UpdatedAt = now,
            UnityProject = Project,
            Members =
            [
                new ModificationMember { ItemId = Shader, FileHash = shaderZip.Hash, Package = "Shader.unitypackage" },
                new ModificationMember { ItemId = Costume },
                new ModificationMember { ItemId = Hair, Detached = true },
                new ModificationMember { ItemId = Hair },
                new ModificationMember { ItemId = Gone },
                new ModificationMember { ItemId = Shader, FileHash = shaderZip.Hash, Package = "Shader.unitypackage" },
            ],
        };
        await app.Store.Modifications.SaveAsync(record);
        return (record.Id, shaderZip, costumeZip);
    }

    [Fact]
    public Task 送り方は行ごとに_送る物か選ぶ候補か送れない理由を返す() => TestApp.Run(async app =>
    {
        var (id, shaderZip, costumeZip) = await MakeModificationAsync(app);
        using var client = await StartApiAsync(app, new FakeUnity());
        try
        {
            var plan = JsonDocument.Parse(await client.GetStringAsync($"/v1/modifications/{id}/send-plan")).RootElement;
            Assert.Equal(Project, plan.GetProperty("project").GetString());
            var rows = plan.GetProperty("rows").EnumerateArray().ToList();

            // 外した行（2）は入らない。番号は外した行も数えた記録の位置
            Assert.Equal([0, 1, 3, 4, 5], rows.Select(row => row.GetProperty("index").GetInt32()));
            Assert.Equal(["send", "choose", "send", "unavailable", "send"], rows.Select(row => row.GetProperty("status").GetString()));

            var shader = rows[0].GetProperty("send")[0];
            Assert.Equal(("Shader.unitypackage", shaderZip.Hash), (shader.GetProperty("entry").GetString(), shader.GetProperty("zipHash").GetString()));
            Assert.Equal(
                ["Costume_v1.unitypackage", "Costume_v2.unitypackage"],
                rows[1].GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("entry").GetString()));
            Assert.Equal(costumeZip.Hash, rows[1].GetProperty("candidates")[0].GetProperty("zipHash").GetString());
            Assert.Equal("itemMissing", rows[3].GetProperty("reason").GetString());

            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/modifications/nothing/send-plan")).StatusCode);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 選ぶ行に選んだ物が無ければ送らずに_どの行かを返す() => TestApp.Run(async app =>
    {
        var (id, _, _) = await MakeModificationAsync(app);
        var unity = new FakeUnity();
        using var client = await StartApiAsync(app, unity);
        try
        {
            var missing = await PostAsync(client, $"/v1/modifications/{id}/send-to-unity", "{}");
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            var problem = await JsonOf(missing);
            Assert.Equal("choiceRequired", problem.GetProperty("code").GetString());
            Assert.Contains("行 1 ", problem.GetProperty("detail").GetString());

            var extra = await PostAsync(client, $"/v1/modifications/{id}/send-to-unity", """{"choices":[{"index":3,"entry":"Hair.unitypackage"}]}""");
            Assert.Equal("choiceNotNeeded", (await JsonOf(extra)).GetProperty("code").GetString());

            var unknown = await PostAsync(client, $"/v1/modifications/{id}/send-to-unity", """{"choices":[{"index":1,"entry":"Costume_v9.unitypackage"}]}""");
            Assert.Equal("unknownChoice", (await JsonOf(unknown)).GetProperty("code").GetString());

            // 配列の中の null は 400（点検40：前は 500 になった）
            var nulled = await PostAsync(client, $"/v1/modifications/{id}/send-to-unity", """{"choices":[null]}""");
            Assert.Equal(HttpStatusCode.BadRequest, nulled.StatusCode);
            Assert.Equal("invalidChoice", (await JsonOf(nulled)).GetProperty("code").GetString());
            Assert.Empty(unity.Runs);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task まとめて送ると1つの列で順に送り_選んだ物を記録し_足跡を付ける() => TestApp.Run(async app =>
    {
        var (id, shaderZip, costumeZip) = await MakeModificationAsync(app);
        var unity = new FakeUnity
        {
            // 髪は取り込みの窓で取り消された
            Outcome = package => new UnityQueueOutcome(package, true, null, Cancelled: package.EntryPath == "Hair.unitypackage"),
        };
        using var client = await StartApiAsync(app, unity);
        try
        {
            var since = JsonDocument.Parse(await client.GetStringAsync("/v1/version")).RootElement.GetProperty("sequence").GetInt64();
            var response = await PostAsync(
                client, $"/v1/modifications/{id}/send-to-unity", $$"""{"choices":[{"index":1,"zipHash":"{{costumeZip.Hash}}","entry":"Costume_v2.unitypackage"}]}""");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var started = await JsonOf(response);
            var sendId = started.GetProperty("sendId").GetString()!;
            Assert.Equal(id, started.GetProperty("modificationId").GetString());

            await UiThread.Until(() => app.Services.ApiUnity!.Find(sendId)?.EndedAt is not null, "送り終える");
            var done = JsonDocument.Parse(await client.GetStringAsync($"/v1/unity/sends/{sendId}")).RootElement;

            // 画面と同じ順（記録の順・外した行を除く）で、1つの列にまとめて送る。同じ包みを使った行が2つあれば2回送る（画面と同じ）
            Assert.Equal(
                ["Shader.unitypackage", "Costume_v2.unitypackage", "Hair.unitypackage", "Shader.unitypackage"],
                Assert.Single(unity.Runs));
            Assert.Equal("imported", done.GetProperty("state").GetString());
            Assert.Equal(
                ["imported", "imported", "cancelled", "skipped", "imported"],
                done.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("state").GetString()));

            // 選んだ物は、その行に記録する（画面の選ぶ窓と同じ形）
            var record = (await app.Store.Modifications.LoadAsync(id))!;
            var costume = record.Members[1];
            Assert.Equal((Costume, costumeZip.Hash, "Costume_v2.unitypackage"), (costume.ItemId, costume.FileHash, costume.Package));

            // 「使った」の足跡は入った商品だけ（取り消した髪には付けない）
            await app.SettleAsync();
            var used = app.Store.Recent.Load().Entries.Where(entry => entry.UsedAt is not null).Select(entry => entry.ItemId).ToHashSet();
            Assert.Contains(Shader, used);
            Assert.Contains(Costume, used);
            Assert.DoesNotContain(Hair, used);

            // 変化の知らせに送りの ID と、送った商品が載る
            // 知らせは送り終えた後に出す（終わりの続きで出る）ので、出るまで少し待つ
            var changes = string.Empty;
            for (var tries = 0; tries < 200 && !changes.Contains(sendId, StringComparison.Ordinal); tries++)
            {
                await Task.Delay(tries == 0 ? 0 : 25);
                changes = await client.GetStringAsync($"/v1/changes?since={since}");
            }

            var change = JsonDocument.Parse(changes).RootElement.GetProperty("changes")
                .EnumerateArray().Single(change => change.GetProperty("kind").GetString() == "unity");
            Assert.Equal(sendId, change.GetProperty("sendId").GetString());
            var changedIds = change.GetProperty("itemIds").EnumerateArray().Select(itemId => itemId.GetString()).ToList();
            Assert.Contains(Hair, changedIds);
            Assert.DoesNotContain(Gone, changedIds);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task 送り先を省くと_紐付けたプロジェクトを開いているエディタへ送る() => TestApp.Run(async app =>
    {
        var (id, _, costumeZip) = await MakeModificationAsync(app);
        var unity = new FakeUnity();
        unity.Editors.Insert(0, new OpenUnityEditor(1, "別のプロジェクト", @"C:\作り物\Other"));
        using var client = await StartApiAsync(app, unity);
        try
        {
            var response = await PostAsync(
                client, $"/v1/modifications/{id}/send-to-unity", $$"""{"choices":[{"index":1,"entry":"Costume_v1.unitypackage"}]}""");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var started = await JsonOf(response);
            Assert.Equal(Project, started.GetProperty("project").GetString());

            // 送り終えるまで待つ（選んだ物の記録と足跡を書いている途中で、試験の保存先を片付けない）
            var sendId = started.GetProperty("sendId").GetString()!;
            await UiThread.Until(() => app.Services.ApiUnity!.Find(sendId)?.EndedAt is not null, "送り終える");
            Assert.Equal([4242], unity.ProcessIds);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });

    [Fact]
    public Task HTTPから止めると_止まった状態で答える() => TestApp.Run(async app =>
    {
        var item = Make.Item(Costume, "作り物の衣装").WithFiles(ZipWith(app, "costume.zip", "Costume.unitypackage"));
        await app.AddItemAsync(item);
        var unity = new FakeUnity { Gate = new TaskCompletionSource() };
        using var client = await StartApiAsync(app, unity);
        try
        {
            var started = await JsonOf(await PostAsync(client, $"/v1/items/{Costume}/send-to-unity", "{}"));
            var sendId = started.GetProperty("sendId").GetString()!;

            var stopped = await PostAsync(client, $"/v1/unity/sends/{sendId}/cancel", "{}");
            Assert.Equal(HttpStatusCode.OK, stopped.StatusCode);
            Assert.Equal("stopped", (await JsonOf(stopped)).GetProperty("state").GetString());

            var again = await PostAsync(client, $"/v1/unity/sends/{sendId}/cancel", "{}");
            Assert.Equal((HttpStatusCode.Conflict, "unitySendEnded"), (again.StatusCode, (await JsonOf(again)).GetProperty("code").GetString()));
            Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(client, "/v1/unity/sends/000000000000/cancel", "{}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/unity/sends/000000000000")).StatusCode);
        }
        finally
        {
            await app.Services.Api.StopAsync();
        }
    });
}
