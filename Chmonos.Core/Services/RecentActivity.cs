namespace Chmonos.Core.Services;

/// <summary>足跡の種類。</summary>
public enum RecentKind
{
    /// <summary>このアプリに取り込んだ。</summary>
    Added,

    /// <summary>使った。いまはUnityへ送ったときだけ（ユーザ判断）。</summary>
    Used,

    /// <summary>商品ページを開いた。</summary>
    Viewed,
}

/// <summary>1商品ぶんの足跡。付いていない種類は null のまま。</summary>
public sealed record RecentEntry
{
    public required string ItemId { get; init; }

    public DateTimeOffset? AddedAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }

    public DateTimeOffset? ViewedAt { get; init; }

    public DateTimeOffset? Get(RecentKind kind) => kind switch
    {
        RecentKind.Added => AddedAt,
        RecentKind.Used => UsedAt,
        _ => ViewedAt,
    };
}

/// <summary>まだ書いていない足跡1つ（<see cref="RecentActivity.TouchAll"/> に渡す）。</summary>
public sealed record RecentStamp(string ItemId, RecentKind Kind, DateTimeOffset At);

/// <summary>
/// 足跡の記録（<c>recent.json</c> の中身）。
/// </summary>
public sealed class RecentLog
{
    public IReadOnlyList<RecentEntry> Entries { get; init; } = [];
}

/// <summary>
/// 「最近」の足跡を打つ規則。
///
/// **itemのJSONには書かない**（ユーザ判断）。閲覧は一番頻度が高くて
/// 一番価値が低い記録で、itemに混ぜると人が入力したものと
/// 勝手に増える足跡が同じファイルに同居する。
/// JSONは人が読んで直せる形を保つ方針なので、足跡で埋めたくない。
///
/// 「使った」は**Unityへ送ったときだけ**（ユーザ判断）。
/// エクスプローラで開くのは場所を確かめるだけのことも多く、使ったとは言い切れない。
/// </summary>
public static class RecentActivity
{
    /// <summary>
    /// 足跡を1つ打つ。同じ商品の行があれば、その種類だけを書き換える。
    ///
    /// 並びは持たない——並べ替えは読む側が時刻でやる。
    /// ここで新しい順に並べても、3種類あるので1つの並びには決まらない。
    /// </summary>
    public static IReadOnlyList<RecentEntry> Touch(
        IEnumerable<RecentEntry> existing,
        string itemId,
        RecentKind kind,
        DateTimeOffset at)
    {
        var entries = existing.ToList();
        var index = entries.FindIndex(entry =>
            string.Equals(entry.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            entries[index] = Stamped(entries[index], kind, at);
        }
        else
        {
            entries.Add(Stamped(new RecentEntry { ItemId = itemId }, kind, at));
        }

        return entries;
    }

    /// <summary>
    /// 足跡をまとめて打つ。**<see cref="Touch"/> を順に当てたのと同じ結果**になる
    /// （既にある行はその場で書き換え、無い商品は初めて出た順に末尾へ足す。同じ商品が2回あれば後の方が勝つ）。
    ///
    /// 1つずつ <see cref="Touch"/> すると、そのたびに全行を写して探すので、件数の2乗になる。
    /// ここでは商品IDから行を引く表を1回だけ作る。
    /// </summary>
    public static IReadOnlyList<RecentEntry> TouchAll(
        IEnumerable<RecentEntry> existing,
        IEnumerable<RecentStamp> stamps)
    {
        var entries = existing.ToList();

        // Touch は先頭から探して最初に当たった行を書き換えるので、同じIDが2行あれば先の方を引く
        var rows = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var row = 0; row < entries.Count; row++)
        {
            rows.TryAdd(entries[row].ItemId, row);
        }

        foreach (var stamp in stamps)
        {
            if (rows.TryGetValue(stamp.ItemId, out var row))
            {
                entries[row] = Stamped(entries[row], stamp.Kind, stamp.At);
            }
            else
            {
                rows[stamp.ItemId] = entries.Count;
                entries.Add(Stamped(new RecentEntry { ItemId = stamp.ItemId }, stamp.Kind, stamp.At));
            }
        }

        return entries;
    }

