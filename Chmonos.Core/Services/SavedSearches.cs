using Chmonos.Core.Models;

namespace Chmonos.Core.Services;

/// <summary>
/// 保存した検索（<c>saved-searches.json</c> の中身）。並びは人が決めた順。
///
/// 1件の形は検索の履歴と同じ <see cref="SearchHistoryEntry"/>（打った文字・条件の並び・表示順）に、
/// 名前（<see cref="SearchHistoryEntry.Name"/>）とカードかリストか（<see cref="SearchHistoryEntry.View"/>）を足した物。
/// 同じ「条件一式」を2つの形で持つと、条件の種類を足すたびに両方を直すことになる。
/// 履歴とファイルを分けたのは、履歴は古い物から押し出す・同じ条件を重ねない、保存した物は押し出さず人が並べる、と扱いが違うため
/// </summary>
public sealed class SavedSearchList
{
    public IReadOnlyList<SearchHistoryEntry> Entries { get; init; } = [];
}

/// <summary>
/// 保存した検索を足す・上書きする・名前を変える・消す・並べ替える規則。
/// どれも「今の並び」を受けて新しい並びを返す関数で、錠の中で今の値に当てる（<see cref="Commands.UiCommand.ChangeSavedSearches"/>）。
///
/// **名前で指す。**名前は1つの並びの中で重ならない（大文字小文字と前後の空白は区別しない）。
/// 同じ条件を別の名前で残すのは許す（「夏の衣装」と「夏の衣装（リスト）」を分けたい人がいる）
/// </summary>
public static class SavedSearches
{
    /// <summary>同じ名前か。画面の「既にあります」と、ここでの重なりの判定を同じにする</summary>
    public static bool SameName(string? left, string? right)
        => string.Equals(left?.Trim(), right?.Trim(), StringComparison.CurrentCultureIgnoreCase);

    public static SearchHistoryEntry? Find(IEnumerable<SearchHistoryEntry> entries, string name)
        => entries.FirstOrDefault(entry => SameName(entry.Name, name));

    /// <summary>
    /// 末尾に足す。**同じ名前が既にあれば、その場所で置き換える**（画面は「既にあります」で止めるが、
    /// 2つの画面から同時に同じ名前で保存されたときに2行にしない）。名前の空白は前後を落とす
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Add(IEnumerable<SearchHistoryEntry> entries, SearchHistoryEntry entry)
    {
        var list = entries.ToList();
        var named = entry with { Name = entry.Name?.Trim() };
        var at = list.FindIndex(other => SameName(other.Name, named.Name));
        if (at >= 0)
        {
            list[at] = named;
        }
        else
        {
            list.Add(named);
        }

        return list;
    }

    /// <summary>名前と場所はそのまま、中身を今の検索に替える。名前が無ければ（別の所で消された）何もしない</summary>
    public static IReadOnlyList<SearchHistoryEntry> Overwrite(IEnumerable<SearchHistoryEntry> entries, string name, SearchHistoryEntry entry)
        => entries.Select(other => SameName(other.Name, name) ? entry with { Name = other.Name } : other).ToList();

    /// <summary>
    /// 名前を変える。新しい名前がほかの行と重なるなら何もしない（大文字小文字だけを直すのは、同じ行なので通す）
    /// </summary>
    public static IReadOnlyList<SearchHistoryEntry> Rename(IEnumerable<SearchHistoryEntry> entries, string name, string newName)
    {
        var list = entries.ToList();
        var trimmed = newName.Trim();
        if (trimmed.Length == 0 || list.Any(other => SameName(other.Name, trimmed) && !SameName(other.Name, name)))
        {
            return list;
        }

        return list.Select(other => SameName(other.Name, name) ? other with { Name = trimmed } : other).ToList();
    }

    public static IReadOnlyList<SearchHistoryEntry> Remove(IEnumerable<SearchHistoryEntry> entries, string name)
        => entries.Where(other => !SameName(other.Name, name)).ToList();

    /// <summary>1つ上（<paramref name="delta"/> = -1）か下（+1）へ動かす。端では動かさない</summary>
    public static IReadOnlyList<SearchHistoryEntry> Move(IEnumerable<SearchHistoryEntry> entries, string name, int delta)
    {
        var list = entries.ToList();
        var at = list.FindIndex(other => SameName(other.Name, name));
        var to = at + delta;
        if (at < 0 || to < 0 || to >= list.Count)
        {
            return list;
        }

        (list[at], list[to]) = (list[to], list[at]);
        return list;
    }
}
