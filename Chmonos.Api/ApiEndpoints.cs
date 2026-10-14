using System.Text.Json;
using System.Text.RegularExpressions;
using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Storage;
using Chmonos.Query;
using Chmonos.Query.Search;

namespace Chmonos.Api;

/// <summary>URL と答えをつなぐ（docs/spec/local-api-shapes.md の URL の表）。答えの中身は Chmonos.Query が作る。</summary>
internal static partial class ApiEndpoints
{
    public static void Map(WebApplication app)
    {
        var json = QueryJson.Context;

        // トークンなしで答えるので、保存先の中身（読めなかった商品の ID）は入れない。数だけ（点検30）
        app.MapGet("/v1/health", (ApiState state) =>
        {
            var snapshot = state.Snapshot;
            return ApiHost.Json(new HealthAnswer(state.TooNew ? "tooNew" : "ok", state.LoadedAt, snapshot.Items.Count, snapshot.FailedItemIds.Count), json.HealthAnswer);
        })
            .Answers<HealthAnswer>("動いているか・読み終えたか・読めなかった商品の数（トークンなし）");

        app.MapGet("/v1/failed-items", (ApiState state) => ApiHost.Json(state.Snapshot.FailedItemIds, json.IReadOnlyListString))
            .Answers<IReadOnlyList<string>>("読めなかった商品の ID（手で壊した JSON など）");

        app.MapGet("/v1/version", (ApiState state, ChangeFeed feed, StoreSync sync) => ApiHost.Json(
            new VersionAnswer(ApiHost.ApiVersion, ApiHost.AppVersion, state.StoreFormat, StoreFormat.Current, ServiceText.Notice, feed.Epoch, feed.Sequence, sync.Busy),
            json.VersionAnswer))
            .Answers<VersionAnswer>("API のバージョン・保存の形・通番・まとめて書いている途中か・注意（トークンなし）");

        // 変化（docs/spec/local-api.md「同期」）。外の道具は前に見た通番を送り、それより後の変化を受け取る
        app.MapGet("/v1/changes", (ChangeFeed feed, long? since, string? epoch) =>
        {
            var (changes, sequence) = feed.Since(since ?? 0);
            if (epoch is not null && epoch != feed.Epoch)
            {
                changes = null;
            }

            return ApiHost.Json(new ChangesAnswer(feed.Epoch, sequence, changes is null, changes ?? []), json.ChangesAnswer);
        })
            .Answers<ChangesAnswer>("前に見た通番より後の変化");

        app.MapGet("/v1/events", (HttpContext context, ChangeFeed feed) => StreamEventsAsync(context, feed))
            .WithSummary("変化を SSE で流す（text/event-stream。fetch で読む）").Produces(200, contentType: "text/event-stream");

        app.MapGet("/v1/display-settings", (ApiState state) => ApiHost.Json(Catalogs.DisplaySettings(state.Snapshot), json.DisplaySettingsRef))
            .Answers<DisplaySettingsRef>("使う人の表示の設定");

        // 商品
        app.MapGet("/v1/items", (ApiState state, string? cursor, int? limit, string? sort, bool? includeHidden, bool? includeAdult) =>
            ApiHost.Json(
                SearchEngine.Search(state.Snapshot, new SearchRequest
                {
                    Cursor = cursor,
                    Limit = limit,
                    Sort = ParseSort(sort),
                    IncludeHidden = includeHidden ?? true,
                    IncludeAdult = includeAdult ?? true,
                }, state.Clock()),
                json.SearchPage))
            .Answers<SearchPage>("商品の一覧（要約）。sort は wishCount:desc,name の形");

        // どの URL も、写しは1回だけ取り、答えができるまでそれを使う（点検30：途中で差し替わると、古い商品と新しい登録簿を組み合わせる）
        app.MapGet("/v1/items/{id}", (ApiState state, string id, string? include) =>
        {
            var snapshot = state.Snapshot;
            return ApiHost.Json(ItemMapper.Detail(FindItem(snapshot, id), snapshot, Includes.ParseItem(include)), json.ItemDetail);
        })
            .Answers<ItemDetail>("商品1件（include で帳簿などを足す）");

        app.MapPost("/v1/items/batch", async (ApiState state, HttpRequest request) =>
        {
            var body = await ReadAsync(request, json.ItemsBatchRequest);
            var snapshot = state.Snapshot;
            var includes = Includes.ParseItem(body.Include);
            var ids = body.Ids ?? [];

            // 配列の中の null は入力の誤り（点検32：照らす所で落ち、内部の誤りとして 500 を返していた）
            if (ids.Any(id => id is null))
            {
                throw QueryException.Invalid("invalidIds", "ids に null は書けません。");
            }

            if (ids.Count > SearchEngine.MaxLimit)
            {
                throw QueryException.Invalid("invalidLimit", $"一度に引けるのは {SearchEngine.MaxLimit} 件までです。");
            }

            var found = ids.Where(snapshot.ItemsById.ContainsKey).Distinct(StringComparer.Ordinal)
                .Select(id => ItemMapper.Detail(snapshot.ItemsById[id], snapshot, includes)).ToList();
            var missing = ids.Where(id => !snapshot.ItemsById.ContainsKey(id)).ToList();
            return ApiHost.Json(new ItemsBatchAnswer(found, missing), json.ItemsBatchAnswer);
        })
            .Takes<ItemsBatchRequest>().Answers<ItemsBatchAnswer>("ID をまとめて詳細を引く");

        // ---- 書き込み（docs/history/headless-core-2026-10-13.md 段3。守りと始末は ApiWriter） ----

        app.MapPut("/v1/items/{id}/favorite", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.FlagRequest);
            using var claim = writer.GuardItem(context, id);

            // 書くのはお気に入りの欄だけ（ほかの欄は、保存の直前に読み直した今の値が残る）
            var command = new UiCommand.SaveItemLocal(id, new LocalBlock { IsFavorite = body.Value }, LocalOwners.Favorite);
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<FlagRequest>().Answers<ItemDetail>("お気に入りを付ける・外す（書き込み）");

        app.MapPut("/v1/items/{id}/hidden", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.FlagRequest);
            using var claim = writer.GuardItem(context, id);
            var command = new UiCommand.SaveItemLocal(id, new LocalBlock { IsHidden = body.Value }, LocalOwners.Visibility);
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<FlagRequest>().Answers<ItemDetail>("非表示にする・戻す（書き込み）");

        // 「使った」の足跡。外の道具から Unity へ送ったときなど（ユーザ判断 2026-10-13：「使った」だけ。「見た」は画面が打つ）
        app.MapPost("/v1/items/{id}/recent", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.RecentRequest);
            if (body.Kind != "used")
            {
                throw QueryException.Invalid("invalidKind", "kind に書けるのは used だけです。");
            }

            using var claim = writer.GuardItem(context, id, checkEditing: false);
            var command = new UiCommand.TouchRecent(id, Core.Services.RecentKind.Used, DateTimeOffset.Now);
            return await NoContentAfterWriteAsync(writer, command, "recent", [id], [state.Paths.RecentFile], context);
        })
            .Takes<RecentRequest>().Produces(204).WithSummary("「使った」の足跡を打つ（書き込み）");

        // ---- 分類（段3b。名前は一覧の綴りに寄せ、一覧に無い名前は createMissing=true のときだけ足す。ApiClassify） ----

        app.MapPut("/v1/items/{id}/user-tags", async (ApiState state, ApiWriter writer, HttpContext context, string id, bool? createMissing) =>
        {
            var body = await ReadAsync(context.Request, json.UserTagsRequest);
            using var claim = writer.GuardItem(context, id);
            var (tags, added) = await ApiClassify.ResolveTagsAsync(state.Store.UserTags.Load, writer, body.Tags ?? [], createMissing == true, context.RequestAborted);
            var command = new UiCommand.SaveItemLocal(id, new LocalBlock { UserTags = tags }, LocalOwners.UserTags);
            return await ItemAfterWriteAsync(state, writer, command, id, context, added ? [state.Paths.UserTagsFile] : []);
        })
            .Takes<UserTagsRequest>().Answers<ItemDetail>("ユーザータグを丸ごと置き換える（書き込み。?createMissing=true で一覧に無い名前も足す）");

        app.MapPost("/v1/items/{id}/user-tags", async (ApiState state, ApiWriter writer, HttpContext context, string id, bool? createMissing) =>
        {
            var body = await ReadAsync(context.Request, json.UserTagChangeRequest);
            using var claim = writer.GuardItem(context, id);
            var (tags, added) = await ApiClassify.ResolveTagsAsync(
                state.Store.UserTags.Load, writer, [new UserTagInput(body.Top, body.Sub is null ? null : [body.Sub])], createMissing == true, context.RequestAborted);
            var tag = tags[0];

            // 足し引きは錠の中で今の値に当てる（読んでから丸ごと書くと、間に書かれた分を消す）
            var command = new UiCommand.ChangeItemLocal(id, local => ApiClassify.AddTag(local, tag.Top, tag.Subs.FirstOrDefault()), LocalOwners.UserTags);
            return await ItemAfterWriteAsync(state, writer, command, id, context, added ? [state.Paths.UserTagsFile] : []);
        })
            .Takes<UserTagChangeRequest>().Answers<ItemDetail>("ユーザータグを1つ足す（書き込み）");

        app.MapPost("/v1/items/{id}/user-tags/remove", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.UserTagChangeRequest);
            if (string.IsNullOrWhiteSpace(body.Top))
            {
                throw QueryException.Invalid("invalidName", "top を書いてください。");
            }

            using var claim = writer.GuardItem(context, id);
            var command = new UiCommand.ChangeItemLocal(id, local => ApiClassify.RemoveTag(local, body.Top, body.Sub), LocalOwners.UserTags);
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<UserTagChangeRequest>().Answers<ItemDetail>("ユーザータグを1つ外す（小分類を省くと大分類ごと。書き込み）");

        app.MapPatch("/v1/items/{id}/attributes", async (ApiState state, ApiWriter writer, HttpContext context, string id, bool? createMissing) =>
        {
            var body = await ReadAsync(context.Request, json.AttributesRequest);
            using var claim = writer.GuardItem(context, id);
            var (values, added) = await ApiClassify.ResolveAttributesAsync(
                state.Store.Attributes.Load, writer, body.Values ?? new Dictionary<string, int?>(), createMissing == true, context.RequestAborted);
            var command = new UiCommand.ChangeItemLocal(id, local => ApiClassify.ApplyAttributes(local, values), LocalOwners.Attributes);
            return await ItemAfterWriteAsync(state, writer, command, id, context, added ? [state.Paths.AttributesFile] : []);
        })
            .Takes<AttributesRequest>().Answers<ItemDetail>("属性の値を書く（名指しした物だけ。null で外す。書き込み）");

        app.MapPut("/v1/items/{id}/memo", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.MemoRequest);
            using var claim = writer.GuardItem(context, id);
            var memo = string.IsNullOrWhiteSpace(body.Memo) ? null : body.Memo;
            var command = new UiCommand.SaveItemLocal(id, new LocalBlock { Memo = memo }, LocalOwners.ItemPageMemo);
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<MemoRequest>().Answers<ItemDetail>("メモを書く（null か空で消す。書き込み）");

        app.MapPost("/v1/user-tags", async (ApiState state, ApiWriter writer, HttpContext context) =>
        {
            var body = await ReadAsync(context.Request, json.UserTagChangeRequest);
            if (string.IsNullOrWhiteSpace(body.Top) || body.Sub is { } sub && string.IsNullOrWhiteSpace(sub))
            {
                throw QueryException.Invalid("invalidName", "top（と sub）を書いてください。");
            }

            writer.Guard(context);
            if (await writer.RunAsync(new UiCommand.AddUserTag(body.Top, body.Sub), "userTags", [], [state.Paths.UserTagsFile], context.RequestAborted) is { } busy)
            {
                return Results.Json(new AcceptedAnswer(busy), json.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted);
            }

            return ApiHost.Json(Catalogs.UserTags(state.Snapshot), json.IReadOnlyListUserTagTopRef);
        })
            .Takes<UserTagChangeRequest>().Answers<IReadOnlyList<UserTagTopRef>>("ユーザータグを一覧に足す（既にあれば足さない。書き込み）");

        app.MapPost("/v1/attributes", async (ApiState state, ApiWriter writer, HttpContext context) =>
        {
            var body = await ReadAsync(context.Request, json.NameRequest);
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                throw QueryException.Invalid("invalidName", "name を書いてください。");
            }

            writer.Guard(context);
            if (await writer.RunAsync(new UiCommand.AddAttribute(body.Name), "attributes", [], [state.Paths.AttributesFile], context.RequestAborted) is { } busy)
            {
                return Results.Json(new AcceptedAnswer(busy), json.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted);
            }

            return ApiHost.Json(Catalogs.Attributes(state.Snapshot), json.IReadOnlyListAttributeEntryRef);
        })
            .Takes<NameRequest>().Answers<IReadOnlyList<AttributeEntryRef>>("属性を一覧に足す（既にあれば足さない。書き込み）");

        // Unity へ送る（ユーザ判断 2026-10-13）。送り終えるまで待たず 202 で返し、終わったら変化の知らせ（unity）で伝える
        app.MapPost("/v1/items/{id}/send-to-unity", async (ApiState state, ApiWriter writer, ChangeFeed feed, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.UnitySendRequest);
            using var claim = writer.GuardItem(context, id, checkEditing: false);
            var unity = writer.Unity ?? throw new QueryException(503, "unityUnavailable", "この API は Unity へ送れません。");
            var (started, finished) = unity.Start(state.Snapshot.ItemsById[id], body);
            _ = finished.ContinueWith(
                task => feed.Publish("unity", [task.IsCompletedSuccessfully ? task.Result.State : "failed"], [id], null, state.Clock()),
                TaskScheduler.Default);
            return Results.Json(started, json.UnitySendStatus, statusCode: StatusCodes.Status202Accepted);
        })
            .Takes<UnitySendRequest>().Produces<UnitySendStatus>(202).WithSummary("Unity へ送る（書き込み。終わりは変化の知らせ unity と /v1/unity/last-send）");

        // 画面で開く（段4）。データは変えないが、使う人の画面を動かすので書き込みと同じ許しで守る（許可したオリジンのページが勝手に窓を出さないように）
        app.MapPost("/v1/items/{id}/show", async (ApiWriter writer, HttpContext context, string id) =>
        {
            using var claim = writer.GuardItem(context, id, checkEditing: false);
            var show = writer.ShowItem ?? throw new QueryException(503, "windowUnavailable", "この API は画面を開けません。");
            if (!await show(id))
            {
                throw new QueryException(503, "windowUnavailable", "Chmonos の画面を開けませんでした。");
            }

            Core.Diagnostics.AppLog.Note("外から使う API", "画面で開く");
            return Results.NoContent();
        })
            .Produces(204).WithSummary("Chmonos の画面でこの商品のページを表示する（画面無しで動いていれば窓を開く。書き込みの許しが要る）");

        // エクスプローラで開く（段4）。画面で開くと同じく、使う人の画面を動かすので書き込みの許しで守る。
        // 場所の選び方は Unity へ送ると同じ：指定があればそれ（その商品の場所に限る）、無ければ1つだけのときだけ
        app.MapPost("/v1/items/{id}/reveal", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.RevealRequest);
            using var claim = writer.GuardItem(context, id, checkEditing: false);
            var reveal = writer.Reveal ?? throw new QueryException(503, "revealUnavailable", "この API はエクスプローラで開けません。");
            var outcome = await reveal(state.Snapshot.ItemsById[id], body.Path);
            Core.Diagnostics.AppLog.Note("外から使う API", $"エクスプローラで開く → {outcome}");
            return outcome switch
            {
                "opened" => Results.NoContent(),
                "ambiguous" => throw QueryException.Invalid("locationAmbiguous", "この商品には場所が複数あります。path で開く場所を指定してください（商品の詳細の files と folders に場所があります）。"),
                "noLocation" => throw QueryException.NotFound("noLocation", "この商品には、手元のファイルもフォルダもありません。"),
                "unknownPath" => throw QueryException.NotFound("locationNotFound", "path はこの商品のファイルやフォルダの場所ではありません。"),
                _ => throw QueryException.NotFound("locationMissing", "記録にある場所に見つかりません。外付けのドライブなら、つないでからもう一度送ってください。"),
            };
        })
            .Takes<RevealRequest>().Produces(204).WithSummary("商品のファイルかフォルダをエクスプローラで開く（場所が1つなら path を省ける。書き込みの許しが要る）");

        MapCurationWrites(app);
        MapDescription(app);

        app.MapGet("/v1/unity/editors", (ApiWriter writer) => ApiHost.Json(writer.Unity?.OpenEditors() ?? [], json.IReadOnlyListUnityEditorRef))
            .Answers<IReadOnlyList<UnityEditorRef>>("開いている Unity のエディタ（送り先を選ぶため）");

        app.MapGet("/v1/unity/last-send", (ApiWriter writer) =>
            writer.Unity?.Last is { } last
                ? ApiHost.Json(last, json.UnitySendStatus)
                : throw QueryException.NotFound("noUnitySend", "API から Unity へ送ったことがありません。"))
            .Answers<UnitySendStatus>("最後に API から Unity へ送った結果");

        app.MapPut("/v1/notifications/{id}/read", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.FlagRequest);
            writer.Guard(context);
            if (state.Snapshot.Notifications.All(record => record.Id != id))
            {
                throw QueryException.NotFound("notificationNotFound", $"通知 {id} は見つかりません。");
            }

            return await NoContentAfterWriteAsync(
                writer, new UiCommand.SetNotificationRead(id, body.Value), "notifications", [id], [state.Paths.NotificationsFile], context);
        })
            .Takes<FlagRequest>().Produces(204).WithSummary("通知を既読・未読にする（書き込み）");

        app.MapPost("/v1/notifications/read", async (ApiState state, ApiWriter writer, HttpContext context) =>
        {
            var ids = NotificationIds(await ReadAsync(context.Request, json.IdsRequest));
            writer.Guard(context);
            return await NoContentAfterWriteAsync(
                writer, new UiCommand.MarkNotificationsRead(ids), "notifications", ids, [state.Paths.NotificationsFile], context);
        })
            .Takes<IdsRequest>().Produces(204).WithSummary("通知をまとめて既読にする（書き込み）");

        // 解消は消さずに印を付けるだけ（「見た」と「無かった」の区別を保つ）
        app.MapPost("/v1/notifications/resolve", async (ApiState state, ApiWriter writer, HttpContext context) =>
        {
            var ids = NotificationIds(await ReadAsync(context.Request, json.IdsRequest));
            writer.Guard(context);
            return await NoContentAfterWriteAsync(
                writer, new UiCommand.ResolveNotifications(ids), "notifications", ids, [state.Paths.NotificationsFile], context);
        })
            .Takes<IdsRequest>().Produces(204).WithSummary("通知をまとめて解消済みにする（書き込み）");

        app.MapGet("/v1/items/{id}/modifications", (ApiState state, string id, string? include) =>
        {
            var snapshot = state.Snapshot;
            FindItem(snapshot, id);
            return ApiHost.Json(Catalogs.ModificationsUsing(snapshot, id, Includes.ParseFlag(include, "detached")), json.IReadOnlyListModificationRef);
        })
            .Answers<IReadOnlyList<ModificationRef>>("その商品を使っている改変");

        app.MapGet("/v1/items/{id}/notifications", (ApiState state, string id) =>
        {
            var snapshot = state.Snapshot;
            FindItem(snapshot, id);
            return ApiHost.Json(Catalogs.Notifications(snapshot, id), json.IReadOnlyListNotificationRef);
        })
            .Answers<IReadOnlyList<NotificationRef>>("その商品の通知");

        // 検索
        app.MapPost("/v1/search", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(SearchEngine.Search(state.Snapshot, await ReadAsync(request, json.SearchRequest), state.Clock()), json.SearchPage))
            .Takes<SearchRequest>().Answers<SearchPage>("式で検索（and・or・not・text・condition）");

        app.MapGet("/v1/search/conditions", () => ApiHost.Json(Catalogs.ConditionKinds(), json.IReadOnlyListConditionKindRef))
            .Answers<IReadOnlyList<ConditionKindRef>>("条件の種類・値の形・選択肢・切り替えの既定");

        app.MapPost("/v1/search/facets", async (ApiState state, HttpRequest request) =>
            ApiHost.Json(SearchEngine.Facets(state.Snapshot, await ReadAsync(request, json.FacetsRequest), state.Clock()), json.FacetsAnswer))
            .Takes<FacetsRequest>().Answers<FacetsAnswer>("選択肢の件数（式に当たった商品の中で）");

        // アバター・分類・ショップ
        app.MapGet("/v1/avatars", (ApiState state) => ApiHost.Json(Catalogs.Avatars(state.Snapshot), json.IReadOnlyListAvatarEntryRef))
            .Answers<IReadOnlyList<AvatarEntryRef>>("アバターの登録簿");

        app.MapGet("/v1/avatars/{id}", (ApiState state, string id) =>
            ApiHost.Json(
                Catalogs.Avatars(state.Snapshot).FirstOrDefault(avatar => avatar.Id == id)
                    ?? throw QueryException.NotFound("avatarNotFound", $"登録簿にないアバターです: {id}"),
                json.AvatarEntryRef))
            .Answers<AvatarEntryRef>("アバター1体");

        app.MapGet("/v1/avatar-bases", (ApiState state, string? include) =>
            ApiHost.Json(Catalogs.AvatarBases(state.Snapshot, Includes.ParseFlag(include, "rejected")), json.IReadOnlyListAvatarBaseRef))
            .Answers<IReadOnlyList<AvatarBaseRef>>("共通素体（include=rejected で人が消した素体も）");

        app.MapGet("/v1/user-tags", (ApiState state) => ApiHost.Json(Catalogs.UserTags(state.Snapshot), json.IReadOnlyListUserTagTopRef))
            .Answers<IReadOnlyList<UserTagTopRef>>("ユーザータグと件数");
        app.MapGet("/v1/attributes", (ApiState state) => ApiHost.Json(Catalogs.Attributes(state.Snapshot), json.IReadOnlyListAttributeEntryRef))
            .Answers<IReadOnlyList<AttributeEntryRef>>("属性と評価した数");
        app.MapGet("/v1/categories", (ApiState state) => ApiHost.Json(Catalogs.Categories(state.Snapshot), json.IReadOnlyListCategoryEntryRef))
            .Answers<IReadOnlyList<CategoryEntryRef>>("カテゴリと件数");
        app.MapGet("/v1/booth-tags", (ApiState state) => ApiHost.Json(Catalogs.BoothTags(state.Snapshot), json.IReadOnlyListCountRef))
            .Answers<IReadOnlyList<CountRef>>("BOOTHタグと件数");
        app.MapGet("/v1/shops", (ApiState state) => ApiHost.Json(Catalogs.Shops(state.Snapshot), json.IReadOnlyListShopEntryRef))
            .Answers<IReadOnlyList<ShopEntryRef>>("ショップ（星・メモ・件数）");
        app.MapGet("/v1/shops/{key}", (ApiState state, string key) => ApiHost.Json(Catalogs.Shop(state.Snapshot, key), json.ShopEntryRef))
            .Answers<ShopEntryRef>("ショップ1つ");

        // ファイル・Unity
        app.MapGet("/v1/files/lookup", (ApiState state, string? path, string? hash) =>
            ApiHost.Json(Catalogs.LookupFiles(state.Snapshot, path, hash), json.IReadOnlyListFileOwnerRef))
            .Answers<IReadOnlyList<FileOwnerRef>>("ファイル・フォルダの場所かハッシュから商品を引く");

        app.MapGet("/v1/unitypackages", (ApiState state, string? avatar) =>
            ApiHost.Json(Catalogs.UnityPackages(state.Snapshot, avatar), json.IReadOnlyListUnityPackageEntryRef))
            .Answers<IReadOnlyList<UnityPackageEntryRef>>("手元の unitypackage（avatar で絞る）");

        // 改変・通知
        app.MapGet("/v1/modifications", (ApiState state, string? include) =>
            ApiHost.Json(Catalogs.Modifications(state.Snapshot, Includes.ParseFlag(include, "detached")), json.IReadOnlyListModificationRef))
            .Answers<IReadOnlyList<ModificationRef>>("改変");

        app.MapGet("/v1/modifications/{id}", (ApiState state, string id, string? include) =>
            ApiHost.Json(Catalogs.Modification(state.Snapshot, id, Includes.ParseFlag(include, "detached")), json.ModificationRef))
            .Answers<ModificationRef>("改変1つ");

        app.MapGet("/v1/unity-projects", (ApiState state) => ApiHost.Json(Catalogs.UnityProjects(state.Snapshot), json.IReadOnlyListUnityProjectRef))
            .Answers<IReadOnlyList<UnityProjectRef>>("Unity プロジェクトとその改変");
        app.MapGet("/v1/notifications", (ApiState state) => ApiHost.Json(Catalogs.Notifications(state.Snapshot, null), json.IReadOnlyListNotificationRef))
            .Answers<IReadOnlyList<NotificationRef>>("通知");

        // 画像の本体（ブラウザの画面のため）。返すのは写しが知っている画像の名前だけ（送られた名前で場所を組まない）
        app.MapGet("/v1/images/{itemId}/{name}", (ApiState state, string itemId, string name) =>
        {
            var snapshot = state.Snapshot;
            FindItem(snapshot, itemId);
            var file = (snapshot.ImageFiles.GetValueOrDefault(itemId) ?? [])
                .FirstOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
            return file is not null && File.Exists(file)
                ? Results.File(file, ContentTypeOf(file))
                : throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
        })
            .WithSummary("商品の画像の本体（写しが知っている名前だけ）").Produces(200, contentType: "image/webp").ProducesProblem(404);

        app.MapGet("/v1/modifications/{id}/images/{name}", (ApiState state, string id, string name) =>
        {
            var record = state.Snapshot.Modifications.Records.FirstOrDefault(record => record.Id == id)
                ?? throw QueryException.NotFound("modificationNotFound", $"改変が見つかりません: {id}");
            var image = record.Images.FirstOrDefault(image => string.Equals(image.FileName, name, StringComparison.OrdinalIgnoreCase))
                ?? throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
            var file = Path.Combine(state.Paths.ModificationImagesDir(record.Id), image.FileName);
            return SafeName().IsMatch(image.FileName) && File.Exists(file)
                ? Results.File(file, ContentTypeOf(file))
                : throw QueryException.NotFound("imageNotFound", $"画像が見つかりません: {name}");
        })
            .WithSummary("改変の画像の本体").Produces(200, contentType: "image/webp").ProducesProblem(404);
    }

    /// <summary>仕様書の印：答えの型（JSON）と、誤りの答え（problem+json）と、短い説明。</summary>
    private static RouteHandlerBuilder Answers<T>(this RouteHandlerBuilder builder, string summary)
        => builder.Produces<T>(StatusCodes.Status200OK, "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary(summary);

    /// <summary>仕様書の印：要求の本文の型（本文は自分で読むので、型を知らせる）。</summary>
    private static RouteHandlerBuilder Takes<T>(this RouteHandlerBuilder builder)
        where T : notnull
        => builder.Accepts<T>("application/json");

    /// <summary>SSE で、ほかの物が何も流れないときに送る印の間隔。間に入る物（プロキシ・ブラウザ）が、つながりを切られたと見なさないように。</summary>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 変化を SSE で流す。ブラウザの EventSource はヘッダ（トークン）を付けられないので、fetch で流れを読んでもらう（docs/spec/local-api.md「ブラウザから」）。
    /// つなぎ直すときは Last-Event-ID（<c>起動の名前-通番</c>）で続きから流す。持っていない分があれば reloaded を流し、全部を取り直してもらう。
    /// </summary>
    private static async Task StreamEventsAsync(HttpContext context, ChangeFeed feed)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        var aborted = context.RequestAborted;

        // 聞き始めと、その時点の通番は一緒に取る（点検30：別々に取ると、間に届いた変化を送り済みと見なして落とす）。
        // 取り直した分と重なった物は通番で落とす
        var (channel, sent) = feed.Listen();
        try
        {
            var last = context.Request.Headers["Last-Event-ID"].ToString();
            if (last.Length > 0)
            {
                var parts = last.Split('-', 2);
                var replay = parts.Length == 2 && parts[0] == feed.Epoch && long.TryParse(parts[1], out var lastSeq) ? feed.Since(lastSeq).Changes : null;
                if (replay is null)
                {
                    // reloaded の通番は送り済みの境と同じにする（今の通番を付けると、その後に小さい通番の変化を送り、
                    // 外の道具が控える Last-Event-ID が逆に戻った・点検31）。この通番より後の変化は待ち行列から続けて流れる
                    await WriteEventAsync(response, feed.Epoch, new ChangeEventRef(sent, "reloaded", [], [], null, DateTimeOffset.Now), aborted);
                }
                else
                {
                    foreach (var change in replay)
                    {
                        await WriteEventAsync(response, feed.Epoch, change, aborted);
                        sent = change.Seq;
                    }
                }
            }

            await response.Body.FlushAsync(aborted);
            while (!aborted.IsCancellationRequested)
            {
                var waiting = channel.Reader.WaitToReadAsync(aborted).AsTask();
                if (await Task.WhenAny(waiting, Task.Delay(Heartbeat, aborted)) != waiting)
                {
                    await response.WriteAsync(": ping\n\n", aborted);
                    await response.Body.FlushAsync(aborted);
                    continue;
                }

                if (!await waiting)
                {
                    return;
                }

                while (channel.Reader.TryRead(out var change))
                {
                    foreach (var outgoing in Outgoing(sent, change))
                    {
                        await WriteEventAsync(response, feed.Epoch, outgoing, aborted);
                    }

                    sent = Math.Max(sent, change.Seq);
                }

                await response.Body.FlushAsync(aborted);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            feed.Stop(channel);
        }
    }

    /// <summary>
    /// 送り済みの通番 <paramref name="sent"/> の後に、待ち行列から出た変化を流すときに送る物。送り済みなら何も送らない。
    /// **通番が飛んでいたら、先に reloaded を送る**（点検30）：受け手が遅れて待ち行列があふれ、古い変化が落ちた印。
    /// 黙って続けると、外の道具の写しが古いまま残る。
    /// </summary>
    internal static IReadOnlyList<ChangeEventRef> Outgoing(long sent, ChangeEventRef change)
        => change.Seq <= sent
            ? []
            : change.Seq > sent + 1
                ? [new ChangeEventRef(change.Seq - 1, "reloaded", [], [], null, change.At), change]
                : [change];

    private static Task WriteEventAsync(HttpResponse response, string epoch, ChangeEventRef change, CancellationToken cancellationToken)
        => response.WriteAsync(
            $"id: {epoch}-{change.Seq}\nevent: {change.Kind}\ndata: {JsonSerializer.Serialize(change, QueryJson.Context.ChangeEventRef)}\n\n",
            cancellationToken);

    private static Core.Models.ItemRecord FindItem(QuerySnapshot snapshot, string id)
        => snapshot.ItemsById.GetValueOrDefault(id) ?? throw QueryException.NotFound("itemNotFound", $"商品が見つかりません: {id}");

    /// <summary>
    /// 商品に書いた後の答え：読み直した写しの商品1件。まとめて書いている途中（取り込み）なら写しを差し替えないので 202 で「受け付けた」と返す
    /// </summary>
    /// <param name="alsoWritten">商品のほかに書いたファイル（一覧に足したときのマスタ）。読み直しに入れる。</param>
    private static async Task<IResult> ItemAfterWriteAsync(
        ApiState state, ApiWriter writer, UiCommand command, string itemId, HttpContext context, IReadOnlyList<string>? alsoWritten = null)
    {
        if (await writer.RunAsync(command, "items", [itemId], [state.Paths.ItemFile(itemId), .. alsoWritten ?? []], context.RequestAborted) is { } busy)
        {
            return Results.Json(new AcceptedAnswer(busy), QueryJson.Context.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted);
        }

        var snapshot = state.Snapshot;
        return ApiHost.Json(ItemMapper.Detail(FindItem(snapshot, itemId), snapshot, Includes.ParseItem(null)), QueryJson.Context.ItemDetail);
    }

    /// <summary>通知の ID の一覧（空・null の混じりは入力の誤り）。</summary>
    private static IReadOnlyList<string> NotificationIds(IdsRequest body)
    {
        if (body.Ids is not { Count: > 0 } ids || ids.Any(id => string.IsNullOrEmpty(id)))
        {
            throw QueryException.Invalid("invalidIds", "ids に通知の ID を1つ以上書いてください。");
        }

        return ids;
    }

    /// <summary>商品の記録の外（通知・足跡）に書いた後の答え：本文なし（204）。取り込みの途中なら 202。</summary>
    private static async Task<IResult> NoContentAfterWriteAsync(
        ApiWriter writer, UiCommand command, string kind, IReadOnlyList<string> ids, IReadOnlyList<string> files, HttpContext context)
        => await writer.RunAsync(command, kind, ids, files, context.RequestAborted) is { } busy
            ? Results.Json(new AcceptedAnswer(busy), QueryJson.Context.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted)
            : Results.NoContent();

    private static async Task<T> ReadAsync<T>(HttpRequest request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
        => await JsonSerializer.DeserializeAsync(request.Body, type)
            ?? throw QueryException.Invalid("invalidJson", "送られた JSON が空です。");

    /// <summary><c>sort=wishCount:desc,name</c> の形。</summary>
    private static IReadOnlyList<SortRequest>? ParseSort(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? null
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => part.Split(':', 2) is [var field, var order] ? new SortRequest(field, order) : new SortRequest(part))
                .ToList();

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };

    /// <summary>画像のファイル名として受ける形（区切り・<c>..</c> を含まない）。記録を手で書き換えられても保存先の外を返さない。</summary>
    [GeneratedRegex(@"\A[\w\-]+(\.[\w\-]+)*\z")]
    private static partial Regex SafeName();
}
