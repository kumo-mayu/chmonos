using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// Unity へ送る道（ユーザ判断 2026-10-13：最初の一束に入れる）。送り終えるまで待たず 202 で返し、終わったら変化の知らせ（unity）で伝える。
///
/// 1回の送りに ID（sendId）を付け、頼んだ物（entry・zipHash）をそのまま返す。外の道具は最後の1件の時刻で自分の送りを見分けていた（API-GAPS 3-7）。
/// 改変の使ったものをまとめて送る道は、画面の「使ったものを順にUnityへ送る」と同じ決め方（<see cref="ModificationSendPlan"/>）と同じ列で送る（3-8）。
/// 止める道は画面の「中止」と同じ止め方（3-6）
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapUnity(WebApplication app)
    {
        var json = QueryJson.Context;
        var states = string.Join("・", UnitySendStates.All);

        app.MapPost("/v1/items/{id}/send-to-unity", async (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.UnitySendRequest);
            using var claim = writer.GuardItem(context, id, checkEditing: false);
            var unity = writer.Unity ?? throw new QueryException(503, "unityUnavailable", "この API は Unity へ送れません。");
            var (started, finished) = unity.Start(state.Snapshot.ItemsById[id], body);
            PublishWhenDone(state, feed, started, finished);
            return Results.Json(started, json.UnitySendStatus, statusCode: StatusCodes.Status202Accepted);
        })
            .Takes<UnitySendRequest>().Produces<UnitySendStatus>(202)
            .WithSummary("Unity へ送る（書き込み。終わりは変化の知らせ unity と /v1/unity/sends/{sendId}）")
            .WithDescription($"state：{states}。誤りの code：noUnityPackage・unityPackageNotFound・unityPackageAmbiguous・unityNotOpen・"
                + "unityProjectNotOpen・unityTargetAmbiguous・unityBusy・unityUnavailable・itemNotFound");

        app.MapGet("/v1/unity/editors", (ApiWriter writer) => ApiHost.Json(writer.Unity?.OpenEditors() ?? [], json.IReadOnlyListUnityEditorRef))
            .Answers<IReadOnlyList<UnityEditorRef>>("開いている Unity のエディタ（送り先を選ぶため）");

        app.MapGet("/v1/unity/last-send", (ApiWriter writer) =>
            writer.Unity?.Last is { } last
                ? ApiHost.Json(last, json.UnitySendStatus)
                : throw QueryException.NotFound("noUnitySend", "API から Unity へ送ったことがありません。"))
            .Answers<UnitySendStatus>("最後に API から Unity へ送った結果")
            .WithDescription($"state：{states}");

        app.MapGet("/v1/unity/sends/{sendId}", (ApiWriter writer, string sendId) =>
            writer.Unity?.Find(sendId) is { } found
                ? ApiHost.Json(found, json.UnitySendStatus)
                : throw UnitySendNotFound())
            .Answers<UnitySendStatus>("API から Unity へ送った送りの結果（直近の数件だけ覚えている）")
            .WithDescription($"state：{states}。誤りの code：unitySendNotFound");

        // 止める（画面の「中止」と同じ止め方）。取り込みの窓を待つ間・zip から取り出す間はすぐ止まるので、少しだけ待って終わった状態で答える。
        // 待ちきれなければ（中身の読み取りやプロジェクトの走査は、始まると最後まで走る）止めると頼んだ時点の状態を 202 で返す
        app.MapPost("/v1/unity/sends/{sendId}/cancel", async (ApiWriter writer, HttpContext context, string sendId) =>
        {
            writer.Guard(context);
            var unity = writer.Unity ?? throw new QueryException(503, "unityUnavailable", "この API は Unity へ送れません。");
            var (current, finished) = unity.Cancel(sendId);
            if (await Task.WhenAny(finished, Task.Delay(CancelWait, context.RequestAborted)) == finished && finished.IsCompletedSuccessfully)
            {
                return ApiHost.Json(finished.Result, json.UnitySendStatus);
            }

            return Results.Json(unity.Find(sendId) ?? current, json.UnitySendStatus, statusCode: StatusCodes.Status202Accepted);
        })
            .Answers<UnitySendStatus>("送りを止める（書き込み。止まったら state が stopped。Unity に残った取り込みの窓は閉じない）")
            .Produces<UnitySendStatus>(202)
            .WithDescription("誤りの code：unitySendNotFound・unitySendEnded・unityUnavailable");

        app.MapGet("/v1/modifications/{id}/send-plan", async (ApiState state, HttpContext context, string id) =>
        {
            var (record, rows) = await PlanAsync(state, id, context.RequestAborted);
            return ApiHost.Json(new UnitySendPlan(record.Id, record.UnityProject, rows.Select(PlanRowOf).ToList()), json.UnitySendPlan);
        })
            .Answers<UnitySendPlan>("改変の使ったものを順に Unity へ送るときの、行ごとの送り方（送る物・選ぶ候補・送れない理由）")
            .WithDescription("誤りの code：modificationNotFound");

        app.MapPost("/v1/modifications/{id}/send-to-unity", async (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.ModificationSendRequest);
            writer.Guard(context);
            var unity = writer.Unity ?? throw new QueryException(503, "unityUnavailable", "この API は Unity へ送れません。");
            var (record, rows) = await PlanAsync(state, id, context.RequestAborted);
            var (started, finished) = unity.StartModification(Resolve(record, rows, body));
            PublishWhenDone(state, feed, started, finished);
            return Results.Json(started, json.UnitySendStatus, statusCode: StatusCodes.Status202Accepted);
        })
            .Takes<ModificationSendRequest>().Produces<UnitySendStatus>(202)
            .WithSummary("改変の使ったものを順に Unity へ送る（書き込み。画面の「使ったものを順にUnityへ送る」と同じ順・同じ列。選んだ物は改変に記録する）")
            .WithDescription($"state：{states}。誤りの code：modificationNotFound・choiceRequired・choiceNotNeeded・invalidChoice・unknownChoice・nothingToSend・"
                + "unityNotOpen・unityProjectNotOpen・unityTargetAmbiguous・unityBusy・unityUnavailable");
    }

    /// <summary>
    /// 止めると頼んでから答えるまで待つ長さ。取り込みの窓を待つ間は 0.2 秒ごとに見ているのですぐ止まり、取り出しも書き出しの途中で止まる。
    /// 止まらないのは中身の読み取りとプロジェクトの走査（大きい物で数秒）で、それを待つほど答えを遅らせない
    /// </summary>
    private static readonly TimeSpan CancelWait = TimeSpan.FromSeconds(3);

    private static QueryException UnitySendNotFound()
        => QueryException.NotFound("unitySendNotFound", "その送りは見つかりません。覚えているのは直近の数件だけです。");

    /// <summary>送り終えたら変化の知らせ（unity）を出す。送りの ID と、送った商品を載せる（送らなかった行の商品は載せない）。</summary>
    private static void PublishWhenDone(ApiState state, ChangeFeed feed, UnitySendStatus started, Task<UnitySendStatus> finished)
        => _ = finished.ContinueWith(
            task =>
            {
                var done = task.IsCompletedSuccessfully ? task.Result : started with { State = UnitySendStates.Failed };
                var itemIds = done.Steps.Where(step => step.State != UnitySendStates.Skipped).Select(step => step.ItemId).Distinct(StringComparer.Ordinal).ToList();
                feed.Publish("unity", [done.State], itemIds, null, state.Clock(), done.SendId);
            },
            TaskScheduler.Default);

    /// <summary>
    /// 改変の送る行を決める。**改変も商品も保存先の今の記録から読む**（選んだ物を記録する命令は行そのもので指すので、写しの古い行では当たらない。
    /// 写しは取り込みの間は古い。点検38 と同じ理由）
    /// </summary>
    private static async Task<(ModificationRecord Record, IReadOnlyList<ModificationSendPlan.Row> Rows)> PlanAsync(
        ApiState state, string id, CancellationToken cancellationToken)
    {
        ModificationRecord? record = null;
        try
        {
            record = await state.Store.Modifications.LoadAsync(id, cancellationToken);
        }
        catch (ArgumentException)
        {
            // 改変の ID の形でない（ファイルの名前にできない）。無いのと同じに答える
        }

        if (record is null)
        {
            throw QueryException.NotFound("modificationNotFound", $"改変 {id} は見つかりません。");
        }

        var items = new Dictionary<string, ItemRecord?>(StringComparer.Ordinal);
        foreach (var itemId in record.Members.Where(ModificationSendPlan.Sends).Select(member => member.ItemId).Distinct(StringComparer.Ordinal))
        {
            items[itemId] = await state.Store.Items.LoadAsync(itemId, cancellationToken);
        }

        // 送れる物を数えるのにディスクを見る（ファイルが今も在るか）
        var rows = await Task.Run(() => ModificationSendPlan.RowsOf(record, itemId => items.GetValueOrDefault(itemId)), cancellationToken);
        return (record, rows);
    }

    private static UnitySendTarget TargetOf(UnityPackageEntry package) => new(package.Name, package.EntryPath, package.ZipHash, package.InFolder);

    private static UnitySendPlanRow PlanRowOf(ModificationSendPlan.Row row)
    {
        var targets = row.Packages.Select(TargetOf).ToList();
        if (!row.Sendable)
        {
            var (reason, problem) = WhyNotSendable(row);
            return new UnitySendPlanRow(row.Index, row.Member.ItemId, "unavailable", [], [], reason, problem);
        }

        return row.NeedsChoice
            ? new UnitySendPlanRow(row.Index, row.Member.ItemId, "choose", [], targets, null, null)
            : new UnitySendPlanRow(row.Index, row.Member.ItemId, "send", targets, [], null, null);
    }

    /// <summary>送れない行の理由（種類と文）。</summary>
    private static (string Reason, string Problem) WhyNotSendable(ModificationSendPlan.Row row)
    {
        if (row.Item is null)
        {
            return ("itemMissing", "この商品は手元の記録にありません。");
        }

        // 使ったファイルを記録した行は、そのファイルが手元に無ければ送らない（別の版を黙って送らない）
        if (row.Member.FileHash is { } hash
            && !row.Item.Local.OwnedFiles.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase) && file.Paths.Any(File.Exists)))
        {
            return ("recordedFileMissing", "使ったと記録したファイルが手元にありません。");
        }

        return ("noUnityPackage", "手元に Unity へ送れる unitypackage がありません。");
    }

    /// <summary>
    /// 行の決め方に、選んだ物を当てて送る列を作る。選ぶ必要がある行に選んだ物が無ければ送らずに誤り（どの行かを言う）。
    /// 選んだ物は choices に書いた順に送る
    /// </summary>
    private static UnityModificationSend Resolve(ModificationRecord record, IReadOnlyList<ModificationSendPlan.Row> rows, ModificationSendRequest body)
    {
        // 配列の中の null と entry の無い選び方は、要求の誤り（400）。前は null で落ちて 500 になった（点検40）。choices そのものを省くのはよい
        if (body.Choices is { } given && given.Any(choice => choice is null || string.IsNullOrWhiteSpace(choice.Entry)))
        {
            throw QueryException.Invalid("invalidChoice", "choices の中に、null か entry の無い物があります。{\"index\": 行の番号, \"entry\": \"…\"} の形で送ってください。");
        }

        var choices = (body.Choices ?? []).GroupBy(choice => choice.Index).ToDictionary(group => group.Key, group => group.ToList());
        var choosing = rows.Where(row => row.Sendable && row.NeedsChoice).Select(row => row.Index).ToHashSet();
        if (choices.Keys.Where(index => !choosing.Contains(index)).Order().ToList() is { Count: > 0 } extra)
        {
            throw QueryException.Invalid("choiceNotNeeded", $"行 {string.Join("・", extra)} は選ぶ必要がありません。send-plan で status が choose の行だけ選んでください。");
        }

        if (choosing.Where(index => !choices.ContainsKey(index)).Order().ToList() is { Count: > 0 } missing)
        {
            throw QueryException.Invalid("choiceRequired", $"行 {string.Join("・", missing)} は送る物を選ぶ必要があります。send-plan の candidates から choices で選んでください。");
        }

        var steps = new List<UnityModificationStep>();
        var records = new List<UnityChoiceRecord>();
        foreach (var row in rows)
        {
            if (!row.Sendable)
            {
                steps.Add(new UnityModificationStep(row.Index, row.Member.ItemId, row.Item, null, WhyNotSendable(row).Problem));
                continue;
            }

            if (!row.NeedsChoice)
            {
                steps.AddRange(row.Packages.Select(package => new UnityModificationStep(row.Index, row.Member.ItemId, row.Item, package, null)));
                continue;
            }

            // 候補は包んでいる手元のファイルと一緒に引く（記録する行のハッシュと種類はそこから取る。画面の選ぶ窓と同じ）
            var candidates = ModificationSendPlan.CandidatesOf(row.Item!);
            var picked = new List<(UnityPackagePlace Place, LocalFileRecord? Owner)>();
            foreach (var choice in choices[row.Index])
            {
                var found = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Place.Entry.EntryPath, choice.Entry, StringComparison.OrdinalIgnoreCase)
                    && (choice.ZipHash is null || string.Equals(candidate.Place.Entry.ZipHash, choice.ZipHash, StringComparison.OrdinalIgnoreCase)));
                if (found.Place is null)
                {
                    throw QueryException.Invalid("unknownChoice", $"行 {row.Index} の候補に「{choice.Entry}」はありません。send-plan の candidates から選んでください。");
                }

                if (!picked.Contains(found))
                {
                    picked.Add(found);
                }
            }

            records.Add(new UnityChoiceRecord(
                row.Index, row.Member, picked.Select(pick => ModificationSendPlan.MemberFor(row.Member.ItemId, pick.Owner, pick.Place.Entry)).ToList()));
            steps.AddRange(picked.Select(pick => new UnityModificationStep(row.Index, row.Member.ItemId, row.Item, pick.Place.Entry, null)));
        }

        if (!steps.Any(step => step.Package is not null))
        {
            throw QueryException.NotFound("nothingToSend", "使ったものの中に、Unity へ送れるもの（手元の zip やフォルダの中の .unitypackage）がありません。");
        }

        return new UnityModificationSend(record, steps, records, body.Project);
    }
}
