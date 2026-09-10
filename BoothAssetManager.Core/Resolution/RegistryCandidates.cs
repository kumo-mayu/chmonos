using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Search;

namespace BoothAssetManager.Core.Resolution;

/// <summary>アバター登録簿から出した候補。</summary>
public sealed record RegistryCandidate
{
    public required string ItemId { get; init; }

    /// <summary>登録簿が持っている名前。BOOTHから取れていなくてもここにはあることがある。</summary>
    public required string Name { get; init; }

    /// <summary>当たった表記。根拠としてそのまま画面に出せる。</summary>
    public required string MatchedOn { get; init; }

    /// <summary>BOOTHから一度も取れていない項目か。「非公開かもしれない」と読める。</summary>
    public bool NeverFetched { get; init; }
}

/// <summary>
/// **手元の登録簿だけで候補を出す。通信は増えない。**
///
/// 登録簿は未所持の商品の名前まで持っている（実データでは13件中6件が未所持）。
/// `4897493` が非公開になっても「くうた」は手元に残るので、
/// BOOTHが404を返すファイルでも名前から辿り着けることがある。
///
/// 判定に使うのは**商品ID・表示名・BOOTHの正式名・別名**。
/// 別名には「くうた対応」「くうた君対応」のような実際の表記が入っていて、
/// **まさにファイル名に現れる形**（実データで7種類集まっている）。
///
/// 表記の橋渡し（<see cref="SearchBridge"/>／<see cref="ReadingMatch"/>）も通す。
/// <c>hotogiya_Kuuta_ver1.03.zip</c> はBOOTH検索では拾えなかったが、
/// 登録簿の別名なら当たる。ここでも通信は増えない。
/// </summary>
public static class RegistryCandidates
{
    /// <summary>照合に使う表記の最短の長さ。1文字は何にでも当たる。</summary>
    private const int MinTextLength = 2;

    /// <summary>1ファイルにつき出す上限。並べすぎると選べない。</summary>
    private const int MaxCandidates = 3;

    /// <summary>
    /// このファイル名に当たる登録簿の項目を、確からしい順に返す。
    /// </summary>
    /// <param name="bridge">読みから別表記を作るもの。渡さなければ字面だけで照合する。</param>
    /// <param name="readings">商品名の読みを作るもの。造語の照合に要る。</param>
    public static IReadOnlyList<RegistryCandidate> For(
        string filePath,
        IReadOnlyList<AvatarRegistryEntry> registry,
        SearchBridge? bridge = null,
        KanjiReadings? readings = null)
    {
        if (registry.Count == 0)
        {
            return [];
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath ?? string.Empty);
        if (fileName.Length == 0)
        {
            return [];
        }

        var query = FileNameQuery.ToSearchQuery(filePath!);
        var extra = FileNameQuery.UndividedTokens(filePath!);

        var found = new List<(RegistryCandidate Candidate, int Score)>();

        foreach (var entry in registry)
        {
            var match = Match(fileName, query, extra, entry, bridge, readings);
            if (match is null)
            {
                continue;
            }

            found.Add((
                new RegistryCandidate
                {
                    ItemId = entry.ItemId,
                    Name = entry.DisplayName ?? entry.BoothName ?? entry.ItemId,
                    MatchedOn = match.Value.Text,

                    // 一度も観測できていない項目。404でも項目は作られるので、
                    // 「categoryを観測できないだけで、アバターではないとは限らない」
                    NeverFetched = entry.CheckedAt is not null && entry.Category is null,
                },
                match.Value.Score));
        }

        return found
            .OrderByDescending(pair => pair.Score)
            .ThenBy(pair => pair.Candidate.Name, StringComparer.CurrentCulture)
            .Take(MaxCandidates)
            .Select(pair => pair.Candidate)
            .ToList();
    }

    /// <summary>
    /// 点数の高い順に：商品IDそのもの → 名前が入っている → 別名が入っている → 読みで当たる。
    /// IDが書かれていればそれ以上の証拠は無い。
    /// </summary>
    private static (string Text, int Score)? Match(
        string fileName,
        string query,
        IReadOnlyList<string> extra,
        AvatarRegistryEntry entry,
        SearchBridge? bridge,
        KanjiReadings? readings)
    {
        if (fileName.Contains(entry.ItemId, StringComparison.Ordinal))
        {
            return (entry.ItemId, 100);
        }

        foreach (var name in new[] { entry.DisplayName, entry.BoothName })
        {
            if (name is { Length: >= MinTextLength }
                && fileName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return (name, 60);
            }
        }

        // 別名は「くうた対応」のような、まさにファイル名に現れる形
        foreach (var alias in entry.Aliases)
        {
            if (alias.Text.Length >= MinTextLength
                && fileName.Contains(alias.Text, StringComparison.OrdinalIgnoreCase))
            {
                return (alias.Text, 40);
            }
        }

        // 読みの経路。ローマ字のファイル名が日本語のアバター名を指している場合
        foreach (var text in Texts(entry))
        {
            if (ReadingMatch.Find(query, text, bridge, readings, extra) is { } reading)
            {
                return (reading, 20);
            }
        }

        return null;
    }

    private static IEnumerable<string> Texts(AvatarRegistryEntry entry)
    {
        if (entry.DisplayName is { Length: >= MinTextLength } display)
        {
            yield return display;
        }

        if (entry.BoothName is { Length: >= MinTextLength } booth)
        {
            yield return booth;
        }

        foreach (var alias in entry.Aliases)
        {
            if (alias.Text.Length >= MinTextLength)
            {
                yield return alias.Text;
            }
        }
    }
}
