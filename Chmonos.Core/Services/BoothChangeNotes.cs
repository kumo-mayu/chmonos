using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>
/// BOOTH から取り直した商品の、変わった所を通知に書く（変わった欄・消えた／戻ったバリエーション・BOOTH に現れた）。
///
/// ItemService から分けた（点検24・ユーザ判断 2026-10-08「クラス分けは進めてくれ」）。取り直し（<c>ItemService.RefreshAsync</c>）が、
/// 商品を書いた後に古い記録と新しい BOOTH の情報を渡して呼ぶ。通知と商品の記録しか触らない
/// </summary>
internal sealed class BoothChangeNotes(DataStore store)
{
    /// <summary>
    /// 変わっていたら要確認へ書く。
    ///
    /// 「知らせる」を商品ごとに切れるようにしてあるので、切っている商品には出さない。
    /// 何が変わったかを列挙するのは、**開かなくても判断できるようにする**ため。
    /// </summary>
    internal async Task NoteChangesAsync(ItemRecord existing, BoothBlock booth, CancellationToken cancellationToken)
    {
        if (!existing.Local.NotifyOnUpdate)
        {
            return;
        }

        var diffs = BoothChanges.Describe(existing.Booth, booth);
        if (diffs.Count == 0)
        {
            return;
        }

        // 同じ商品の未読が既にあれば、そこへ重ねる（ユーザ判断 2026-10-02「重ねましょう」）。
        // 前は差し替えていて、既読にする前に2回変わると1回目の差が消えていた。
        // 別の知らせとして溜めず1件にするのは、要確認の行・商品ページの印・ナビの数・「既読にする」が
        // どれも「商品1件に未読1件」で数えているから。読む方も、最初の前と最後の後が分かれば足りる。
        // 錠の中で今の一覧に当てる（読んでから書くまでに人が既読にしていたら、その知らせには重ねない）
        var id = $"item-updated:{existing.Id}";
        var now = DateTimeOffset.Now;
        await store.Notifications.UpdateAsync(
            notifications =>
            {
                var unread = notifications
                    .Where(entry => entry.Id == id && !entry.IsRead && !entry.IsResolved)
                    .OrderBy(entry => entry.CreatedAt)
                    .ToList();

                if (unread.Count == 0)
                {
                    notifications.Add(new NotificationRecord
                    {
                        Id = id,
                        Kind = NotificationKind.ItemUpdated,
                        ItemId = existing.Id,
                        Title = booth.Name ?? existing.Id,
                        Detail = BoothChanges.Summarize(diffs),
                        Diffs = diffs,
                        CreatedAt = now,
                        IsStrong = BoothChanges.HasStrongChange(diffs),
                    });

                    return notifications;
                }

                // 手で直した JSON などで未読が2件以上あっても、古い順に重ねて1件にまとめる
                var stacked = unread.Skip(1).Aggregate(
                    unread[0].Diffs ?? [],
                    (accumulated, entry) => ChangeStack.Stack(accumulated, entry.Diffs ?? []));
                stacked = ChangeStack.Stack(stacked, diffs);

                // 記録は値で比べると同じ中身の別の行も拾うので、置き場所は参照で探す
                var at = notifications.FindIndex(entry => ReferenceEquals(entry, unread[0]));
                notifications.RemoveAll(entry => unread.Any(target => ReferenceEquals(target, entry)));

                // 戻って元と同じになった（価格が上がって戻った、など）なら、知らせることが無いので消す
                if (stacked.Count > 0)
                {
                    notifications.Insert(Math.Min(at, notifications.Count), unread[0] with
                    {
                        Title = booth.Name ?? existing.Id,
                        Detail = BoothChanges.Summarize(stacked),
                        Diffs = stacked,
                        UpdatedAt = now,
                        IsStrong = BoothChanges.HasStrongChange(stacked),
                    });
                }

                return notifications;
            },
            cancellationToken);
    }

