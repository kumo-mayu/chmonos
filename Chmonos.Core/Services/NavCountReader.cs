using Chmonos.Core.Models;
using Chmonos.Core.Storage;

namespace Chmonos.Core.Services;

/// <summary>ナビに出す件数のうち、ファイルから数える物。</summary>
/// <param name="Unresolved">未確定の数。</param>
/// <param name="Unread">未読で解消していない知らせの数。</param>
/// <param name="StructureAlert">BOOTH側の作りが変わった疑いの知らせ（解消していない一番新しい物）の本文。無ければ空。</param>
public sealed record NavCounts(int Unresolved, int Unread, string StructureAlert);

/// <summary>
/// 1つの JSON ファイルの印（在るか・大きさ・更新日時・このアプリの書き込みの数）。同じ間は中身が変わっていないとみなす。
/// </summary>
public readonly record struct JsonFileStamp(bool Exists, long Length, DateTime LastWriteUtc, int Writes)
{
    public static JsonFileStamp Of<T>(JsonFileStore<T> file)
        where T : class, new()
    {
        // 書き込みの数は日時より先に取る。後に取ると、見た日時より新しい書き込みの数と組になり得る
        var writes = file.WriteCount;
        var info = new FileInfo(file.Path);
        return info.Exists
            ? new JsonFileStamp(true, info.Length, info.LastWriteTimeUtc, writes)
            : new JsonFileStamp(false, 0, default, writes);
    }
}

/// <summary>数えた結果と、未確定の記録をこの回に読んだかどうか。</summary>
/// <param name="Counts">ナビの件数（未確定はファイルの数）。</param>
/// <param name="UnresolvedStamp">未確定の記録の印。前の回と同じなら、記録は変わっていない。</param>
/// <param name="UnresolvedRead">
/// この回に読んだ未確定の記録。印が前と同じで読まなかった回は null。
/// 札は登録する回数で数え直すので、呼び手（主画面）も記録の中身が要る。読み手が読んだ物を渡して、同じ記録を2回読まない
/// （未確定 8万件で1回 190〜270ms・68MB。2026-09-30 に測った）。呼び手は使い終えたら持ち続けない。
/// </param>
public sealed record NavReading(NavCounts Counts, JsonFileStamp UnresolvedStamp, List<UnresolvedFile>? UnresolvedRead);

/// <summary>
/// ナビの件数（未確定・要確認の未読・作りが変わった疑い）を数える。
///
/// **2つのファイル（<c>unresolved.json</c>・<c>notifications.json</c>）が前に数えたときと同じなら読まずに前の数を返す。**
/// 片方だけ変わった回は、変わった方だけ読む。
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

    private sealed record Snapshot(JsonFileStamp Unresolved, JsonFileStamp Notifications, NavCounts Counts);

    public NavCountReader(DataStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 数える。読めなければ投げる（呼ぶ側はログに残し、数は古いまま残す）。
    /// 裏のスレッドから重ねて呼ばれ得るので、数える間は錠を持つ（2本目は1本目の結果を使える）。
    /// </summary>
    public NavCounts Read() => ReadWithUnresolved().Counts;

    /// <summary>
    /// <see cref="Read"/> と同じく数え、未確定の記録をこの回に読んだなら、その中身も返す（<see cref="NavReading"/>）。
    ///
    /// **変わった方のファイルだけ読む。**前は片方が変わると両方を読み直していたので、知らせを既読にするだけで
    /// 未確定の記録（件数に比例して大きい）も読んでいた。
    /// </summary>
    public NavReading ReadWithUnresolved()
    {
        lock (_gate)
        {
            // 印は読む前に取る。読んでいる間に書かれたら、次に見る印が変わって読み直しになる
            var unresolvedStamp = JsonFileStamp.Of(_store.Unresolved);
            var notificationsStamp = JsonFileStamp.Of(_store.Notifications);
            if (_last is { } last && last.Unresolved == unresolvedStamp && last.Notifications == notificationsStamp)
            {
                return new NavReading(last.Counts, unresolvedStamp, UnresolvedRead: null);
            }

            List<UnresolvedFile>? unresolved = null;
            int unresolvedCount;
            if (_last is { } previous && previous.Unresolved == unresolvedStamp)
            {
                unresolvedCount = previous.Counts.Unresolved;
            }
            else
            {
                unresolved = _store.Unresolved.Load();
                unresolvedCount = unresolved.Count;
            }

            var counts = Count(unresolvedCount, _store.Notifications.Load());
            _last = new Snapshot(unresolvedStamp, notificationsStamp, counts);
            return new NavReading(counts, unresolvedStamp, unresolved);
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
