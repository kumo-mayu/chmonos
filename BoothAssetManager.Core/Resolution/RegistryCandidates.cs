using BoothAssetManager.Core.Models;
using BoothAssetManager.Core.Search;
using BoothAssetManager.Core.Services;

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

    /// <summary>ラテン文字の表記の最短の長さ。「VR」「si」は何かの語の一部として当たってしまう。</summary>
    private const int MinLatinLength = 3;

    /// <summary>読みで当てるときの最短の長さ。</summary>
    private const int MinReadingLength = 3;

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
        var shared = SharedTexts(registry);

        var found = new List<(RegistryCandidate Candidate, int Score)>();

        foreach (var entry in registry)
        {
            var match = Match(fileName, query, extra, entry, shared, bridge, readings);
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
        IReadOnlySet<string> shared,
        SearchBridge? bridge,
        KanjiReadings? readings)
    {
        if (fileName.Contains(entry.ItemId, StringComparison.Ordinal))
        {
            return (entry.ItemId, 100);
        }

        foreach (var name in new[] { entry.DisplayName, entry.BoothName })
        {
            if (name is not null && Appears(fileName, name, shared))
            {
                return (name, 60);
            }
        }

        // 別名は「くうた対応」のような、まさにファイル名に現れる形
        foreach (var alias in entry.Aliases)
        {
            if (Appears(fileName, alias.Text, shared))
            {
                return (alias.Text, 40);
            }
        }

        // 読みの経路。ローマ字のファイル名が日本語のアバター名を指している場合。
        // 2文字の読み（まふ・えも）は長い名前の一部として当たってしまうことが多い。
        // 2文字で許すのは、名前そのものが短い項目（エク・『Bird/鳥』の鳥）に当たったときだけ
        foreach (var text in Texts(entry))
        {
            if (shared.Contains(text)
                || ReadingMatch.Find(query, text, bridge, readings, extra) is not { } reading)
            {
                continue;
            }

            if (reading.Length >= MinReadingLength || MatchesAShortName(query, extra, entry, bridge, readings))
            {
                return (reading, 20);
            }
        }

        return null;
    }

    /// <summary>
    /// 項目の名前を区切りで分けた、3文字以下の一片に読みで当たるか。
    /// 締めすぎて、実データで正解だった「Eku → エク」「Tori → Bird/鳥」の4本を落としたので足した。
    /// </summary>
    private static bool MatchesAShortName(
        string query,
        IReadOnlyList<string> extra,
        AvatarRegistryEntry entry,
        SearchBridge? bridge,
        KanjiReadings? readings)
        => Texts(entry)
            .SelectMany(text => NamePartSeparator.Split(text))
            .Where(part => part.Length is > 0 and <= MinReadingLength)
            .Distinct(StringComparer.Ordinal)
            .Any(part => ReadingMatch.Find(query, part, bridge, readings, extra) is not null);

    private static readonly System.Text.RegularExpressions.Regex NamePartSeparator =
        new(@"[\s/／・\-－_『』「」【】()（）:：]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// ファイル名にこの表記が、**このアバターを指す形で**現れているか。
    ///
    /// 正解の分かる319本で測ると、登録簿の候補が外ればかりだったものが141本あり、
    /// 当たっていた語の多くは「VR」「si」「シェーダー」「ポーズ」「天使」のような短い語か一般的な語だった。
    /// <list type="bullet">
    ///   <item>一般的な語（<see cref="AvatarText.IsGenericName"/>）は使わない</item>
    ///   <item>ラテン文字は3文字以上、語の境目で当たること（「Nemo」が「Nemoria」に当たらない）</item>
    ///   <item>登録簿の2項目以上が持っている表記は使わない。どの項目を指しているか決められない</item>
    /// </list>
    /// </summary>
    private static bool Appears(string fileName, string text, IReadOnlySet<string> shared)
    {
        if (text.Length < MinTextLength || AvatarText.IsGenericName(text) || shared.Contains(text))
        {
            return false;
        }

        return text.All(char.IsAscii)
            ? text.Length >= MinLatinLength && FileNameQuery.ContainsWord(fileName, text)
            : fileName.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>登録簿の2項目以上が持っている表記（表示名・正式名・別名）。</summary>
    private static IReadOnlySet<string> SharedTexts(IReadOnlyList<AvatarRegistryEntry> registry)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in registry)
        {
            foreach (var text in Texts(entry).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[text] = counts.GetValueOrDefault(text) + 1;
            }
        }

        return counts.Where(pair => pair.Value >= 2)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
