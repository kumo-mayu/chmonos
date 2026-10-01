using Chmonos.Core.Search;

namespace Chmonos.Core.Resolution;

/// <summary>
/// ファイル名から作った検索語が空振りしたときに、**別の表記で引き直す**ための語を作る。
///
/// ローマ字のファイル名が日本語の商品を指していると、そのままでは当たらない。
/// 実測（`experiments/FallbackSearchProbe`・正解の分かる10ファイル）では
/// 検索の当たりが 5/10 で、外れた5件のうち2件がここで拾える見込み：
/// <c>Tori</c> → 「鳥」1位、<c>HeartBeat</c> → 「心拍」1位。
///
/// 順序に意味がある。
/// <list type="number">
///   <item><b>読みの経路</b>（ローマ字→かな→漢字）。ラテン文字のファイル名は
///   日本語商品の<b>ローマ字表記</b>であることが多いので、まずこちらを試す</item>
///   <item><b>英語の経路</b>。<c>heartbeat</c> のように英単語で名付けられている場合に効く</item>
/// </list>
///
/// 英語の経路は**分かち書きにする前の綴り**で引く。
/// <c>Heart</c> <c>Beat</c> に割ってから引くと 心臓・拍 にしかならず、
/// 割らずに <c>heartbeat</c> で引くと 心拍・心音 が出る（実測）。
/// </summary>
public static class AlternateQueries
{
    /// <summary>引き直す回数の上限。1回につきBOOTHへの問い合わせが1本増える。</summary>
    public const int MaxQueries = 2;

    /// <summary>
    /// 引き直す語を、試す順に返す。作れなければ空——**そのときは何もしない**ので、
    /// 通信も増えない（実測では外れた5件のうち2件がこれに当たった）。
    /// </summary>
    public static IReadOnlyList<string> For(string filePath, string query, SearchBridge? bridge)
    {
        if (bridge is null || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { query };

        void Add(string? text)
        {
            if (text is { Length: > 1 } && seen.Add(text) && results.Count < MaxQueries)
            {
                results.Add(text);
            }
        }

        // ① 読みの経路。漢字を含むものを先に——かなよりも商品名に使われている見込みが高い
        Add(tokens
            .SelectMany(token => bridge.Expand(token))
            .Where(candidate => candidate.Via != BridgeRoute.English)
            .Select(candidate => candidate.Text)
            .OrderByDescending(HasKanji)
            .FirstOrDefault());

        // ② 英語の経路。割る前の綴りで引く。
        //    日本語の表記を先に——ＳＩＧ のような全角ラテン文字は商品名に使われない
        Add(FileNameQuery.UndividedTokens(filePath)
            .SelectMany(token => bridge.Expand(token))
            .Where(candidate => candidate.Via == BridgeRoute.English)
            .Select(candidate => candidate.Text)
            .OrderByDescending(IsJapanese)
            .FirstOrDefault());

        return results;
    }

    private static bool HasKanji(string text) => text.Any(c => c is >= '一' and <= '鿿');

    /// <summary>漢字かかなを含むか。全角のラテン文字（ＳＩＧ）を後ろに回すために見る。</summary>
    private static bool IsJapanese(string text)
        => text.Any(c => c is >= '一' and <= '鿿' or >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ');
}
