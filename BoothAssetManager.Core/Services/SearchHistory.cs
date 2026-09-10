using BoothAssetManager.Core.Models;

namespace BoothAssetManager.Core.Services;

/// <summary>
/// 検索の履歴（<c>search-history.json</c> の中身）。
/// </summary>
public sealed class SearchHistoryList
{
    /// <summary>新しいものが先。</summary>
    public IReadOnlyList<SearchHistoryEntry> Entries { get; init; } = [];
}

/// <summary>
/// 検索の履歴を積む規則。
///
/// **同じ条件は積まない**（ユーザ指示）。同じ検索を2回しても2行にはせず、
/// 上に持ち上げるだけ。指紋（<see cref="SearchHistoryEntry.Fingerprint"/>）で見る。
///
/// 記録するのは**商品を開いたとき**（ユーザ指示）。
/// 絞り込みは打つたびに変わるので、変わるたびに残すとゴミになる。
/// 「探して見つけた」が一区切りで、実りのあった検索だけが残る。
/// </summary>
public static class SearchHistory
{
    /// <summary>残す件数の既定。設定で変えられる。</summary>
    public const int DefaultLimit = 20;

    /// <summary>設定で指定できる上限。ここを超えるとスロットが横に伸び続けて選べなくなる。</summary>
    public const int MaxLimit = 100;

    /// <summary>
    /// 1件積む。新しいものが先の並びで返す。
    ///
    /// <list type="bullet">
    /// <item>何も絞っていないものは積まない（戻す価値が無い）</item>
    /// <item>同じ指紋のものがあれば、**古い行を消して先頭に置く**</item>
    /// <item>名前が付いたものは件数の上限では落とさない</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Add(
        IEnumerable<SearchHistoryEntry> existing,
        SearchHistoryEntry entry,
        int limit = DefaultLimit)
    {
        var kept = existing.ToList();

        if (entry.IsEmpty)
        {
            return kept;
        }

        var fingerprint = entry.Fingerprint;

        // 同じ条件が既にあれば、その行の名前を引き継ぐ。
        // 名前を付けた検索をもう一度使ったら名前が消える、というのは筋が通らない
        var previous = kept.FirstOrDefault(other => other.Fingerprint == fingerprint);
        var head = previous?.IsNamed == true ? entry with { Name = previous.Name } : entry;

        kept.RemoveAll(other => other.Fingerprint == fingerprint);
        kept.Insert(0, head);

        return Trim(kept, limit);
    }

    /// <summary>
    /// 件数の上限まで削る。
    ///
    /// **名前が付いたものは落とさない。**自動で溜まる履歴と、
    /// 意図して残したものを同じ扱いにすると、残したはずのものが押し出されて消える。
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Trim(IEnumerable<SearchHistoryEntry> entries, int limit)
    {
        var all = entries.ToList();
        var cap = Math.Clamp(limit, 1, MaxLimit);

        var unnamed = 0;
        var kept = new List<SearchHistoryEntry>(all.Count);
        foreach (var entry in all)
        {
            if (entry.IsNamed)
            {
                kept.Add(entry);
                continue;
            }

            if (unnamed < cap)
            {
                kept.Add(entry);
                unnamed++;
            }
        }

        return kept;
    }

    /// <summary>1件だけ消す。指紋で指す（同じものは1行しか無い）。</summary>
    public static IReadOnlyList<SearchHistoryEntry> Remove(
        IEnumerable<SearchHistoryEntry> entries,
        string fingerprint)
        => entries.Where(entry => entry.Fingerprint != fingerprint).ToList();
}
