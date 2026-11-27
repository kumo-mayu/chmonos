using Chmonos.Core.Models;
using Chmonos.Core.Services;
using Chmonos.Query.Search;

namespace Chmonos.Query;

/// <summary>保存した検索1件（<c>/v1/saved-searches</c>）。</summary>
/// <param name="Name">名前（条件 <c>savedSearch</c> の <c>values</c> に書く名前）。</param>
/// <param name="Folder">フォルダの名前（フォルダの外なら null）。</param>
/// <param name="Summary">中身の要約（画面の吹き出しと同じ言い方。名前は入れない）。</param>
/// <param name="Text">保存した検索文字列（無い・含めずに保存したなら空）。</param>
/// <param name="Sort">呼び出したときの表示順の名前（画面の言い方）。表示順を含めずに保存したなら null。</param>
/// <param name="View">呼び出したときの形（<c>card</c>・<c>list</c>）。含めずに保存したなら null。</param>
/// <param name="KeepSort">表示順を含めずに保存した（呼び出しても今の表示順のまま）。</param>
/// <param name="KeepView">カードかリストかを含めずに保存した。</param>
/// <param name="Search">検索の中身。<c>POST /v1/search</c> の本文にそのまま入れられる。</param>
/// <param name="Unconverted">式に入れられなかった物（無ければ空）。入れなかった分だけ、<c>search</c> の答えは画面より広い。</param>
/// <param name="InCycle">条件の保存した検索をたどると自分に戻る（画面も条件 <c>savedSearch</c> も何にも当てない）。</param>
public sealed record SavedSearchRef(
    string Name,
    string? Folder,
    string Summary,
    string Text,
    string? Sort,
    string? View,
    bool KeepSort,
    bool KeepView,
    SavedSearchQuery Search,
    IReadOnlyList<UnconvertedPart> Unconverted,
    bool InCycle);

/// <param name="Items">人が決めた並び（画面の一覧と同じ。フォルダの見出しでまとめる前の並び）。</param>
/// <param name="Total">全部の数。</param>
/// <param name="NextCursor">続きの印（無ければ null）。</param>
public sealed record SavedSearchPage(IReadOnlyList<SavedSearchRef> Items, int Total, string? NextCursor);

/// <summary>
/// 保存した検索（<c>saved-searches.json</c>）を読む道（2026-11-27 に出すと決めた物の⑤。docs/spec/local-api-internal.md）。
/// 読むだけで、保存・上書き・並べ替えは画面に残す。記録は写しの <see cref="QuerySnapshot.SavedSearches"/>（見張りの部分 <see cref="StorePart.SavedSearches"/>）から取る。
/// 保存の形（<see cref="SearchModuleState"/>）はそのまま出さず、API の検索の式へ戻して出す（中を直しても外の約束が壊れないように。<see cref="SavedSearchExport"/>）
/// </summary>
public static class SavedSearchApiList
{
    private const string ListName = "savedSearches";

    /// <summary>表示順の null の意味（<see cref="SearchHistoryEntry.Sort"/>：前に書いた記録と揃えて、入手日が新しい順のまま）。</summary>
    private const string RecordedNullSort = "入手日が新しい順";

    public static SavedSearchPage Page(QuerySnapshot snapshot, string? cursor, int? limit, DateTimeOffset now)
    {
        var entries = snapshot.SavedSearches.Where(entry => entry.IsNamed).ToList();
        var rows = entries.Select((entry, index) => (Entry: entry, Index: index)).ToList();

        static IReadOnlyList<string> Key((SearchHistoryEntry Entry, int Index) row) => [ListPages.Text(row.Index)];
        static int Compare(IReadOnlyList<string> left, IReadOnlyList<string> right)
            => ListPages.Number(left[0]).CompareTo(ListPages.Number(right[0]));

        var (page, next) = ListPages.Take(rows, Key, Compare, ListName, keyLength: 1, cursor, limit);
        if (page.Count == 0)
        {
            return new SavedSearchPage([], rows.Count, next);
        }

        // 範囲の端（空欄の境）・絞っているかは画面と同じ材料で見る。輪は並び全体で1回だけ求める
        var context = snapshot.ConditionContext(now);
        var cycles = SavedSearchReferences.CycleNames(entries);
        return new SavedSearchPage(page.Select(row => Ref(row.Entry, context, cycles)).ToList(), rows.Count, next);
    }

    private static SavedSearchRef Ref(SearchHistoryEntry entry, SearchModuleContext context, IReadOnlySet<string> cycles)
    {
        var (query, unconverted) = SavedSearchExport.Of(entry, context);
        return new SavedSearchRef(
            entry.Name!.Trim(),
            entry.Folder,
            (entry with { Name = null }).Summary,
            entry.Text.Trim(),
            entry.KeepSort ? null : entry.Sort ?? RecordedNullSort,
            entry.KeepView ? null : (entry.View ?? ResultView.Card) == ResultView.List ? "list" : "card",
            entry.KeepSort,
            entry.KeepView,
            query,
            unconverted,
            cycles.Contains(entry.Name!.Trim()));
    }
}
