namespace BoothAssetManager.Core.Search;

/// <summary>橋渡しで作った候補1つ。どの道で出てきたかを持ち回る。</summary>
/// <param name="Text">探しに行く語。</param>
/// <param name="Via">出どころ。画面に「何で当たったか」を出すために使う。</param>
public readonly record struct BridgeCandidate(string Text, BridgeRoute Via);

/// <summary>候補の出どころ。</summary>
public enum BridgeRoute
{
    /// <summary>ローマ字を読みに直しただけ（通信も辞書も要らない）。</summary>
    Reading,

    /// <summary>読みをカタカナに寄せただけ（同上）。</summary>
    Katakana,

    /// <summary>読みから辞書で漢字などを引いた。</summary>
    Dictionary,

    /// <summary>英語から辞書で日本語を引いた。</summary>
    English,
}

/// <summary>
/// 打った語から「同じものの別表記」を作る。
///
/// **意味の近さは扱わない。**`tori` と「鳥」は同じものの別の書き方で、
/// 「鳥」と「羽」のような近さの話ではない。辞書は引いた結果を説明できるが、
/// 埋め込みは説明できない——そこが分かれ目（`MEMORY.md`「意味検索はしない」）。
///
/// 段は4つあり、上2つは**通信も辞書も要らない**：
/// ローマ字→読み、読み→カタカナ、読み→辞書、英語→辞書。
/// </summary>
public sealed class SearchBridge
{
    private readonly JapaneseDictionary _dictionary;

    public SearchBridge(JapaneseDictionary dictionary) => _dictionary = dictionary;

    public bool IsAvailable => _dictionary.IsAvailable;

    /// <summary>
    /// この語から作れる別表記。作れなければ空。
    ///
    /// 対象はラテン文字の語だけ。日本語で打たれた語はそのまま当たるので、
    /// 橋を架ける必要が無い。
    /// </summary>
    public IReadOnlyList<BridgeCandidate> Expand(string word)
    {
        if (word.Length < 2 || !RomajiReading.LooksRomaji(word))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { word };
        var results = new List<BridgeCandidate>();

        void Add(string text, BridgeRoute via)
        {
            // 1文字のかなは候補にしない。「わ」「め」が無関係な語の中に当たってしまう。
            // 漢字1文字（鳥・猫・音）は残す——短いが、当たれば正しいことが多い
            if (text.Length == 0 || (text.Length == 1 && IsKana(text[0])))
            {
                return;
            }

            if (seen.Add(text))
            {
                results.Add(new BridgeCandidate(text, via));
            }
        }

        // ── 英語として引く ──
        foreach (var form in _dictionary.ByEnglish(word))
        {
            Add(form, BridgeRoute.English);
        }

        // ── ローマ字として読む ──
        foreach (var reading in RomajiReading.Readings(word))
        {
            Add(reading, BridgeRoute.Reading);
            Add(RomajiReading.ToKatakana(reading), BridgeRoute.Katakana);

            foreach (var form in _dictionary.ByReading(reading))
            {
                Add(form, BridgeRoute.Dictionary);
            }
        }

        return results;
    }

    /// <summary>
    /// 組み立て済みの検索式の中で、ラテン文字の語だけを「その語 または 別表記」に置き換える。
    ///
    /// **文字列を作り直さず、木を組み替える。**そうしないと
    /// <c>-無料</c> や <c>"夏セット"</c> の意味が壊れる。
    /// 除外（<c>-tori</c>）の中でも同じように広げる——
    /// 「鳥は要らない」と言われて「鳥」だけ残すのは筋が通らない。
    /// </summary>
    /// <param name="used">実際に使った別表記。画面に「何で当たったか」を出すために受け取る。</param>
    public Services.SearchNode Widen(Services.SearchNode node, IDictionary<string, List<BridgeCandidate>> used)
    {
        switch (node)
        {
            case Services.SearchNode.Term term:
            {
                var candidates = Expand(term.Text);
                if (candidates.Count == 0)
                {
                    return term;
                }

                used[term.Text] = candidates.ToList();

                var parts = new List<Services.SearchNode> { term };
                parts.AddRange(candidates.Select(c => new Services.SearchNode.Term(c.Text)));
                return new Services.SearchNode.Or(parts);
            }

            case Services.SearchNode.Not not:
                return new Services.SearchNode.Not(Widen(not.Inner, used));

            case Services.SearchNode.And and:
                return new Services.SearchNode.And(and.Parts.Select(part => Widen(part, used)).ToList());

            case Services.SearchNode.Or or:
                return new Services.SearchNode.Or(or.Parts.Select(part => Widen(part, used)).ToList());

            default:
                return node;
        }
    }

    private static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー';
}
