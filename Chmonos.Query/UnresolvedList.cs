using System.Text.Json;
using Chmonos.Core.Models;

namespace Chmonos.Query;

/// <summary>商品が決まっていないファイル1つ（<c>/v1/unresolved</c>）。読むだけで、決めるのは画面に残す。</summary>
/// <param name="Hash">中身のハッシュ（<c>/v1/unresolved/candidates?hash=</c> で、このファイルの自動検索の控えを引ける）。</param>
/// <param name="Paths">最後に数えたときの場所（記録の順）。</param>
/// <param name="ModifiedAt">ファイルの更新日時。</param>
/// <param name="FirstSeenAt">初めて未確定に入れた日時。</param>
/// <param name="ArchiveBroken">取り込みで zip として開けなかったか（開いてみた結果の記録）。</param>
/// <param name="ZoneHostUrl">ダウンロード元の記録（Zone.Identifier の HostUrl）。無ければ null。</param>
/// <param name="ZoneReferrerUrl">同じく ReferrerUrl。無ければ null。</param>
/// <param name="CandidateItemIds">中の手掛かりから出た商品 ID（0件は手掛かりなし・2件以上は曖昧）。**推定で、決まった物ではない**。</param>
/// <param name="SamePathItemIds">同じ場所に前にあった別の中身を持っていた商品（上書きの手掛かり）。**推定で、自動では結ばない**。</param>
/// <param name="NotOnBooth">取り込みが BOOTH に聞いて「無い」と答えられた記録。聞いていなければ null。</param>
public sealed record UnresolvedRef(
    string Hash,
    IReadOnlyList<string> Paths,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    DateTimeOffset FirstSeenAt,
    bool ArchiveBroken,
    string? ZoneHostUrl,
    string? ZoneReferrerUrl,
    IReadOnlyList<string> CandidateItemIds,
    IReadOnlyList<string> SamePathItemIds,
    NotOnBoothRef? NotOnBooth);

/// <param name="ItemId">聞いた商品 ID。</param>
/// <param name="CheckedAt">最後に「無い」と答えられた日時。</param>
public sealed record NotOnBoothRef(string ItemId, DateTimeOffset CheckedAt);

/// <param name="Items">最初の場所の順（大文字小文字を区別しない）・同じならハッシュの順。</param>
/// <param name="Total">全部の数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record UnresolvedPage(IReadOnlyList<UnresolvedRef> Items, int Total, string? NextCursor);

/// <summary>
/// 商品が決まっていないファイル（<c>unresolved.json</c>）を読む道（2026-11-27 に出すと決めた物の③）。
///
/// **写しに持たず、初めて頼まれたときに読んで、ファイルの大きさと日時が同じ間は使い回す**（<see cref="ArchivedNotifications"/> と同じ作り）：
/// 記録は大きくなり得る（8万件で 35MB：docs/research/large-files-2026-09-30.md）ので、使わない人の API の起動と読み直しを重くしない。
/// 持つのは答えの形だけで、zip の中の一覧（<see cref="UnresolvedFile.Contents"/>。記録の大半）は落とす。
/// 変わったことは見張りの部分 <see cref="StorePart.Unresolved"/> で知らせる。
/// </summary>
public sealed class UnresolvedList
{
    private const string ListName = "unresolved";

    private readonly object _gate = new();
    private (long Length, DateTime Written, IReadOnlyList<UnresolvedRef> Rows)? _kept;

    public UnresolvedPage Page(QuerySnapshot snapshot, string? cursor, int? limit)
    {
        var rows = Rows(snapshot);

        static IReadOnlyList<string> Key((UnresolvedRef Row, int Index) entry)
            => [entry.Row.Paths.FirstOrDefault() ?? string.Empty, entry.Row.Hash, ListPages.Text(entry.Index)];
        static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
        {
            var order = StringComparer.OrdinalIgnoreCase.Compare(left[0], right[0]);
            if (order == 0)
            {
                order = string.CompareOrdinal(left[0], right[0]);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(left[1], right[1]);
            }

            // 手で書いた記録で同じハッシュが2行ある時のため、記録の位置で一意にする
            return order != 0 ? order : ListPages.Number(left[2]).CompareTo(ListPages.Number(right[2]));
        }

        var ordered = rows.Select((row, index) => (Row: row, Index: index)).ToList();
        ordered.Sort((left, right) => Compare(Key(left), Key(right)));
        var (page, next) = ListPages.Take(ordered, Key, Compare, ListName, keyLength: 3, cursor, limit);
        return new UnresolvedPage(page.Select(entry => entry.Row).ToList(), ordered.Count, next);
    }

    private IReadOnlyList<UnresolvedRef> Rows(QuerySnapshot snapshot)
    {
        var file = new FileInfo(snapshot.Paths.UnresolvedFile);
        var length = file.Exists ? file.Length : -1;
        var written = file.Exists ? file.LastWriteTimeUtc : default;
        lock (_gate)
        {
            if (_kept is { } kept && kept.Length == length && kept.Written == written)
            {
                return kept.Rows;
            }
        }

        List<UnresolvedFile> records;
        try
        {
            records = snapshot.Store.Unresolved.Load();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // 読めない（手で壊した・書いている途中で開けない）ときは、前に読めた物で答える。印は覚えないので次の問い合わせで読み直す
            lock (_gate)
            {
                return _kept?.Rows ?? throw new QueryException(503, "unresolvedUnreadable", "商品が決まっていないファイルの記録を読めませんでした。少し待ってから送り直してください。");
            }
        }

        var rows = records
            .Where(record => record is { Hash.Length: > 0 })
            .Select(record => new UnresolvedRef(
                record.Hash,
                record.Paths ?? [],
                record.SizeBytes,
                record.ModifiedAtUtc,
                record.FirstSeenAt,
                record.ArchiveBroken,
                record.ZoneHostUrl,
                record.ZoneReferrerUrl,
                record.CandidateItemIds ?? [],
                record.SamePathItemIds ?? [],
                record.NotOnBooth is { } note ? new NotOnBoothRef(note.ItemId, note.CheckedAt) : null))
            .ToList();
        lock (_gate)
        {
            _kept = (length, written, rows);
        }

        return rows;
    }
}
