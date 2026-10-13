using Chmonos.Core.Commands;
using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query;

namespace Chmonos.Api;

/// <summary>
/// 対応アバターと改変の書き込み（段3c。ユーザ判断 2026-10-13：API は「資産を使う」ための一部を出す B。docs/spec/local-api.md「出す範囲」）。
///
/// 出すのは、人の選択をそのまま写す物と、取り返しのつく物だけ：
/// - 対応アバター・共通素体を名指しで足す・消す（消した印を残す。画面の「消したもの」から戻せる）・確認待ちを名指しで確認済みにする。まとめての確定は出さない（推定が人を介さず確定になる）
/// - 改変を作る・メモ・blueprint ID・Unity プロジェクト・使った物を足す・外す・戻す。改変を消す・名前を変える・使った物を完全に消すは画面に残す
/// 変え方は画面と同じ関数（<see cref="AvatarService.WithManualAvatarLink"/> など）を通し、「消した」「確認待ち」の扱いを揃える。
/// **書く前の確かめは保存先の今の記録で行う**（API の写しは取り込みの間は古いまま。写しで確かめると、取り込みの途中に 202 で受けた書き込みを
/// 続く要求が知らず、作った改変へ書けない・足した対応アバターを消せない・同じ名前の改変を重ねて作れた。点検38）。答えは今までどおり写しから作る
/// </summary>
internal static partial class ApiEndpoints
{
    private static void MapCurationWrites(WebApplication app)
    {
        var json = QueryJson.Context;

        // ---- 対応アバター（商品の記録に書くので、編集画面で開いている商品は断る） ----

        app.MapPost("/v1/items/{id}/avatars", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.AvatarLinkRequest);
            using var claim = writer.GuardItem(context, id);
            var avatar = RequireAvatar(state.Store, body.AvatarId);
            if (avatar.ItemId == id)
            {
                throw QueryException.Invalid("selfLink", "商品を自分自身の対応アバターにはできません。");
            }

            var name = AvatarNames.ShownName(avatar);
            var command = new UiCommand.ChangeAvatarLinks(id, Avatars: links => AvatarService.WithManualAvatarLink(links, avatar.ItemId, name));
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<AvatarLinkRequest>().Answers<ItemDetail>("対応アバターを手で足す（消した物・確認待ちの物は確認済みに戻す。書き込み）");

        app.MapPost("/v1/items/{id}/avatars/remove", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.AvatarLinkRequest);
            using var claim = writer.GuardItem(context, id);
            if (!(await CurrentItemAsync(state, id, context)).Local.Avatars.Any(link => link.AvatarItemId == body.AvatarId && !link.Rejected))
            {
                throw QueryException.NotFound("avatarNotLinked", "この商品にそのアバターは付いていません。");
            }

            var command = new UiCommand.ChangeAvatarLinks(id, Avatars: links => AvatarService.WithRejectedAvatar(links, body.AvatarId));
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<AvatarLinkRequest>().Answers<ItemDetail>("対応アバターを消す（次の検出でも戻らない印を残す。画面の「消したもの」から戻せる。書き込み）");

        app.MapPost("/v1/items/{id}/avatars/confirm", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.AvatarConfirmRequest);
            if (body.AvatarIds is not { Count: > 0 } ids || ids.Any(string.IsNullOrEmpty))
            {
                throw QueryException.Invalid("invalidIds", "avatarIds に確認済みにするアバターの ID を1つ以上書いてください。");
            }

            using var claim = writer.GuardItem(context, id);
            return await ItemAfterWriteAsync(state, writer, new UiCommand.ConfirmAvatars(id, ids), id, context);
        })
            .Takes<AvatarConfirmRequest>().Answers<ItemDetail>("確認待ちの対応アバターを、名指しした物だけ確認済みにする（書き込み）");

        app.MapPost("/v1/items/{id}/avatar-bases", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.BaseLinkRequest);
            using var claim = writer.GuardItem(context, id);
            var baseName = RequireBase(state.Store, body.BaseName);
            var command = new UiCommand.ChangeAvatarLinks(id, Bases: links => AvatarService.WithManualBaseLink(links, baseName));
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<BaseLinkRequest>().Answers<ItemDetail>("共通素体を手で足す（一覧にある素体だけ。書き込み）");

        app.MapPost("/v1/items/{id}/avatar-bases/remove", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.BaseLinkRequest);
            using var claim = writer.GuardItem(context, id);
            if (!(await CurrentItemAsync(state, id, context)).Local.AvatarBases.Any(link =>
                    string.Equals(link.BaseName, body.BaseName?.Trim(), StringComparison.CurrentCultureIgnoreCase) && !link.Rejected))
            {
                throw QueryException.NotFound("baseNotLinked", "この商品にその共通素体は付いていません。");
            }

            var command = new UiCommand.ChangeAvatarLinks(id, Bases: links => AvatarService.WithRejectedBase(links, body.BaseName!.Trim()));
            return await ItemAfterWriteAsync(state, writer, command, id, context);
        })
            .Takes<BaseLinkRequest>().Answers<ItemDetail>("共通素体を消す（消した印を残す。書き込み）");

        // ---- 改変（改変の画面は欄ごとに書くので、編集画面のような札は要らない） ----

        app.MapPost("/v1/modifications", async (ApiState state, ApiWriter writer, HttpContext context) =>
        {
            var body = await ReadAsync(context.Request, json.ModificationCreateRequest);
            writer.Guard(context);

            // アバターに限る。改変を作る命令は、登録簿に無い商品をその場でアバターとして足すので、帽子などを渡すと登録簿が汚れる
            var avatar = RequireAvatar(state.Store, body.AvatarId);
            var name = body.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                throw QueryException.Invalid("invalidName", "name を書いてください。");
            }

            // 同じ名前の確かめと作るのを、API の作る要求どうしで1つずつにする（確かめた後に別の要求が同じ名前で作らないように。
            // 作る命令そのものは同じ名前を許す：画面は知らせてから作らせる）
            await CreatingModification.WaitAsync(context.RequestAborted);
            try
            {
                var existing = await state.Store.Modifications.LoadAllAsync(context.RequestAborted);
                if (existing.Modifications.Any(record =>
                        record.AvatarItemId == avatar.ItemId && string.Equals(record.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                {
                    throw new QueryException(409, "modificationNameTaken", "このアバターに同じ名前の改変が既にあります。");
                }

                var (busy, result) = await writer.RunWithResultAsync(
                    new UiCommand.CreateModification(avatar.ItemId, name),
                    "modifications",
                    result => result is CommandResult.ModificationCreated created
                        ? ([created.Record.Id], [state.Paths.ModificationFile(created.Record.Id)])
                        : ([], []),
                    context.RequestAborted);
                if (result is not CommandResult.ModificationCreated made)
                {
                    throw new QueryException(500, "commandFailed", "改変を作れませんでした。");
                }

                // 取り込みの途中でも ID は決まっているので、受け付けた答えにも場所を付ける
                context.Response.Headers.Location = $"/v1/modifications/{made.Record.Id}";
                return busy is not null
                    ? Results.Json(new AcceptedAnswer(busy), json.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted)
                    : Results.Json(Catalogs.Modification(state.Snapshot, made.Record.Id), json.ModificationRef, statusCode: StatusCodes.Status201Created);
            }
            finally
            {
                CreatingModification.Release();
            }
        })
            .Takes<ModificationCreateRequest>().Produces<ModificationRef>(201).WithSummary("改変を作る（アバター1体＋名前。書き込み）");

        app.MapPut("/v1/modifications/{id}/memo", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.MemoRequest);
            await GuardModificationAsync(writer, state, context, id);
            var memo = string.IsNullOrWhiteSpace(body.Memo) ? null : body.Memo;
            return await ModificationAfterWriteAsync(state, writer, new UiCommand.SetModificationMemo(id, memo), id, context);
        })
            .Takes<MemoRequest>().Answers<ModificationRef>("改変のメモを書く（null か空で消す。書き込み）");

        app.MapPut("/v1/modifications/{id}/blueprint-id", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.BlueprintIdRequest);
            await GuardModificationAsync(writer, state, context, id);

            // 画面は形が違っても保存して注意を出す（人が貼った物を捨てない）。道具が送る値は形を確かめて断る
            var blueprint = string.IsNullOrWhiteSpace(body.BlueprintId) ? null : body.BlueprintId.Trim();
            if (blueprint is not null && !VrcOsc.LooksLikeAvatarId(blueprint))
            {
                throw QueryException.Invalid("invalidBlueprintId", "blueprintId は「avtr_」で始まる VRChat のアバターの ID です。");
            }

            return await ModificationAfterWriteAsync(state, writer, new UiCommand.SetModificationBlueprintId(id, blueprint), id, context);
        })
            .Takes<BlueprintIdRequest>().Answers<ModificationRef>("改変の blueprint ID を書く（null で外す。書き込み）");

        app.MapPut("/v1/modifications/{id}/unity-project", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.UnityProjectRequest);
            await GuardModificationAsync(writer, state, context, id);
            var path = string.IsNullOrWhiteSpace(body.Path) ? null : Path.TrimEndingDirectorySeparator(body.Path.Trim());
            if (path is not null && (!Path.IsPathFullyQualified(path) || !Directory.Exists(Path.Combine(path, "Assets"))))
            {
                throw QueryException.Invalid("notUnityProject", "path には Unity プロジェクトのフォルダ（中に Assets がある所）を完全なパスで書いてください。");
            }

            return await ModificationAfterWriteAsync(state, writer, new UiCommand.SetModificationProject(id, path), id, context);
        })
            .Takes<UnityProjectRequest>().Answers<ModificationRef>("改変に Unity プロジェクトを結ぶ（null で外す。書き込み）");

        app.MapPost("/v1/modifications/{id}/members", async (ApiState state, ApiWriter writer, HttpContext context, string id) =>
        {
            var body = await ReadAsync(context.Request, json.ModificationMemberRequest);
            await GuardModificationAsync(writer, state, context, id);
            if (string.IsNullOrEmpty(body.ItemId) || !state.Snapshot.ItemsById.TryGetValue(body.ItemId, out var item))
            {
                throw QueryException.NotFound("itemNotFound", $"商品 {body.ItemId} は見つかりません。");
            }

            // どのファイルを使ったかは、分かるときだけ書く（推定で埋めない。分からなければ省く）。書くなら、その商品のファイルに限る
            if (body.FileHash is { Length: > 0 } hash
                && !item.Local.LocalFiles.Any(file => string.Equals(file.Hash, hash, StringComparison.OrdinalIgnoreCase)))
            {
                throw QueryException.Invalid("unknownFile", "fileHash はこの商品のファイルのハッシュではありません。");
            }

            var member = new ModificationMember
            {
                ItemId = body.ItemId,
                VariationId = body.VariationId,
                FileHash = string.IsNullOrEmpty(body.FileHash) ? null : body.FileHash,
                Package = string.IsNullOrWhiteSpace(body.Package) ? null : body.Package,
                AddedAt = DateTimeOffset.Now,
            };
            return await ModificationAfterWriteAsync(state, writer, new UiCommand.AddModificationMember(id, member), id, context);
        })
            .Takes<ModificationMemberRequest>().Answers<ModificationRef>("改変に使った物を足す（末尾に付く。書き込み）");

        app.MapPost("/v1/modifications/{id}/members/detach", (ApiState state, ApiWriter writer, HttpContext context, string id)
            => SetMemberDetachedAsync(state, writer, context, id, detached: true))
            .Takes<ModificationMemberRequest>().Answers<ModificationRef>("使った物を外す（行と記録は残り、戻せる。書き込み）");

        app.MapPost("/v1/modifications/{id}/members/restore", (ApiState state, ApiWriter writer, HttpContext context, string id)
            => SetMemberDetachedAsync(state, writer, context, id, detached: false))
            .Takes<ModificationMemberRequest>().Answers<ModificationRef>("外した物を戻す（書き込み）");
    }

    /// <summary>API の改変を作る要求を1つずつにする（同じ名前の確かめと作るの間に、別の要求が入らないように）。</summary>
    private static readonly SemaphoreSlim CreatingModification = new(1, 1);

    /// <summary>保存先の今の商品（書く前の確かめに使う。写しは取り込みの間は古い）。</summary>
    private static async Task<ItemRecord> CurrentItemAsync(ApiState state, string id, HttpContext context)
        => await state.Store.Items.LoadAsync(id, context.RequestAborted)
            ?? throw QueryException.NotFound("itemNotFound", $"商品 {id} は見つかりません。");

    /// <summary>アバターとして扱っている登録簿の項目（無い・アバターでなければ 400）。保存先の今の登録簿で見る。</summary>
    private static AvatarRegistryEntry RequireAvatar(Core.Storage.DataStore store, string? avatarId)
        => store.Avatars.Load().Entries.FirstOrDefault(entry => entry.ItemId == avatarId && AvatarService.IsAvatar(entry))
            ?? throw QueryException.Invalid("unknownAvatar", "avatarId はアバターの管理にあるアバターの ID ではありません。/v1/avatars で確かめてください。");

    /// <summary>一覧にある共通素体の名前（一覧の綴りに寄せる。無ければ 400）。</summary>
    private static string RequireBase(Core.Storage.DataStore store, string? baseName)
        => store.Avatars.Load().BaseGroups.FirstOrDefault(group => string.Equals(group.Name, baseName?.Trim(), StringComparison.CurrentCultureIgnoreCase))?.Name
            ?? throw QueryException.Invalid("unknownBase", "baseName は共通素体の一覧にありません。/v1/avatar-bases で確かめてください。");

    /// <summary>書いてよいかと、改変が在るか（保存先の今の記録で見る。取り込みの途中に作った改変にも書けるように）。</summary>
    private static async Task<ModificationRecord> GuardModificationAsync(ApiWriter writer, ApiState state, HttpContext context, string id)
    {
        writer.Guard(context);
        ModificationRecord? record = null;
        try
        {
            record = await state.Store.Modifications.LoadAsync(id, context.RequestAborted);
        }
        catch (ArgumentException)
        {
            // 改変の ID の形でない（ファイルの名前にできない）。無いのと同じに答える
        }

        return record ?? throw QueryException.NotFound("modificationNotFound", $"改変 {id} は見つかりません。");
    }

    private static async Task<IResult> SetMemberDetachedAsync(ApiState state, ApiWriter writer, HttpContext context, string id, bool detached)
    {
        var body = await ReadAsync(context.Request, QueryJson.Context.ModificationMemberRequest);
        // 行は保存先の今の記録から選ぶ（命令は行そのもので指す。写しは取り込みの間は古い）
        var record = await GuardModificationAsync(writer, state, context, id);
        var matches = record.Members
            .Where(member => member.ItemId == body.ItemId && member.Detached != detached
                && (body.FileHash is null || string.Equals(member.FileHash, body.FileHash, StringComparison.OrdinalIgnoreCase))
                && (body.Package is null || string.Equals(member.Package, body.Package, StringComparison.OrdinalIgnoreCase))
                && (body.AddedAt is null || member.AddedAt == body.AddedAt))
            .ToList();
        var member = matches.Count switch
        {
            0 => throw QueryException.NotFound("memberNotFound", detached ? "外せる行が見つかりません。" : "戻せる行が見つかりません。"),
            1 => matches[0],
            _ => throw QueryException.Invalid("memberAmbiguous", "当てはまる行が複数あります。fileHash・package・addedAt で絞ってください。"),
        };

        return await ModificationAfterWriteAsync(state, writer, new UiCommand.SetModificationMemberDetached(id, member, detached), id, context);
    }

    /// <summary>改変に書いた後の答え：その改変（外した行も含める。外した・戻したが見えるように）。取り込みの途中なら 202。</summary>
    private static async Task<IResult> ModificationAfterWriteAsync(ApiState state, ApiWriter writer, UiCommand command, string id, HttpContext context)
    {
        if (await writer.RunAsync(command, "modifications", [id], [state.Paths.ModificationFile(id)], context.RequestAborted) is { } busy)
        {
            return Results.Json(new AcceptedAnswer(busy), QueryJson.Context.AcceptedAnswer, statusCode: StatusCodes.Status202Accepted);
        }

        return ApiHost.Json(Catalogs.Modification(state.Snapshot, id, includeDetached: true), QueryJson.Context.ModificationRef);
    }
}
