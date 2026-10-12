using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>商品ごとの未読の「商品の更新」の知らせ（知らせのID と、知らせが含む変化の種類）。</summary>
public sealed record UnreadUpdate(IReadOnlyList<string> Ids, BoothChangeKind Kinds);

/// <summary>
/// 未読の「商品の更新」の知らせの表（2026-10-12・検索画面から Core へ移した。外から使う API と同じ数え方にする）。
/// 数え方はショップの画面の「更新あり」と同じ：要確認に未読で、片付けていない <see cref="NotificationKind.ItemUpdated"/> がある商品。
/// 種類は知らせのファイルを読んだときに1回だけ見分ける（絞り込みのたびに差を読み直さない）。
/// </summary>
public static class UnreadUpdates
{
    public static Dictionary<string, UnreadUpdate> Of(IEnumerable<NotificationRecord> records)
        => records
            .Where(record => !record.IsRead && !record.IsResolved && record.Kind == NotificationKind.ItemUpdated && record.ItemId is not null)
            .GroupBy(record => record.ItemId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new UnreadUpdate(
                    group.Select(record => record.Id).ToList(),
                    group.Aggregate(BoothChangeKind.None, (kinds, record) => kinds | BoothChanges.KindsOf(record))),
                StringComparer.Ordinal);
}