    /// <summary>
    /// 手元のファイル・購入の記録が指す種類が、BOOTH側から消えた／戻ったことを要確認に出す
    /// （ユーザ判断 2026-09-18：どちらも一度きりの出来事で、商品ごとに結び直しの手当てができる）。
    ///
    /// 種類ごとの販売終了は普通の商品でも起こるので、消えたままだと
    /// 「買ったのに記録を入れる行が無い」状態に気付けない。
    /// </summary>
    internal async Task NoteVariationLinksAsync(ItemRecord existing, BoothBlock booth, CancellationToken cancellationToken)
    {
        var linked = existing.Local.LocalFiles.Select(file => file.VariationId)
            .Concat(existing.Local.Purchases.Select(purchase => purchase.VariationId))
            .OfType<long>()
            .Distinct()
            .ToList();

        if (linked.Count == 0)
        {
            return;
        }

        var present = booth.Variations.Select(variation => variation.Id).ToHashSet();
        var missing = linked.Where(id => !present.Contains(id)).ToList();

        var goneId = $"variation-gone:{existing.Id}";
        var name = booth.Name ?? existing.Id;

        await store.Notifications.TryUpdateAsync(
            notifications =>
            {
                var wasGone = notifications.FindIndex(entry => entry.Id == goneId && !entry.IsResolved);
                if (missing.Count > 0)
                {
                    if (wasGone >= 0)
                    {
                        return null;
                    }

                    notifications.Add(new NotificationRecord
                    {
                        Id = goneId,
                        Kind = NotificationKind.OrphanVariationLink,
                        ItemId = existing.Id,
                        // 説明は束の見出しに出るので、行にはこの行だけの事実を書く（ユーザ指示 2026-09-18）
                        Title = name,
                        Detail = $"消えたバリエーション：{NameVariations(missing, existing)}",
                        CreatedAt = DateTimeOffset.Now,
                    });

                    return notifications;
                }

                if (wasGone < 0)
                {
                    return null;
                }

                // 消えていた種類が戻った。前の知らせは用が済んだので解消済みにし、戻ったことを1件出す
                notifications[wasGone] = notifications[wasGone] with { IsResolved = true };
                notifications.Add(new NotificationRecord
                {
                    Id = $"variation-back:{existing.Id}:{DateTimeOffset.Now:yyyyMMddHHmmss}",
                    Kind = NotificationKind.VariationBackOnBooth,
                    ItemId = existing.Id,
                    Title = name,
                    Detail = $"戻ったバリエーション：{NameVariations(linked.Where(present.Contains).ToList(), existing, booth)}",
                    CreatedAt = DateTimeOffset.Now,
                });

                return notifications;
            },
            cancellationToken);
    }

    /// <summary>行に出すバリエーションの名前を並べる（ユーザ要望 2026-09-18：件数だけでは何が消えたか分からない）。</summary>
    /// <remarks>
    /// 消えたバリエーションの名前は**BOOTHにはもう無い**。
    /// 取り直す前の <c>booth</c> ブロックと、購入時に写し取った名前（<see cref="Purchase.NameSnapshot"/>）から引く。
    /// BOOTH が名前を持たせていなかったもの（種類が1つだけの商品に多い）は、ほかの画面と同じく <see cref="DisplayText.NoVariationName"/> と呼ぶ
    /// （ユーザ判断 2026-09-29。「ID 12345」では何のことか分からない）。
    /// どちらでもない（在ったかも分からない）ものだけIDで言う（黙って落とすと、どれのことか辿れなくなる）。
    /// </remarks>
    private static string NameVariations(IReadOnlyList<long> ids, ItemRecord existing, BoothBlock? booth = null)
    {
        const int shown = 3;

        var names = new Dictionary<long, string>();
        var unnamed = new HashSet<long>();
        foreach (var variation in (booth ?? existing.Booth).Variations.Concat(existing.Booth.Variations))
        {
            if (variation.Name is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text))
            {
                names.TryAdd(variation.Id, text);
            }
            else
            {
                unnamed.Add(variation.Id);
            }
        }

        foreach (var purchase in existing.Local.Purchases)
        {
            if (purchase.VariationId is { } id && purchase.NameSnapshot is { Length: > 0 } text)
            {
                names.TryAdd(id, text);
            }
        }

        var labels = ids
            .Select(id => names.TryGetValue(id, out var text) ? text
                : unnamed.Contains(id) ? DisplayText.NoVariationName
                : $"ID {id}")
            .ToList();

        return labels.Count <= shown
            ? string.Join("・", labels)
            : string.Join("・", labels.Take(shown)) + $"　ほか {labels.Count - shown} 件";
    }

    /// <summary>
    /// 非公開と見なしていた商品が戻ってきたことを要確認に出す。
    ///
    /// 黙って埋めると、画像が急に増え、価格が入り、印が消える。
    /// 説明が無いと「壊れた」と読まれる。
    ///
    /// **名前を切り替えるかは聞かない。**自分で付けた名前を優先すると決めてあるので、
    /// そこを毎回問い直す理由がない（編集画面で変えられることだけ言う）。
    /// 「知らせる」を切っている商品にも出す——これは更新の知らせではなく、
    /// **こちらが「もう無い」と判断していたのが誤りだったという訂正**だから。
    /// </summary>
    internal async Task NoteBackOnBoothAsync(
        ItemRecord existing,
        BoothBlock booth,
        CancellationToken cancellationToken)
    {
        if (!existing.Local.IsDelisted)
        {
            return;
        }

        var id = $"item-back:{existing.Id}";
        var name = existing.Local.DisplayName;
        var detail = name is { Length: > 0 }
            ? $"「販売終了」の印を外しました。名前は自分で付けた「{name}」のままです。編集画面で変えられます。"
            : "「販売終了」の印を外しました。";

        await store.Notifications.UpdateAsync(
            notifications =>
            {
                notifications.RemoveAll(entry => entry.Id == id && !entry.IsRead);
                notifications.Add(new NotificationRecord
                {
                    Id = id,
                    Kind = NotificationKind.ItemBackOnBooth,
                    ItemId = existing.Id,
                    Title = name ?? booth.Name ?? existing.Id,
                    Detail = detail,
                    CreatedAt = DateTimeOffset.Now,
                });

                return notifications;
            },
            cancellationToken);
    }
}
