using System.Globalization;
using Chmonos.Core.Storage;

namespace Chmonos.Query;

/// <summary>保管した通知の1ページ（<c>/v1/notifications/archive/{month}</c>）。</summary>
/// <param name="Items">通知（画面と同じ並び：最後に変わった日時の新しい順。同じ日時は後から保管した物が先）。</param>
/// <param name="Total">絞った後の、その月の件数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
/// <param name="UnreadableLines">読めない行（壊れた行・新しい版の行・知らない種類）の数。</param>
/// <param name="UnreadableBlocks">gzip が壊れていて飛ばした所の数。</param>
public sealed record ArchivedNotificationsPage(
    string Month, IReadOnlyList<NotificationRef> Items, int Total, string? NextCursor, int UnreadableLines, int UnreadableBlocks);

/// <summary>
/// 通知の保管庫（<c>notification-archive/</c>。<see cref="NotificationArchive"/>）を読む道（plan 32・plan 24 の読む口）。
/// 画面の「保管した通知」と同じ読み口：月の目録（<see cref="NotificationArchive.IndexAsync"/>）を作り、商品名で絞り（<see cref="NotificationArchiveIndex.Matching"/>）、
/// 1ページ分だけ中身を読む（<see cref="NotificationArchive.ReadEntriesAsync"/>）。
///
/// 目録は最後に読んだ1か月分だけ、月のファイルの大きさと日時が同じ間は使い回す：ページを送るたびに月を頭から流し直すと、
/// 大きな月（容量の既定は 50MB）では1ページごとに全部を解くことになる。目録は位置・ID・題・日時だけで、本文は持たない。
/// </summary>
public sealed class ArchivedNotifications
{
    private readonly object _gate = new();
    private (string Month, long Length, DateTime Written, NotificationArchiveIndex Index)? _kept;

    /// <summary>保管庫にある月（新しい順。画面の月を選ぶ欄と同じ）。</summary>
    public static IReadOnlyList<string> Months(QuerySnapshot snapshot)
        => snapshot.Store.NotificationArchive.Months().Reverse().ToList();

    public async Task<ArchivedNotificationsPage> PageAsync(
        QuerySnapshot snapshot, string month, string? name, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        var archive = snapshot.Store.NotificationArchive;

        // 月はある物だけ受ける（送られた文字で場所を組まない）
        if (!archive.Months().Contains(month, StringComparer.Ordinal))
        {
            throw QueryException.NotFound("monthNotFound", $"保管した月にありません: {month}");
        }

        var size = limit ?? Search.SearchEngine.DefaultLimit;
        if (size is < 1 or > Search.SearchEngine.MaxLimit)
        {
            throw QueryException.Invalid("invalidLimit", $"limit は 1〜{Search.SearchEngine.MaxLimit} です: {size}");
        }

        var after = ParseCursor(cursor);
        var index = await IndexOfAsync(archive, month, cancellationToken);
        var matching = index.Matching(name);

        // 並びは（最後に変わった日時・行の位置）の降順で一意。印はそこまでに返した最後の1件
        var start = after is { } last
            ? matching.Count(entry => entry.LastChangedAt > last.At || (entry.LastChangedAt == last.At && entry.Ordinal >= last.Ordinal))
            : 0;
        var page = matching.Skip(start).Take(size).ToList();
        var records = await archive.ReadEntriesAsync(index, page, cancellationToken);
        var next = start + page.Count < matching.Count && page.Count > 0
            ? $"{page[^1].LastChangedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}.{page[^1].Ordinal.ToString(CultureInfo.InvariantCulture)}"
            : null;

        return new ArchivedNotificationsPage(
            month, records.Select(Catalogs.NotificationOf).ToList(), matching.Count, next, index.UnreadableLines, index.UnreadableBlocks);
    }

    private async Task<NotificationArchiveIndex> IndexOfAsync(NotificationArchive archive, string month, CancellationToken cancellationToken)
    {
        var file = new FileInfo(archive.FileFor(month));
        lock (_gate)
        {
            if (_kept is { } kept && kept.Month == month && kept.Length == file.Length && kept.Written == file.LastWriteTimeUtc)
            {
                return kept.Index;
            }
        }

        var index = await archive.IndexAsync(month, cancellationToken);
        lock (_gate)
        {
            _kept = (month, file.Length, file.LastWriteTimeUtc, index);
        }

        return index;
    }

    /// <summary>続きの印（<c>最後の日時の UTC の刻み.行の位置</c>）。中身は約束にしない。</summary>
    private static (DateTimeOffset At, int Ordinal)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return null;
        }

        var parts = cursor.Split('.');
        return parts.Length == 2
            && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks <= DateTimeOffset.MaxValue.UtcTicks
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), ordinal)
                : throw QueryException.Invalid("invalidCursor", "cursor は前の答えの nextCursor をそのまま送ってください。");
    }
}
