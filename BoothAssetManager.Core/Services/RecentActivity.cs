namespace BoothAssetManager.Core.Services;

/// <summary>足跡の種類。</summary>
public enum RecentKind
{
    /// <summary>手元に入った。</summary>
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

        var current = index >= 0 ? entries[index] : new RecentEntry { ItemId = itemId };
        var updated = kind switch
        {
            RecentKind.Added => current with { AddedAt = at },
            RecentKind.Used => current with { UsedAt = at },
            _ => current with { ViewedAt = at },
        };

        if (index >= 0)
        {
            entries[index] = updated;
        }
        else
        {
            entries.Add(updated);
        }

        return entries;
    }

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
            if (entry.Get(kind) is { } at)
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
