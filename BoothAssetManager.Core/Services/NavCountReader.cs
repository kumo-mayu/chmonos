using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Storage;

namespace BoothAssetManager.Core.Services;

/// <summary>ナビに出す件数のうち、ファイルから数える物。</summary>
/// <param name="Unresolved">未確定の数。</param>
/// <param name="Unread">未読で解消していない知らせの数。</param>
/// <param name="StructureAlert">BOOTH側の作りが変わった疑いの知らせ（解消していない一番新しい物）の本文。無ければ空。</param>
public sealed record NavCounts(int Unresolved, int Unread, string StructureAlert);

/// <summary>
/// ナビの件数（未確定・要確認の未読・作りが変わった疑い）を数える。
///
/// **2つのファイル（<c>unresolved.json</c>・<c>notifications.json</c>）が前に数えたときと同じなら読まずに前の数を返す。**
/// 取り込み中は③待ちの数が変わるたび（商品1件あたり約2回）に数え直しが頼まれ、そのたびに2つを読んでいた
/// （知らせは上限2000件で約1MB。stress-manage の1000件で1回 約2ms・割り当て 720KB）。
/// 取り込みが書くのは商品と画像で、この2つはほとんど変わらない。
///
/// 「同じ」は大きさ・更新日時と、このアプリの書き込みの数（<see cref="JsonFileStore{T}.WriteCount"/>）で見る。
/// 書き込みの数も見るので、同じ大きさのまま時刻の刻みの中で既読にし直しても取り違えない。
/// 控えるのは3つの数だけなので、持つメモリは無いに等しい。
/// </summary>
public sealed class NavCountReader
{
    private readonly DataStore _store;
    private readonly object _gate = new();
    private Snapshot? _last;

    private readonly record struct Stamp(bool Exists, long Length, DateTime LastWriteUtc, int Writes)
    {
        public static Stamp Of<T>(JsonFileStore<T> file)
            where T : class, new()
        {
            // 書き込みの数は日時より先に取る。後に取ると、見た日時より新しい書き込みの数と組になり得る
            var writes = file.WriteCount;
            var info = new FileInfo(file.Path);
            return info.Exists
                ? new Stamp(true, info.Length, info.LastWriteTimeUtc, writes)
                : new Stamp(false, 0, default, writes);
        }
    }

    private sealed record Snapshot(Stamp Unresolved, Stamp Notifications, NavCounts Counts);

    public NavCountReader(DataStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 数える。読めなければ投げる（呼ぶ側はログに残し、数は古いまま残す）。
    /// 裏のスレッドから重ねて呼ばれ得るので、数える間は錠を持つ（2本目は1本目の結果を使える）。
    /// </summary>
    public NavCounts Read()
    {
        lock (_gate)
        {
            // 印は読む前に取る。読んでいる間に書かれたら、次に見る印が変わって読み直しになる
            var unresolvedStamp = Stamp.Of(_store.Unresolved);
            var notificationsStamp = Stamp.Of(_store.Notifications);
            if (_last is { } last && last.Unresolved == unresolvedStamp && last.Notifications == notificationsStamp)
            {
                return last.Counts;
            }

            var counts = Count(_store.Unresolved.Load().Count, _store.Notifications.Load());
            _last = new Snapshot(unresolvedStamp, notificationsStamp, counts);
            return counts;
        }
    }

    /// <summary>
    /// 知らせから数える。解消済みは一覧（未読のみ）に出ないので、未読にも数えない
    /// （数えると「バッジは残っているのに画面に出ない」状態になる。ユーザ指摘 2026-09-18）。
    /// BOOTH側の作りが変わった疑いは、商品1件ごとの話と並べずにナビの帯で出す（ユーザ判断 2026-09-18）。
    /// </summary>
    public static NavCounts Count(int unresolved, IEnumerable<NotificationRecord> notifications)
    {
        var unread = 0;
        NotificationRecord? alert = null;
        foreach (var record in notifications)
        {
            if (record.IsResolved)
            {
                continue;
            }

            if (!record.IsRead)
            {
                unread++;
            }

            if (record.Kind == NotificationKind.PageStructureChanged
                && (alert is null || record.CreatedAt > alert.CreatedAt))
            {
                alert = record;
            }
        }

        return new NavCounts(unresolved, unread, alert?.Detail ?? string.Empty);
    }
}
