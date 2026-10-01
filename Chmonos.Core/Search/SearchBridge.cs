namespace Chmonos.Core.Search;

/// <summary>橋渡しで作った候補1つ。どの道で出てきたかを持ち回る。</summary>
/// <param name="Text">探しに行く語。</param>
/// <param name="Via">出どころ。画面に「何で当たったか」を出すために使う。</param>
public readonly record struct BridgeCandidate(string Text, BridgeRoute Via);

/// <summary>候補の出どころ。</summary>
public enum BridgeRoute
{
    /// <summary>ローマ字を読みに直しただけ（辞書は要らない）。</summary>
    Reading,

    /// <summary>読みをカタカナに寄せただけ（同上）。</summary>
    Katakana,

    /// <summary>読みから辞書で漢字などを引いた。</summary>
    Dictionary,

    /// <summary>英語から辞書で日本語を引いた。</summary>
    English,

    /// <summary>カタカナの語から辞書で英語を引いた（日英変換・2026-09-16）。</summary>
    Japanese,
}

/// <summary>
/// どの道で別表記を作るか（ユーザ案 2026-09-15：別表記の中身をそれぞれ切り替える）。
/// 造語変換は語を作らず商品名の読みで照らすので、ここではなく <see cref="Services.SearchOptions.IncludeReadings"/>。
/// </summary>
/// <param name="Romaji">ローマ字をひらがな・カタカナに直す。</param>
/// <param name="Kanji">読み（ひらがな、またはローマ字から直した読み）から漢字などを引く。</param>
/// <param name="EnglishToJapanese">英語から日本語を引く。</param>
/// <param name="JapaneseToEnglish">カタカナの語から英語を引く。</param>
public sealed record BridgeOptions(
    bool Romaji = true,
    bool Kanji = true,
    bool EnglishToJapanese = true,
    bool JapaneseToEnglish = true)
{
    public static BridgeOptions All { get; } = new();

    public bool Any => Romaji || Kanji || EnglishToJapanese || JapaneseToEnglish;
}

/// <summary>
/// 打った語から「同じものの別表記」を作る。
///
/// **意味の近さは扱わない。**`tori` と「鳥」は同じものの別の書き方で、
/// 「鳥」と「羽」のような近さの話ではない。辞書は引いた結果を説明できるが、
/// 埋め込みは説明できない——そこが分かれ目（`MEMORY.md`「意味検索はしない」）。
///
/// 道は5つ（造語変換を除く4つがここ）：ローマ字→読み・カタカナ、読み→辞書（漢字）、英語→辞書、カタカナ→辞書（英語）。
/// </summary>
public sealed class SearchBridge
{
    private readonly JapaneseDictionary _dictionary;

    public SearchBridge(JapaneseDictionary dictionary) => _dictionary = dictionary;

    public bool IsAvailable => _dictionary.IsAvailable;

    /// <summary>
    /// この語から作れる別表記。作れなければ空。
    ///
    /// ラテン文字の語はローマ字・英語として、ひらがなだけの語は読みとして、カタカナだけの語は外来語として引く。
    /// 漢字を含む語はそのまま当たるので、橋を架けない。
    /// </summary>
    public IReadOnlyList<BridgeCandidate> Expand(string word, BridgeOptions? options = null)
    {
        options ??= BridgeOptions.All;
        if (word.Length < 2)
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

        if (RomajiReading.LooksRomaji(word))
        {
            // ── 英語として引く ──
            if (options.EnglishToJapanese)
            {
                foreach (var form in _dictionary.ByEnglish(word))
                {
                    Add(form, BridgeRoute.English);
                }
            }

            // ── ローマ字として読む ──
            if (options.Romaji)
            {
                foreach (var reading in RomajiReading.Readings(word))
                {
                    Add(reading, BridgeRoute.Reading);
                    Add(RomajiReading.ToKatakana(reading), BridgeRoute.Katakana);

                    if (options.Kanji)
                    {
                        foreach (var form in _dictionary.ByReading(reading))
                        {
                            Add(form, BridgeRoute.Dictionary);
                        }
                    }
                }
            }
        }
        else if (IsAllHiragana(word))
        {
            // ── 読みとして漢字を引く（ひらがなで打った語） ──
            if (options.Kanji)
            {
                foreach (var form in _dictionary.ByReading(word))
                {
                    Add(form, BridgeRoute.Dictionary);
                }
            }
        }
        else if (IsAllKatakana(word))
        {
            // ── 外来語として英語を引く（カタカナで打った語） ──
            if (options.JapaneseToEnglish)
            {
                foreach (var english in _dictionary.ByKana(Services.SearchQuery.ToHiragana(word)))
                {
                    Add(english, BridgeRoute.Japanese);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 組み立て済みの検索式の中の語を「その語 または 別表記」に置き換える。
    ///
    /// **文字列を作り直さず、木を組み替える。**そうしないと
    /// <c>-無料</c> や <c>"夏セット"</c> の意味が壊れる。
    /// 除外（<c>-tori</c>）の中でも同じように広げる——
    /// 「鳥は要らない」と言われて「鳥」だけ残すのは筋が通らない。
    /// 前置き（<c>name:tori</c>）で絞った語は、別表記も同じ対象で探す。
    /// </summary>
    /// <param name="used">実際に使った別表記。画面に「何で当たったか」を出すために受け取る。</param>
    public Services.SearchNode Widen(
        Services.SearchNode node,
        IDictionary<string, List<BridgeCandidate>> used,
        BridgeOptions? options = null)
    {
        switch (node)
        {
            case Services.SearchNode.Term term:
            {
                var candidates = Expand(term.Text, options);
                if (candidates.Count == 0)
                {
                    return term;
                }

                used[term.Text] = candidates.ToList();

                var parts = new List<Services.SearchNode> { term };
                // 日英変換の英語は英単語の区切りで当てる（SearchNode.Term.WholeWord の測定）
                parts.AddRange(candidates.Select(c =>
                    new Services.SearchNode.Term(c.Text, c.Text, term.Field, WholeWord: c.Via == BridgeRoute.Japanese)));
                return new Services.SearchNode.Or(parts);
            }

            case Services.SearchNode.Not not:
                return new Services.SearchNode.Not(Widen(not.Inner, used, options));

            case Services.SearchNode.And and:
                return new Services.SearchNode.And(and.Parts.Select(part => Widen(part, used, options)).ToList());

            case Services.SearchNode.Or or:
                return new Services.SearchNode.Or(or.Parts.Select(part => Widen(part, used, options)).ToList());

            default:
                return node;
        }
    }

    private static bool IsKana(char c) => c is >= 'ぁ' and <= 'ゖ' or >= 'ァ' and <= 'ヶ' or 'ー';

    private static bool IsAllHiragana(string text) => text.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー');

    private static bool IsAllKatakana(string text) => text.All(c => c is >= 'ァ' and <= 'ヶ' or 'ー' or '・');
}
