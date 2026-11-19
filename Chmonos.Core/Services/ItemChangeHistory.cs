using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>1つの商品の BOOTH での変化の履歴。新しい順。保管庫に読めなかった所があれば <see cref="HasDamage"/>。</summary>
public sealed record ItemChangeHistoryRead(IReadOnlyList<NotificationRecord> Records, bool HasDamage);

/// <summary>
/// 商品ページの「BOOTHでの変化の履歴」（ユーザ判断 2026-11-20）の元を集める。
///
/// 前の本文は保存しないので、変化の記録は商品の更新の知らせ（<see cref="NotificationKind.ItemUpdated"/> の <see cref="NotificationRecord.Diffs"/>）にしか無い。
/// 新しい知らせは <c>notifications.json</c>、件数の上限を超えた古い既読は保管庫（<see cref="NotificationArchive"/>）にあるので、両方を束ねる。
///
/// 保管庫は月ごとの gzip で、1つの商品のために全部の月を毎回解かない：通知の画面と同じ月をまたぐ目録（<see cref="NotificationArchive.CatalogAsync"/>。
/// 位置・ID・商品・種類だけで中身は持たず、変わっていない月は控えを使う）でこの商品の行の位置を引き、その位置の行だけを解く
/// </summary>
public static class ItemChangeHistory
{
    /// <summary>この商品の変化の記録か（商品の更新の知らせだけ。消えたバリエーションなどは「起きた事」でも BOOTH の変化の差ではない）。</summary>
    public static bool IsChangeOf(NotificationRecord record, string itemId)
        => record.Kind == NotificationKind.ItemUpdated && string.Equals(record.ItemId, itemId, StringComparison.Ordinal);

    /// <summary>保管庫の目録にある、この商品の変化の記録（新しい順）。</summary>
    public static IReadOnlyList<NotificationArchiveCatalogEntry> ArchivedOf(NotificationArchiveCatalog catalog, string itemId)
        => catalog.Matching(NotificationKind.ItemUpdated, new HashSet<string>(StringComparer.Ordinal) { itemId });

    /// <summary>
    /// 今の知らせと保管した知らせを束ねて、最後に変わった日時の新しい順にする。
    /// **同じ ID・同じ作った日時は1つにし、今の知らせの方を残す**：保管庫に書けてから <c>notifications.json</c> から外す順なので、
    /// 間で落ちると両方に残る（notifications.md「順序」）。未読でありうるのは今の知らせの側だけ。
    /// 商品の更新の知らせは商品ごとに同じ ID なので、ID だけでは別の機会の更新まで1つにしてしまう
    /// </summary>
    public static IReadOnlyList<NotificationRecord> Merge(
        IEnumerable<NotificationRecord> current, IEnumerable<NotificationRecord> archived, string itemId)
    {
        var seen = new HashSet<(string, DateTimeOffset)>();
        var result = new List<NotificationRecord>();
        foreach (var record in current.Concat(archived))
        {
            if (IsChangeOf(record, itemId) && seen.Add((record.Id, record.CreatedAt)))
            {
                result.Add(record);
            }
        }

        return result
            .OrderByDescending(record => record.LastChangedAt)
            .ThenByDescending(record => record.CreatedAt)
            .ToList();
    }

    /// <summary>今の知らせと保管庫から、この商品の変化の履歴を読む。保管庫は目録で引いた位置の行だけを解く。</summary>
    public static async Task<ItemChangeHistoryRead> ReadAsync(
        IReadOnlyList<NotificationRecord> current, NotificationArchive archive, string itemId, CancellationToken cancellationToken = default)
    {
        var catalog = await archive.CatalogAsync(cancellationToken);
        var archived = await catalog.ReadAsync(archive, ArchivedOf(catalog, itemId), cancellationToken);
        return new ItemChangeHistoryRead(Merge(current, archived, itemId), catalog.HasDamage);
    }
}
