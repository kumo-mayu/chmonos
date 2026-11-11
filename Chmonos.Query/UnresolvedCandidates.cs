using System.Text.Json;
using Chmonos.Core.Resolution;

namespace Chmonos.Query;

/// <summary>未確定の自動検索の控え1件（<c>/v1/unresolved/candidates</c>）。1件は検索した対象ごと。</summary>
/// <param name="Target">検索した対象の場所（元の zip・展開物の根のフォルダ・ファイル）。</param>
/// <param name="Files">この対象で引く、商品が決まっていないファイルのハッシュ。</param>
/// <param name="SearchedAt">検索した日時（古くなっても引き直さない。人が「再検索」を押したときだけ置き換わる）。</param>
/// <param name="Candidates">候補（画面と同じ順）。</param>
public sealed record CandidateSearchRef(string Target, IReadOnlyList<string> Files, DateTimeOffset SearchedAt, IReadOnlyList<CandidateRef> Candidates);

/// <summary>候補の商品1つ。**推定であって決まった物ではない**（人が選んだときだけ登録する）。</summary>
/// <param name="Score">点数（大きいほど確か）。</param>
/// <param name="IsStrong">自動で確定してよい水準の裏付けがあるか（Core の <see cref="ResolutionCandidate.IsStrong"/>。点数から出す値も返す）。</param>
/// <param name="Reasons">なぜこの候補なのか（画面に出す文のまま）。</param>
public sealed record CandidateRef(string ItemId, string? Name, string? ShopName, string? ShopSubdomain, int Score, bool IsStrong, IReadOnlyList<string> Reasons);

/// <summary>
/// 未確定の自動検索の控え（<c>unresolved-searches.json</c>。<see cref="CandidateSearchCache"/>）を読む道（plan 32・plan 21）。
/// 控えを書くのはアプリの先回りと「再検索」だけで、API は BOOTH に問い合わせない・控えを書かない。
/// </summary>
public static class UnresolvedCandidates
{
    /// <param name="hash">渡せば、そのファイルを持ち主に含む控えだけ（大文字小文字を区別しない）。</param>
    public static IReadOnlyList<CandidateSearchRef> All(QuerySnapshot snapshot, string? hash)
    {
        CandidateSearchFile file;
        try
        {
            file = snapshot.Store.UnresolvedSearches.Load();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // 作り直せる控えなので、読めなければ無いのと同じ（答えを止めない）
            return [];
        }

        return (file.Searches ?? [])
            .Where(entry => entry is { Target.Length: > 0 })
            .Where(entry => string.IsNullOrWhiteSpace(hash) || (entry.Files ?? []).Contains(hash.Trim(), StringComparer.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Target, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new CandidateSearchRef(
                entry.Target,
                entry.Files ?? [],
                entry.SearchedAt,
                (entry.Candidates ?? []).Where(candidate => candidate is not null)
                    .Select(candidate => new CandidateRef(
                        candidate.ItemId, candidate.Name, candidate.ShopName, candidate.ShopSubdomain, candidate.Score, candidate.IsStrong, candidate.Reasons ?? []))
                    .ToList()))
            .ToList();
    }
}
