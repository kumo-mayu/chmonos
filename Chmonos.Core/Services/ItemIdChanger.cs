using Chmonos.Core.Booth;
using Chmonos.Core.Diagnostics;
using Chmonos.Core.Images;
using Chmonos.Core.Models;
using Chmonos.Core.Scanning;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// 商品IDの付け替え（仮IDから BOOTH の ID へ、別の ID へ）と、途中で止まった付け替えの再開。
///
/// ItemService から分けた（点検24・ユーザ判断 2026-10-08「クラス分けは進めてくれ」）。点検26の案どおり、商品の錠2つ・画像の写し・
/// 指紋・途中の操作の記録・参照の付け替え・再開を、ばらさずに1つの担当に置く。移す先を読み、合わせてから元を消す一連の処理（<c>MoveAwayAsync</c>）は、
/// 錠の中のまま。BOOTH から取ってくる部分（<c>FetchNewItemAsync</c>）と、空の商品・非公開の商品の作り方は ItemService のものを呼ぶ
/// </summary>
internal sealed class ItemIdChanger(DataStore store, IBoothClient client, ItemService owner)
{
    /// <summary>
    /// IDを変更したら何が起きるかの下見。**書き込まない。**
    ///
    /// 移した先が手元に無ければBOOTHへ聞きに行く（①②の2本）。
    /// **取れなくても止めない**——非公開の商品へ寄せることもあるので、
    /// 「見つかりませんが、このIDで登録しますか」と聞ける形にする。
    /// </summary>
    public async Task<ItemIdChangePlan?> PlanItemIdChangeAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default)
    {
        var source = await store.Items.LoadAsync(fromId, cancellationToken);
        if (source is null || string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return null;
        }

        var target = await store.Items.LoadAsync(toId, cancellationToken);
        if (target is not null)
        {
            // 既に手元にあるなら聞きに行かない。通信を増やさない
            return ItemIdChange.Plan(source, target, toId, ItemIdTargetStatus.Found);
        }

        // 仮IDへ移すことはない（BOOTHに無いIDへ寄せる意味がない）ので、そこは聞きに行かない
        if (LocalItemId.IsLocal(toId))
        {
            return ItemIdChange.Plan(source, target: null, toId, ItemIdTargetStatus.NotFound);
        }

        // 一時的に届かないのを「BOOTHにある」と読まない。窓が「移すときに取得します」と言い切ってしまう（点検 2026-09-29・19）
        var onBooth = (await client.GetItemJsonAsync(toId, cancellationToken)).Status switch
        {
            BoothFetchStatus.Success => ItemIdTargetStatus.Found,
            BoothFetchStatus.NotFound => ItemIdTargetStatus.NotFound,
            _ => ItemIdTargetStatus.Unknown,
        };

        return ItemIdChange.Plan(source, target: null, toId, onBooth);
    }

    /// <summary>
    /// 商品まるごとを別のIDへ移す。
    ///
    /// **IDは書き換えない。**新しいIDの商品へ中身を移し、元の商品を消す。
    /// 商品IDはファイル名にもフォルダ名にもなっていて、他の商品からも名前で
    /// 参照されているので、IDだけ書き換えると参照が全部迷子になる。
    ///
    /// 移した先が手元に無ければ、BOOTHから取って作る。取れなければ
    /// **中身が空の商品として作る**——買って手元にあるものを、
    /// 移し先が非公開だという理由で消してはいけない。
    /// </summary>
    public async Task<ItemIdChangeOutcome> ChangeItemIdAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        CancellationToken cancellationToken = default,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null)
    {
        if (string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return ItemIdChangeOutcome.SameId;
        }

        // ID は場所の名前になる（items/{id}.json・images/{id}/）。やりかけの記録（pending-operations.json）は手で直せるので、
        // 形の外れた ID では何も書かず・消さず、参照も付け替えない（StoreIds）
        if (!StoreIds.IsItemId(fromId) || !StoreIds.IsItemId(toId))
        {
            return ItemIdChangeOutcome.TargetUnavailable;
        }

        if (await store.Items.LoadAsync(fromId, cancellationToken) is not { } before)
        {
            return ItemIdChangeOutcome.SourceMissing;
        }

        // 始めた時の元の指紋を、何かを書く前に記録へ残す。BOOTH から取っている間に落ちても、続きで
        // 「元が記録した時のままか」を見られる（同じIDで作り直された物に当てないため。OperationFingerprint）
        // 元のIDを指している参照も、ここで記録へ残す。続きはこれに載った物だけを書き換える（ItemIdReferences）
        if (record is not null
            && !await record(new ItemIdChangeFingerprints(
                OperationFingerprint.Of(before.Local),
                References: await CollectReferencesAsync(fromId, cancellationToken))))
        {
            return ItemIdChangeOutcome.NotRecorded;
        }

        // 自分で足した画像のファイルは、何かを書く前に移した先のフォルダへ写す。
        // 元の商品を消すと画像のフォルダごと消えるので、写せなかったら何も書かずに元を残す。
        // BOOTH から取るより前に行うのは、写せなかったときに取ってきた空の商品を残さないため
        if (CopyUserImages(fromId, toId, before.Local.UserImages) is not { } copied)
        {
            return ItemIdChangeOutcome.ImagesNotMoved;
        }

        var prepared = await store.Items.LoadAsync(toId, cancellationToken);
        var fetchStatus = Booth.BoothFetchStatus.Success;
        if (prepared is null)
        {
            (prepared, fetchStatus) = await owner.FetchNewItemAsync(toId, cancellationToken);
        }

        // BOOTHが「無い」と答えたIDは、未確定の「見つからないIDのまま登録」と同じ状態で作る（⑦で確かめ直し、公開されたら情報を取る）。
        // 一時的に届かなかっただけのIDに販売終了の印を付けると、統計や検索で販売終了として数えてしまうので空の商品のまま。
        // 仮IDは BOOTH に存在しないので⑦に乗せない
        // 一時的に届かなかったIDは、販売終了の印を付けず、予定日を今にして次の⑦で取りに行く（ユーザ判断 2026-09-29）。
        // 予定日を持たない空の商品は⑦に乗らず、後から情報を取りに行かなかった
        ItemRecord NewItem() => LocalItemId.IsLocal(toId)
            ? ItemService.EmptyItem(toId)
            : fetchStatus switch
            {
                Booth.BoothFetchStatus.NotFound => owner.UnpublishedItem(toId, null),
                Booth.BoothFetchStatus.TemporaryFailure => ItemService.EmptyItem(toId) is var empty
                    ? empty with { Local = empty.Local with { NextFetchDueAt = DateTimeOffset.Now } }
                    : empty,
                _ => ItemService.EmptyItem(toId),
            };

        var skipped = skippedPurchases ?? new HashSet<int>();
        var refused = ItemIdChangeOutcome.TargetUnavailable;

        // **移す元の錠を持ったまま、今の値を読み、移し、消す**（ItemRepository.MoveAwayAsync）。
        // BOOTH から取って作ると数秒かかり、その間に取り込みが元の商品へファイルを足したり、人がメモを書いたり画像を足したりする。
        // 前は錠の外で読み直してから消したので、読み直してから消すまでに書かれた分が、元の商品と一緒に消えていた
        var moved = await store.Items.MoveAwayAsync(
            fromId,
            async source =>
            {
                // 取っている間に足された画像の分（写し済みの物は飛ばされる）
                if (CopyUserImages(fromId, toId, source.Local.UserImages) is not { } late)
                {
                    refused = ItemIdChangeOutcome.ImagesNotMoved;
                    return false;
                }

                copied.AddRange(late);

                // **合わせるのは錠の中で読み直した今の値**（技術的負債 1-5）。
                // 移すのは手元の記録の全部なので、全項目の持ち主として書く。
                // 購入記録は移した先のvariation一覧で照合し直される（保存側）。指していない記録は支出にそのまま数える
                LocalBlock? Merge(LocalBlock current) => ItemIdChange.Merge(source.Local, current, skipped);
                var beforeWrite = RecordBeforeMerge(source.Local, record, () => refused = ItemIdChangeOutcome.NotRecorded);

                // 手元にも BOOTH にも無ければ新しく作る。在るかは移す先の錠の中で見る——取れなかった間に、
                // 取り込みが同じIDの商品を作っていることがある（L13 と同じ形）。在ればそちらへ重ねる。
                // 取って作った物が取った後で消されていれば、作り直さずに断る（元は消さずに残す）
                return prepared is null
                    ? await store.Items.CreateOrChangeLocalAsync(
                        toId, NewItem, Merge, Enum.GetValues<LocalField>(), cancellationToken, beforeWrite: beforeWrite)
                    : await store.Items.ChangeLocalAsync(
                        toId, Merge, Enum.GetValues<LocalField>(), cancellationToken, beforeWrite);
            },
            cancellationToken);

        if (moved is not true)
        {
            // 元は残っている（または初めから無い）。写した画像は片付ける
            DeleteQuietly(copied);
            return moved is null ? ItemIdChangeOutcome.SourceMissing : refused;
        }

        // 元の商品は移し終えた後で消えている。ここまでで落ちても、中身は移した先に残っている
        // （両方に出るのは二重に見えるが、消えてしまうよりはるかによい）

        // 外した印はファイルの行と一緒に移した先へ移っている（ItemIdChange.Merge）。
        // 以前は別の detached.json をここで読み替えていた

        await MoveModificationsAsync(fromId, toId, cancellationToken);
        await MoveReferencesAsync(fromId, toId, cancellationToken);

        return ItemIdChangeOutcome.Moved;
    }

    /// <summary>
    /// 途中で止まった IDの変更の続き（やりかけの記録が次の起動で残っていたとき）。**BOOTHへは問い合わせない。**
    ///
    /// 段は ①移す先へ書く → ②元を消す → ③改変 → ④ほかの参照。どこまで済んだかは、記録した指紋（<see cref="OperationFingerprint"/>）で読む。
    /// 前は手元の様子だけで読み、移す先の購入記録を値で照らして「合わせ済み」と見たので、操作の前から同じ値の購入を
    /// 持っていた移す先で元の購入が1件落ち、元の商品も消えていた。
    /// - 指紋が1つも無い：何も書く前に止まった（記録を消すだけ）
    /// - 合わせた後の指紋が無い：移す先へはまだ何も書いていない。元が無いのは人が消した物なので、何も当てない。
    ///   元が記録した時のままで移す先があれば、ふつうに合わせる
    /// - 元が無い（合わせた後の指紋はある）：①②は済んでいるので③④だけ当てる
    /// - 元が記録した時と違う：人が触ったか、同じIDで作り直した物。当てない
    /// - 移す先が合わせた後の指紋のまま：①は済んでいる。合わせ直さずに元を消す
    /// - 移す先が合わせる前の指紋のまま：①はまだ。ふつうに合わせる
    /// - 移す先がどちらとも違う：人が触った。当てない
    /// ③④は「古いIDを新しいIDへ」なので、何回当てても同じ。当てるのは始めた時に記録した参照だけ（<see cref="ItemIdReferences"/>）。
    /// </summary>
    public async Task<ItemIdChangeOutcome> ResumeItemIdChangeAsync(
        string fromId,
        string toId,
        IReadOnlySet<int>? skippedPurchases = null,
        ItemIdChangeFingerprints? recorded = null,
        Func<ItemIdChangeFingerprints, Task<bool>>? record = null,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(fromId, toId, StringComparison.Ordinal))
        {
            return ItemIdChangeOutcome.SameId;
        }

        // ID は場所の名前になる（items/{id}.json・images/{id}/）。やりかけの記録（pending-operations.json）は手で直せるので、
        // 形の外れた ID では何も書かず・消さず、参照も付け替えない（StoreIds）
        if (!StoreIds.IsItemId(fromId) || !StoreIds.IsItemId(toId))
        {
            return ItemIdChangeOutcome.TargetUnavailable;
        }

        if (recorded is null)
        {
            return ItemIdChangeOutcome.NotStarted;
        }

        var written = recorded.Merged is not null;
        if (await store.Items.LoadAsync(fromId, cancellationToken) is null)
        {
            if (!written)
            {
                return ItemIdChangeOutcome.NotStarted;
            }
        }
        else
        {
            if (await store.Items.LoadAsync(toId, cancellationToken) is null)
            {
                return ItemIdChangeOutcome.NotStarted;
            }

            var skipped = skippedPurchases ?? new HashSet<int>();
            var refused = ItemIdChangeOutcome.TargetUnavailable;
            var copied = new List<string>();
            var moved = await store.Items.MoveAwayAsync(
                fromId,
                async source =>
                {
                    // 元の錠の中で見る。ここから消すまで、ほかの書き手は元に触れない
                    if (OperationFingerprint.Of(source.Local) != recorded.Source)
                    {
                        refused = ItemIdChangeOutcome.ChangedSinceStarted;
                        return false;
                    }

                    // 写し済みの画像は飛ばされる（名前が中身のハッシュなので、同じ名前は同じ絵）
                    if (CopyUserImages(fromId, toId, source.Local.UserImages) is not { } late)
                    {
                        refused = ItemIdChangeOutcome.ImagesNotMoved;
                        return false;
                    }

                    copied.AddRange(late);

                    // 移す先が合わせ済みかは、移す先の錠の中（合わせ直しと同じ所）で見る
                    var applied = false;
                    LocalBlock? MergeIfNotYet(LocalBlock current)
                    {
                        var now = OperationFingerprint.Of(current);
                        if (written && now == recorded.Merged)
                        {
                            applied = true;
                            return null;
                        }

                        if (written && now != recorded.Target)
                        {
                            refused = ItemIdChangeOutcome.ChangedSinceStarted;
                            return null;
                        }

                        return ItemIdChange.Merge(source.Local, current, skipped);
                    }

                    var merged = await store.Items.ChangeLocalAsync(
                        toId,
                        MergeIfNotYet,
                        Enum.GetValues<LocalField>(),
                        cancellationToken,
                        RecordBeforeMerge(source.Local, record, () => refused = ItemIdChangeOutcome.NotRecorded));
                    return applied || merged;
                },
                cancellationToken);

            if (moved is false)
            {
                DeleteQuietly(copied);
                return refused;
            }
        }

        // 参照は始めた時に記録した物だけを書き換える。止まった後に元のIDで登録し直した持っていないアバターや、
        // その後に作った改変は、記録に無いので触らない（外部の点検 2026-10-06・L110）
        var recordedReferences = recorded.References ?? new ItemIdReferences();
        await MoveModificationsAsync(fromId, toId, cancellationToken, recordedReferences);
        await MoveReferencesAsync(fromId, toId, cancellationToken, recordedReferences);
        return ItemIdChangeOutcome.Moved;
    }

    /// <summary>
    /// 元のIDを指している、商品の JSON の外の参照を集める（IDの変更を始めた時に記録へ書く）。読むだけ。
    /// </summary>
    private async Task<ItemIdReferences> CollectReferencesAsync(string fromId, CancellationToken cancellationToken)
    {
        var modifications = await store.Modifications.LoadAllAsync(cancellationToken);
        var registry = await store.Avatars.LoadAsync(cancellationToken);
        var items = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        return new ItemIdReferences
        {
            Modifications = [.. modifications.Modifications
                .Where(record => UsesItem(record, fromId))
                .Select(record => record.Id)
                .Order(StringComparer.Ordinal)],
            RegistryEntry = registry.Entries.Any(entry => entry.ItemId == fromId),
            BaseGroups = [.. registry.BaseGroups.Where(group => group.ItemId == fromId).Select(group => group.Name)],
            LinkingItems = [.. items.Items
                .Where(item => item.Local.Avatars.Any(link => link.AvatarItemId == fromId))
                .Select(item => item.Id)
                .Order(StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// 移す先へ書く直前（移す先の錠の中）に、元・合わせる前・合わせた後の指紋を記録へ書く。書けなければ書かずに断る——
    /// 合わせた後の指紋が記録に無いまま移す先へ書くと、続きが「まだ合わせていない」と読み、もう一度合わせて購入記録が2回分になる。
    /// </summary>
    private static Func<LocalBlock, LocalBlock, Task<bool>>? RecordBeforeMerge(
        LocalBlock source,
        Func<ItemIdChangeFingerprints, Task<bool>>? record,
        Action refusedForRecord)
    {
        if (record is null)
        {
            return null;
        }

        return async (before, after) =>
        {
            var fingerprints = new ItemIdChangeFingerprints(
                OperationFingerprint.Of(source),
                OperationFingerprint.Of(before),
                OperationFingerprint.Of(after));
            if (await record(fingerprints))
            {
                return true;
            }

            refusedForRecord();
            return false;
        };
    }

    /// <summary>
    /// 自分で足した画像のファイルを、移した先の画像のフォルダへ写す（元は元の商品と一緒に消える）。
    ///
    /// **名前がぶつかったら写さない。**保存名は中身のハッシュなので、同じ名前なら同じ絵で、
    /// 移した先の記録と1枚にまとまる（<see cref="ItemIdChange.Merge"/>）。
    /// 元のファイルが既に無い記録はそのまま移す（元の商品でも絵は出ていなかったので、失う物は無い）。
    /// </summary>
    /// <returns>新しく写したファイル。写せない物があれば、写した分を片付けて null。</returns>
    private List<string>? CopyUserImages(string fromId, string toId, IReadOnlyList<UserImage> images)
    {
        var fromDir = store.Paths.ItemImagesDir(fromId);
        var toDir = store.Paths.ItemImagesDir(toId);
        var copied = new List<string>();

        try
        {
            foreach (var image in images)
            {
                var name = Path.GetFileName(image.FileName);
                var from = Path.Combine(fromDir, name);
                var to = Path.Combine(toDir, name);
                if (!File.Exists(from) || File.Exists(to))
                {
                    continue;
                }

                Directory.CreateDirectory(toDir);
                File.Copy(from, to);
                copied.Add(to);
            }

            return copied;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("IDの変更で自分で足した画像を写す", exception);
            DeleteQuietly(copied);
            return null;
        }
    }

    private static void DeleteQuietly(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // 片付けられなかった写しは、どの記録からも指されない1枚が残るだけ。移し替えの結果は変わらない
                AppLog.Error("IDの変更で写した画像を片付ける", exception);
            }
        }
    }

    /// <summary>
    /// 商品IDを指している他の記録を、移した先へ付け替える（ユーザ判断 2026-09-21・L16）。
    ///
    /// 改変だけを読み替えていたので、**登録簿・他の商品の対応アバター・足跡・要確認が
    /// 消えたIDを指したまま**になっていた（アバターの一覧から消える、持っていない扱いになる、
    /// 他の商品の対応アバターが迷子になる）。
    /// </summary>
    /// <param name="only">
    /// 続きのとき、始めた時に記録した参照（<see cref="ItemIdReferences"/>）。載った物だけを書き換える。null なら今の様子の全部（初めの1回）。
    /// </param>
    private async Task MoveReferencesAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken,
        ItemIdReferences? only = null)
    {
        // 登録簿（アバターそのもの・素体グループが指す商品）
        await store.Avatars.TryUpdateAsync(
            registry =>
            {
                // 続きでは、移す先の行が既にあれば元のIDの行は後から登録し直した物（行はもう移っている）
                var moveEntry = only is null
                    || (only.RegistryEntry && !registry.Entries.Any(entry => entry.ItemId == toId));
                bool MovesGroup(AvatarBaseGroup group)
                    => group.ItemId == fromId && (only is null || only.BaseGroups.Contains(group.Name, StringComparer.Ordinal));

                var entries = registry.Entries
                    .Select(entry => moveEntry && entry.ItemId == fromId ? entry with { ItemId = toId } : entry)
                    .ToList();

                var groups = registry.BaseGroups
                    .Select(group => MovesGroup(group) ? group with { ItemId = toId } : group)
                    .ToList();

                return (moveEntry && registry.Entries.Any(entry => entry.ItemId == fromId))
                    || registry.BaseGroups.Any(MovesGroup)
                        ? new AvatarRegistry
                        {
                            DetectedAt = registry.DetectedAt,
                            Entries = entries,
                            BaseGroups = groups,
                        }
                        : null;
            },
            cancellationToken);

        // 足跡と要確認
        await store.Recent.TryUpdateAsync(
            log => log.Entries.Any(entry => entry.ItemId == fromId)
                ? new Services.RecentLog
                {
                    // 移し先にも足跡があれば1行にまとめる（ID の書き換えだけだと同じ商品の行が2つ残った）
                    Entries = [.. RecentActivity.Renamed(log.Entries, fromId, toId)],
                }
                : null,
            cancellationToken);

        await store.Notifications.TryUpdateAsync(
            records =>
            {
                var touched = false;
                for (var index = 0; index < records.Count; index++)
                {
                    if (records[index].ItemId == fromId)
                    {
                        records[index] = records[index] with { ItemId = toId };
                        touched = true;
                    }
                }

                return touched ? records : null;
            },
            cancellationToken);

        // 他の商品が対応アバターとして指している分
        var loaded = await store.Items.LoadAllAsync(cancellationToken: cancellationToken);
        foreach (var item in loaded.Items.Where(item =>
            item.Local.Avatars.Any(link => link.AvatarItemId == fromId)
            && (only is null || only.LinkingItems.Contains(item.Id, StringComparer.Ordinal))))
        {
            await store.Items.ChangeLocalAsync(
                item.Id,
                current => current.Avatars.Any(link => link.AvatarItemId == fromId)
                    ? current with
                    {
                        Avatars = [.. current.Avatars
                            .Select(link => link.AvatarItemId == fromId ? link with { AvatarItemId = toId } : link)],
                    }
                    : null,
                LocalOwners.SupportedAvatars,
                cancellationToken);
        }
    }

    /// <summary>
    /// 改変の記録を移した先のIDへ読み替える。
    ///
    /// **改変は「そのとき何を使ったか」という過去の事実。**IDを移したからといって
    /// 使った事実は変わらないので、指す先だけを付け替える。
    /// 読み替えないと、消えたIDを指したまま「手元に無い」と出続ける。
    ///
    /// アバターとして指されている場合も同じ（改変はアバター1体に属する）。
    /// </summary>
    /// <param name="only">続きのとき、始めた時に記録した参照。載った改変だけを読み替える。null なら今の様子の全部（初めの1回）。</param>
    private async Task MoveModificationsAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken,
        ItemIdReferences? only = null)
    {
        var loaded = await store.Modifications.LoadAllAsync(cancellationToken);

        foreach (var found in loaded.Modifications)
        {
            if (!UsesItem(found, fromId)
                || (only is not null && !only.Modifications.Contains(found.Id, StringComparer.Ordinal)))
            {
                continue;
            }

            // 全件を読んでから1件ずつ書くまでの間に、改変の画面が名前やメモを書き、Unity から構成物が届く。
            // 読んだ写しで丸ごと書くとそれが消えるので、錠の中で読み直した今の値に当てる
            await store.Modifications.UpdateAsync(
                found.Id,
                record =>
                {
                    if (!UsesItem(record, fromId))
                    {
                        return record;
                    }

                    return record with
                    {
                        AvatarItemId = string.Equals(record.AvatarItemId, fromId, StringComparison.Ordinal)
                            ? toId
                            : record.AvatarItemId,
                        Members = [.. record.Members
                            .Select(member => string.Equals(member.ItemId, fromId, StringComparison.Ordinal)
                                ? member with { ItemId = toId }
                                : member)],

                        // 触った跡は残す。あとで「なぜ変わったか」を辿れるようにする
                        UpdatedAt = DateTimeOffset.Now,
                    };
                },
                cancellationToken);
        }
    }

    private static bool UsesItem(ModificationRecord record, string itemId)
        => string.Equals(record.AvatarItemId, itemId, StringComparison.Ordinal)
            || record.Members.Any(member => string.Equals(member.ItemId, itemId, StringComparison.Ordinal));
}