    /// <summary>
    /// 商品のIDを変えたときに、足跡を移し先へ付け替える（外部の点検 2026-10-06）。
    ///
    /// 前は ID を書き換えるだけで、移し先にも足跡があると同じ商品の行が2つ残った。足跡を打つ側（<see cref="Touch"/>）は
    /// 先の行を、日時を引く側は後の行を見るので、新しい閲覧が検索に出なくなった。
    /// 移し元と移し先の行を、先に出た方の場所に1行にまとめ、種類ごとに新しい方の日時を残す。どちらも無ければそのまま返す
    /// </summary>
    public static IReadOnlyList<RecentEntry> Renamed(IEnumerable<RecentEntry> existing, string fromId, string toId)
    {
        var entries = existing.ToList();
        bool Matches(RecentEntry entry) => string.Equals(entry.ItemId, fromId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.ItemId, toId, StringComparison.OrdinalIgnoreCase);

        var first = entries.FindIndex(entry => Matches(entry));
        if (first < 0)
        {
            return entries;
        }

        var merged = new RecentEntry { ItemId = toId };
        foreach (var entry in entries.Where(Matches))
        {
            merged = merged with
            {
                AddedAt = Latest(merged.AddedAt, entry.AddedAt),
                UsedAt = Latest(merged.UsedAt, entry.UsedAt),
                ViewedAt = Latest(merged.ViewedAt, entry.ViewedAt),
            };
        }

        var result = new List<RecentEntry>(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            if (index == first)
            {
                result.Add(merged);
            }
            else if (!Matches(entries[index]))
            {
                result.Add(entries[index]);
            }
        }

        return result;
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b)
        => a is null ? b : b is null ? a : a > b ? a : b;

    private static RecentEntry Stamped(RecentEntry entry, RecentKind kind, DateTimeOffset at) => kind switch
    {
        RecentKind.Added => entry with { AddedAt = at },
        RecentKind.Used => entry with { UsedAt = at },
        _ => entry with { ViewedAt = at },
    };

    /// <summary>
    /// その種類の時刻を商品IDから引ける形にする。並べ替えのために作る。
    /// </summary>
    public static IReadOnlyDictionary<string, DateTimeOffset> Times(
        IEnumerable<RecentEntry> entries,
        RecentKind kind)
    {
        var result = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            // 同じ商品の行が2つあれば新しい方を取る（手で直した JSON・前の版の ID の付け替えで重なった行に、古い日時で引きずられない）
            if (entry.Get(kind) is { } at && (!result.TryGetValue(entry.ItemId, out var seen) || at > seen))
            {
                result[entry.ItemId] = at;
            }
        }

        return result;
    }

    /// <summary>
    /// その足跡が指定の日数以内にあるか。
    ///
    /// **記録が無ければ当てはまらない。**「値が小さい」ではなく「値が無い」ので、
    /// 何日以内にも入らない（公開日の範囲指定と同じ考え方）。
    ///
    /// 日数が0以下なら絞っていない扱いで、全部通す。
    /// </summary>
    public static bool IsWithin(DateTimeOffset? at, int days, DateTimeOffset now)
    {
        if (days <= 0)
        {
            return true;
        }

        return at is { } stamped && stamped >= now.AddDays(-days);
    }

    /// <summary>
    /// 手元に無くなった商品の行を落とす。
    ///
    /// 足跡だけが残り続けると、消した商品の記録が延々と溜まる。
    /// **消すのは商品が消えたときだけ**——足跡そのものに寿命は付けない
    /// （「1年前に使った」も知りたい情報なので）。
    /// </summary>
    public static IReadOnlyList<RecentEntry> KeepOnly(
        IEnumerable<RecentEntry> entries,
        IReadOnlySet<string> itemIds)
        => entries.Where(entry => itemIds.Contains(entry.ItemId)).ToList();
}
